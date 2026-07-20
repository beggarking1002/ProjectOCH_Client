# Project OCH Client — 세션 시작 가이드

> 새 Codex 세션에서는 이 문서부터 읽고, 이어서 아래 링크의 문서를 순서대로 읽는다.

## 문서 지도

1. [01_Architecture.md](01_Architecture.md) — 실행 흐름, 장면, 런타임 구성
2. [02_Gameplay_Protocol_Data.md](02_Gameplay_Protocol_Data.md) — 필드/전투 동작, 서버 패킷, 게임 데이터
3. [03_Current_Status_Workflow.md](03_Current_Status_Workflow.md) — 현재 구현 상태, 검증 방법, 작업 규칙

## 프로젝트 한 줄 설명

Unity 6로 제작 중인 2D 멀티플레이어 전술 게임 클라이언트다. 필드에서 다른 플레이어에게 전투를 신청하고, 서버가 권위(authoritative)를 갖는 턴제 헥스 전투를 진행한다.

## 작업 전 반드시 확인할 것

- Unity 버전: `6000.3.9f1`
- 기본 실행 장면: `TitleScene`
- 서버 기본 주소: `127.0.0.1:7777`
- 현재 작업 트리에는 **커밋되지 않은 전투 UI / GameData / 스킬 아이콘 / Addressables / Pawn 프리팹 변경**이 있다. 기존 변경을 덮어쓰거나 되돌리지 않는다.
- `Assets/Scripts/Packet/Generated/`의 Protobuf 생성 코드는 직접 수정하지 않는다. 프로토콜 변경은 서버 원본 `.proto` 및 생성 절차에서 처리해야 한다.

## 빠른 코드 진입점

| 관심사 | 첫 파일 |
| --- | --- |
| 앱 부트스트랩 / 서비스 | `Assets/Scripts/App/GameRoot.cs` |
| 네트워크 송수신 | `Assets/Scripts/Services/Network/NetworkService.cs` |
| 장면 전환 | `Assets/Scripts/Scenes/TitleSceneFlow.cs`, `BattleSceneFlow.cs` |
| 필드 플레이어 | `Assets/Scripts/Field/FieldObjectManager.cs` |
| 전투 상태 반영 / 입력 | `Assets/Scripts/Battle/BattleObjectManager.cs` |
| 전투 맵 / 좌표 변환 / 타일 Overlay | `Assets/Scripts/Battle/BattleMapGrid.cs` |
| 전투 Pawn 공통 상태 / 팀 링 | `Assets/Scripts/Battle/BattlePawn.cs` |
| Beige Ice 고유 Aura | `Assets/Scripts/Battle/BeigeIce.cs` |
| 전투 UI | `Assets/Scripts/Battle/BattleUIController.cs` |
| CSV GameData | `Assets/Scripts/Battle/BattleGameDataRepository.cs` |

## 현재 개발 원칙

- 전투의 판정과 상태 전이는 서버가 최종 결정한다.
- 클라이언트 검증은 잘못된 입력을 줄이기 위한 UX 보조일 뿐, 보안 또는 최종 판정이 아니다.
- 런타임 콘텐츠는 Addressables 주소로 로드한다. 새 런타임 에셋을 추가하면 Addressables 등록과 빌드 반영까지 확인한다.
- Battle 프로토콜의 좌표는 순수 axial이다. Unity Tilemap cell 좌표를 패킷에 직접 사용하지 않는다.
- 기능을 수정하면 가능한 한 C# 빌드와 Unity Play Mode 흐름을 모두 확인한다.
