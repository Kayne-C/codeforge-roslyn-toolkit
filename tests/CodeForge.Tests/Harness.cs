using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace CodeForge.Tests;

public static class AnalyzerVerifier
{
    public static async Task VerifyAsync<TAnalyzer>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    public static async Task VerifyFixAsync<TAnalyzer, TFix>(string source, string fixedSource)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TFix : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}

public sealed class GeneratorRun
{
    public required Compilation Output { get; init; }
    public required ImmutableArray<Diagnostic> GeneratorDiagnostics { get; init; }
    public required GeneratorDriverRunResult Result { get; init; }

    public IEnumerable<Diagnostic> OutputErrors => Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);

    public IEnumerable<Diagnostic> GeneratorDiagnosticsOf(string id) => GeneratorDiagnostics.Where(d => d.Id == id);

    public string GeneratedSource(string hintContains)
        => Result.GeneratedTrees.Single(t => t.FilePath.Contains(hintContains)).GetText().ToString();

    public Assembly Load()
    {
        Assert.Empty(OutputErrors);
        using var stream = new MemoryStream();
        var emit = Output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;
        return new AssemblyLoadContext(Guid.NewGuid().ToString(), isCollectible: true).LoadFromStream(stream);
    }
}

public static class GeneratorHarness
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "mscorlib.dll" or "netstandard.dll" or "Microsoft.CSharp.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))]);

    public static CSharpCompilation CreateCompilation(string source, string assemblyName = "TestAssembly", params string[] more)
        => CSharpCompilation.Create(
            assemblyName,
            new[] { source }.Concat(more).Select((text, i) => CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest), path: $"Test{i}.cs")),
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    public static GeneratorRun Run(string source, params IIncrementalGenerator[] generators)
    {
        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(generators.Select(g => g.AsSourceGenerator()), parseOptions: new CSharpParseOptions(LanguageVersion.Latest));
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return new GeneratorRun { Output = output, GeneratorDiagnostics = diagnostics, Result = driver.GetRunResult() };
    }

    public static GeneratorRun Run(string source, IIncrementalGenerator generator) => Run(source, [generator]);
}
