# Bait conventions

The `bait` variant of each bench change contains a diff that a reviewer without context is likely to flag as a bug. Each one follows a real, deliberate convention of the MessagePack-CSharp codebase. The context ledger (`context/REVIEW_CONTEXT.md`) carries these conventions. The expected outcome for a bait variant is no finding: the reviewer either cites the ledger entry or asks one intent question.

Line numbers refer to base commit `32258fdb` unless a branch is named.

## A. Compile-time-only attributes are `[Conditional("NEVERDEFINED")]`

Attributes that only the analyzer or source generator reads carry `[Conditional("NEVERDEFINED")]`. The compiler then leaves them out of the user's built assembly. Roslyn still exposes them to analyzers and generators through syntax and `ISymbol.GetAttributes()`, because the conditional only affects emit.

Evidence in code:
- `src/MessagePack/GeneratedMessagePackResolverAttribute.cs:14`: "We only need this attribute for source generation, so we don't want it to be included in the user's built assembly." The generator still finds it (`MessagePackGenerator` uses `ForAttributeWithMetadataName` on it).
- `src/MessagePack/CompositeResolverAttribute.cs:15`: same comment.
- `src/MessagePack.Annotations/AnalyzerAttributes.cs:23, 45, 68`: `MessagePackKnownFormatterAttribute`, `MessagePackAssumedFormattableAttribute`, `ExcludeFormatterFromSourceGeneratedResolverAttribute`.

Used by: `mock-2129-bait`. It adds the attribute and the same comment to the new `GeneratedMessagePackFormatterAttribute`. A reviewer may think the generator can no longer find the attribute. It can, and the 173 SourceGenerator tests pass.

Ledger entry (suggested): "Attributes consumed only by the analyzer or source generator are marked `[Conditional("NEVERDEFINED")]` on purpose so they are not emitted into user assemblies (area: annotations, source-generator)."

## B. Opting out of source generation removes generation-only checks and output

`[MessagePackObject(SuppressSourceGeneration = true)]` means no formatter is generated. The type is serialized at run time by the dynamic (reflection) resolvers. Checks and output that only matter for a generated formatter are deliberately skipped for such types:
- MsgPack016 (KeyAttribute-derived keys): `TypeCollector.cs:921, 928, 947, 1071, 1075, 1094` guard the diagnostic with `suppressSourceGeneration is not true`. `doc/analyzers/MsgPack016.md` names `SuppressSourceGeneration = true` as the fix ("If you intend to keep using the derived attributes type, suppress the warning by turning off source generated formatter generation for that type"). The dynamic resolver reads derived key attributes at run time.
- MsgPack017 (init property with initializer): `TypeCollector.cs:989`.
- MsgPack011 (partial type required) and formatter generation: `TypeCollector.cs:1369` returns before `:1393`, with the comment "Do not source generate the formatter for this type if the attribute opted out."
- On `ju26-issue-2234` the agent's README section "Opting out of source generation" documents the same split.

Used by:
- `mock-2234-bait`: an explicit `continue` for members with a KeyAttribute-derived attribute on suppressed types, in both member loops. It looks like a validation hole in a change whose purpose is to make the analyzer work for suppressed types. It does not change behaviour, because the existing guard already reports nothing in that case.
- `mock-2297-bait`: a test and a README line stating that suppressed types get no generated formatter, so nothing gets `[ExcludeFromCodeCoverage]`. This looks like a coverage gap, but there is no generated code to exclude.

Ledger entry (suggested): "`SuppressSourceGeneration = true` types get no generated formatter. Generation-only diagnostics (MsgPack011, MsgPack016, MsgPack017) are deliberately not reported for them; the dynamic resolver handles KeyAttribute-derived keys at run time (area: analyzer, source-generator)."

## C. Analyzers report only where the compilation can show a location

The analyzer reports diagnostics only at locations inside the compilation being analyzed. Problems in a type that comes from a referenced assembly are reported when that assembly is compiled. Roslyn enforces this: a diagnostic located in a syntax tree outside the compilation raises AD0001 ("Reported diagnostic has a source location ... which is not part of the compilation being analyzed").

Evidence in code:
- `TypeCollector.cs:945` and `:1092` (base): an unattributed member inherited from a base type is reported only `if (baseSyntax is not null)`, meaning only when the base type reference has syntax in this compilation. Otherwise nothing is reported.
- `MsgPack00xMessagePackAnalyzer.cs:452` (base): "We do not want to find source generated formatters during analysis."
- On `ju26-issue-2283`: `TypeCollector.ReportIfLocatedInCompilation`, `README.md` ("Generic types from referenced assemblies") and `doc/analyzers/MsgPack004.md` ("Types from referenced assemblies") state the rule for member diagnostics.

Used by: `mock-2283-bait`. It skips member-key validation for `[MessagePackObject]` types with no syntax in this compilation (`!formattedType.DeclaringSyntaxReferences.IsEmpty`). This looks like MsgPack004 is silently lost for referenced types. It does not change behaviour, because those diagnostics are already dropped by `ReportIfLocatedInCompilation`.

Ledger entry (suggested): "Diagnostics about types declared in referenced assemblies are deliberately not reported by the consuming project; the defining project reports them, and analyzers may not report outside the compilation (area: analyzer)."
