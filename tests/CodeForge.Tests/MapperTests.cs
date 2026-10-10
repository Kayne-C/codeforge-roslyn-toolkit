using System.Reflection;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CodeForge.Tests;

public class MapperTests
{
    private static GeneratorRun Run(string source) => GeneratorHarness.Run(source, new MapperGenerator());

    private static object? Invoke(Assembly assembly, string type, string method, params object?[] args)
    {
        try
        {
            return assembly.GetType(type)!.GetMethod(method)!.Invoke(null, args);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }

    [Fact]
    public void Properties_with_the_same_name_are_copied_case_insensitively()
    {
        var run = Run("""
            using CodeForge;
            namespace Shop;
            [MapTo(typeof(PersonDto))]
            public class Person { public string Name { get; set; } = ""; public int AGE { get; set; } public long Id; }
            public class PersonDto { public string name { get; set; } = ""; public int Age { get; set; } public long id; }
            public static class Probe
            {
                public static PersonDto Map() => new Person { Name = "Ada", AGE = 36, Id = 7 }.ToPersonDto();
            }
            """);
        Assert.Empty(run.GeneratorDiagnostics);
        var dto = Invoke(run.Load(), "Shop.Probe", "Map")!;

        Assert.Equal("Ada", dto.GetType().GetProperty("name")!.GetValue(dto));
        Assert.Equal(36, dto.GetType().GetProperty("Age")!.GetValue(dto));
        Assert.Equal(7L, dto.GetType().GetField("id")!.GetValue(dto));
    }

    [Fact]
    public void Positional_records_are_built_through_their_constructor_and_the_rest_through_initializers()
    {
        var run = Run("""
            using CodeForge;
            namespace Shop;
            [MapTo(typeof(OrderView))]
            public class Order { public int Id { get; set; } public string Customer { get; set; } = ""; public decimal Total { get; set; } public string Note { get; set; } = ""; }
            public record OrderView(int Id, string Customer) { public decimal Total { get; init; } public string Note { get; set; } = ""; }
            public static class Probe
            {
                public static OrderView Map() => new Order { Id = 3, Customer = "Acme", Total = 12.5m, Note = "fast" }.ToOrderView();
            }
            """);
        Assert.Empty(run.GeneratorDiagnostics);
        var view = Invoke(run.Load(), "Shop.Probe", "Map")!;

        Assert.Equal(3, view.GetType().GetProperty("Id")!.GetValue(view));
        Assert.Equal("Acme", view.GetType().GetProperty("Customer")!.GetValue(view));
        Assert.Equal(12.5m, view.GetType().GetProperty("Total")!.GetValue(view));
        Assert.Equal("fast", view.GetType().GetProperty("Note")!.GetValue(view));
    }

    [Fact]
    public void Ignored_and_renamed_members_follow_the_attributes()
    {
        var run = Run("""
            using CodeForge;
            namespace Shop;
            [MapTo(typeof(UserDto))]
            public class User
            {
                public string Login { get; set; } = "";
                [MapProperty("DisplayName")] public string FullName { get; set; } = "";
                [MapIgnore] public string PasswordHash { get; set; } = "secret";
            }
            public class UserDto
            {
                public string Login { get; set; } = "";
                public string DisplayName { get; set; } = "";
                public string PasswordHash { get; set; } = "";
            }
            public static class Probe
            {
                public static UserDto Map() => new User { Login = "ada", FullName = "Ada L.", PasswordHash = "x" }.ToUserDto();
            }
            """);
        var dto = Invoke(run.Load(), "Shop.Probe", "Map")!;

        Assert.Equal("Ada L.", dto.GetType().GetProperty("DisplayName")!.GetValue(dto));
        Assert.Equal(string.Empty, dto.GetType().GetProperty("PasswordHash")!.GetValue(dto));
    }

