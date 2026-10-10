# CodeForge — Roslyn Analyzers, Code Fixes and Source Generators for Enterprise .NET

**English** · [Türkçe](README.tr.md)

A single NuGet package (`analyzers/dotnet/cs`, no runtime dependency) that adds compile-time tooling to a .NET codebase:
**incremental source generators** that remove boilerplate, and **analyzers with code fixes** that catch async and architecture mistakes while you type.

## Install

```bash
dotnet add package CodeForge
```

The attributes (`[StronglyTypedId]`, `[EnumExtensions]`, `[MapTo]`, `[assembly: ForbidDependency]`) are emitted into your own assembly as `internal` types, so there is nothing to reference at runtime.
Requires an SDK with Roslyn 4.11 or newer (.NET 8.0.4xx SDK / Visual Studio 17.11 and later).

## Source generators

| Generator | You write | You get |
|---|---|---|
| **Strongly typed IDs** | `[StronglyTypedId] public readonly partial struct OrderId;` (backing `Guid` by default, or `int`, `long`, `string`) | A value-semantics struct: `New()`, `From()`, equality and ordering operators, `IParsable<T>`, `System.Text.Json` converter (including dictionary keys), `TypeConverter` for model binding. `OrderId` and `CustomerId` can no longer be mixed up |
| **Compile-time mappers** | `[MapTo(typeof(OrderDto))] public class Order { … }` | `order.ToOrderDto()`: constructors and initializers chosen automatically, nested mapped types, `List`/array/`IEnumerable` collections, numeric widening, matching enums, nullable handling, `[MapIgnore]`, `[MapProperty("Other")]`. A member that cannot be mapped is a **compile error**, not a runtime surprise |
| **Enum extensions** | `[EnumExtensions] public enum Status { … }` | `ToStringFast()`, `ToDescription()` (from `[Description]`), `TryParseFast()`, `IsDefinedFast()`, `GetNames()`, `GetValues()` with no reflection |

```csharp
[StronglyTypedId] public readonly partial struct OrderId;

[EnumExtensions]
public enum OrderStatus { [Description("Waiting for payment")] Pending = 1, Paid = 2 }

[MapTo(typeof(OrderDto))]
public class Order { public OrderId Id { get; init; } public OrderStatus Status { get; init; } public List<OrderLine> Lines { get; init; } = new(); }

var dto = order.ToOrderDto();            // generated
var text = dto.Status.ToDescription();   // generated
```

A runnable example is in [samples/CodeForge.Sample](samples/CodeForge.Sample).

### Mapper diagnostics

| Id | Severity | Meaning |
|---|---|---|
| `CFG101` | Warning | A destination member has no source; it keeps its default. A `required` member or constructor parameter without a source is not generated at all |
| `CFG102` | Error | Two members cannot be mapped (for example `string` → `int`, or enums with different members). Add a `[MapTo]` between the types or `[MapIgnore]` the member |
| `CFG103` | Warning | A nullable source feeds a non-nullable destination |
| `CFG104` | Error | The destination has no accessible constructor, or the source is nested |

`CFG001`–`CFG003` report invalid `[StronglyTypedId]` / `[EnumExtensions]` targets (not a partial struct, unsupported backing type, nested type).

## Analyzers and code fixes

| Id | Default | Rule | Code fix |
|---|---|---|---|
| `CF0002` | Warning | Do not block on a `Task` (`.Result`, `.Wait()`, `WaitAll/WaitAny`, `GetAwaiter().GetResult()`) | Replaces it with `await` when the enclosing method is `async` |
| `CF0003` | Info | Prefer `TimeProvider` over `DateTime.Now/UtcNow/Today` and `DateTimeOffset.Now/UtcNow` (only offered where `TimeProvider` exists) | – |
| `CF0004` | Warning | Do not `new HttpClient()` per call (static fields, static properties and static constructors are allowed) | – |
| `CF0005` | Warning | Avoid `async void` (event handlers are exempt) | Changes the return type to `Task` |
| `CF0006` | Warning | `Thread.Sleep` inside an `async` method or lambda | Replaces it with `await Task.Delay(...)` |
| `CF0100` | Error | **Architecture rule violated**: a type in one layer references a type in a forbidden layer | – |

