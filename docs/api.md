# Administrator API

Every endpoint below requires Jellyfin's RequiresElevation policy. A child's
session receives HTTP 403; anonymous requests receive HTTP 401. Actor identity
comes from authenticated claims, never from a body field.

| Method / path | Body / result |
| --- | --- |
| GET /FamilyPolicy/State | Revision, managed account policies, confirmed annotations, last 500 audit entries; original native policies are not exposed |
| PUT /FamilyPolicy/Accounts/{userId} | ExpectedRevision + Policy; requires healthy guard coverage; rejects administrator enrollment |
| POST /FamilyPolicy/Accounts/{userId}/Restore | ExpectedRevision; restores captured native policy before dropping plugin enforcement |
| PUT /FamilyPolicy/Labels/{itemId} | ExpectedRevision + Labels with optional Categories, Subjects, Franchises arrays; parent-owned source |
| POST /FamilyPolicy/Preview/{itemId} | Policy; returns ContentAllowed, TimeAllowed, Allowed, rule matches and reasons |
| POST /FamilyPolicy/Reconcile | Rebuild derived catalog tags |
| POST /FamilyPolicy/Heartbeat | UserIds covered by the independent guard; lease lasts five seconds |
| GET /FamilyPolicy/Health | Plugin version and guard lease health |

Stale revisions return 409. Invalid policies/labels return 400. Missing guard
coverage returns 503. Policy writes commit before catalog reconciliation; if a
request fails after commit, reload State before retrying. Reconciliation is
idempotent and retried by the background worker. Runtime decisions use the current
policy/annotation snapshot; browse-cache synchronization can lag.

## Policy

SchemaVersion 1. TimeZone is an IANA timezone ID. AgeYears is optional, explicitly
configured, and does not advance automatically. DefaultContentAllowed defaults
false; DefaultTimeAllowed defaults true. Rules have unique Id, Dimension
(Content or Time), Effect (Allow or Deny), Priority, optional HardBlock, Enabled,
ExpiresAt, and When. Omitting Effect conservatively defaults to Deny.

When is exactly one of: leaf Field, All, Any, Not. Available leaves:
Always; RatingAtMost + Number; Unrated; MediaKind/Category/Subject/Franchise/Title
+ Values; AgeAtLeast + Number; Window with Start, End and optional Days.
Window boundaries are start inclusive/end exclusive; overnight ranges belong to
their starting weekday. Equal boundaries are rejected rather than treated as 24h.

RatingAtMost uses Jellyfin's normalized rating score (PG/TV-PG=10 in the tested
US fixture). Country-specific subratings are not exposed as a separate rule
operand in this preview. Confirm the server's rating mapping before configuring
another system. Title values accept dashed or compact GUIDs; ancestor title IDs
support series grants and narrower episode decisions.

## Storage and audit

family-policy-state.json lives under Jellyfin's plugin configuration directory.
Writes use a private temporary file, flush-to-disk and atomic replacement before
publishing the new snapshot. Unix mode is 0600; Windows uses inherited operator
ACLs. The file contains private account IDs, configured age/labels and native
policy snapshots; it contains no password/token values. Back it up with the
Jellyfin configuration, never in a public source repository.

Audit is bounded to 500 revisioned actions with authenticated actor, target,
action and UTC time. Full historical policy-diff retention is not implemented.
