# Parent-managed JellyPrivateLibraries fork

Design proposal, revised 2026-10-07. Based on upstream v2.1.0.0,
`d0a13b356caed12aec7200efa83f4b8dfbb071b8`. Original design; implementation evidence is tracked in DEVELOPMENT-PLAN.md and
docs/validation/. No production migration has been performed.

## Product behavior

Parent dashboard: select a movie, series or supported episode, then choose
Approve for Child A, Approve for Child B, Approve for both, Block, or Reset to
default. Display the effective decision and its reason separately from the
industry rating. Parent decisions belong to individual children.

Initial migration preserves the existing PG/TV-PG catalog by snapshotting the
actual catalog visible under each child's current identity; recapture before
migration. This is an initial baseline, not a claim that every PG title is
parent-reviewed. Household records stay outside the public source repository.

The decision rule is: explicit child denial wins; otherwise explicit approval
wins; otherwise the configured PG/TV-PG default applies; unknown/unrated content
needs explicit approval. Unmanaged adult account permissions are preserved.

## General product scope

A reusable public fork with a simple preset and an advanced policy editor.
No household names, birthdays, account IDs, credentials, local network addresses
or media inventories belong in source/examples/test fixtures. Retain upstream
MIT license and attribution; use a distinct fork identity and release channel.

Simple mode: choose a maximum rating (for example PG), unrated behavior and
optional hours. Advanced mode: combine named rules matching account/group,
rating system/ceiling, configured age band, media kind, genre/category, subject,
franchise/collection, exact title/episode and local schedule. Parent-defined age
bands map to explicit policies; age is not an objective content-suitability score.
Automatic birthday progression, if added, is opt-in and reviewed by the parent.

Each rule has an ID, enabled flag, scope, predicates (all/any/not), effect,
explicit priority and optional expiration. Explain matching rules and the final
decision. Block rules win same-priority conflicts; a grant bypasses a narrower
restriction only through an explicit override scope. Hard blocks and account
security constraints cannot be bypassed by ordinary grants. Do not use hidden
ordering based on row position.

### Two independent policy decisions

Content eligibility and permission to play at the current time are separate.
Playback requires both. A franchise approval changes content eligibility; it
does not silently bypass bedtime. A workout schedule exception changes hours;
it does not silently approve otherwise blocked content. Exact-title decisions
may override either dimension only when the parent explicitly selects it.

### Example policy (illustrative; not an activated household configuration)

| Dimension | Rule | Effect |
| --- | --- | --- |
| Content default | Recognized rating at PG/TV-PG or below | Eligible |
| Content exception | Unrated AND parent-confirmed subject Soccer | Eligible despite unrated default |
| Content exception | Member of selected verified Star Wars franchise collection | Eligible despite default rating ceiling |
| Content override | Particular movie/episode blocked for selected child | Ineligible; overrides category/franchise eligibility |
| Schedule default | 08:00 inclusive to 18:00 exclusive in configured timezone | Playback permitted for eligible content |
| Schedule exception | Before 08:00 AND parent-confirmed Workout category | Playback permitted for eligible workout content |
| Schedule fallback | All other times | Playback denied |

For this example, a workout at 07:00 passes both gates; an eligible Star Wars
movie at 07:00 fails the time gate; a workout at 19:00 fails because the exception
explicitly covers mornings only. Whether workouts should also be allowed after
18:00 is a separate visible rule, not an inference.

### Metadata and subject provenance

Distinguish media kind (movie/episode), genre/category (sports/fitness), subject
(Soccer) and franchise identity (Star Wars). Parents can define and approve local
labels. Prefer curated tags, library/collection membership and typed provider
IDs over title-keyword matching. Show the label's source and allow corrections.
A review mentioning soccer is not proof that the media is a soccer match.

External ratings/content advisories and AI-assisted labels can suggest rules or
review candidates; they do not automatically override parent policy. Uncertain
or conflicting classifications stay unknown. An exception requiring Soccer or
Workout must not match an unknown subject. Parent-approved automatic label
sources, if supported later, must be explicitly enabled with provenance and
confidence handling. Broad matching rules preview current affected titles and
state whether future matching titles will also be included.

## Enforcement design

