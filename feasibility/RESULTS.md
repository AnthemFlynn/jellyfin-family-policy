# Feasibility evidence — 2026-10-07

## Observed execution

| Check | Result |
| --- | --- |
| Server | Unmodified Jellyfin tag v12.1, commit ee91c75e777da41a9c4f4855e70adc604fbf2ef8; git diff empty |
| Server build | .NET SDK 10.0.401, Release; zero warnings/errors |
| Plugin build | Jellyfin.Controller 12.1.0; zero warnings/errors |
| Extension loading | Test plugin loaded through IPluginServiceRegistrator; global MVC resource filter applied to native controller |
| Public system endpoint | HTTP 200 with filter marker |
| Synthetic workout decision | HTTP 200 |
| Synthetic movie/missing classification | HTTP 403 |
| Native stream route interception | Authenticated /Videos/{id}/stream, HTTP 403 before native action |
| Native HLS route interception | Authenticated /Videos/{id}/hls1/p/0.ts, HTTP 403 before native action |
| Native playback-info interception | Authenticated /Items/{id}/PlaybackInfo, HTTP 403 before native action |
| Continuous synthetic stream | HTTP 200 began; server abort at 3 seconds; curl exit 56 at 3.02 seconds, 57,344 bytes received |
| Physical-file response | 1 GiB sparse synthetic file, MVC PhysicalFile result, range processing enabled; server abort at 3 seconds; curl exit 56 at 4.34 seconds, 4,538,316 bytes received |
| Network | Test server bound only to loopback, port 18096; discovery/UPnP disabled |
| Production | No production plugin installation or account-policy changes |

## Conclusion and boundary

A plugin can add server-side request-time authorization and abort ongoing MVC
file/stream responses without editing Jellyfin's source. This validates the
architectural extension path for content-specific schedule rules. Pure rule
composition (rating/age/category/subject/franchise/title overrides) does not
require a core rewrite.

This is not a completed policy engine or a parental-security certification.
Native-route probes used synthetic missing item IDs and a test administrator;
real child authorization, actual media/HLS output and device clients remain
release tests. The synthetic query parameters are not a production identity or
metadata mechanism. The spike is unsafe for production and has no manifest.

A server can stop supplying more bytes and issue client Stop commands; it cannot
recall bytes already buffered/downloaded. The physical-file measurement itself
shows that client receipt can outlast the abort deadline. Do not promise an exact
visible frame cutoff across all unmodified clients. Offline copies are outside
server enforcement; downloading remains disabled for managed accounts.

Some legacy segment/subtitle routes lack authentication in the reviewed server.
Production enforcement must reject unbound anonymous media requests or establish
a securely validated playback binding; there must be no blanket anonymous bypass.
Some old clients may consequently be unsupported. Test current Swiftfin first.

Fail-closed operation if the plugin fails to load needs an external health guard
or another independently enforced fallback; native content tags alone cannot
replace a dynamic schedule. This is a deployment requirement, not a Jellyfin
rewrite. A guard can disable managed accounts/restore conservative native
schedules through existing administrator APIs. Race windows must be measured.

## Reproduction

Build unmodified Jellyfin v12.1 with the pinned SDK and official Jellyfin FFmpeg.
Create a fresh disposable data/config directory; configure port 18096 and bind
127.0.0.1 only, disable discovery/UPnP; run without the web client. Build
PolicySpike/PolicySpike.csproj in Release and put only PolicySpike.dll into a
PolicySpike folder under the test data/plugins directory before starting server.
Create a private sparse 1 GiB file at the system temporary path named
family-policy-spike.bin. Run check_spike.py, then check_native_routes.py (the
latter initializes a fresh disposable administrator; do not rerun on an existing
household/test database). Capture sanitized outputs. Stop the test server and
remove the synthetic file afterward. Never copy the spike into production.
