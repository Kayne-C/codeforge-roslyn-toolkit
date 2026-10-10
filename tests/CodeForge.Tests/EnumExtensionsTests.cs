using System.Reflection;
using Xunit;

namespace CodeForge.Tests;

public class EnumExtensionsTests
{
    private static Assembly Compile(string source)
    {
        var run = GeneratorHarness.Run(source, new EnumExtensionsGenerator());
        Assert.Empty(run.GeneratorDiagnostics);
        return run.Load();
    }

    private const string Status = """
        using System.ComponentModel;
        using CodeForge;
        namespace Shop;
        [EnumExtensions]
        public enum Status
        {
            [Description("Waiting for payment")] Pending = 1,
            Paid = 2,
            [Description("Sent to the customer")] Shipped = 3,
        }
        """;

    private static object Value(Assembly assembly, string name) => Enum.Parse(assembly.GetType("Shop.Status")!, name);

    private static object? Call(Assembly assembly, string method, params object?[] args)
        => assembly.GetType("Shop.StatusExtensions")!.GetMethod(method)!.Invoke(null, args);

    [Fact]
    public void Names_and_descriptions_are_resolved_without_reflection()
    {
        var assembly = Compile(Status);

        Assert.Equal("Paid", Call(assembly, "ToStringFast", Value(assembly, "Paid")));
        Assert.Equal("Waiting for payment", Call(assembly, "ToDescription", Value(assembly, "Pending")));
        Assert.Equal("Paid", Call(assembly, "ToDescription", Value(assembly, "Paid")));
    }

    [Fact]
    public void Undefined_values_fall_back_to_the_numeric_text_and_are_detected()
    {
        var assembly = Compile(Status);
        var undefined = Enum.ToObject(assembly.GetType("Shop.Status")!, 99);

        Assert.Equal("99", Call(assembly, "ToStringFast", undefined));
        Assert.False((bool)Call(assembly, "IsDefinedFast", undefined)!);
        Assert.True((bool)Call(assembly, "IsDefinedFast", Value(assembly, "Shipped"))!);
    }

    [Fact]
    public void Parsing_supports_case_insensitive_matching()
    {
        var assembly = Compile(Status);
        var method = assembly.GetType("Shop.StatusExtensions")!.GetMethod("TryParseFast")!;

        var exact = new object?[] { "Shipped", false, null };
        var lower = new object?[] { "shipped", true, null };
        var wrongCase = new object?[] { "shipped", false, null };
        var unknown = new object?[] { "Lost", true, null };

        Assert.True((bool)method.Invoke(null, exact)!);
        Assert.Equal(Value(assembly, "Shipped"), exact[2]);
        Assert.True((bool)method.Invoke(null, lower)!);
        Assert.False((bool)method.Invoke(null, wrongCase)!);
        Assert.False((bool)method.Invoke(null, unknown)!);
    }

    [Fact]
    public void Names_and_values_are_listed_in_declaration_order()
    {
        var assembly = Compile(Status);

        Assert.Equal(new[] { "Pending", "Paid", "Shipped" }, (string[])Call(assembly, "GetNames")!);
        Assert.Equal(new[] { 1, 2, 3 }, ((Array)Call(assembly, "GetValues")!).Cast<object>().Select(v => (int)v).ToArray());
    }

    [Fact]
    public void Aliases_with_the_same_value_do_not_break_the_generated_switches()
    {
        var run = GeneratorHarness.Run("""
            using CodeForge;
            [EnumExtensions]
            public enum Level { Low = 1, Minimum = 1, High = 2 }
            """, new EnumExtensionsGenerator());

        Assert.Empty(run.OutputErrors);
    }

    [Fact]
    public void Flag_combinations_use_the_runtime_formatting()
    {
        var assembly = Compile("""
            using System;
            using CodeForge;
            namespace Shop;
            [EnumExtensions, Flags]
            public enum Status { None = 0, Read = 1, Write = 2 }
            """);
        var combined = Enum.ToObject(assembly.GetType("Shop.Status")!, 3);

        Assert.Equal("Read, Write", Call(assembly, "ToStringFast", combined));
        Assert.Equal("Read", Call(assembly, "ToStringFast", Value(assembly, "Read")));
    }

    [Fact]
    public void Enums_with_a_non_int_underlying_type_and_no_namespace_work()
    {
        var run = GeneratorHarness.Run("""
            using CodeForge;
            [EnumExtensions]
            internal enum Small : byte { A = 1, B = 2 }
            """, new EnumExtensionsGenerator());

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Contains("internal static class SmallExtensions", run.GeneratedSource("Small"));
        Assert.Empty(run.OutputErrors);
    }

    [Fact]
    public void A_nested_enum_is_rejected()
    {
        var run = GeneratorHarness.Run("""
            using CodeForge;
            public class Outer
            {
                [EnumExtensions]
                public enum Inner { A }
            }
            """, new EnumExtensionsGenerator());

        Assert.Single(run.GeneratorDiagnosticsOf("CFG003"));
    }
}
