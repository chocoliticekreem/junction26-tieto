# Upstream comparison (feeds metrics T7)

Checked Sat 14 Nov 2026 with `gh` against MessagePack-CSharp/MessagePack-CSharp. Agent changes are the sample repo branches `origin/ju26-issue-<n>` against base 32258fdb. Diff reading only: nothing in this note was built or run by this track. Items marked (inferred) are my reading of the code.

## Status table

| Issue | Upstream state | Upstream fix or PR | Agent change (sample repo branch) |
|---|---|---|---|
| #2283 closed generic struct crash | open | PR #2305 "Fix IndexOutOfRangeException on closed generic struct from referenced assembly", open, not draft, opened 2026-09-23 by akashchamp, no reviews, only the CLA bot comment, 5 files +261/-3 | `ju26-issue-2283`, 4 files +225/-3 |
| #2234 analyzer off with SuppressSourceGeneration | open, no comments | none. `gh pr list --search 2234` is empty. Search "SuppressSourceGeneration" only finds older merged PRs #2106 and #2103 (lower numbers, merged before the issue was filed; related history, not a fix) | `ju26-issue-2234`, 4 files +262/-4 |
| #2129 generate formatter for type in referenced assembly | open, labels enhancement and source generation, 7 comments | none. Searches for 2129 and `GeneratedMessagePackFormatter` are empty. Design discussion only (contractless default, surrogate type idea) | `ju26-issue-2129`, 28 files +1034/-9 |
| #2297 ExcludeFromCodeCoverage on generated code | open, 2 comments | none. Searches for 2297 and `ExcludeFromCodeCoverage` are empty. A commenter (armadavalor) offered a PR; no maintainer answer | `ju26-issue-2297`, 14 source files +44/-8, plus 223 changed test snapshot files |

## #2283: agent change vs upstream PR #2305

Common ground: both diagnose the same root cause the issue names (`DeclaringSyntaxReferences[0]` indexed on a metadata symbol in `TypeCollector`), both replace the unsafe index with `FirstOrDefault()`, both add a regression test that compiles the library to IL so the referenced type is a real metadata symbol (a same-solution `AdditionalProjects` reference keeps syntax and would not catch it).

| Aspect | Upstream PR #2305 | Agent change |
|---|---|---|
| Files | 5: `TypeCollector.cs` (+5/-3), `GenericsFormatterTests.cs` (+62), three verified snapshot files under `Resources/Generics_Defined_In_ReferencedProject_Struct/` (+194) | 4: `TypeCollector.cs` (+26/-3), `GenericsFormatterTests.cs` (+189), `README.md` (+5), `doc/analyzers/MsgPack004.md` (+5). No snapshot files |
| Production approach | Minimal at-site fixes: `TypeMustBeMessagePackObject` uses the existing `GetIdentifierLocation(formattedType)` helper; `BothStringAndIntKeyAreNull` reads `FirstOrDefault()?.GetSyntax()` and takes the identifier from a `PropertyDeclarationSyntax` or a `ParameterSyntax`; `MemberNeedsKey` uses `FirstOrDefault()?.GetSyntax().GetLocation()`. Diagnostics with no syntax fall back to `Location.None` | Fixes two sites (`BothStringAndIntKeyAreNull` via new `GetPropertyIdentifierLocation`, `MemberNeedsKey` via `FirstOrDefault()`) and adds a policy layer: the constructor wraps `reportDiagnostic` in `ReportIfLocatedInCompilation`, which drops every diagnostic whose location is not in a syntax tree of the current compilation |
| Third site | Touches `CheckValidMessagePackFormatterAttribute` (the `TypeMustBeMessagePackObject` site at line ~735 in the issue's 3.1.8 numbering) | At base 32258fdb that call is commented out (`////`, TypeCollector.cs:715), so there is nothing to fix there. Base has two live `[0]` sites (lines 985 and 1077). The two trees differ, so this is a base-vs-tip difference, not a miss |
| Behaviour for metadata types (inferred) | Diagnostics about members of a referenced type can still be produced, with no location | Never produced; the agent's README and MsgPack004 doc text state "reported by the assembly that defines it". This matches the base convention at TypeCollector.cs:367 (see `notes/bait.md`) but is a wider behaviour change than the issue asks for |
| Positional record parameter (inferred) | Handled: `ParameterSyntax` identifier is used for `BothStringAndIntKeyAreNull` | `GetPropertyIdentifierLocation` returns null for a `ParameterSyntax`, and the new filter then drops the diagnostic, so a null/null `[Key]` on a source positional record parameter would no longer be reported. Worth a bench `edge` or `logic` probe |
| Tests | 1 test, `Generics_Defined_In_ReferencedProject_Struct`, using the repro type shape (`Item<T>` record struct, `Cmd` record); verified-snapshot output of three generated files | 3 tests: `ClosedGenericStruct_FromMetadataReference`, `ClosedGenericRecordStruct_FromMetadataReference` (exact repro), `ClosedGenericStruct_FromMetadataReference_UnkeyedMember`; shared helper asserts no analyzer diagnostics, no generator warnings, and that the resolver hint name is generated (directly guards "silently no resolver") |
| Docs | none | README subsection and MsgPack004 note |
| Review state | open, unreviewed | not a PR; local branch |

Net: same diagnosis. Upstream is the smaller, more conservative diff; the agent adds a broader suppression rule plus docs and stronger assertions (resolver actually generated). The agent's extra filter is the part a maintainer would question, and it is what the bait convention interacts with.

## Notes for the other three

- #2234: no upstream fix to compare. The agent's production change in `TypeCollector.cs` is limited to skipping members after `DoNotMixStringAndIntKeys` and guarding `intMembers.Add`/`stringMembers.Add` against duplicate keys, plus a README section "Opting out of source generation" and analyzer tests under SuppressSourceGeneration. Whether that resolves the reported symptom is for the review to establish; the issue text has no repro.
- #2129: no upstream fix. Agent implements the surrogate-type direction discussed in the thread: `GeneratedMessagePackFormatterAttribute` with `SurrogateType`, contractless default, new `GeneratedFormatterCollector`, analyzers MsgPack019-022, tests and a sample external-types project.
- #2297: no upstream fix. Agent picked reading A: unconditional `[ExcludeFromCodeCoverage]` on all generated formatters, resolvers and the composite resolver, guarded by `#if !NETSTANDARD2_0 && !NETFRAMEWORK`, no setting. Reading B (opt-out setting) and "formatters only" remain the open alternatives.
