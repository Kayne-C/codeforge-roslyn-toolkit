using Xunit;

namespace CodeForge.Tests;

public class ArchitectureAnalyzerTests
{
    private const string Attribute = """
        namespace CodeForge
        {
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            internal sealed class ForbidDependencyAttribute : System.Attribute
            {
                public ForbidDependencyAttribute(string from, string to) { }
            }
        }
        """;

    private const string Rules = """
        [assembly: CodeForge.ForbidDependency("Shop.Domain", "Shop.Infrastructure")]
        [assembly: CodeForge.ForbidDependency("Shop.Application", "Shop.Infrastructure")]

        """;

    private const string Layers = """
        namespace Shop.Infrastructure
        {
            public class SqlRepository { public static int Count; public class Inner { } }
            public class Repository<T> { }
        }

        namespace Shop.Domain
        {
            public class Order { }
        }
        """;

    [Fact]
    public Task A_forbidden_type_reference_is_reported() => AnalyzerVerifier.VerifyAsync<ArchitectureAnalyzer>(Rules + Attribute + Layers + """
        namespace Shop.Domain.Services
        {
            using Shop.Infrastructure;

            public class PricingService
            {
                private readonly {|CF0100:SqlRepository|} _repository = new {|CF0100:SqlRepository|}();
            }
        }
        """);

    [Fact]
    public Task A_fully_qualified_reference_is_reported_once() => AnalyzerVerifier.VerifyAsync<ArchitectureAnalyzer>(Rules + Attribute + Layers + """
        namespace Shop.Domain
        {
            public class Pricing
            {
                private readonly {|CF0100:Shop.Infrastructure.SqlRepository|} _repository = null!;
            }
        }
        """);

    [Fact]
    public Task Generic_arguments_and_open_generics_are_checked() => AnalyzerVerifier.VerifyAsync<ArchitectureAnalyzer>(Rules + Attribute + Layers + """
        namespace Shop.Application
        {
            using System.Collections.Generic;
            using Shop.Infrastructure;

            public class Handler
            {
                private readonly List<{|CF0100:SqlRepository|}> _items = new();
                private readonly {|CF0100:Repository<int>|} _generic = new();
            }
        }
        """);

    [Fact]
    public Task Sub_namespaces_of_the_source_layer_are_covered() => AnalyzerVerifier.VerifyAsync<ArchitectureAnalyzer>(Rules + Attribute + Layers + """
        namespace Shop.Domain.Orders.Pricing
        {
            using Shop.Infrastructure;

            public class Calculator
            {
                public int Total() => {|CF0100:SqlRepository|}.Count;
            }
        }
        """);

    [Fact]
    public Task Allowed_directions_are_not_reported() => AnalyzerVerifier.VerifyAsync<ArchitectureAnalyzer>(Rules + Attribute + Layers + """
        namespace Shop.Infrastructure.Persistence
        {
            using Shop.Domain;

            public class OrderStore
            {
                public Order? Load() => null;
                public SqlRepository? Repository { get; set; }
            }
        }

        namespace Shop.Application
        {
            using Shop.Domain;

            public class Checkout
            {
                public Order? Order { get; set; }
            }
        }
        """);

    [Fact]
    public Task Code_outside_any_governed_layer_is_ignored() => AnalyzerVerifier.VerifyAsync<ArchitectureAnalyzer>(Rules + Attribute + Layers + """
        namespace Shop.Web
        {
            using Shop.Infrastructure;

            public class Startup
            {
                public SqlRepository? Repository { get; set; }
            }
        }
        """);

    [Fact]
    public Task A_prefix_that_only_looks_similar_does_not_match() => AnalyzerVerifier.VerifyAsync<ArchitectureAnalyzer>(Rules + Attribute + Layers + """
        namespace Shop.DomainExtras
        {
            using Shop.Infrastructure;

            public class Unrelated
            {
                public SqlRepository? Repository { get; set; }
            }
        }
        """);

    [Fact]
    public Task Without_rules_nothing_is_reported() => AnalyzerVerifier.VerifyAsync<ArchitectureAnalyzer>("""
        namespace Shop.Infrastructure { public class SqlRepository { } }
        namespace Shop.Domain
        {
            public class Pricing
            {
                public Shop.Infrastructure.SqlRepository? Repository { get; set; }
            }
        }
        """);

    [Fact]
    public Task Nested_types_of_a_forbidden_namespace_are_reported() => AnalyzerVerifier.VerifyAsync<ArchitectureAnalyzer>(Rules + Attribute + Layers + """
        namespace Shop.Domain
        {
            using Shop.Infrastructure;

            public class Holder
            {
                public {|CF0100:SqlRepository.Inner|}? Value { get; set; }
            }
        }
        """);
}

public class UsageAnalyzerTests
{
    [Fact]
    public Task The_ambient_clock_is_reported() => AnalyzerVerifier.VerifyAsync<UsageAnalyzer>("""
        using System;

        class C
        {
            DateTime A() => {|CF0003:DateTime.UtcNow|};
            DateTime B() => {|CF0003:DateTime.Now|};
            DateTime D() => {|CF0003:DateTime.Today|};
            DateTimeOffset E() => {|CF0003:DateTimeOffset.UtcNow|};
            DateTimeOffset F() => {|CF0003:DateTimeOffset.Now|};
        }
        """);

    [Fact]
    public Task A_time_provider_and_plain_dates_are_fine() => AnalyzerVerifier.VerifyAsync<UsageAnalyzer>("""
        using System;

        class C
        {
            DateTimeOffset A(TimeProvider time) => time.GetUtcNow();
            DateTime B() => DateTime.MinValue;
            DateTime D() => new DateTime(2026, 1, 1);
        }
        """);

    [Fact]
    public Task New_http_client_per_call_is_reported() => AnalyzerVerifier.VerifyAsync<UsageAnalyzer>("""
        using System.Net.Http;

        class C
        {
            readonly HttpClient _field = {|CF0004:new HttpClient()|};

            void M()
            {
                using var client = {|CF0004:new HttpClient()|};
            }
        }
        """);

    [Fact]
    public Task A_shared_static_client_is_allowed() => AnalyzerVerifier.VerifyAsync<UsageAnalyzer>("""
        using System.Net.Http;

        class C
        {
            static readonly HttpClient Shared = new HttpClient();
            static HttpClient Property { get; } = new HttpClient();
            static readonly HttpClient FromConstructor;

            static C()
            {
                FromConstructor = new HttpClient();
            }
        }
        """);
}
