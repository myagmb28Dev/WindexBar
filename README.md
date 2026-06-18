# Win-CodexBar

Windows-first CodexBar v1 implementation. The first vertical slice supports Codex usage in a Windows system tray app plus a small CLI.

## Toolchain

This repo carries a local .NET SDK under `.dotnet/` when bootstrapped by Codex. Use it explicitly if `dotnet` is not on PATH:

```powershell
.\.dotnet\dotnet.exe --info
```

## Build and test

```powershell
.\.dotnet\dotnet.exe test .\WinCodexBar.slnx
.\.dotnet\dotnet.exe build .\WinCodexBar.slnx
```

## Run

```powershell
.\.dotnet\dotnet.exe run --project .\src\CodexBar.Cli -- usage --provider codex --json
.\.dotnet\dotnet.exe run --project .\src\CodexBar.Windows
```

The Windows app opens the Codex status window on launch and also stays available in the system tray. Left-click the tray icon to reopen the status window; right-click opens Refresh, Settings, and Quit.
