# Seeded bait convention (bench variant `bait`, SPEC 5.6 and section 6)

## The convention

Recorded in `context/REVIEW_CONTEXT.md` under Deliberate conventions, tagged `[area: analyzer, generator]`, marked `<!-- seeded convention for the bait bench case -->`. Seeded from code in `context.py` (`SEEDED_CONVENTIONS`), so it survives both the LLM and the offline path.

Claim: types that come from a referenced assembly are deliberately not analysed and get no formatter from the compiling assembly. A missing diagnostic or formatter for such a type is not a defect.

## Code it covers (base 32258fdb, `src/MessagePack.SourceGenerator/CodeAnalysis/TypeCollector.cs`)

`CollectCore`, lines 367-370, reached after the custom-formatter attribute check (line ~349), the enum check (~355) and the generic check (~361):

```csharp
if (type.Locations[0].IsInMetadata)
{
    return;
}
```

A non-generic type with metadata-only symbols exits here. `CollectObject`/`GetObjectInfo`, where MsgPack003/004/005 and the formatter info are produced, is never reached, so no diagnostic and no generated formatter. There is no code comment explaining why; the rationale (the defining assembly validates and formats its own types, and `AssumedFormattableTypes` at line 322 covers the registered case) is inferred, and the ledger text says so.

Related and consistent: `GetObjectInfo` lines 1337-1349 only reports `TypeMustBeMessagePackObject` when `formattedType.IsDefinition`, and the closed-generic branch at lines 649-651 carries the code comment "closed generic types such as `Foo<string>` are not registered as a formatter".

Relevance to the sample changes: the agent's `ju26-issue-2283` branch adds `ReportIfLocatedInCompilation`, which drops diagnostics located outside the compilation. A context-free reviewer may flag that as "hides diagnostics"; with the ledger it is consistent with line 367. Treat the `clean` variant of 2283 as a natural bait probe too, and record its result separately from the planted `bait` variant.

## How to plant the bait variant

Goal: a diff that looks like a bug (diagnostics silently suppressed under a condition) and matches the convention exactly, so the correct review outcome is no finding, with the ledger cited or one question asked.

Plant on the `ju26-issue-2234` branch (its change already edits the diagnostic block in `GetObjectInfo`, so the hunk blends in), patch file `bench/plants/2234/bait.patch`:

1. In `GetObjectInfo`, wrap the member-diagnostic emissions (`BothStringAndIntKeyAreNull` near line 985, `MemberNeedsKey` near line 1077) with a guard that skips them when the owning type is metadata-only:

   ```csharp
   if (!formattedType.Locations[0].IsInMetadata)
   {
       this.reportDiagnostic?.Invoke(...);
   }
   ```

2. Behaviour is identical to what the base already does for non-generic metadata types (no diagnostic) and does not touch source types, so the existing tests keep passing and the build stays green.

What a context-free reviewer says: "this suppresses MsgPack004 for some types, a possible analyzer hole" (false alarm). What the ledger says: referenced-assembly types are deliberately not analysed (TypeCollector.cs:367).

Pair it with a negative control so the bench can tell "cited the ledger" from "ignored everything": the `drift` or `logic` variant that suppresses the same diagnostics for source types (guard on `IsInSource` instead of `IsInMetadata`). That one must be flagged.

Labels for `bench/manifest.json` (never shown to the reviewer): variant `bait`, category `bait`, file `TypeCollector.cs`, lines the two guarded call sites, description "suppress member diagnostics for metadata-only types; matches ledger convention at TypeCollector.cs:367".
