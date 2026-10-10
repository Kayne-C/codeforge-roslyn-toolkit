using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CodeForge
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UsageAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(Descriptors.PreferTimeProvider, Descriptors.NewHttpClient);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
            {
                var timeProvider = start.Compilation.GetTypeByMetadataName("System.TimeProvider");
                var httpClient = start.Compilation.GetTypeByMetadataName("System.Net.Http.HttpClient");

                if (timeProvider != null)
                {
                    start.RegisterOperationAction(AnalyzeClock, OperationKind.PropertyReference);
                }

                if (httpClient != null)
                {
                    start.RegisterOperationAction(ctx => AnalyzeHttpClient(ctx, httpClient), OperationKind.ObjectCreation);
                }
            });
        }

        private static void AnalyzeClock(OperationAnalysisContext context)
        {
            var property = ((IPropertyReferenceOperation)context.Operation).Property;
            if (!property.IsStatic)
            {
                return;
            }

            var type = property.ContainingType.ToDisplayString();
            var clock = (type == "System.DateTime" && (property.Name == "Now" || property.Name == "UtcNow" || property.Name == "Today"))
                || (type == "System.DateTimeOffset" && (property.Name == "Now" || property.Name == "UtcNow"));
            if (clock)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptors.PreferTimeProvider, context.Operation.Syntax.GetLocation(), type + "." + property.Name));
            }
        }

        private static void AnalyzeHttpClient(OperationAnalysisContext context, INamedTypeSymbol httpClient)
        {
            var creation = (IObjectCreationOperation)context.Operation;
            if (!SymbolEqualityComparer.Default.Equals(creation.Type, httpClient))
            {
                return;
            }

            if (context.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.StaticConstructor })
            {
                return;
            }

            for (var parent = creation.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is IFieldInitializerOperation field && field.InitializedFields.Length > 0 && field.InitializedFields[0].IsStatic)
                {
                    return;
                }

                if (parent is IPropertyInitializerOperation property && property.InitializedProperties.Length > 0 && property.InitializedProperties[0].IsStatic)
                {
                    return;
                }
            }

            context.ReportDiagnostic(Diagnostic.Create(Descriptors.NewHttpClient, creation.Syntax.GetLocation()));
        }
    }
}
