# Compatibility and validation

## Executed local evidence

Local native/browser acceptance passed on unmodified Jellyfin 12.1 and 12.2.
See validation/2026-10-07.json and validation/2026-10-07-server-12.2.json for
sanitized reports. The 28 core behavioral tests passed on macOS arm64.
Core tests express policy behavior; the integration harness builds a fresh,
loopback-only server, generates permitted media, creates synthetic identities,
tests privileged/child paths and removes successful disposable data.

The browser uses real Chromium and calls native Jellyfin APIs; a small ApiClient
shim supplies the host interface. It proves form behavior, not the installed
Jellyfin web shell or Swiftfin UI. Apple TV/iPhone device acceptance is pending.

Native acceptance covers administrator-only endpoints, imported-label rejection,
individual R exception, denied other child, series grant/episode block, catalog
visibility, actual file and HLS segment delivery, timed ongoing physical-file
cutoff, post-cutoff denial, cross-user query rejection, legacy segment rejection,
original-policy restoration and independent fallback after plugin removal.

## Run locally

Build core/plugin/guard with the pinned SDK. Build an **unmodified** Jellyfin tag
v12.1 or v12.2 separately, with official Jellyfin FFmpeg 8.1.3-1. Then:

```sh
python -m pip install -r tests/integration/requirements.txt
python -m playwright install chromium
export DOTNET_EXE="$(command -v dotnet)"
export JELLYFIN_SERVER_DLL="/path/to/unmodified/server/jellyfin.dll"
export JELLYFIN_FFMPEG="/path/to/jellyfin-ffmpeg/ffmpeg"
FAMILY_POLICY_BROWSER_TESTS=1 python tests/integration/run.py
```

Port 18097 must be free. The harness uses fresh private temporary directories,
no household media/credentials. Failed diagnostics are retained locally for
inspection; successful directories are removed. Never upload raw diagnostics.

## Remaining production release gates

- Actual installed web-shell and Apple TV/iPhone child sessions, including buffered
  playback at cutoffs and reconnection.
- Wider routes: universal audio, remote sources, extras/trailers, attachments,
  subtitles and alternate-source/edition attempts; unsupported paths fail closed.
- Labels and policy persistence across replacement IDs; this preview does not
  auto-rematch provider IDs.
- Large-library metadata/reconciliation latency and native pagination count accuracy
  during cache lag; streamed access is evaluated separately.
- Guard container/process supervision and failure latency on target deployments.
- Independent community review before describing the preview as production-ready.

Automated build matrices do not imply device compatibility. Update this page only
from dated execution results, not assumptions.
