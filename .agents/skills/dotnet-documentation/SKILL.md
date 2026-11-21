---
name: dotnet-documentation
description: Documentation standards for .NET and C# code. Use when asked to document C# or .NET code, write or review XML documentation comments (///), add or clean up code comments, review documentation quality, or write a README for a set of types.
---

# Documentation approach

Documentation must help a reader understand the code and use it correctly. Comments must earn their place. Prefer clear names and clear code before adding an explanation. Write for a developer who understands C# and the business domain, but has not read this implementation.

## Comments

1. Do not duplicate the code.
2. Do not use comments to excuse unclear code.
3. If a clear comment is difficult to write, examine the code's design.
4. Dispel confusion. Do not introduce new ambiguity.
5. Explain necessary, unidiomatic code and surprising constraints.
6. Link to the original source of copied code.
7. Place external references where they help the reader understand a decision.
8. When fixing a bug, explain the non-obvious constraint that prevents it from returning. Keep the regression test as evidence. Skip commentary on obvious fixes.
9. Mark incomplete implementations. State the limitation and its effect. Link to a tracking issue when one exists.

Keep an actionable explanation beside the affected code. Keep experiment logs, benchmark details, rejected approaches, and implementation history out of code comments and XML documentation comments. Put that material in a separate document, for example under `docs/`, or in the commit message or pull request. Comments must stand on their own. Do not refer to temporary files, chat history, or agent instructions.

```csharp
// Read the shared string table before the worksheet.
// Cells store indexes into that table, so a cell cannot be decoded without it.
```

## XML documentation comments

Use `///` XML documentation comments for public APIs. Use `<summary>` to describe the API. Add `<param>`, `<typeparam>`, `<returns>`, `<value>`, `<exception>`, and `<remarks>` as applicable when they explain the contract. Use `<inheritdoc/>` when inherited documentation accurately describes the contract. Do not repeat information already clear from names and type declarations. See [Recommended XML tags](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/recommended-tags) on Microsoft Learn.

Internal helpers may use a short documentation comment or no comment when the code is self-explanatory. Complex internal operations deserve a fuller contract. Document relevant preconditions, side effects, resource ownership, failure behavior, retry guarantees, and limitations. Put shared context on the containing type where possible. Use a focused README for context that spans several types.

## Language and maintenance

Use an adapted Simplified Technical English style. Use active voice, short sentences, concrete subjects, and consistent terms. Keep C# identifiers and necessary domain terms precise. Do not require the ASD-STE100 word list.

Prefer literal, descriptive terms over metaphors or invented terminology. Use specialized terms only when they identify a distinct concept, and explain them at first use. Do not shorten sentences by omitting the subject, action, condition, or relationship needed to understand them.

Separate rules the caller must follow from implementation details. Explain what happens, under which conditions, and why important constraints exist. Replace dense explanations with a short sequence or a useful example. Preserve the reason for important ordering, concurrency, and data-integrity constraints when moving code. Update or remove documentation when behavior changes.

During review, prioritize misleading documentation, missing contracts, and undocumented constraints. Avoid mechanical expansion of XML documentation comments or comments on every statement. If documentation exposes unclear code, report the design problem.
