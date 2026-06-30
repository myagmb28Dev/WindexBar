# WindexBar

WindexBar is a small Windows tray app for quickly checking Codex usage and status.

## English

### Install

Download and run `WindexBarSetup.exe` from the GitHub Releases page.

### Usage

Run the app, then WindexBar appears as an icon in the system tray.

- Left click: open the status window
- Right click: open Settings or Quit
- Alt+O: hide or show the WindexBar window

### Development

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

## 한국어

WindexBar는 Codex 사용량과 상태를 Windows 트레이에서 빠르게 확인하는 작은 앱입니다.

### 설치

GitHub Releases 페이지에서 `WindexBarSetup.exe`를 내려받아 실행합니다.

### 사용

앱을 실행하면 시스템 트레이에 WindexBar 아이콘이 표시됩니다.

- 왼쪽 클릭: 상태 창 열기
- 오른쪽 클릭: Settings 또는 Quit 열기
- Alt+O: WindexBar 창 숨기기 또는 다시 나타내기

### 개발

- SDK: `.NET SDK 10.0.301` (`global.json`)
- 로컬 SDK 설치: [dotnet-install scripts](https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script)
- 로컬 SDK가 없으면 `run.cmd`와 `build-installer.cmd`는 시스템에 설치된 `dotnet` 또는 `PATH`의 `dotnet`을 사용합니다.

실행:

```powershell
.\run.cmd
```

테스트:

```powershell
dotnet test .\tests\WindexBar.Core.Tests\WindexBar.Core.Tests.csproj -p:NuGetAudit=false
```
