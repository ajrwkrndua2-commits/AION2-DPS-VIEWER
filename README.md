# AION2-DPS-VIEWER

아이온2용 `DPS 미터 + 아툴 정보 조회` 통합 프로그램입니다.

`DPSVIEWER`는 실시간 전투 정보를 표시하고, 감지된 캐릭터의 아툴 정보와 상세 스킬 정보를 함께 확인할 수 있게 만든 WPF 데스크톱 앱입니다.

## 현재 버전

- `v1.0.3`
- 기준 날짜: `2026-03-17`

## 최근 수정 내역

- `26.03.17 06:22` - 본인 닉네임 힌트 입력 시 본인 캐릭터를 최상단에 우선 표기
- `26.03.17 06:22` - DPS 집계 시간 범위 조정 및 설정 최대값 확장
- `26.03.17 06:22` - 새 배포 파일이 있을 때 안내 멘트가 나오도록 업데이트 확인 기능 추가
- `26.03.17 06:47` - 게임 중에도 `Ctrl+F1`, `Ctrl+R` 전역 단축키가 동작하도록 수정
- `26.03.17 07:05` - 패치가 생기면 새 버전으로 바로 감지되도록 버전 체계를 `v1.0.2`로 상향
- `26.03.17 07:20` - 업데이트 확인 결과가 상태줄에 남고 설정창에서 수동 확인 가능하도록 보강
- `26.03.17 07:20` - 단축키 변경 후 전역 단축키가 다시 등록되도록 보강
- `26.03.17 07:20` - 보스만 집계 옵션이 보스 타겟에만 정확히 적용되도록 수정

## v1.0.3 수정 사항

- 업데이트 확인 구조 보강
- 설정창 수동 업데이트 확인 버튼 추가
- 전역 단축키 재등록 보강
- 보스만 집계 필터 정확도 개선

## 주요 기능

- 실시간 DPS 집계
- 자동 감지된 캐릭터의 아툴 정보 조회
- 캐릭터 더블클릭 상세창
- 스킬별 딜 비중 / 크리티컬 / 누적 데미지 확인
- 파티 후보 표시
- 간소화 모드
- 투명도 / 집계 시간 / 비활성 삭제 시간 / 단축키 설정

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

- 개발용 실행본: `dist-dev-1.0.3`
- 배포용 실행본: `dist-release-1.0.3`

## 실행 방법

관리자 권한으로 실행하는 것을 권장합니다.

```powershell
.\dist-release-1.0.3\Aion2Dashboard.exe
```

또는 개발용:

```powershell
.\dist-dev-1.0.3\Aion2Dashboard.exe
```

## 빌드

```powershell
dotnet publish .\Aion2Dashboard\Aion2Dashboard.csproj -c Release -o .\dist-dev-1.0.3
dotnet publish .\Aion2Dashboard\Aion2Dashboard.csproj -c Release -o .\dist-release-1.0.3 -p:DefineConstants=DISTRIBUTION
```

## 배포 파일

GitHub 저장소에는 소스코드를 올리고, 실행 파일은 `Releases`에 올리는 방식을 권장합니다.

- 배포용 zip: `DPSVIEWER-release-1.0.3.zip`
- 개발용 zip: `DPSVIEWER-dev-1.0.3.zip`

## 주의

- 관리자 권한 실행 권장
- 네트워크/패킷 캡처 환경에 따라 동작 차이가 있을 수 있음
- 일부 기능은 게임 패킷 변화에 따라 추가 보정이 필요할 수 있음
