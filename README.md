# AION2-DPS-VIEWER

아이온2용 `DPS 미터 + 아툴 정보 조회` 통합 프로그램입니다.

`DPSVIEWER`는 실시간 전투 정보를 표시하고, 감지된 캐릭터의 아툴 정보와 상세 스킬 정보를 함께 확인할 수 있게 만든 WPF 데스크톱 앱입니다.

## 주요 기능

- 실시간 DPS 집계
- 자동 감지된 캐릭터의 아툴 정보 조회
- 캐릭터 더블클릭 상세창
- 스킬별 딜 비중 / 크리티컬 / 누적 데미지 확인
- 파티 후보 표시
- 간소화 모드
- 투명도 / 집계 시간 / 비활성 삭제 시간 / 단축키 설정

## 실행 파일

- 개발용 실행본: `dist-dev`
- 배포용 실행본: `dist-release`

## 개발용 / 배포용 차이

### 개발용

- 파티 로그 수집 옵션 포함
- 테스트용 설정 확인 가능

### 배포용

- 파티 로그 수집 옵션 제외
- 일반 사용자 배포용 UI 기준 정리

## 실행 방법

관리자 권한으로 실행하는 것을 권장합니다.

```powershell
.\dist-release\Aion2Dashboard.exe
```

또는 개발용:

```powershell
.\dist-dev\Aion2Dashboard.exe
```

## 빌드

```powershell
dotnet publish .\Aion2Dashboard\Aion2Dashboard.csproj -c Release -o .\dist-dev
dotnet publish .\Aion2Dashboard\Aion2Dashboard.csproj -c Release -o .\dist-release -p:DefineConstants=DISTRIBUTION
```

## 주의

- 관리자 권한 실행 권장
- 네트워크/패킷 캡처 환경에 따라 동작 차이가 있을 수 있음
- 일부 기능은 게임 패킷 변화에 따라 추가 보정이 필요할 수 있음

