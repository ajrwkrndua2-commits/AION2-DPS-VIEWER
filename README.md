# AION2-DPS-VIEWER

아이온2용 `DPS 미터 + 아툴 정보 조회` 통합 프로그램입니다.

`DPSVIEWER`는 실시간 전투 정보를 표시하고, 감지된 캐릭터의 아툴 정보와 상세 스킬 정보, 전투 기록을 함께 확인할 수 있게 만든 WPF 데스크톱 앱입니다.

## 현재 버전

- `v3.1.3`
- 기준 날짜: `2026-03-17`

## v3.1.3 수정 사항

- 보스 HP 표시를 `스폰 HP + 누적 피해` 기준으로 보강
- 보스 HP를 아직 못 읽은 경우 `HP 추적 중 / 누적 피해` 안내 표시 추가
- 버전 `v3.1.3` 반영

## 주요 기능

- 실시간 DPS 집계
- 자동 감지된 캐릭터의 아툴 정보 조회
- 캐릭터 더블클릭 상세창
- 스킬별 딜 비중 / 크리티컬 / 누적 데미지 확인
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

- 개발용 실행본: `dist-dev-3.1.3`
- 배포용 실행본: `dist-release-3.1.3`

## 실행 방법

관리자 권한으로 실행하는 것을 권장합니다.

```powershell
.\dist-release-3.1.3\Aion2Dashboard.exe
```

또는 개발용:

```powershell
.\dist-dev-3.1.3\Aion2Dashboard.exe
```

## 빌드

```powershell
dotnet publish .\Aion2Dashboard\Aion2Dashboard.csproj -c Release -o .\dist-dev-3.1.3
dotnet publish .\Aion2Dashboard\Aion2Dashboard.csproj -c Release -o .\dist-release-3.1.3 -p:DefineConstants=DISTRIBUTION
```

## 배포 파일

GitHub 저장소에는 소스코드를 올리고, 실행 파일은 `Releases`에 올리는 방식을 권장합니다.

- 배포용 zip: `DPSVIEWER-release-3.1.3.zip`
- 개발용 zip: `DPSVIEWER-dev-3.1.3.zip`

## 주의

- 관리자 권한 실행 권장
- 네트워크/패킷 캡처 환경에 따라 동작 차이가 있을 수 있음
- 게임 패치나 패킷 변화에 따라 일부 기능은 추가 보정이 필요할 수 있음