Jellyfin's native per-user AllowedTags/BlockedTags enforce access across clients.
Managed children get their own allow tag and deny tag. No user may self-grant,
remove the managed restriction, or broaden search through the plugin.

A PG rating ceiling cannot remain as a second gate once higher-rated exceptions
are enabled. Switch managed children to the validated tag allowlist only after
seeding it, checking its effective content, capturing the previous policy and
passing staged migration tests. Similarly, native unrated blocking must not
reject an explicitly parent-approved unrated title; the fork denies unapproved
unrated media through the allowlist. Keep other child security settings intact.

Do not tag whole libraries or filesystem parent folders: inherited tags could
approve unrelated media. Series approvals explicitly cover its episodes; an
individual episode denial must beat its inherited series approval. Series-level
approval must be visibly distinct from episode-level approval in the UI.

PG defaults must be derived from Jellyfin's current effective rating mapping,
not a hardcoded string comparison. Metadata-update handling must remove stale
default grants when a rating changes. Test exposure during import/refresh before
enabling automatic default grants. Until that lifecycle is proven, new media
stays outside the allowlist and the initial preserved catalog remains available.

### Time rules require a distinct enforcement path

Jellyfin's native account-wide access schedule would prevent all playback during
a blocked period, including the workout exception. Therefore selective schedules
cannot be implemented by setting that schedule alone. Persistent allow/deny tags
can protect content eligibility; time-and-item decisions require a validated
server playback authorization path.

Investigate a plugin hook/filter covering playback authorization, direct streams,
HLS manifests/segments, resumed sessions and active cutoff handling on the exact
server version. A UI-only timer or periodic tag change is insufficient. If the
plugin API cannot enforce this across those paths, selective schedules need a
server-side change or must remain explicitly unsupported; do not claim support
from a successful web-client demonstration. Standard user-wide schedules can
remain the fallback when no media-specific exception is requested.

Timezone is configured per policy; test DST transitions, midnight-spanning
windows, weekdays, exact 08:00/18:00 boundaries, device clock tampering and server
restart. Offline/downloaded copies cannot be recalled by server schedules;
managed accounts keep downloading disabled. Loss of the time-enforcement
component must not leave a permissive unrestricted schedule for managed users;
prove a fail-closed startup/health strategy before releasing selective schedules.

## Changes to upstream

| Area | Required change |
| --- | --- |
| Controller | Require Jellyfin elevation for all mutation and unrestricted-search routes; remove or deny legacy self-service mutations for children |
| Parent endpoints | Target an explicit managed child; list effective decisions; approve/block/reset; validate movie/series/episode type and identity |
| Enrollment | Explicit managed-user list; locked restrictions; never enroll unmanaged adults through a global default |
| Grants | Separate baseline, manual approval, explicit denial and request-derived grant; denial has precedence over every grant source |
| Rules | Typed versioned content/schedule policy model; preview, explicit priorities, effective-decision explanation and optional expiry |
| UI | Simple PG preset and advanced rule editor; administrator configuration page manages children; child widget may show status/request help only; UI hiding is not the authorization boundary |
| Audit | Actor ID, child ID, title identity, old/new decision, UTC timestamp, source and optional reason; no credentials |
| Persistence | Versioned schema; durable decision written before acknowledgement; serialized reconcile; recover incomplete operations |
| Metadata | Preserve unrelated tags/locks; use separate per-child deny tags; reconcile only managed entries |
| Identity | Stable typed provider identity plus local item ID; distinguish movie vs series and versions/editions; ambiguous rematches stay pending |
| Packaging | Distinct fork GUID/name/version/manifest, exact Jellyfin 12.1 references and SDK pin; do not silently overwrite upstream |

## Seerr and agent integration

Keep Seerr request-only permissions and parent approvals. Existing request
history is not automatically granted. Phase one uses manual viewing decisions.
Phase two can add an explicitly enabled parent policy translating approved
requests into viewing grants; manual denies always win. Pending requests cannot
produce grants. Webhooks need a secret and validation against Seerr's actual
request status/identity before changing child access; do not trust payload fields
alone. Repeated or out-of-order notifications must be idempotent.

Agent access uses administrator authorization stored in the vault. Agents may
execute a parent's explicit title decision; an agent's assessment is not itself
parental approval. JSON endpoints support list, grant, deny, reset and dry-run
impact, with attributable audit entries.

