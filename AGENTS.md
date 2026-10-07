# Family Policy development

Read README.md, MEMORY.md and DEVELOPMENT-PLAN.md before implementation. This is
an experimental Jellyfin 12.1 extension. Household state and credentials are
never source/test fixtures. C#/.NET is required by the existing plugin interface;
use the exact global.json SDK, locked restore and repository test gates.

Layout: src/ and tests/ contain maintained code; upstream implementation is retained in Git history, and its DLL must never be
included in Family Policy artifacts. feasibility/ is a test-only
extension spike, not production authorization. Retain upstream MIT attribution.

Administrator authorization belongs on every mutation and unrestricted-search
endpoint. Never trust caller-supplied actor IDs, imported/AI tags as grants, or
unknown classification through negation. Content and schedule decisions remain
independent. Failures must not broaden a child's access. A guard is required for
managed accounts. Do not publish tokens or sanitized-looking authenticated dumps.

Before release: behavioral tests; native API/media/HLS/cutoff/recovery/browser
tests; documented compatibility gaps; no claims of real-device validation until
observed. Maintain docs/validation/ and update MEMORY.md. Public claims describe
executed evidence, not intended capability.
