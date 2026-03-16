# Aion2Dashboard

`A2Viewer` 느낌의 카드형 UI와 `Aion2-Dps-Meter-Packet-Process`의 패킷 처리 방식을 합친 통합 대시보드입니다.

## 포함 기능

- 아툴 서버 목록 자동 로드
- 종족 / 서버 / 닉네임 기반 캐릭터 검색
- 검색한 캐릭터를 카드 UI로 유지
- `PacketProcessor.dll` + `WindivertDotnet` 기반 실시간 DPS 집계
- DPS에서 감지된 캐릭터명을 아툴 카드로 자동 동기화

## 필요 조건

- Windows 10/11
- 관리자 권한
- .NET SDK 8 이상
- NuGet 복원 가능 환경

## 실행

```powershell
dotnet restore
dotnet run --project .\Aion2Dashboard\Aion2Dashboard.csproj
```

## 참고

- 아툴 검색은 `https://aion2tool.com`의 공개 웹 API를 사용합니다.
- DPS 미터는 원본 저장소의 `PacketProcessor.dll`을 함께 포함합니다.
- `A2Viewer`는 스크린샷 기반으로 UI를 재구성한 버전이며, 원본과 동일한 파티 패킷 감지 로직까지는 아직 포함하지 않았습니다.
