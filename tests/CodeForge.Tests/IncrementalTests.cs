using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CodeForge.Tests;

public class IncrementalTests
{
    private const string Source = """
        using CodeForge;
        namespace Shop;
        [StronglyTypedId] public readonly partial struct OrderId;
        [EnumExtensions] public enum Status { A = 1, B = 2 }
        [MapTo(typeof(Dto))] public class Entity { public int Id { get; set; } }
        public class Dto { public int Id { get; set; } }
        """;

    private static (GeneratorDriver Driver, Compilation Compilation) Prime(params IIncrementalGenerator[] generators)
    {
        var compilation = GeneratorHarness.CreateCompilation(Source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators.Select(g => g.AsSourceGenerator()),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);
        return (driver, compilation);
    }

    private static IEnumerable<IncrementalStepRunReason> OutputReasons(GeneratorDriver driver)
        => driver.GetRunResult().Results.SelectMany(r => r.TrackedOutputSteps.Values.SelectMany(s => s)).SelectMany(s => s.Outputs).Select(o => o.Reason);

    [Fact]
    public void Re_running_with_an_identical_compilation_reuses_every_output()
    {
        var (driver, compilation) = Prime(new StronglyTypedIdGenerator(), new EnumExtensionsGenerator(), new MapperGenerator());

        driver = driver.RunGenerators(compilation.Clone());

        var reasons = OutputReasons(driver).ToList();
        Assert.NotEmpty(reasons);
        Assert.All(reasons, r => Assert.True(r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, r.ToString()));
    }

    [Fact]
    public void Editing_an_unrelated_file_does_not_regenerate_anything()
    {
        var (driver, compilation) = Prime(new StronglyTypedIdGenerator(), new EnumExtensionsGenerator(), new MapperGenerator());
        var other = CSharpSyntaxTree.ParseText("namespace Other { public class Unrelated { public int X; } }", path: "Other.cs");
        var edited = compilation.AddSyntaxTrees(other);

        driver = driver.RunGenerators(edited);
        var changed = driver.GetRunResult().Results
            .SelectMany(r => r.TrackedOutputSteps.Values.SelectMany(s => s))
            .SelectMany(s => s.Outputs)
            .Count(o => o.Reason is IncrementalStepRunReason.New or IncrementalStepRunReason.Modified);

        Assert.Equal(0, changed);
    }

    [Fact]
    public void Changing_one_declaration_regenerates_only_that_output()
    {
        var (driver, compilation) = Prime(new EnumExtensionsGenerator());
        var tree = compilation.SyntaxTrees.Single(t => t.FilePath == "Test0.cs");
        var edited = compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(Source.Replace("B = 2", "B = 2, C = 3"), path: "Test0.cs"));

        driver = driver.RunGenerators(edited);
        var reasons = OutputReasons(driver).ToList();

        Assert.Contains(IncrementalStepRunReason.Modified, reasons);
    }

    [Fact]
    public void Generated_output_is_deterministic()
    {
        var first = GeneratorHarness.Run(Source, new StronglyTypedIdGenerator(), new EnumExtensionsGenerator(), new MapperGenerator());
        var second = GeneratorHarness.Run(Source, new StronglyTypedIdGenerator(), new EnumExtensionsGenerator(), new MapperGenerator());

        var a = first.Result.GeneratedTrees.OrderBy(t => t.FilePath).Select(t => t.FilePath + t.GetText()).ToList();
        var b = second.Result.GeneratedTrees.OrderBy(t => t.FilePath).Select(t => t.FilePath + t.GetText()).ToList();

        Assert.Equal(a, b);
        Assert.Empty(first.OutputErrors);
    }

    [Fact]
    public void All_generators_together_do_not_collide()
    {
        var run = GeneratorHarness.Run(Source, new StronglyTypedIdGenerator(), new EnumExtensionsGenerator(), new MapperGenerator(), new ArchitectureAttributesGenerator());

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.OutputErrors);
        Assert.Equal(run.Result.GeneratedTrees.Length, run.Result.GeneratedTrees.Select(t => t.FilePath).Distinct().Count());
    }
}
