# Workspace Organizer

Keep personal and work logins, sessions and subscriptions apart — within a single OS user account.

- **Workspaces** (Work and Personal by default) each get their own browser profile and app data folders, so
  cookies, sessions and saved logins never mix.
- **Accounts**: which service, login and 2FA method belongs to which workspace. "Open in workspace browser"
  launches the login URL in that workspace's isolated browser.
- **Subscriptions**: cost, billing cycle and renewal date per workspace, with monthly/yearly totals per currency
  and a heads-up for renewals in the next 14 days.

Only metadata is stored. Passwords stay in your password manager or in the workspace's browser profile.

## Profile folders

Every workspace owns a folder under the profiles root (default `~\Profiles`, configurable in Settings):

```
~\Profiles\
  .shims\              claude.exe, gh.exe, git.exe … (installed on demand)
  work\
    .wso-profile.json  generated: the profile's environment (read by wso and the shims)
    activate.ps1/.cmd  generated: sets the environment and a "[work]" prompt
    bin\               put on PATH first: profile-specific programs
    config\<tool>\     each CLI tool's login and settings
    browser\<browser>\ isolated browser data
    apps\<app>\        {profileDir} for app shortcuts
    …                  your own files and repos for this workspace
```

A program uses a profile's logins when it is started:

1. **from the workspace's terminal** (*Open terminal*, or `wso shell work`), or
2. **from any folder inside the profile folder**, through the command shims: after *Settings → Install shims*,
   `cd ~\Profiles\work\some-repo; claude` uses the work login in any terminal. Outside profile folders the
   shims run the tool unchanged.

### Command-line tools

| Tool | Env var |
|---|---|
| Claude Code (`claude`) | `CLAUDE_CONFIG_DIR` |
| GitHub CLI (`gh`) | `GH_CONFIG_DIR` |
| Git | `GIT_CONFIG_GLOBAL` → `config\git\.gitconfig` (includes your real `~\.gitconfig`; sets `credential.namespace` so Git Credential Manager stores separate credentials) |
| AWS CLI | `AWS_CONFIG_FILE`, `AWS_SHARED_CREDENTIALS_FILE` |
| Azure CLI | `AZURE_CONFIG_DIR` |
| Google Cloud CLI | `CLOUDSDK_CONFIG` |
| npm | `NPM_CONFIG_USERCONFIG` |
| Docker CLI | `DOCKER_CONFIG` |
| kubectl | `KUBECONFIG` |

Tools can be turned off per workspace. For tools without their own setting, a workspace can redirect the whole
home folder (`USERPROFILE`, `HOME`, `APPDATA`, `LOCALAPPDATA`) or set extra `KEY=VALUE` variables. Logins kept
in the Windows Credential Manager can't be separated by env vars alone.

Set a profile's git identity in `~\Profiles\<profile>\config\git\.gitconfig`; the organizer creates that file once
and never overwrites it.

### `wso` CLI

```
wso which [command]               which profile applies here, and why
wso list                          list profiles
wso run <profile> [--] <cmd> ...  run a command with a profile's logins
wso shell <profile>               PowerShell with the profile activated
wso env <profile>                 print the profile's environment
wso shims install | uninstall     manage the command shims on your user PATH
```

Shims are renamed copies of `wso.exe`. Windows searches the system PATH before the user PATH, so a tool installed
system-wide (e.g. Git in Program Files) is only shimmed in profile terminals unless `.shims` is put first in your
PowerShell `$PROFILE`; the installer tells you which tools this affects.

### Browsers

| Browser | No profile name set (isolated, default) | Profile name set |
|---|---|---|
| Chrome, Edge, Brave, Vivaldi | `--user-data-dir=<profile>\browser\<browser>` | `--profile-directory=<name>` |
| Firefox | `-profile <profile>\browser\firefox` | `-P <name>` |

An isolated data dir is a separate browser instance; a named profile reuses one of the browser's existing profiles.

Apps can be isolated the same way: put `{profileDir}` in an app's arguments and it expands to a per-workspace
folder, e.g. VS Code or Electron apps such as Slack: `--user-data-dir "{profileDir}"`. App shortcuts also get the
profile's environment.

Removing a workspace asks for confirmation (showing the folder and its size) and then moves its whole profile
folder to the Recycle Bin. A workspace with accounts or subscriptions must be emptied first.

Data lives in `%APPDATA%\WorkspaceOrganizer\organizer.json` and is saved automatically. An unreadable file is
moved aside as `organizer.json.corrupt-<timestamp>` instead of being overwritten.

## Build and run

Requires the .NET 10 SDK.

```sh
dotnet run --project src/WorkspaceOrganizer.App
dotnet test
```

## Layout

- `src/WorkspaceOrganizer.Core` — models, storage, profiles (layout, environment, resolver), launching, shims, subscription math (no UI).
- `src/WorkspaceOrganizer.Cli` — the `wso` command and shims.
- `src/WorkspaceOrganizer.App` — Avalonia 12 desktop UI (MVVM with CommunityToolkit.Mvvm).
- `tests/WorkspaceOrganizer.Core.Tests` — xUnit tests for Core.
