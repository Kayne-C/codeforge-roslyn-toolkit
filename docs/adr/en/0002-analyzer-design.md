# ADR 0002 — Analyzer design

**Status:** Accepted

- **Operations over syntax where meaning matters.** `.Result`, `Wait()`, `Thread.Sleep`, `DateTime.UtcNow` and `new HttpClient()` are found through `IOperation` actions, so the rule is about the resolved member, not the spelling. A property named `Result` on an unrelated type is never reported.
- **Syntax where structure matters.** The architecture rule works on names (`IdentifierName`, `GenericName`, `QualifiedName`) and resolves each with the semantic model, because the report must point at the exact reference in the source.
- **Context from syntax, not from the containing symbol.** Whether a call sits in an `async` method is decided by walking the syntax ancestors (method, local function, lambda). The operation's containing symbol is the member, not the lambda, which hid `Thread.Sleep` inside async lambdas until a test caught it.
- **No overlap with built-in rules.** Forwarding a `CancellationToken` is covered by `CA2016`, so it is not duplicated.
- **Severity is the user's choice.** Descriptors have sensible defaults; `.editorconfig` overrides them.
- **Code fixes only when they are safe.** `.Result` / `Wait()` / `GetAwaiter().GetResult()` are fixed only inside an `async` context, and parentheses are added only when the surrounding expression needs them. A test asserts that no fix is offered in a non-async method.
- Analyzers skip generated code and run concurrently.
