using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CodeForge.Tests;

public class StronglyTypedIdTests
{
    private static Assembly Compile(string source)
    {
        var run = GeneratorHarness.Run(source, new StronglyTypedIdGenerator());
        Assert.Empty(run.GeneratorDiagnostics);
        return run.Load();
    }

    private const string Guid = """
        using CodeForge;
        namespace Shop;
        [StronglyTypedId]
        public readonly partial struct OrderId;
        """;

    [Fact]
    public void Guid_ids_are_created_compared_and_formatted()
    {
        var type = Compile(Guid).GetType("Shop.OrderId")!;
        var a = type.GetMethod("New")!.Invoke(null, null)!;
        var b = type.GetMethod("New")!.Invoke(null, null)!;
        var empty = type.GetProperty("Empty")!.GetValue(null)!;

        Assert.NotEqual(a, b);
        Assert.True((bool)type.GetProperty("IsEmpty")!.GetValue(empty)!);
        Assert.False((bool)type.GetProperty("IsEmpty")!.GetValue(a)!);
        Assert.Equal(((System.Guid)type.GetProperty("Value")!.GetValue(a)!).ToString(), a.ToString());
        Assert.True(a.Equals(a));
        Assert.Equal(a.GetHashCode(), a.GetHashCode());
    }

    [Fact]
    public void Ids_of_different_types_cannot_be_mixed()
    {
        var run = GeneratorHarness.Run("""
            using CodeForge;
            [StronglyTypedId] public readonly partial struct OrderId;
            [StronglyTypedId] public readonly partial struct CustomerId;
            class Use
            {
                void M(OrderId order, CustomerId customer)
                {
                    bool same = order == customer;
                }
            }
            """, new StronglyTypedIdGenerator());

        Assert.Contains(run.OutputErrors, d => d.Id == "CS0019");
    }

    [Fact]
    public void Parsing_round_trips_and_rejects_garbage()
    {
        var assembly = Compile("""
            using CodeForge;
            using System;
            namespace Shop;
            [StronglyTypedId(typeof(long))]
            public readonly partial struct CustomerId;
            public static class Probe
            {
                public static T Parse<T>(string text) where T : IParsable<T> => T.Parse(text, null);
                public static bool TryParse<T>(string text, out T value) where T : IParsable<T> => T.TryParse(text, null, out value);
                public static object ParseCustomer(string text) => Parse<CustomerId>(text);
                public static bool TryCustomer(string text) => TryParse<CustomerId>(text, out _);
            }
            """);
        var probe = assembly.GetType("Shop.Probe")!;

        var parsed = probe.GetMethod("ParseCustomer")!.Invoke(null, ["12345"])!;
        Assert.Equal("12345", parsed.ToString());
        Assert.True((bool)probe.GetMethod("TryCustomer")!.Invoke(null, ["7"])!);
        Assert.False((bool)probe.GetMethod("TryCustomer")!.Invoke(null, ["seven"])!);
        var failure = Assert.Throws<TargetInvocationException>(() => probe.GetMethod("ParseCustomer")!.Invoke(null, ["x"]));
        Assert.IsType<FormatException>(failure.InnerException);
    }

    [Fact]
    public void Json_uses_the_underlying_value_including_dictionary_keys()
    {
        var assembly = Compile("""
            using CodeForge;
            using System.Collections.Generic;
            namespace Shop;
            [StronglyTypedId] public readonly partial struct OrderId;
            [StronglyTypedId(typeof(int))] public readonly partial struct LineNo;
            public class Dto
            {
                public OrderId Order { get; set; }
                public LineNo Line { get; set; }
                public Dictionary<OrderId, string> Notes { get; set; } = new();
            }
            """);
        var dto = Activator.CreateInstance(assembly.GetType("Shop.Dto")!)!;
        var order = assembly.GetType("Shop.OrderId")!.GetMethod("New")!.Invoke(null, null)!;
        dto.GetType().GetProperty("Order")!.SetValue(dto, order);
        dto.GetType().GetProperty("Line")!.SetValue(dto, Activator.CreateInstance(assembly.GetType("Shop.LineNo")!, 7));
        var notes = dto.GetType().GetProperty("Notes")!.GetValue(dto)!;
        notes.GetType().GetProperty("Item")!.SetValue(notes, "first", [order]);

        var json = JsonSerializer.Serialize(dto, dto.GetType());
        var back = JsonSerializer.Deserialize(json, dto.GetType())!;

        Assert.Contains($"\"Order\":\"{order}\"", json);
        Assert.Contains("\"Line\":7", json);
        Assert.Contains($"\"{order}\":\"first\"", json);
        Assert.Equal(order, dto.GetType().GetProperty("Order")!.GetValue(back));
        Assert.Equal("first", ((System.Collections.IDictionary)dto.GetType().GetProperty("Notes")!.GetValue(back)!)[order]);
    }

    [Fact]
    public void The_type_converter_supports_string_conversion_for_model_binding()
    {
        var assembly = Compile(Guid);
        var type = assembly.GetType("Shop.OrderId")!;
        var attribute = type.GetCustomAttribute<TypeConverterAttribute>();
        Assert.NotNull(attribute);
        var converterType = assembly.GetType(attribute.ConverterTypeName.Split(',')[0])!;
        var converter = (TypeConverter)Activator.CreateInstance(converterType)!;
        var id = type.GetMethod("New")!.Invoke(null, null)!;

        Assert.True(converter.CanConvertFrom(typeof(string)));
        Assert.True(converter.CanConvertTo(typeof(string)));
        Assert.Equal(id, converter.ConvertFromInvariantString(id.ToString()!));
        Assert.Equal(id.ToString(), converter.ConvertToInvariantString(id));
    }

