# Blast radius: does this change touch code other features rely on?

Status 2026-11-14: built and run on the four Tieto changes. Code: `tools/impact/` (Roslyn syntax
tool), `pipeline/receipts/impact.py` (rule, coverage, impact.json), `pipeline/receipts/characterize.py`
+ `pipeline/receipts/lenses/regression.py` (characterization receipts). Tests:
`pipeline/tests/test_impact.py`.

## The question, from a Tieto engineer

> "Even if it's all green, I need to know if this implementation has the risk of impacting other
> features. If a function that is already used in many other places is modified, it may do what the
> new feature expects but break the functionality of the old feature. A new function used only by
> this feature is fine: if it's wrong, only this feature fails. Because of legacy code we cannot be
> sure we have full test coverage, and we cannot run a full regression after every simple change. Is
> the router (Jev) targeting this situation? It has to be a concrete rule."

## Does Jev target it? No, and it cannot

What Jev sees, per hunk (`routers/jev.py`, `build_state`): one request per diff hunk with

- `task`: the requirement text;
- `diff`: `File: <path> (kind, area)`, the `@@` header and the hunk body (truncated to the ~32k-token
  input ceiling).

That is all. Jev gets no call graph, no other files, no list of who calls the function the hunk sits
in, and no test coverage. It can guess that "a logic change other code depends on" is risky (its
level-5 description says so), but it has no evidence for *which* hunks that is. A hunk inside a
function with 40 callers and a hunk inside a brand-new helper look the same to it.

So the answer to the engineer: Jev scores *what the hunk says*; the new rule scores *where the hunk
lands*. The rule is deterministic, runs before Jev, and sets a floor Jev cannot lower
(`routers/combine.py`: final risk = max(rule floor, Jev score)).

## The concrete rule

| Hunk lands in | Floor | Category | Verdict effect |
|---|---|---|---|
| a `modified` existing member with >= 1 caller outside the change | 8 | compatibility | never `auto-ready`; reason names the member and caller count |
| a `deleted` member whose name is still called outside the change | 9 | compatibility | never `auto-ready` |
| a `new` member | none (other floors still apply) | - | - |
| a modified member whose only callers are inside the change | none | - | - |
| characterization test passes on base, fails on head | - | - | verified **high** finding (lens `regression`) -> `blocked` |

Reason text on the route: `modifies existing <Type.Member> used in N other places`.

"Outside the change" means the call sits in a member this change neither adds nor modifies. A call
from an untouched member of a touched file counts as outside (on 2283 the callers of
`TypeCollector.GetObjectInfo` are `CollectObject` and `CollectGeneric` in the same file; they are the
old features the engineer is worried about). This is stricter than "the caller's file is not in the
diff", on purpose.

## How it works

1. **Member diff** (`tools/impact`, no LLM, about 1 s per change). Every changed `.cs` file under
   `src/` is parsed on base and head with Roslyn syntax trees (no MSBuild workspace, no semantic
   model). Members (methods, constructors, properties, indexers, operators) are keyed by containing
   type + name + parameter types and compared token by token (comments and whitespace ignored):
   `new`, `modified` (body or signature differs; `signature_changed` when the signature does), or
   `deleted`.
2. **Call sites**. All `.cs` files under `src/` (and, separately, `tests/`) of HEAD are scanned for
   invocations, member accesses, object creations and constructor initializers whose name matches a
   modified or deleted member. A match must fit the argument count; when the receiver's type is
   visible (`this`, a static `Type.Member`, `new T()`, a local or field declared with a type) it must
   be that type or related by inheritance. Unresolved receivers still count.
3. **Coverage of callers**. The SourceGenerator test project runs on HEAD once with coverlet
   (`--collect:"XPlat Code Coverage"`), in a private worktree whose csproj gets
   `coverlet.collector` for that run only, then is reset; the change under review is never edited.
   A caller is `tested: true` when a line from its statement start to the call line was executed.
   Untested callers outside the change are listed under "not checked" as a legacy coverage gap.
4. **Characterization receipts** (lens `regression`, Claude Opus 5.5, effort high). For at most 3
   modified members with outside callers (most callers first), the lens sees ONLY the base source of
   the member and the code of its callers: no requirement, no diff, no head code. It writes up to 3
   xUnit tests per member pinning what the callers rely on. The tests must pass on base, else they
   are discarded as invalid (recorded). Then on head: pass -> evidence `behaviour-preserved`;
   fail -> evidence `behaviour-changed` + verified high finding.

Output per run: `impact.json` (shape below), raw tool output `impact/members.json`, caller line hits
`impact/coverage-lines.json`, lens record `impact/regression-lens.json`, the tests under
`generated/regression/`.

```
{"method": "name-based call sites (Roslyn syntax, not semantic)",
 "members": [{"name", "file", "kind", "signature_changed",
   "callers": [{"file", "line", "member", "in_change", "tested"}],
   "callers_outside_change", "untested_callers", "ambiguous"}],
 "characterization": [{"member", "test_names", "base", "head", "result", "evidence_id"}],
 "coverage_available": bool,
 "summary": {"modified_existing", "new", "deleted", "callers_outside_change",
             "untested_callers", "behaviour_changes"}}
```

