---
project: family-policy
status: active
updated: 2026-10-07
---

# Family Policy re-entry

Implemented: deterministic content/time core, administrator-only adapter, parent
annotations, native content tags, ongoing-stream authorization, independent guard,
revisioned persistence/restoration and PG/advanced editor. Source under src/;
original upstream project is reference only, never the release DLL.

Standing decisions: explicit priorities and deny-on-tie; unknown exceptions do not
grant and unknown denies block; independent content/time gates; no child self-grants;
parent annotations are separate from imported/AI tags. Runtime clock is server-owned.
Do not promise exact buffered-client cutoff or untested device compatibility.

Run the pinned SDK, locked restore, core tests and native/browser integration.
DEVELOPMENT-PLAN.md and docs/testing.md track delivery/remaining acceptance.
Household state remains private and outside source/fixtures. Public publication
is authorized by the user. Native Apple TV/iPhone pilot remains outstanding.

## Handoff Note
- Date: 2026-10-07
- Next: complete real-device pilot and remaining route/replacement/performance gates.
- Do not: install the upstream reference/PolicySpike DLL, publish credentials or
  claim production readiness from synthetic/browser acceptance alone.
