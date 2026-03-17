# AION2-DPS-VIEWER

아이온2용 `DPS 미터 + 아툴 정보 조회` 통합 프로그램입니다.

`DPSVIEWER`는 실시간 전투 정보를 표시하고, 감지된 캐릭터의 아툴 정보와 스킬 정보, 전투 기록을 함께 확인할 수 있게 만든 WPF 데스크톱 앱입니다.

## 현재 버전

- `v3.1.0`
- 기준 날짜: `2026-03-17`

## v3.1.0 수정 사항

- 간소화 모드에서 캐릭터 카드 사이 세로 여백 최소화
- 간소화 모드에서 카드 목록 상단 여백 제거
- 간소화 모드 카드 내부 패딩 추가 축소
- 간소화 모드의 좌측 클래스 표기를 임시 마크 대신 실제 직업 아이콘 우선 표시로 변경

## 주요 기능

- 실시간 DPS 집계
- 자동 감지된 캐릭터의 아툴 정보 조회
- 캐릭터 더블클릭 상세창
- 스킬별 딜 비중 / 크리티컬 / 누적 데미지 확인
- 전투 기록 저장 / 목록 / 상세 보기
- 파티 후보 표시
- 간소화 모드
- 투명도 / 집계 시간 / 보관 시간 / 비활성 삭제 시간 / 단축키 설정

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

- 개발용 실행본: `dist-dev-3.1.0`
- 배포용 실행본: `dist-release-3.1.0`

## 실행 방법

관리자 권한으로 실행하는 것을 권장합니다.

```powershell
.\dist-release-3.1.0\Aion2Dashboard.exe
```

또는 개발용:

```powershell
.\dist-dev-3.1.0\Aion2Dashboard.exe
```

## 빌드

```powershell
dotnet publish .\Aion2Dashboard\Aion2Dashboard.csproj -c Release -o .\dist-dev-3.1.0
dotnet publish .\Aion2Dashboard\Aion2Dashboard.csproj -c Release -o .\dist-release-3.1.0 -p:DefineConstants=DISTRIBUTION
```

## 배포 파일

GitHub 저장소에는 소스코드를 올리고, 실행 파일은 `Releases`에 올리는 방식을 권장합니다.

- 배포용 zip: `DPSVIEWER-release-3.1.0.zip`
- 개발용 zip: `DPSVIEWER-dev-3.1.0.zip`

## 사용 방법

1. 프로그램 실행 시 자동으로 DPS 감지가 시작됩니다.
2. 일반 모드에서는 상단 검색창으로 종족, 서버, 닉네임 기준 검색이 가능합니다.
3. 실시간 DPS 목록에서 캐릭터를 더블클릭하면 아툴 정보와 스킬 상세를 볼 수 있습니다.
4. `전투 기록` 버튼으로 저장된 전투 기록 목록을 열 수 있습니다.
5. 간소화 모드에서는 오버레이형으로 상위 DPS만 간단히 표시됩니다.

## 주의

- 관리자 권한 실행 권장
- 네트워크 / 패킷 캡처 환경에 따라 동작 차이가 있을 수 있음
- 게임 패치나 패킷 구조 변경에 따라 일부 기능은 추가 보정이 필요할 수 있음
