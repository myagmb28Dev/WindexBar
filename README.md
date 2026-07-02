# WindexBar
<img width="372" height="457" alt="image" src="https://github.com/user-attachments/assets/5f608937-4362-4450-9327-606bf7b33c4f" />

### WindexBar is a small Windows tray app for quickly checking Codex usage and status.



## Features

- System tray status for Codex usage, with a compact always-on-top window.
- Current/session and weekly rate-limit windows, including reset countdowns when Codex exposes reset times.
- Active Codex model and token usage, including context-window usage when available.
- Banked rate-limit reset credit count and best-effort expiration estimates.
- Reset credit detail view grouped by estimated expiration.
- Remaining ChatGPT credits when Codex exposes the balance.
- Collapsible sidebar, toggled from the title or a shortcut.
- Settings for refresh interval, language, Windows startup, Alt+O show/hide, and Alt+B sidebar shortcuts.

Bank-reset expiration estimates are local best-effort estimates.
They are calculated for banked reset credits observed by WindexBar.
Credits that existed before tracking began may show an unknown expiry.

## Install

Download and run `WindexBarSetup.exe` from the GitHub Releases page.

Source install:

```powershell
.\install.cmd
```

Source install launches WindexBar after installation by default.
Use `.\install.cmd -NoLaunch` to install without launching, or `.\install.cmd -NoStartup` to skip the Windows startup shortcut.

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
- Click the title: collapse or expand the sidebar
- Alt+O: hide or show the WindexBar window
- Alt+B: collapse or expand the sidebar

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
