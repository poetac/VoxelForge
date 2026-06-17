# Security policy

## Reporting a vulnerability

Please do **not** open a public GitHub issue for security reports. Public
issues are indexed and notify watchers immediately, which can publish an
exploit before it can be patched.

Email reports to: **1543393+poetac@users.noreply.github.com**

Include:
- A description of the issue and the impact you believe it has.
- Steps to reproduce — minimal repro preferred.
- The commit SHA or release tag you observed it on.
- Any proposed remediation, if you have one.

You will receive an acknowledgement within **5 business days**. Triage,
fix timeline, and coordinated-disclosure date are agreed by reply.

## Supported branches

Only `main` is supported. Fixes land on `main`; older commits are not
backported. If you are running a fork or a pinned SHA, rebase onto
current `main` to pick up a fix.

## Scope

In scope:
- Source under `Voxelforge.*` projects in this repository.
- CI workflows under `.github/workflows/`.
- Published artifacts produced by this repository's CI.

Out of scope:
- Vulnerabilities in `PicoGK`, `.NET`, `Avalonia`, `BenchmarkDotNet`,
  `SU2`, or other third-party dependencies — report those upstream.
- Self-hosted runner configuration on contributors' own workstations.
- Findings that require local code-execution as a prerequisite (this is
  a developer-tool repo; running untrusted code locally is out-of-model).

## Safe-harbour

Good-faith research that follows this policy, stays within scope, and
avoids privacy violation, data destruction, and service degradation will
not be subject to legal action by the project maintainers.
