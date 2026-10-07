# Contributing

Maintained by Anthem Flynn. Small, reviewable changes with behavioral evidence are
welcome. Discuss new authorization paths or client compatibility before broad changes.

1. Install the SDK pinned in global.json.
2. Restore with --locked-mode; dependency changes must update packages.lock.json.
3. Run core tests, plugin/guard Release builds, formatting and the native integration
   harness described in docs/testing.md.
4. Use synthetic accounts and generated/permitted media only.
5. Document user-visible behavior, migration effects and observed limitations.

Tests should express policy behavior and access boundaries. Never weaken denial
paths to make a client test pass. New rule fields require unknown handling,
validation, conflict tests and explainable results. Retain original MIT notices.
