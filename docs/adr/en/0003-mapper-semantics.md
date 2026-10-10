# ADR 0003 — Mapper semantics: explicit, compile-time, no guessing

**Status:** Accepted

## Decision

Mapping is generated from `[MapTo(typeof(TDestination))]` on the source type, as an extension method `To{Destination}()`.

- **Members are matched by name, case-insensitively.** `[MapProperty("Other")]` on a source member renames the match; `[MapIgnore]` removes a member.
- **Construction:** the constructor that can be satisfied by the most source members (and needs no unmatched required parameter) is chosen; remaining settable members use an object initializer.
- **Conversions are a closed list:** identical types; implicit numeric widening; `T` ↔ `T?`; enums with identical members; types that have their own `[MapTo]` for that destination; and `List<T>`, arrays, `IEnumerable<T>`, `IReadOnlyList<T>`, `IList<T>`, `ICollection<T>` of any of those. Anything else is **CFG102, an error**.
- **A missing source is a warning, not silence.** The destination member keeps its default (`CFG101`), and `[MapIgnore]` makes the intent explicit. A `required` member or constructor parameter without a source stops generation, because the result could not compile or would be wrong.
- **Nullability is reported** (`CFG103`) when a nullable source feeds a non-nullable destination.
- A null source throws `ArgumentNullException` rather than producing a half-built object.

## Consequences

- The behaviour is predictable and reviewable: the generated method is plain code.
- The feature set is deliberately smaller than general-purpose mapping libraries (no flattening, no custom converters, no polymorphism); see the README limits.
