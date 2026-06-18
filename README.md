# Win CodexBar

Win CodexBar는 현재 Codex 세션의 사용량 한도를 Windows 트레이에서 확인하는 작은 앱입니다.

## 설치 방법

### Release

터미널을 쓰지 않는 일반 사용자용 설치 방법입니다.

GitHub Release에서 `WinCodexBarSetup.exe`를 내려받아 실행하세요.

### winget

터미널에서 명령줄로 설치하려는 사용자용 설치 방법입니다.

```powershell
winget install --id WinCodexBar.WinCodexBar -e
```

### 소스에서 설치

이 저장소를 clone 받은 개발자/기여자용 설치 방법입니다.

```powershell
.\install.cmd
```

## 사용

설치 후 앱은 시스템 트레이에 남아 있습니다. 트레이 아이콘을 왼쪽 클릭하면 상태 창을 열고, 오른쪽 클릭하면 Settings와 Quit을 사용할 수 있습니다.

## 개발 실행

소스에서 바로 실행하려면:

```powershell
.\run.cmd
```
