using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace CodeForge
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ArchitectureAnalyzer : DiagnosticAnalyzer
    {
        internal const string AttributeName = "CodeForge.ForbidDependencyAttribute";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(Descriptors.ForbiddenDependency, Descriptors.InvalidRule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
            {
                var rules = ReadRules(start.Compilation);
                start.RegisterSyntaxNodeAction(ctx => AnalyzeName(ctx, rules), SyntaxKind.IdentifierName, SyntaxKind.GenericName, SyntaxKind.QualifiedName, SyntaxKind.AliasQualifiedName);
            });
        }

        private sealed class Rule
        {
            public Rule(string from, string to)
            {
                From = from;
                To = to;
            }

            public string From { get; }

            public string To { get; }

            public bool AppliesTo(string ns) => NamespaceMatches(ns, From);

            public bool Forbids(string ns) => NamespaceMatches(ns, To);

            private static bool NamespaceMatches(string ns, string prefix) =>
                ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal);
        }

        private static List<Rule> ReadRules(Compilation compilation)
        {
            var rules = new List<Rule>();
            foreach (var attribute in compilation.Assembly.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != AttributeName || attribute.ConstructorArguments.Length != 2)
                {
                    continue;
                }

                var from = attribute.ConstructorArguments[0].Value as string;
                var to = attribute.ConstructorArguments[1].Value as string;
                if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                {
                    continue;
                }

                rules.Add(new Rule(from!.Trim(), to!.Trim()));
            }

            return rules;
        }

        private static void AnalyzeName(SyntaxNodeAnalysisContext context, List<Rule> rules)
        {
            if (rules.Count == 0)
            {
                return;
            }

            var node = context.Node;
            if ((node is IdentifierNameSyntax || node is GenericNameSyntax) && node.Parent is QualifiedNameSyntax qualified && qualified.Right == node)
            {
                return;
            }

            if (node is IdentifierNameSyntax && node.Parent is MemberAccessExpressionSyntax access && access.Name == node)
            {
                return;
            }

            if (node.Parent is QualifiedNameSyntax parentQualified && parentQualified.Left == node)
            {
                return;
            }

            if (node.Ancestors().Any(a => a is UsingDirectiveSyntax))
            {
                return;
            }

            var inside = context.ContainingSymbol?.ContainingNamespace?.ToDisplayString();
            if (inside == null)
            {
                inside = FindNamespaceOfNode(node);
            }

            if (string.IsNullOrEmpty(inside))
            {
                return;
            }

            var applicable = rules.Where(r => r.AppliesTo(inside!)).ToList();
            if (applicable.Count == 0)
            {
                return;
            }

            var symbol = context.SemanticModel.GetSymbolInfo(node, context.CancellationToken).Symbol;
            if (symbol == null)
            {
                return;
            }

            var target = TargetNamespace(symbol);
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            foreach (var rule in applicable)
            {
                if (rule.Forbids(target!) && !rule.AppliesTo(target!))
                {
                    var written = node.ToString();
                    context.ReportDiagnostic(Diagnostic.Create(Descriptors.ForbiddenDependency, node.GetLocation(), inside, written, rule.From, rule.To));
                    return;
                }
            }
        }

        private static string? TargetNamespace(ISymbol symbol)
        {
            switch (symbol)
            {
                case INamespaceSymbol ns:
                    return ns.IsGlobalNamespace ? null : ns.ToDisplayString();
                case ITypeSymbol type:
                    return type.ContainingNamespace?.IsGlobalNamespace == false ? type.ContainingNamespace.ToDisplayString() : null;
                case IMethodSymbol method when method.MethodKind == MethodKind.Constructor:
                    return method.ContainingType.ContainingNamespace?.ToDisplayString();
                default:
                    return null;
            }
        }

        private static string? FindNamespaceOfNode(SyntaxNode node)
        {
            var parts = new List<string>();
            for (var current = node.Parent; current != null; current = current.Parent)
            {
                if (current is BaseNamespaceDeclarationSyntax declaration)
                {
                    parts.Insert(0, declaration.Name.ToString());
                }
            }

            return parts.Count == 0 ? null : string.Join(".", parts);
        }
    }
}
