# WindexBar

Codex 사용량 한도를 Windows 트레이에서 빠르게 확인하는 작은 앱입니다.

## 설치

| 대상 | 방법 |
| --- | --- |
| 일반 사용자 | GitHub Release에서 `WindexBarSetup.exe`를 내려받아 실행 |
| 터미널 사용자 | `winget install --id myagmb28Dev.WindexBar -e` |
| 소스 사용자 | 저장소 루트에서 `.\install.cmd` 실행 |

## 사용

앱을 실행하면 시스템 트레이에 아이콘이 표시됩니다.

- 왼쪽 클릭: 상태 창 열기
- 오른쪽 클릭: Settings, Quit

## 개발

소스에서 바로 실행하려면:

```powershell
.\run.cmd
```

## Release automation

Run the GitHub Actions `Release` workflow to test, build the installer, publish a GitHub Release, and optionally submit a winget update PR.

Required repository secrets:

- `WINGET_CREATE_GITHUB_TOKEN`: GitHub PAT for `wingetcreate` to submit winget PRs.
- `WINDOWS_CODESIGN_PFX_BASE64`: Optional Base64-encoded PFX code signing certificate.
- `WINDOWS_CODESIGN_PFX_PASSWORD`: Optional PFX password.

After the first winget registration PR is merged, run the `Release` workflow with the next version number for each update.
