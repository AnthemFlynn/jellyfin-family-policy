# Development, validation and household rollout

2026-10-07. Architecture extension feasibility demonstrated; implementation is
not yet complete. See FORK-DESIGN.md and feasibility/RESULTS.md.

## Support contract

Public, reusable, opt-in plugin; version one targets Jellyfin 12.1 specifically.
Default PG preset; advanced rating/age-band/media-kind/category/subject/franchise/
title policies and independent schedules. Only administrator-authorized actions
change boundaries. Curated labels/provider identities; classification uncertainty
never earns exception access. Parent approval and acquisition remain distinct.

Server rejects ineligible playback and ceases serving at a deadline; client Stop
is attempted. Already-buffered playback is not guaranteed to stop on an exact
frame. Offline copies and shared administrator sessions are outside the child
policy. Actual client support and cutoff lag must be documented from tests.

## Milestones and acceptance gates

| Phase | Deliverables | Completion evidence |
| --- | --- | --- |
| 0 — extension feasibility | Unmodified test server; plugin global authorization filter; ongoing stream abort | Complete for synthetic and native-route interception probes; feasibility/RESULTS.md |
| 1 — pure policy core | Typed conditions and effects, two independent decision dimensions, explicit priority/conflicts, unknown handling, provenance, timezone-aware windows, decision explanation, versioned serialization | Behavioral tests covering PG, unrated Soccer, Star Wars, title block, morning workout, after-hours denial, overrides, expiry, DST/midnight and configured age progression |
| 2 — parent control | Managed-account enrollment, parent-only routes/UI, grant/block/reset, audits, preview current/future matches; distinct fork GUID/name/packaging | Child token cannot mutate self or other users, disable restrictions, access unrestricted search, spoof actor IDs or submit config; parent operations attributable and durable |
| 3 — content enforcement | Compute/persist child allow and deny tags, safe initial PG snapshot, type-aware stable identities, series/episode precedence, metadata reconciliation | Real local media catalog/direct-item/stream tests, individually approved higher/unrated titles, denied PG titles, identity replacement, refresh/import races; rollback restores native policies |
| 4 — schedules and resilience | Global filter resolves authenticated actor + media; track/cancel ongoing responses; session Stop/transcode kill; server clock; legacy-route fail-closed handling; independent health guard | Direct/range/head/universal audio/HLS/subtitle/extras/download/remote-source/resume paths tested; forged/missing binding denied; plugin absence/startup failure tested; measurable cutoff and guard latency |
| 5 — parent experience | PG preset, advanced rule builder, per-title overrides, matching-rule explanation, dry-run preview, backup/import/export without credentials | Parent completes supplied example policy; decisions understandable; complex rules don't require editing JSON; no household data in public fixtures |
| 6 — optional Seerr integration | Approved-only validated request grants, opt-in mapping, no historical bulk grant, replay/idempotency protection | Pending/denied/forged/duplicate/out-of-order events cannot expand access; manual denies win |
| 7 — household pilot | Consistent prechange backup, pinned artifact/checksum, managed identities only; adult permissions preserved | Current catalog preserved, exception exercised separately for two children, actual Swiftfin Apple TV and iPhone tests, client buffering/stop behavior recorded, tested rollback |
| 8 — public release | MIT attribution, independent version/channel, supported-version matrix, installation/upgrade/rollback docs, privacy-safe examples, reproducible CI and release artifacts | Security gates passing and client limits disclosed; no claim of support for untested clients/server releases |

## Implementation layout

Policy.Core: dependency-light deterministic C# library and behavioral tests;
Plugin.Adapter: Jellyfin actor/item resolution, native tags and MVC filter;
Parent.UI: Jellyfin administrator page using parent-only APIs;
HealthGuard: separate supervisor using scoped operational credentials, restoring
safe account state if plugin enforcement disappears. Rust is a suitable option
for the external guard once its narrow contract is specified. Keep UI and agent
requests on the same authorization/evaluation paths.

Pin SDK 10.0.401 and Jellyfin.Controller 12.1.0. Use .NET 10 for the existing
plugin integration. Preserve upstream MIT notice. Do not distribute Jellyfin
server binaries as part of the plugin; server runtime dependencies stay external.

## Rollout sequence

1. Disposable local server and synthetic identities for implementation tests.
2. Real small permitted media fixture, no production media writable mounts.
3. Staging canary, then migration dry run against current child catalog.
4. Consistent configuration backup and captured policies/tags/locks.
5. Apply/tag/check first; replace native rating/schedule gates only when the new
   adapter and health guard are active and validated. Keep adult accounts untouched.
6. Verify both children on real devices; verify parent changes propagate.
7. On failure restore native PG/unrated restrictions before removing the fork.

Actual household names, account IDs, server addresses and credentials stay in
private operational records. Public publishing is a separate action; no remote
public fork has been created during feasibility work.

## Immediate next work

Implement the independent policy core and its behavioral tests, then parent-only
API authorization. Do not promote the feasibility spike to production code.

## Implementation evidence — 2026-10-07

Phases 1–5 have an experimental implementation under src/ with 28 passing core
behavioral cases and passing native/browser acceptance on unmodified 12.1/12.2.
Parent annotations replace the design's initial imported-tag suggestion. The
independent guard and parent-only API are implemented. See docs/validation/.

Remaining production gates: installed web-shell/device pilot, expanded route and
metadata-replacement matrix, large-library measurements and independently
supervised target deployment. Seerr/provider rematching remains unimplemented.
Public publication is now authorized; do not advertise production readiness.