    [Fact]
    public void Int_ids_compare_by_value_and_format_with_the_invariant_culture()
    {
        var assembly = Compile("""
            using CodeForge;
            namespace Shop;
            [StronglyTypedId(typeof(int))]
            public readonly partial struct LineNo;
            public static class Probe
            {
                public static bool Less(int a, int b) => new LineNo(a) < new LineNo(b);
                public static bool Equal(int a, int b) => new LineNo(a) == new LineNo(b);
                public static string Text(int a) => new LineNo(a).ToString();
                public static int Explicit(LineNo no) => (int)no;
            }
            """);
        var probe = assembly.GetType("Shop.Probe")!;

        Assert.True((bool)probe.GetMethod("Less")!.Invoke(null, [1, 2])!);
        Assert.False((bool)probe.GetMethod("Less")!.Invoke(null, [2, 1])!);
        Assert.True((bool)probe.GetMethod("Equal")!.Invoke(null, [3, 3])!);
        Assert.Equal("1234567", probe.GetMethod("Text")!.Invoke(null, [1234567]));
    }

    [Fact]
    public void String_ids_reject_null_and_compare_ordinally()
    {
        var assembly = Compile("""
            using CodeForge;
            namespace Shop;
            [StronglyTypedId(typeof(string))]
            public readonly partial struct Sku;
            public static class Probe
            {
                public static Sku Make(string value) => new Sku(value);
                public static bool Same(string a, string b) => new Sku(a) == new Sku(b);
                public static int Order(string a, string b) => new Sku(a).CompareTo(new Sku(b));
            }
            """);
        var probe = assembly.GetType("Shop.Probe")!;

        Assert.True((bool)probe.GetMethod("Same")!.Invoke(null, ["AB-1", "AB-1"])!);
        Assert.False((bool)probe.GetMethod("Same")!.Invoke(null, ["AB-1", "ab-1"])!);
        Assert.True((int)probe.GetMethod("Order")!.Invoke(null, ["A", "B"])! < 0);
        var failure = Assert.Throws<TargetInvocationException>(() => probe.GetMethod("Make")!.Invoke(null, [null]));
        Assert.IsType<ArgumentNullException>(failure.InnerException);
    }

    [Fact]
    public void The_default_value_of_a_string_id_is_usable()
    {
        var assembly = Compile("""
            using CodeForge;
            namespace Shop;
            [StronglyTypedId(typeof(string))]
            public readonly partial struct Sku;
            public static class Probe
            {
                public static string Text() => default(Sku).ToString();
                public static bool EqualsEmpty() => default(Sku) == new Sku("");
            }
            """);
        var probe = assembly.GetType("Shop.Probe")!;

        Assert.Equal(string.Empty, probe.GetMethod("Text")!.Invoke(null, null));
        Assert.True((bool)probe.GetMethod("EqualsEmpty")!.Invoke(null, null)!);
    }

    [Fact]
    public void Ids_in_the_global_namespace_and_with_internal_accessibility_work()
    {
        var run = GeneratorHarness.Run("""
            using CodeForge;
            [StronglyTypedId]
            internal readonly partial struct LocalId;
            """, new StronglyTypedIdGenerator());

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Contains("internal readonly partial struct LocalId", run.GeneratedSource("LocalId"));
        Assert.Empty(run.OutputErrors);
    }

    [Fact]
    public void A_type_that_is_not_partial_is_rejected_with_a_clear_error()
    {
        var run = GeneratorHarness.Run("""
            using CodeForge;
            [StronglyTypedId]
            public readonly struct OrderId { }
            """, new StronglyTypedIdGenerator());

        var diagnostic = Assert.Single(run.GeneratorDiagnosticsOf("CFG001"));
        Assert.Contains("partial struct", diagnostic.GetMessage());
        Assert.DoesNotContain(run.Result.GeneratedTrees, t => t.FilePath.Contains("OrderId"));
    }

    [Fact]
    public void A_nested_type_is_rejected()
    {
        var run = GeneratorHarness.Run("""
            using CodeForge;
            public class Outer
            {
                [StronglyTypedId]
                public readonly partial struct InnerId;
            }
            """, new StronglyTypedIdGenerator());

        Assert.Single(run.GeneratorDiagnosticsOf("CFG001"));
    }

    [Fact]
    public void An_unsupported_backing_type_is_rejected()
    {
        var run = GeneratorHarness.Run("""
            using CodeForge;
            [StronglyTypedId(typeof(decimal))]
            public readonly partial struct Money;
            """, new StronglyTypedIdGenerator());

        var diagnostic = Assert.Single(run.GeneratorDiagnosticsOf("CFG002"));
        Assert.Contains("decimal", diagnostic.GetMessage());
    }

    [Fact]
    public void Generated_code_compiles_without_warnings_under_nullable_analysis()
    {
        var run = GeneratorHarness.Run(Guid, new StronglyTypedIdGenerator());

        Assert.Empty(run.Output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }
}