`untested_callers` counts callers outside the change with `tested: false`. New evidence kinds for
the dashboard: `behaviour-preserved` (pass) and `behaviour-changed` (fail); new lens name
`regression` (dashboard/lib/types.ts not edited here: the dashboard track owns it).

## Limits (stated, not hidden)

- **Name-based, not semantic.** Common names over-approximate: `ObjectSerializationInfo.Equals` on
  2129 matches 11 `.Equals(` calls outside the change, most on other types (`ambiguous: true`). The
  count is an upper bound when `ambiguous` is true. It can also under-count: members called only by
  the framework (a generator's `Initialize`, an analyzer's `SupportedDiagnostics`), by reflection,
  through delegates stored elsewhere, or from other repositories show no callers. Operators and
  indexers have no named call site and are always `ambiguous`.
- **Coverage from one test project only** (`tests/MessagePack.SourceGenerator.Tests`). Callers in
  projects that suite does not load (e.g. `MessagePack.SourceGenerator.Unity`) are reported as
  untested even if another suite covers them.
- **Characterization tests are only as good as the LLM's understanding of the old code.** What
  makes them a receipt is that they must pass on base: a test that does not describe today's
  behaviour is thrown out, never counted. A passing pair (base and head) proves the pinned contract
  survived, not that nothing else changed. A head compile error is recorded as invalid, never as a
  behaviour change.
- Deleted members are floored but not characterized (their tests could not compile on head).

## Results on the four Tieto changes

Run 2026-11-14 (`runs/ju26-issue-*`, one run per change; A/B sides regenerated). Every existing
lens, reader and Jev score replayed from fixtures; only `lens-regression` was new.

| Change | Modified / new / deleted members | Callers outside the change | Untested callers | Characterization | Max risk | Verdict before -> after |
|---|---|---|---|---|---|---|
| 2283 | 2 / 2 / 0 | 3 (`GetObjectInfo` 2, `TypeCollector` ctor 1) | 0 | 2 members, 4 tests: preserved | 7 -> 8 | needs-sign-off -> needs-sign-off |
| 2234 | 1 / 0 / 0 | 2 (`GetObjectInfo`) | 0 | 1 member, 3 tests: preserved | 7 -> 8 | needs-sign-off -> needs-sign-off |
| 2129 (+A, B) | 6 / 10 / 0 (B: 6 / 12) | 14 (11 of them name-only `Equals` matches, ambiguous) | 6 | 3 members, 7 tests: preserved | 8 -> 8 | blocked -> blocked |
| 2297 (+A, B) | 7 / 0 / 0 (B: 8 / 2) | 12 (B: 13), all `TransformText` templates | 1 | 3 members, 5 tests: preserved | 7 -> 8 | blocked -> blocked |

What changed and why:

- **2283 and 2234** stay `needs-sign-off`. The verdict reason now says why in the engineer's terms:
  "modifies existing code used elsewhere (blast radius): TypeCollector.GetObjectInfo (2 callers
  outside the change)". Before, the reason was only "max routed risk 7". Those hunks now route at 8
  (floor; Jev scored 7). The characterization tests ran on base and head: `GetObjectInfo` still
  produces compiling formatters for int-key and map-mode objects and still reports the
  unattributed-property diagnostic at the property identifier. Those are the behaviours
  `CollectObject` and `CollectGeneric` rely on.
- **2129 and 2297** were already `blocked` by verified requirement and logic-edge findings. The
  blast radius adds context and receipts but no new blocker. No characterization test showed a
  behaviour change.
- **Mostly new code?** Partly. 2129 is mostly new: 10 new members, including
  `GeneratedFormatterCollector`. A new collector used only by the feature is low blast radius,
  exactly as the engineer said. But all four changes also modify existing members other code calls
  (`GetObjectInfo`, the T4 `TransformText` methods). That is why every one of them now carries the
  floor and can never be `auto-ready`.
- **Legacy gap, concretely:** on 2297, `src/MessagePack.SourceGenerator.Unity/CompositeResolverGenerator.cs:44`
  calls the modified `CompositeResolverTemplate.TransformText` and no test executes it. On 2129,
  `MessagePackGenerator.cs:156` (`Comparer.Equals`, the incremental-generator comparer, untouched by
  the change) calls the modified `ObjectSerializationInfo.Equals` untested. That is the call that
  matters; the other 5 untested `Equals` hits are name-only noise (Unity `Color.Equals`, runtime
  resolver).

Cost: 4 live `lens-regression` calls, $1.40 total (2283 $0.34, 2234 $0.30, 2129 $0.40,
2297 $0.35). The A/B sides replayed the parent's fixture, because the same members and callers give
the same prompt. Impact tool: about 0.6 to 1.5 s per change, including 1.0 s on 2129 (28 files
reviewed, 192 src + 145 test files scanned). Coverage run: about 30 to 60 s per head.
