# WindexBar
<img width="376" height="396" alt="image" src="https://github.com/user-attachments/assets/d170b39a-54d5-4adb-8bf3-d79ee332899b" />


### WindexBar is a small Windows tray app for quickly checking Codex usage and status.



## Features

- System tray status for Codex usage, with a compact always-on-top window.
- Account-wide current and weekly rate-limit windows, including reset countdowns when Codex exposes reset times.
- Active Codex model and reasoning effort (including Max and Ultra when exposed).
- Per-session context-window usage and cumulative token totals, grouped by project.
- Background Codex CLI compatibility checks with a 24-hour latest-version cache, automatic updates, an in-app progress gauge, post-update version verification, and an optional manual check button.
- Automatic Codex CLI updates through auto-detected or selected PowerShell, npm, Bun, Homebrew, WinGet, or custom commands.
- The current WindexBar release version is shown in the title bar.
- Live updates for Codex-generated and manually edited session names, with project-free conversations grouped under `default-session`.
- Banked rate-limit reset credit count with exact expiration details from Codex app-server when provided.
- Reset-credit details grouped by exact expiration time, with unavailable expirations shown explicitly.
- Remaining ChatGPT credits when Codex exposes the balance.
- Collapsible sidebar, toggled from the title or a shortcut.
- A dedicated Style sidebar for gauge thickness, a pop-up fill color picker, and animation controls.
- Optional auto-show mode while ChatGPT Desktop or a terminal Codex process is active.
- Settings for refresh interval, language, Windows startup, Alt+O show/hide, and Alt+B sidebar shortcuts.

Reset-credit expiration dates come directly from Codex app-server. If the installed Codex version or backend returns only the available count, WindexBar shows that expiration details are unavailable instead of estimating a date.

## Install

Download and run `WindexBarSetup.exe` from the GitHub Releases page.

Source install:

```powershell
.\install.cmd
```

Source install launches WindexBar after installation by default.
Use `.\install.cmd -NoLaunch` to install without launching, or `.\install.cmd -NoStartup` to skip the Windows startup shortcut.

## Usage

Run the app, then WindexBar appears as an icon in the system tray.

- Left click: open the status window
- Right click: open Settings or Quit
- Click the title: collapse or expand the sidebar
- Open Sessions from the sidebar to inspect per-session usage
- Alt+O: hide or show the WindexBar window
- Alt+B: collapse or expand the sidebar
- Enable Codex auto-show in Settings to show WindexBar only while ChatGPT Desktop or a terminal Codex process is active

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

## License

MIT. See [LICENSE](LICENSE).
