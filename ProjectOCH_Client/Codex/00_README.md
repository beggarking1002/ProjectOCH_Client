# Project OCH Client 문서 시작 가이드

> 기준일: 2026-07-29. 이 폴더는 Obsidian Vault로 열 수 있는 클라이언트 기술 문서다.

## 문서 순서

1. [01_Architecture.md](01_Architecture.md): 런타임, 씬, 모듈, 스레드 경계
2. [02_Gameplay_Protocol_Data.md](02_Gameplay_Protocol_Data.md): 필드/전투 규칙, 패킷, CSV 계약
3. [03_Current_Status_Workflow.md](03_Current_Status_Workflow.md): 구현 현황, 검증 절차, 남은 작업

## 프로젝트 한눈에 보기

- Unity `6000.3.9f1`, URP 2D 기반의 2D 멀티플레이 턴제 전술 게임 클라이언트다.
- 기본 실행 씬은 `TitleScene`이며, 흐름은 `TitleScene → FieldScene → BattleScene → FieldScene`이다.
- 게임 서버 기본 주소는 `127.0.0.1:7777`이다. 이 저장소에는 서버 구현이 포함되지 않는다.
- 서버가 전투 및 필드 상태의 최종 권위다. 클라이언트의 거리/타일 검사는 입력 UX를 위한 보조 검증이다.
- 맵, Pawn 시각 프리팹, UI, 타일, 스킬 아이콘, CSV는 Addressables로 로드한다.

## 빠른 진입점

| 관심사 | 시작 파일 |
| --- | --- |
| 부트스트랩/전역 서비스 | `Assets/Scripts/App/GameRoot.cs` |
| 네트워크 서비스 | `Assets/Scripts/Services/Network/NetworkService.cs` |
| 씬 흐름 | `Assets/Scripts/Scenes/TitleSceneFlow.cs`, `BattleSceneFlow.cs` |
| 필드 객체/초대 | `Assets/Scripts/Field/FieldObjectManager.cs`, `FieldBattleInviteUI.cs` |
| 전투 상태/입력 | `Assets/Scripts/Battle/BattleObjectManager.cs` |
| 전투 맵/좌표 | `Assets/Scripts/Battle/BattleMapGrid.cs` |
| Pawn 공통 상태/표현 | `Assets/Scripts/Battle/BattlePawn.cs` |
| 전투 UI/턴 큐/결과 | `Assets/Scripts/Battle/BattleUIController.cs`, `BattleTurnQueueView.cs` |
| CSV GameData | `Assets/Scripts/Battle/BattleGameDataRepository.cs` |

## 작업 전 주의사항

- `Assets/Scripts/Packet/Generated/`는 Protobuf 생성 코드다. `.proto` 원본과 생성 절차 없이 직접 수정하지 않는다.
- 전투 좌표는 서버의 pure axial 좌표다. Unity Tilemap cell 좌표와 혼용하지 말고 `BattleMapGrid` 변환 함수를 사용한다.
- Addressables 주소/프리팹/CSV를 변경하면 Unity Editor에서 Addressables 콘텐츠 빌드와 Play Mode 검증까지 수행한다.
- 문서가 바뀌는 구조/계약을 다루는 작업이면 이 Vault도 같은 변경에서 갱신한다.
