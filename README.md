# AION2-DPS-VIEWER

아이온2용 `DPS 미터 + 아툴 정보 조회` 통합 프로그램입니다.

`DPSVIEWER`는 실시간 전투 정보를 표시하고, 감지된 캐릭터의 아툴 정보와 상세 스킬 정보, 전투 기록을 함께 확인할 수 있게 만든 WPF 데스크톱 앱입니다.

## 현재 버전

- `v3.0.4`
- 기준 날짜: `2026-03-17`

## v3.0.4 수정 사항

- 보스 패킷 정보와 `mobs.json` 보스 메타데이터를 함께 사용하도록 보강
- 현재 보스 타겟 추적 및 타겟 HP 표시 추가
- 간소화 모드에서 검색창 숨김 및 상위 4명만 표시
- 전투 기록 상세를 캐릭터 목록 중심으로 정리
- 전체 UI 크기와 여백을 축소해 더 타이트하게 정리
- 한글 깨짐 문구 복구
- 업데이트 버튼을 자동 업데이트 실행 버튼으로 변경
- 간소화 모드에서 업데이트 버튼 숨김
- 간소화 모드에서 상태/타겟 요약 패널 숨김
- 간소화 모드 카드 높이와 칩 크기를 줄여 상위 4명이 더 촘촘하게 보이도록 조정

## 주요 기능

- 실시간 DPS 집계
- 자동 감지된 캐릭터의 아툴 정보 조회
- 캐릭터 더블클릭 상세창
- 스킬별 딜 비중 / 치명률 / 누적 데미지 확인
- 전투 기록 저장 / 목록 / 상세 보기
- 파티 후보 표시
- 간소화 모드
- 투명도 / 집계 시간 / 보관 시간 / 단축키 설정

## 실행 전 준비

### 1. Npcap 설치

이 프로그램은 Windows 패킷 캡처 드라이버가 필요합니다.

- 공식 사이트: [Npcap](https://npcap.com/)
- 다운로드: [Npcap Download](https://npcap.com/#download)

설치 후 프로그램은 `관리자 권한`으로 실행하는 것을 권장합니다.

### 2. .NET Desktop Runtime 8 설치

배포 실행 파일은 `.NET Desktop Runtime 8`이 필요합니다.

- 공식 페이지: [.NET 8 다운로드](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- 직접 설치: [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime)

## 실행 파일

- 개발용 실행본: `dist-dev-3.0.4`
- 배포용 실행본: `dist-release-3.0.4`

## 실행 방법

관리자 권한으로 실행하는 것을 권장합니다.

```powershell
.\dist-release-3.0.4\Aion2Dashboard.exe
```

또는 개발용:

```powershell
.\dist-dev-3.0.4\Aion2Dashboard.exe
```

## 빌드

```powershell
dotnet publish .\Aion2Dashboard\Aion2Dashboard.csproj -c Release -o .\dist-dev-3.0.4
dotnet publish .\Aion2Dashboard\Aion2Dashboard.csproj -c Release -o .\dist-release-3.0.4 -p:DefineConstants=DISTRIBUTION
```

## 배포 파일

GitHub 저장소에는 소스코드를 올리고, 실행 파일은 `Releases`에 올리는 방식을 권장합니다.

- 배포용 zip: `DPSVIEWER-release-3.0.4.zip`
- 개발용 zip: `DPSVIEWER-dev-3.0.4.zip`

## 사용 방법

1. 프로그램 실행 후 자동으로 DPS 감지가 시작됩니다.
2. 상단 검색창에서 종족, 서버, 닉네임으로 캐릭터를 검색할 수 있습니다.
3. 실시간 DPS 목록에서 캐릭터를 더블클릭하면 아툴 정보와 스킬 상세를 볼 수 있습니다.
4. `전투 기록` 버튼을 누르면 저장된 전투 목록을 열 수 있습니다.
5. 기록 목록에서 전투를 더블클릭하면 참가자 목록이 열리고, 참가 캐릭터를 더블클릭하면 해당 캐릭터의 스킬 상세를 볼 수 있습니다.
6. `간소화` 버튼을 누르면 검색창이 숨겨지고 상위 4명의 DPS만 표시됩니다.

## 주의

- 관리자 권한 실행 권장
- 네트워크 / 패킷 캡처 환경에 따라 동작 차이가 있을 수 있음
- 게임 패치나 패킷 구조 변경에 따라 일부 기능은 추가 보정이 필요할 수 있음