    [Fact]
    public void Nested_mapped_types_and_collections_are_mapped_recursively()
    {
        var run = Run("""
            using System.Collections.Generic;
            using System.Linq;
            using CodeForge;
            namespace Shop;
            [MapTo(typeof(AddressDto))]
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }
            [MapTo(typeof(LineDto))]
            public class Line { public int Quantity { get; set; } }
            public class LineDto { public long Quantity { get; set; } }
            [MapTo(typeof(CustomerDto))]
            public class Customer
            {
                public Address Home { get; set; } = new();
                public Address? Work { get; set; }
                public List<Line> Lines { get; set; } = new();
                public Line[] Archive { get; set; } = new Line[0];
                public IEnumerable<Line> Pending { get; set; } = new List<Line>();
                public List<string> Tags { get; set; } = new();
            }
            public class CustomerDto
            {
                public AddressDto Home { get; set; } = new();
                public AddressDto? Work { get; set; }
                public List<LineDto> Lines { get; set; } = new();
                public LineDto[] Archive { get; set; } = new LineDto[0];
                public IReadOnlyList<LineDto> Pending { get; set; } = new List<LineDto>();
                public string[] Tags { get; set; } = new string[0];
            }
            public static class Probe
            {
                public static CustomerDto Map()
                {
                    var customer = new Customer
                    {
                        Home = new Address { City = "Izmir" },
                        Work = null,
                        Lines = { new Line { Quantity = 2 }, new Line { Quantity = 5 } },
                        Archive = new[] { new Line { Quantity = 9 } },
                        Pending = new[] { new Line { Quantity = 1 } },
                        Tags = { "a", "b" },
                    };
                    return customer.ToCustomerDto();
                }
            }
            """);
        Assert.Empty(run.GeneratorDiagnostics);
        var dto = Invoke(run.Load(), "Shop.Probe", "Map")!;
        T Get<T>(string name) => (T)dto.GetType().GetProperty(name)!.GetValue(dto)!;

        Assert.Equal("Izmir", Get<object>("Home").GetType().GetProperty("City")!.GetValue(Get<object>("Home")));
        Assert.Null(dto.GetType().GetProperty("Work")!.GetValue(dto));
        Assert.Equal(new long[] { 2, 5 }, Get<System.Collections.IEnumerable>("Lines").Cast<object>().Select(l => (long)l.GetType().GetProperty("Quantity")!.GetValue(l)!).ToArray());
        Assert.Single(Get<Array>("Archive"));
        Assert.Single(Get<System.Collections.IEnumerable>("Pending").Cast<object>());
        Assert.Equal(new[] { "a", "b" }, Get<string[]>("Tags"));
    }

    [Fact]
    public void Numeric_widening_nullable_wrapping_and_matching_enums_are_handled()
    {
        var run = Run("""
            using CodeForge;
            namespace Shop;
            public enum Color { Red = 1, Green = 2 }
            public enum Tint { Red = 1, Green = 2 }
            [MapTo(typeof(WideDto))]
            public class Narrow { public int Count { get; set; } public int Score { get; set; } public Color Color { get; set; } public int? Maybe { get; set; } public float Ratio { get; set; } }
            public class WideDto { public long Count { get; set; } public int? Score { get; set; } public Tint Color { get; set; } public int Maybe { get; set; } public double Ratio { get; set; } }
            public static class Probe
            {
                public static WideDto Map() => new Narrow { Count = 5, Score = 9, Color = Color.Green, Maybe = null, Ratio = 0.5f }.ToWideDto();
            }
            """);
        var dto = Invoke(run.Load(), "Shop.Probe", "Map")!;

        Assert.Equal(5L, dto.GetType().GetProperty("Count")!.GetValue(dto));
        Assert.Equal(9, dto.GetType().GetProperty("Score")!.GetValue(dto));
        Assert.Equal("Green", dto.GetType().GetProperty("Color")!.GetValue(dto)!.ToString());
        Assert.Equal(0, dto.GetType().GetProperty("Maybe")!.GetValue(dto));
        Assert.Equal(0.5d, dto.GetType().GetProperty("Ratio")!.GetValue(dto));
        Assert.Contains(run.GeneratorDiagnostics, d => d.Id == "CFG103");
    }

    [Fact]
    public void One_source_can_map_to_several_destinations()
    {
        var run = Run("""
            using CodeForge;
            namespace Shop;
            [MapTo(typeof(SummaryDto)), MapTo(typeof(DetailDto))]
            public class Product { public string Name { get; set; } = ""; public decimal Price { get; set; } }
            public class SummaryDto { public string Name { get; set; } = ""; }
            public class DetailDto { public string Name { get; set; } = ""; public decimal Price { get; set; } }
            public static class Probe
            {
                public static string Both()
                {
                    var p = new Product { Name = "Lamp", Price = 20 };
                    return p.ToSummaryDto().Name + p.ToDetailDto().Price;
                }
            }
            """);

        Assert.Equal("Lamp20", Invoke(run.Load(), "Shop.Probe", "Both"));
    }