Severities are standard Roslyn severities, so `.editorconfig` can change them: `dotnet_diagnostic.CF0003.severity = warning`.

`CF0001` (forwarding a `CancellationToken`) is deliberately absent: the .NET SDK already ships `CA2016` for that.

### Architecture rules

```csharp
[assembly: ForbidDependency("Shop.Domain", "Shop.Infrastructure")]
[assembly: ForbidDependency("Shop.Application", "Shop.Infrastructure")]
```

Any type in `Shop.Domain` (or a sub-namespace) that mentions a type from `Shop.Infrastructure` is reported at the exact reference, including generic arguments, nested types, fully qualified names and static member access.
Namespace matching is on whole segments (`Shop.DomainExtras` is not `Shop.Domain`).

## How it is tested

```bash
dotnet test --solution CodeForge.slnx     # no Docker needed
```

73 tests:

- **Analyzers and fixes:** `Microsoft.CodeAnalysis.Testing` with markup-based expectations (`{|CF0002:task.Result|}`), covering reported cases, near misses that must stay silent, and fix output.
- **Generators:** each generator runs inside a real `CSharpGeneratorDriver`, the output compilation is **emitted and loaded**, and the generated code is exercised at runtime (parsing, JSON round trip, dictionary keys, type converter, mapping results).
  Generated code is also required to compile without warnings under nullable analysis.
- **Incremental behaviour:** with step tracking on, re-running on an identical compilation reuses every output, editing an unrelated file regenerates nothing, and changing one declaration regenerates only that output. Output is byte-for-byte deterministic.
- **Packaging:** the packed `.nupkg` is consumed by a throw-away project in CI and must generate code and raise a diagnostic.

## Design notes

- Generators use `ForAttributeWithMetadataName` and cache on **value-equatable models** (`EquatableArray<T>`, records); diagnostics are stored as plain data instead of `Location`, so nothing holds on to syntax trees or symbols. See [ADR 0001](docs/adr/en/0001-incremental-generators.md).
- Analyzers register operation / symbol / syntax actions with concurrent execution on and skip generated code. See [ADR 0002](docs/adr/en/0002-analyzer-design.md).
- The mapper is explicit by design: nothing is guessed silently. See [ADR 0003](docs/adr/en/0003-mapper-semantics.md).
- Architecture rules live in code as assembly attributes, versioned with the project. See [ADR 0004](docs/adr/en/0004-architecture-rules.md).

## Known limits (an honest list)

- The mapper is intentionally smaller than libraries such as Mapperly: no user-defined converters, no flattening (`Customer.Address.City` → `CustomerCity`), no polymorphic mappings, no `IQueryable` projections, and no dictionaries or immutable collections.
- Strongly typed IDs support top-level `partial struct` only, and `Guid`, `int`, `long`, `string`. There is no EF Core value converter or OpenAPI schema mapping.
- `CF0002` is syntactic about intent: it also reports `.Result` on a task that is already completed, and does not understand `Task.WhenAll(...).Result` as safe. Suppress it where blocking is deliberate (for example a console `Main`).
- `CF0100` checks names that appear in the code of the governed namespaces; it does not check `using` directives, project or package references, reflection, or strings. It is a guard rail, not a replacement for architecture tests that load assemblies.
- The analyzers target Roslyn 4.11 (`netstandard2.0`) and were tested with the .NET 10 SDK; older SDKs were not tried.
- Generator and analyzer **performance on very large solutions was not measured**; only incremental correctness is tested.
- The `CodeForge` NuGet package is built and verified locally and in CI, but **not published** to nuget.org.