## Failure and rollback

A missing/crashed plugin must leave persisted Jellyfin child allow/deny policies
in place; startup cannot clear tags or restore unrestricted access. A stale
allowlist may retain previous decisions and delay new content; it must never
permit the whole library. Do not claim metadata freshness until tested.

Revocation denies new catalog/item/playback access and stops active sessions for
that child. Test direct-play files, HLS segments and resumed sessions; report
remaining client caches/in-flight playback limits rather than promising instant
revocation without evidence.

Capture child policies, metadata tags/locks, grant data and consistent Jellyfin
configuration backup before deployment. Roll back by first restoring the old PG
policies (including unrated blocks), checking child access, then disabling/removing
the fork. Removing the fork alone is not a rollback procedure.

## Required release tests

| Test | Pass condition |
| --- | --- |
| Child self-grant/opt-out | Every legacy and new mutation route denied; no grant or policy change |
| Child cross-user access | Cannot act as parent or another child by changing IDs, request bodies or URLs |
| Parent PG-13 exception | Approved child can discover and play title; other child cannot |
| Parent PG denial | Denied child cannot discover/play title despite baseline/default rule |
| Explicit unrated approval | Works for approved child only; other unrated content stays blocked |
| Series/episode hierarchy | Series grant covers episodes; episode denial wins; season containers remain navigable |
| Alternative paths | Search, collections, recommendations, trailers/extras, direct item, stream/download and playback-info paths respect effective access |
| Revocation | New playback denied; active-session handling verified on direct play and HLS |
| Lifecycle | Metadata refresh, file replacement, restart, plugin removal and overlapping reconcile do not broaden access |
| Seerr events | Pending/rejected/forged/duplicate/out-of-order events cannot bypass parent decisions |
| Rollback | Original child PG policies restored and allowed/blocked samples pass again |
| Devices | Real Swiftfin Apple TV and iPhone child sessions tested after server API tests |

## Additional acceptance cases

| Test | Pass condition |
| --- | --- |
| Unrated Soccer exception | Confirmed soccer passes; unrated non-soccer and unknown subjects fail |
| Franchise exception | Verified membership matches; similarly named unrelated title does not; individual deny wins |
| Content/time independence | Content grant cannot accidentally bypass the schedule; time grant cannot bypass content denial |
| Morning workout exception | Eligible workout at 07:00 passes; ordinary movie at 07:00 fails; workout after 18:00 fails absent another rule |
| Rule conflicts | Deterministic outcome with visible matching rules; block wins tied priority; explicit override scopes tested |
| Age-band transition | Policy changes only under configured progression; no unrequested expansion at birthday |
| Schedule enforcement | APIs, direct play, transcode/HLS, existing sessions and device clock changes respect server policy |
| Calendar boundaries | DST, timezone changes, weekdays and midnight ranges tested |
| Public examples | Synthetic identities and metadata only; no private household state or credentials |

## Implementation sequence

1. Authorization changes + isolated tests. Preserve upstream behavior for existing
   unrestricted adult accounts; no production migration.
2. Explicit child enrollment, decisions, audit and parent dashboard; build against
   Jellyfin 12.1; exercise two disposable child identities on a test server.
3. Baseline migration, exception enforcement, episode handling and rollback tests.
4. Production installation only after required API/security tests pass, followed
   by actual client verification.
5. Configurable rating/category/subject/franchise rules, deterministic preview and
   explanations; automatic PG defaults and optional Seerr viewing grants after
   their lifecycle and event-validation tests pass.
6. Selective schedule feasibility spike and cross-path authorization tests, then
   runtime enforcement only if proven. Publish feature support accurately.

C#/.NET is required by the Jellyfin plugin interface and existing upstream code.
Rust remains suitable for a later external operator CLI, not a plugin rewrite.
Build toolchain and test-server access must be established before implementation
validation. This design is not a working release.

Implementation amendment, 2026-10-07: exception labels are parent-owned annotations
in the plugin state, not imported metadata tags. This closes a trust boundary that
prefix-based classification would leave open. The current independent guard covers
explicit user IDs. See the implementation and dated native/browser evidence.