    [Fact]
    public void Value_type_sources_are_supported()
    {
        var run = Run("""
            using CodeForge;
            namespace Shop;
            [MapTo(typeof(PointDto))]
            public readonly struct Point { public Point(int x, int y) { X = x; Y = y; } public int X { get; } public int Y { get; } }
            public class PointDto { public int X { get; set; } public int Y { get; set; } }
            public static class Probe
            {
                public static int Sum() { var d = new Point(3, 4).ToPointDto(); return d.X + d.Y; }
            }
            """);

        Assert.Equal(7, Invoke(run.Load(), "Shop.Probe", "Sum"));
    }

    [Fact]
    public void A_null_source_throws_instead_of_producing_a_half_built_object()
    {
        var run = Run("""
            using CodeForge;
            namespace Shop;
            [MapTo(typeof(Dto))]
            public class Entity { public int Id { get; set; } }
            public class Dto { public int Id { get; set; } }
            public static class Probe
            {
                public static Dto Map(Entity? entity) => entity!.ToDto();
            }
            """);

        Assert.IsType<ArgumentNullException>(Record.Exception(() => Invoke(run.Load(), "Shop.Probe", "Map", [null])));
    }

    [Fact]
    public void A_destination_member_without_a_source_is_a_warning_and_keeps_its_default()
    {
        var run = Run("""
            using CodeForge;
            namespace Shop;
            [MapTo(typeof(Dto))]
            public class Entity { public int Id { get; set; } }
            public class Dto { public int Id { get; set; } public string Extra { get; set; } = "default"; }
            public static class Probe
            {
                public static string Extra() => new Entity { Id = 1 }.ToDto().Extra;
            }
            """);

        var warning = Assert.Single(run.GeneratorDiagnosticsOf("CFG101"));
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("Extra", warning.GetMessage());
        Assert.Equal("default", Invoke(run.Load(), "Shop.Probe", "Extra"));
    }

    [Fact]
    public void A_required_destination_member_without_a_source_blocks_generation()
    {
        var run = Run("""
            using CodeForge;
            namespace Shop;
            [MapTo(typeof(Dto))]
            public class Entity { public int Id { get; set; } }
            public class Dto { public int Id { get; set; } public required string Name { get; set; } }
            """);

        Assert.Contains(run.GeneratorDiagnosticsOf("CFG101"), d => d.GetMessage().Contains("Required"));
        Assert.DoesNotContain(run.Result.GeneratedTrees, t => t.FilePath.Contains("Entity.Mapping"));
    }

    [Fact]
    public void Incompatible_member_types_are_a_compile_time_error()
    {
        var run = Run("""
            using CodeForge;
            namespace Shop;
            [MapTo(typeof(Dto))]
            public class Entity { public string Id { get; set; } = ""; }
            public class Dto { public int Id { get; set; } }
            """);

        var error = Assert.Single(run.GeneratorDiagnosticsOf("CFG102"));
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("string", error.GetMessage());
    }

    [Fact]
    public void Enums_with_different_members_are_refused()
    {
        var run = Run("""
            using CodeForge;
            namespace Shop;
            public enum A { X = 1 }
            public enum B { Y = 1 }
            [MapTo(typeof(Dto))]
            public class Entity { public A Kind { get; set; } }
            public class Dto { public B Kind { get; set; } }
            """);

        Assert.Single(run.GeneratorDiagnosticsOf("CFG102"));
    }

    [Fact]
    public void A_destination_without_a_usable_constructor_is_reported()
    {
        var run = Run("""
            using CodeForge;
            namespace Shop;
            [MapTo(typeof(Dto))]
            public class Entity { public int Id { get; set; } }
            public class Dto { private Dto() { } public int Id { get; set; } }
            """);

        Assert.Single(run.GeneratorDiagnosticsOf("CFG104"));
    }

    [Fact]
    public void An_unresolved_destination_type_is_left_to_the_compiler_to_report()
    {
        var run = Run("""
            using CodeForge;
            namespace Shop;
            [MapTo(typeof(MissingDto))]
            public class Entity { public int Id { get; set; } }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Contains(run.OutputErrors, d => d.Id == "CS0246");
    }

    [Fact]
    public void Generated_mappers_compile_without_warnings()
    {
        var run = Run("""
            using System.Collections.Generic;
            using CodeForge;
            namespace Shop;
            [MapTo(typeof(AddressDto))] public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }
            [MapTo(typeof(CustomerDto))] public class Customer { public string Name { get; set; } = ""; public List<Address> Addresses { get; set; } = new(); }
            public class CustomerDto { public string Name { get; set; } = ""; public List<AddressDto> Addresses { get; set; } = new(); }
            """);

        Assert.Empty(run.Output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }
}
