using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CodeForge
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AsyncAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(Descriptors.SyncOverAsync, Descriptors.AsyncVoid, Descriptors.BlockingSleep);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzeProperty, OperationKind.PropertyReference);
            context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
            context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        }

        private static bool IsTask(INamedTypeSymbol? type)
        {
            if (type == null)
            {
                return false;
            }

            var name = type.OriginalDefinition.ToDisplayString();
            return name == "System.Threading.Tasks.Task" || name == "System.Threading.Tasks.Task<TResult>"
                || name == "System.Threading.Tasks.ValueTask" || name == "System.Threading.Tasks.ValueTask<TResult>";
        }

        private static bool IsAwaiter(INamedTypeSymbol? type)
        {
            if (type == null)
            {
                return false;
            }

            var name = type.OriginalDefinition.ToDisplayString();
            return name.StartsWith("System.Runtime.CompilerServices.TaskAwaiter", System.StringComparison.Ordinal)
                || name.StartsWith("System.Runtime.CompilerServices.ValueTaskAwaiter", System.StringComparison.Ordinal)
                || name.StartsWith("System.Runtime.CompilerServices.ConfiguredTaskAwaitable", System.StringComparison.Ordinal)
                || name.StartsWith("System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable", System.StringComparison.Ordinal);
        }

        private static void AnalyzeProperty(OperationAnalysisContext context)
        {
            var reference = (IPropertyReferenceOperation)context.Operation;
            if (reference.Property.Name == "Result" && IsTask(reference.Property.ContainingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.SyncOverAsync, reference.Syntax.GetLocation(), ".Result"));
            }
        }

        private static void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            var method = invocation.TargetMethod;
            var containing = method.ContainingType;

            if (IsTask(containing) && (method.Name == "Wait" || (method.IsStatic && (method.Name == "WaitAll" || method.Name == "WaitAny"))))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.SyncOverAsync, invocation.Syntax.GetLocation(), method.Name + "()"));
                return;
            }

            if (method.Name == "GetResult" && IsAwaiter(containing))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.SyncOverAsync, invocation.Syntax.GetLocation(), "GetAwaiter().GetResult()"));
                return;
            }

            if (method.Name == "Sleep" && method.IsStatic && containing.ToDisplayString() == "System.Threading.Thread" && IsInAsyncMember(invocation.Syntax))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.BlockingSleep, invocation.Syntax.GetLocation()));
            }
        }

        private static bool IsInAsyncMember(SyntaxNode node)
        {
            foreach (var ancestor in node.Ancestors())
            {
                switch (ancestor)
                {
                    case AnonymousFunctionExpressionSyntax lambda:
                        return lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
                    case LocalFunctionStatementSyntax local:
                        return local.Modifiers.Any(SyntaxKind.AsyncKeyword);
                    case MethodDeclarationSyntax method:
                        return method.Modifiers.Any(SyntaxKind.AsyncKeyword);
                    case BaseMethodDeclarationSyntax _:
                    case AccessorDeclarationSyntax _:
                        return false;
                }
            }

            return false;
        }

        private static void AnalyzeMethod(SymbolAnalysisContext context)
        {
            var method = (IMethodSymbol)context.Symbol;
            if (!method.IsAsync || !method.ReturnsVoid || method.MethodKind != MethodKind.Ordinary)
            {
                return;
            }

            if (method.Parameters.Length == 2
                && method.Parameters[0].Type.SpecialType == SpecialType.System_Object
                && InheritsFromEventArgs(method.Parameters[1].Type))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(Descriptors.AsyncVoid, method.Locations[0], method.Name));
        }

        private static bool InheritsFromEventArgs(ITypeSymbol type)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                if (t.ToDisplayString() == "System.EventArgs")
                {
                    return true;
                }
            }

            return false;
        }
    }
}
