# tools/

Operational scripts that support voxelforge development on the canonical
Windows workstation. Anything that does not belong in the build pipeline or in
a test project lives here.

## Runner health monitor

`RunnerHealth.ps1` probes the self-hosted GitHub Actions runner services and
raises a toast notification if any of them is not RUNNING. `Register-RunnerHealth.ps1`
wires it into Windows Task Scheduler so the check runs hourly (or at any
interval you choose).

### Why this exists

The self-hosted runners (Windows service names
`actions.runner.poetac-voxelforge.<RUNNER_NAME_A>` and `...<RUNNER_NAME_B>`;
pass `-RunnerServiceA` / `-RunnerServiceB` to match your own registration)
only auto-start at boot. After a clean service exit they stay stopped
until somebody runs `Start-Service` manually. The visible symptom is that a
queued workflow appears to be running for the full 30-minute timeout before
failing — by which point the regression is already in the PR queue. The
monitor surfaces the runner-down state inside one polling interval rather than
half an hour after the next push.

### One-off manual check

```powershell
.\tools\RunnerHealth.ps1                     # check both runners, log + toast
.\tools\RunnerHealth.ps1 -RunnerService A    # check runner A only
.\tools\RunnerHealth.ps1 -Quiet -NoToast     # silent (exit code only)
```

Exit codes: `0` all healthy · `1` one or more runners not RUNNING · `2` script
failure (bad arguments, log write failed).

Logs are written to `%LOCALAPPDATA%\voxelforge\runner-health\runner-health-YYYY-MM-DD.log`.
One file per day; entries older than 30 days are pruned automatically the next
time the script runs.

Notifications use `BurntToast` if it is installed
(`Install-Module BurntToast -Scope CurrentUser`). If not, the script falls back
to `msg.exe`, which is present on every Windows install and surfaces a session
dialog. Pass `-NoToast` to suppress notifications entirely.

### Install the hourly Task Scheduler entry

From an **elevated** PowerShell session at the repo root:

```powershell
.\tools\Register-RunnerHealth.ps1
```

This registers a scheduled task named `voxelforge-runner-health` that runs as
SYSTEM, fires every 60 minutes, and invokes `RunnerHealth.ps1 -Quiet`.

Customize:

```powershell
.\tools\Register-RunnerHealth.ps1 -IntervalMinutes 30       # poll every 30 min
.\tools\Register-RunnerHealth.ps1 -TaskName my-task         # alternate task name
.\tools\Register-RunnerHealth.ps1 -WhatIf                   # preview, do not register
```

Inspect or remove:

```powershell
Get-ScheduledTask -TaskName voxelforge-runner-health | Get-ScheduledTaskInfo
.\tools\Register-RunnerHealth.ps1 -Unregister
```

When the task fires under SYSTEM, toast notifications surface in the
SYSTEM session rather than the interactive user session, so `msg.exe` (which
broadcasts to all sessions) is the most reliable surface. If you want the
toast to appear on your interactive desktop, re-register the task with a
user principal — the registrar defaults to SYSTEM so the check survives
user logouts.

## Other scripts

| Script | Purpose |
| --- | --- |
| `fetch-hdri.ps1` | Downloads CC0 HDRi maps for `Voxelforge.Renderer`, hash-verified against `hdri-manifest.json`. |
| `test-hdri-hash-fail.ps1` | Smoke test that `fetch-hdri.ps1` refuses a CDN swap and recovers from local tampering. |
| `gen_propellant_tables.py` | Regenerates CEA-derived propellant tables. |
| `promote_publicapi.py` | Moves `PublicAPI.Unshipped.txt` entries into `PublicAPI.Shipped.txt`. |
