# Security

Family Policy is an experimental community project. Use a disposable/staging
server before a household pilot. Real Apple TV/iPhone client validation is pending.

Please report suspected authorization bypasses privately using GitHub's private
vulnerability reporting when enabled; do not include tokens, household data or
working exploit details in a public issue. If private reporting is unavailable,
open an issue asking for a private contact without exploit details.

## Boundaries

- Jellyfin administrators/API keys can change policies and remain trusted.
- Children must use their own accounts. A saved administrator login bypasses child rules.
- A separate guard, supervised with automatic restart, must cover every managed ID.
- Native content tags are a browsing cache; request-time decisions remain authoritative.
- A server cutoff stops future delivery and attempts Stop; already buffered/offline bytes cannot be recalled.
- Unbound anonymous legacy media routes are denied; no claim of universal client compatibility.
- Simultaneous loss of plugin and guard is outside the tested single-failure guarantee.
- Raw household config/state contains private IDs/labels; keep backups private.

See docs/validation/ for executed tests. Security and supported-version claims
must follow actual results. Every release gate should test both allowed and denied
paths, metadata changes, cross-user requests, live streams and failure recovery.
