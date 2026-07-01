# WindexBar

WindexBar is a small Windows tray app for quickly checking Codex usage and status.

<img width="335" height="505" alt="kr" src="https://github.com/user-attachments/assets/c545974d-044b-4b73-aaeb-5472315f8d1f" />
<img width="341" height="507" alt="eng" src="https://github.com/user-attachments/assets/8cc6a0ca-178b-43ce-8779-8831e01842b9" />

## Features

- System tray status for Codex usage, with a compact always-on-top window.
- Current/session and weekly rate-limit windows, including reset countdowns when Codex exposes reset times.
- Active Codex model and token usage, including context-window usage when available.
- Banked rate-limit reset credit count and best-effort expiration estimates.
- Remaining ChatGPT credits when Codex exposes the balance.
- Settings for refresh interval, language, Windows startup, and the Alt+O show/hide shortcut.

Bank-reset expiration estimates are local best-effort estimates.
They are calculated only for banked reset credits granted since v.1.2.
Credits that existed prior to this update may show an unknown expiry.

## Install

Download and run `WindexBarSetup.exe` from the GitHub Releases page.

## Requirements

- Codex CLI: WindexBar reads Codex usage through `codex app-server`, so the `codex` command must be available on `PATH`.
- Install Codex CLI: [Codex CLI setup](https://developers.openai.com/codex/cli)

Windows PowerShell:

```powershell
powershell -ExecutionPolicy ByPass -c "irm https://chatgpt.com/codex/install.ps1 | iex"
```

Node/npm:

```shell
npm install -g @openai/codex
```

Bun:

```shell
bun install -g @openai/codex
```

Homebrew:

```shell
brew install --cask codex
```

Other downloads: [Codex releases](https://github.com/openai/codex/releases/latest)

## Usage

Run the app, then WindexBar appears as an icon in the system tray.

- Left click: open the status window
- Right click: open Settings or Quit
- Alt+O: hide or show the WindexBar window

## Development

- SDK: `.NET SDK 10.0.301` (`global.json`)
- Local SDK install: [dotnet-install scripts](https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script)
- If the local SDK is missing, `run.cmd` and `build-installer.cmd` use the system-installed `dotnet` or the `dotnet` on `PATH`.

Run:

```powershell
.\run.cmd
```

Test:

```powershell
dotnet test .\tests\WindexBar.Core.Tests\WindexBar.Core.Tests.csproj -p:NuGetAudit=false
```
