# Staging installation and rollback

This is an experimental preview. Back up your server configuration and test on
synthetic accounts first. Do not replace an existing household control until the
supported-version and real-client checks in docs/testing.md pass for your setup.

1. Build the maintained src/FamilyPolicy.Plugin project using the pinned SDK.
2. Copy **FamilyPolicy.Plugin.dll and FamilyPolicy.Core.dll only** to a
   FamilyPolicy folder under the test server's data/plugins directory.
3. Restart Jellyfin. Confirm the Family Policy page appears in administrator plugin
   settings. Never install PolicySpike.dll or the retained upstream reference DLL.
4. Build/run the independent src/FamilyPolicy.Guard project with these variables:
   FAMILY_POLICY_SERVER (base URL), FAMILY_POLICY_TOKEN (administrator token from
   secure storage), FAMILY_POLICY_USERS (explicit comma-separated managed UUIDs).
   Do not put credentials in command arguments, compose examples or source control.
5. Use HTTPS outside loopback. Trusted-network HTTP requires explicit
   FAMILY_POLICY_ALLOW_HTTP=true; traffic must stay on a restricted management path.
6. Supervise the guard independently with automatic restart. A rootless pinned
   Dockerfile is provided; container build/runtime is covered by CI; local target deployment remains unverified.
7. Select a non-administrator account, choose the PG preset and timezone, then
   configure exceptions. Search titles and save parent-confirmed labels as needed.
8. Preview decisions, apply, and test allowed/blocked native playback as the child.
   Confirm device accounts are not administrator sessions.

The preview requires the guard's five-second lease for managed playback. After
three failed heartbeat attempts, the guard tries to disable each explicitly
configured non-administrator account through native Jellyfin APIs. It does not
automatically reenable them; recovery requires an administrator review. Timeout,
startup and client-buffer effects mean this is a bounded fallback, not an exact
instantaneous guarantee. Simultaneous plugin/guard failure is not covered.

## Rollback

Use “Restore original account policy” while the plugin is loaded and verify the
original rating/unrated/schedule settings. Then stop the guard and remove the
plugin/restart. Derived metadata tags may remain; restored native policy no
longer references them. Do not remove the plugin first and leave broadened native
rating/time policies without the guard.

If the plugin cannot load, keep affected accounts disabled. Restore native policies
from the private captured original snapshots/configuration backup with administrator
access, verify restrictions, then reenable deliberately. Keep the backup private.
