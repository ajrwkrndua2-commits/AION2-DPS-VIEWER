# AION2-DPS-VIEWER

아이온2용 `실시간 DPS 미터 + 전투 기록` 통합 프로그램입니다.

`DPSVIEWER`는 게임 화면 위에 오버레이처럼 띄워 실시간 DPS를 확인하고, 보스전 기록과 전투 로그를 함께 관리할 수 있도록 만든 WPF 데스크톱 앱입니다.

## 현재 버전

- `v4.0.32`
- 기준 날짜: `2026-03-18`

## 최근 수정 사항

- 보스 HP 대비 각 캐릭터 누적 데미지 기준으로 카드 배경 바가 차오르도록 수정
- 카드 우측 지분율 숫자도 같은 기준을 따르도록 보강
- 같은 보스 / 같은 시간대 / 같은 타겟 기록은 저장 전에 기존 기록을 덮어써 전투기록 분리 현상 완화
- 로그 자동 전송 제거
- 설정창 `로그 전송` 버튼을 통한 수동 전송 방식 유지
- `WinDivert` 의존 제거, `Npcap / SharpPcap` 기반 캡처 구조 유지

## 주요 기능

- 실시간 DPS 집계
- 보스 타겟 추적 및 보스전 전투 기록 저장
- 전투기록 목록 및 상세 확인
- 오버레이형 UI / 간소화 모드
- 전역 단축키 지원
- 로그 저장 및 수동 로그 전송
- 새 버전 감지 및 업데이트 안내

## 실행 전 준비

### 1. Npcap 설치

패킷 캡처를 위해 `Npcap`이 필요합니다.

- 공식 사이트: [Npcap](https://npcap.com/)
- 다운로드: [Npcap Download](https://npcap.com/#download)

### 2. .NET Desktop Runtime 8 설치

- 공식 페이지: [.NET 8 다운로드](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- 직접 설치: [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime)

## 실행 파일

- 개발용 실행본: `dist-dev-4.0.32`
- 배포용 실행본: `dist-release-4.0.32`

## 실행 방법

관리자 권한으로 실행하는 것을 권장합니다.

배포판:

```powershell
.\dist-release-4.0.32\Aion2Dashboard.exe
```

개발판:

```powershell
.\dist-dev-4.0.32\Aion2Dashboard.exe
```

## 배포 파일

- 배포판 zip: `DPSVIEWER-release-4.0.32.zip`
- 개발판 zip: `DPSVIEWER-dev-4.0.32.zip`

## PowerShell 빌드 / 배포 파일 생성

```powershell
cd "C:\Users\test\Desktop\아이온2 DPS+아툴조회기"

$VERSION = "4.0.32"

$env:APPDATA='C:\Users\test\Desktop\아이온2 DPS+아툴조회기\.appdata'
$env:DOTNET_CLI_HOME='C:\Users\test\Desktop\아이온2 DPS+아툴조회기\.dotnet-home'
$env:NUGET_PACKAGES='C:\Users\test\Desktop\아이온2 DPS+아툴조회기\.nuget-packages'

dotnet build .\Aion2Dashboard\Aion2Dashboard.csproj -c Release -p:RestoreIgnoreFailedSources=true -p:NuGetAudit=false
dotnet publish .\Aion2Dashboard\Aion2Dashboard.csproj -c Release -o ".\dist-dev-$VERSION" -p:RestoreIgnoreFailedSources=true -p:NuGetAudit=false
dotnet publish .\Aion2Dashboard\Aion2Dashboard.csproj -c Release -o ".\dist-release-$VERSION" -p:DefineConstants=DISTRIBUTION -p:RestoreIgnoreFailedSources=true -p:NuGetAudit=false

Compress-Archive -Path ".\dist-dev-$VERSION\*" -DestinationPath ".\DPSVIEWER-dev-$VERSION.zip" -Force
Compress-Archive -Path ".\dist-release-$VERSION\*" -DestinationPath ".\DPSVIEWER-release-$VERSION.zip" -Force
```

## GitHub 반영

```powershell
cd "C:\Users\test\Desktop\아이온2 DPS+아툴조회기"

git add README.md Aion2Dashboard\AppVersion.cs Aion2Dashboard\MainWindow.xaml
git commit -m "Release version 4.0.32"
git pull --rebase origin main
git push
```

## GitHub 릴리즈 자동 업로드

태그를 push하면 GitHub Actions가 배포판 zip을 자동으로 Release에 업로드합니다.

```powershell
cd "C:\Users\test\Desktop\아이온2 DPS+아툴조회기"

git tag 4.0.32
git push origin 4.0.32
```

자동 처리 항목:

- GitHub Actions 실행
- 배포판 빌드
- `DPSVIEWER-release-4.0.32.zip` 생성
- GitHub Release 생성 또는 갱신
- 릴리즈 자산 자동 업로드

## 주의

- 관리자 권한 실행 권장
- Npcap 미설치 시 패킷 수집이 동작하지 않을 수 있음
- 게임 패치나 패킷 구조 변경에 따라 일부 기능은 추가 보정이 필요할 수 있음
- 로그 전송은 설정창의 `로그 전송` 버튼을 눌렀을 때만 실행됨
