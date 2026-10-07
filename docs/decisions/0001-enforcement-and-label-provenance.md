# 0001 — Server enforcement and parent-owned labels

- Status: accepted
- Date: 2026-10-07
- Context: Upstream offers self-grants and opt-out; rating gates reject individual
  exceptions, and account-wide schedules reject media-specific time exceptions.
- Decision: Use a deterministic independent rule core, administrator-only controls,
  native content tags and a global MVC filter. Require an independent guard. Store
  confirmed labels separately from imported metadata, with revisioned parent audit.
- Consequences: Existing clients need no new rule engine. Buffered content cannot
  be recalled. Some unauthenticated legacy stream paths must be rejected. Plugin
  failures alone do not disable the independently supervised fallback.

Imported subject/franchise tags can be written by external providers or NFO files.
They must not become access authority merely because they have a special prefix.
Only administrator-confirmed annotations feed exception predicates. AI may propose
annotations in a later feature; it cannot write grants.

Explicit priorities resolve rule conflicts; deny wins a tied priority and hard
blocks cannot be overridden. Unknown classification remains unknown under Not.
Content and time gates must both allow playback. No client-supplied time is used.
