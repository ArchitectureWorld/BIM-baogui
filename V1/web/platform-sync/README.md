# Web platform synchronization 0.2.0-web.1

This is the Web adaptation of the read-only imported Local 0.2.0 source. The original `V1/platform-sync` is unchanged. Production packages contain this CLI and locked production dependencies; clients never run npm install or download browsers.

The Web controller prepares signed components and provisions `%LOCALAPPDATA%/HIFC/0.5.0-Web/PlatformSync/<sha256(accountId)>`. Configuration, DPAPI entry credential, Edge profile, SQLite tasks, worker logs and spools remain under that account directory. Local paths and fixture adapters are refused. Existing unversioned SQLite and unsupported schema/readers are refused rather than migrated. Every run retains the component, Node, main release/source and schema identity under `web`.

`plan` performs preview only. A user-confirmed `start --apply --web-permit <file>` verifies the server signature and exact account, document/export receipt, project, model/generation, IFC SHA256, run and component binding. The detached child receives a one-use parent proof, re-verifies frozen files, then consumes the permit over ordinary same-origin HTTPS. A signed server consumption is bound to that process's fresh nonce. PostgreSQL permits exactly one consumption per account/run, including across service restarts or recreated local state. Bare `run --apply`, expired/mismatched permits and fixture modes do not grant writes.

The worker keeps its verified authorization in memory while finishing the already-started task. Normal Web login expiry does not terminate it. `resume` reconciles model state without apply or new consumption; report download/retry is independent and cannot replay model replacement. No old component directory is cleaned. A failed or lost launch consumption fails closed; users must inspect state rather than repeat apply.

Node uses the Windows system CA alongside its default roots. The owned parent clears inherited Node options, insecure TLS overrides and environment proxies, sets `NODE_USE_SYSTEM_CA=1` and `NODE_USE_ENV_PROXY=0`, and the detached worker inherits this environment. Edge is detected explicitly and is never downloaded silently.

Build/test on the build machine: `npm ci --ignore-scripts`, `npm test`. `tools/Publish-WebComponents.ps1` builds and prunes dependencies with install scripts disabled, verifies Node's OpenJS signature/Windows x64/version, probes the packaged Web CLI and creates reproducible archives. Test fixture keys and network doubles are only in `tests/`, which is excluded from production packages. Fixture checks are not live platform acceptance.
