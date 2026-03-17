# AION2-DPS-VIEWER

아이온2용 `DPS 미터 + 캐릭터 정보 조회 + 전투 기록` 통합 프로그램입니다.

`DPSVIEWER`는 게임 화면 위에 오버레이처럼 띄워 실시간 DPS를 확인하고, 감지된 캐릭터의 직업/점수 정보와 전투 기록까지 함께 볼 수 있도록 만든 WPF 데스크톱 앱입니다.

## 현재 버전

- `v4.0.16`
- 기준 날짜: `2026-03-17`

## 최근 수정 사항

- 직업 마크 표기를 `jobCode + PlayNC 공식 직업 아이콘` 기준으로 보강
- 아툴 조회가 늦어도 기존 직업 마크가 덮어써지지 않도록 수정
- 상단 헤더에 감지 상태 표시 추가
  - 초록: `감지중`
  - 노랑: `감지 지연`
  - 빨강: `감지 안됨`
- GitHub 태그 푸시만으로 배포 zip이 자동 릴리즈되도록 GitHub Actions 구성

## 주요 기능

- 실시간 DPS 집계
- 보스 대상 추적 및 보스 DPS 필터
- 자동 감지 캐릭터 정보 조회
- 전투 기록 저장 및 조회
- 간소화 오버레이 모드
- 전역 단축키 지원
- 로그 전송 및 DPS 스냅샷 로그 저장

## 실행 전 준비

### 1. Npcap 설치

- 공식 사이트: [Npcap](https://npcap.com/)
- 다운로드: [Npcap Download](https://npcap.com/#download)

### 2. .NET Desktop Runtime 8 설치

- 공식 페이지: [.NET 8 다운로드](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- 직접 설치: [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime)

## 실행 파일

- 개발용 실행본: `dist-dev-4.0.16`
- 배포용 실행본: `dist-release-4.0.16`

## 실행 방법

관리자 권한으로 실행하는 것을 권장합니다.

```powershell
.\dist-release-4.0.16\Aion2Dashboard.exe
```

또는 개발용:

```powershell
.\dist-dev-4.0.16\Aion2Dashboard.exe
```

## 배포 파일

- 배포용 zip: `DPSVIEWER-release-4.0.16.zip`
- 개발용 zip: `DPSVIEWER-dev-4.0.16.zip`

## GitHub 자동 릴리즈 방법

이 저장소는 태그를 push하면 GitHub Actions가 자동으로 배포 zip을 릴리즈에 업로드하도록 구성되어 있습니다.

예시:

```powershell
git add README.md Aion2Dashboard\AppVersion.cs Aion2Dashboard\MainWindow.xaml .github\workflows\release.yml .github\README-RELEASE.md
git commit -m "Release version 4.0.16"
git pull --rebase origin main
git push
git tag v4.0.16
git push origin v4.0.16
```

동작:

- 로컬 저장소 변경사항 push
- `v4.0.16` 태그 push
- GitHub Actions가 배포판 zip 생성
- GitHub Releases에 `DPSVIEWER-release-4.0.16.zip` 자동 업로드

## 주의

- 관리자 권한 실행 권장
- 네트워크/패킷 캡처 환경에 따라 동작 차이가 있을 수 있음
- 게임 패치나 패킷 변경에 따라 일부 기능은 추가 보정이 필요할 수 있음
