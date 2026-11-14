# Reviewer context: MessagePack-CSharp (Tieto sample)

Status: draft, bootstrapped from README.md, CONTRIBUTING.md, `doc/analyzers/*.md`, `AnalyzerReleases.*.md` and 12 months of git log. Entries carry provenance; a named human confirms the file in the dashboard.

## Product

MessagePack-CSharp is a high-performance binary serializer for .NET with a Roslyn source generator that emits formatters and resolvers at compile time. Consumers include game engines (Unity), ASP.NET services and mobile apps.

## Users

- Library consumers who annotate types with `[MessagePackObject]` and expect generated formatters without reflection.
- Teams on NativeAOT and Unity IL2CPP, where reflection-based fallbacks are not available.

## Public contract

- Wire format is stable: integer keys, key order and default null handling never change in a minor release. (source: CONTRIBUTING.md, "Compatibility")
- Analyzer diagnostic ids `MsgPack001`-`MsgPack01x` are listed in `AnalyzerReleases.Shipped.md`; new ids go in `Unshipped.md` first.

## Deliberate conventions

- Analyzer diagnostics for types declared in **referenced assemblies** are deliberately not reported: their authors cannot act on them. (source: ledger Q-bait-1, answered by Anson) <!-- seeded convention for the bait bench case -->
- Generated files under `tests/**/Snapshots/*.verified.cs` are verified by regeneration, never hand-edited. (source: doc/analyzers/index.md)
- `MessagePackSecurity.UntrustedData` is the default for deserializing external input; relaxing it is a security change, not a convenience. (source: README.md)

## Risk areas

| Area | Why |
|---|---|
| source-generator/emit | Templates change every generated formatter at once |
| annotations/public-api | Public attributes are the consumer contract |
| security | `MessagePackSecurity` and depth limits |

## Decisions

- 2026-11-14: REQ-2297 scope (all generated types vs formatters only) is a product decision; pending in the dashboard.
