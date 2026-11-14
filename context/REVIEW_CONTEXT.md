# Review context

Status: draft (not confirmed by a named human). Built from the sample repo at 32258fdb by claude-sonnet-5-5 (purpose context-bootstrap).

## Product

- [area: product] MessagePack for C# is a fast MessagePack binary serializer with built-in LZ4 compression support. (source: README.md)
- [area: build] The library targets .NET Standard 2.0 with special optimizations for .NET 8+ and .NET Framework, and uses either runtime IL generation or AOT-safe source generators. (source: README.md)
- [area: analyzer] The MessagePackAnalyzer NuGet package provides compiler warnings, code fixes, and optional AOT formatter generation. (source: README.md)

## Users

- [area: annotations] Users annotate their types with [MessagePackObject] and members with [Key], and call MessagePackSerializer.Serialize/Deserialize. (source: README.md)
- [area: serializer] Users who want Json.NET-like behavior without attributes can use the ContractlessStandardResolver. (source: README.md)
- [area: build] Contributors build with Visual Studio 2022 or 'dotnet build /p:platform=NoVSIX', and install the required SDKs by running init.ps1. (source: CONTRIBUTING.md)
- [area: build] Maintainers publish releases by running the GitHub Actions workflow_dispatch workflow with a version tag starting with v. (source: CONTRIBUTING.md)

## Public contract

- [area: serializer] If all keys are integer indexes the object serializes as a MessagePack array, otherwise as a map with string keys. (source: README.md)
- [area: serializer] Gaps in integer key sequences are filled with null placeholders in the array, and indexes of removed members should not be reused. (source: README.md)
- [area: annotations] Serializable types must be public by default and need [MessagePackObject], and keyAsPropertyName: true makes members use their property names as string keys without [Key]. (source: README.md)
- [area: analyzer] MsgPack003, 004, 005, 006, 007, 008, 009, 011, 012, 016 and 018 are Errors, while MsgPack010, 013, 014, 015 and 017 are Warnings. (source: src/MessagePack.SourceGenerator/AnalyzerReleases.Unshipped.md)
- [area: analyzer] MsgPack004 requires public members of [MessagePackObject] types to have either [Key] or [IgnoreMember]. (source: doc/analyzers/MsgPack004.md)
- [area: annotations] MessagePackKnownFormatterAttribute and MessagePackAssumedFormattableAttribute are assembly-level attributes that suppress analyzer warnings for types with custom formatters. (source: README.md)

## Deliberate conventions

- [area: analyzer] MsgPack001 and MsgPack002 are Disabled by default in the rule table. (source: src/MessagePack.SourceGenerator/AnalyzerReleases.Unshipped.md)
- [area: serializer] Missing keys during deserialization leave members at their default value, so reference-type members can be null. (source: README.md)
- [area: docs] Requiring the MessagePackObject annotation by default is intentional, to enforce explicitness. (source: README.md)
- [area: analyzer] MsgPack017 warns that an init property initializer's default is replaced by the type's default value on deserialization. (source: doc/analyzers/MsgPack017.md)
- [area: generator] Source generated formatters do not support everything dynamic formatters do, which is why MsgPack008 and MsgPack016 exist. (source: doc/analyzers/MsgPack008.md)
- [area: analyzer, generator] Types that come from a referenced assembly are deliberately not analysed and get no formatter from the compiling assembly. In `TypeCollector.CollectCore`, after the custom-formatter, enum and generic checks, a non-generic type whose symbol is metadata-only exits with a bare `if (type.Locations[0].IsInMetadata) { return; }` (TypeCollector.cs:367-370), so no MsgPack003/004/005 diagnostic and no generated formatter appear for it. The defining assembly owns validating and formatting its own types (rationale inferred: the code has no comment). A missing diagnostic or formatter for such a type is not a defect. (source: src/MessagePack.SourceGenerator/CodeAnalysis/TypeCollector.cs:367) <!-- seeded convention for the bait bench case -->

## Risk areas

- [area: security] Recent history includes secure-by-default changes, a collision-resistant hash for untrusted data, a stack depth limit of 500 even for trusted data, and a revert to v2's insecure-by-default mode. (source: git log)
- [area: serializer] Recent fixes to serializing BigInteger, Decimal, long numbers, and float SIMD show numeric serialization is bug-prone. (source: git log)
- [area: generator] Source generator and TypeCollector logic has had many recent fixes, including name collisions, nested types, generic constraints, and recursive constraints that caused a stack overflow. (source: git log)
- [area: analyzer] Analyzer diagnostics such as MsgPack003 and MsgPack004 have recently been adjusted for false positives, so changes can cause wrong reports. (source: git log)
- [area: build] The release CI workflows and the NUGET_KEY secret (which expires after 365 days) are recently changed and operationally sensitive. (source: CONTRIBUTING.md)

## Decisions (from answered questions)

_No decisions recorded yet._
