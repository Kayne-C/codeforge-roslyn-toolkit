# ADR 0001 — Incremental generators on value-equatable models

**Status:** Accepted

## Context

A source generator runs on every keystroke in the IDE. A generator that re-emits everything, or that keeps syntax nodes and symbols alive in its pipeline, makes the editor slow and leaks compilations.

## Decision

- Every generator is an `IIncrementalGenerator` using `ForAttributeWithMetadataName`, which lets Roslyn filter by attribute before the generator sees a node.
- The pipeline's transform step turns symbols into a **small immutable model** (records plus `EquatableArray<T>`). Roslyn compares models with `Equals`; if the model did not change, the emit step is skipped and the previous output is reused.
- Nothing that references a compilation is stored in a model. Diagnostics are captured as plain data (`DiagnosticInfo`: id, message, file path, spans) and turned into `Diagnostic` objects in the output step.
- Attribute declarations are emitted with `RegisterPostInitializationOutput` and are `internal`, so two projects using CodeForge do not clash and nothing is needed at runtime.
- Generated hint names are derived from the type's namespace and name, so a rename removes the old file instead of leaving a duplicate.

## Consequences

- Tests assert the behaviour directly: with step tracking enabled, an identical compilation yields only `Cached`/`Unchanged` outputs, an unrelated edit regenerates nothing, and one changed declaration changes one output.
- Models must stay free of `ISymbol`, `SyntaxNode` and `Location`. A new field that is not value-equatable would silently break caching, which is why the caching tests are part of the suite.
