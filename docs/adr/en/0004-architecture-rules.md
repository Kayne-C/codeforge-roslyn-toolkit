# ADR 0004 — Architecture rules as assembly attributes

**Status:** Accepted

## Context

Layering rules ("the domain must not know about infrastructure") are usually checked by reflection-based tests that run late, or by convention that nobody checks. We want the compiler to refuse the violation at the line where it happens.

## Decision

- Rules are declared in the code they govern: `[assembly: ForbidDependency("Shop.Domain", "Shop.Infrastructure")]`. They are versioned with the project, visible in code review, and need no extra configuration file.
- The analyzer reads the attributes once per compilation and checks every type name written inside a governed namespace. The rule applies to the namespace and its sub-namespaces, matched on whole segments.
- A reference to another type **inside the governed layer** is never a violation; the rule only fires across the boundary.
- The report is an error (`CF0100`) located at the offending name, with both namespaces and the rule in the message.
- `using` directives are not reported on their own; the actual uses are. This avoids two errors for one dependency.

## Consequences

- Fast feedback in the IDE and in CI with no test project to maintain.
- It only sees names in the source of the governed namespaces; project references, packages, reflection and string-based access are out of reach. For those, assembly-loading architecture tests remain the right tool.
