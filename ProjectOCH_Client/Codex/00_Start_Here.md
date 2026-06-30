# ProjectOCH Client - Codex Start Here

Last updated: 2026-06-30

이 폴더는 새 Codex 대화를 시작할 때 빠르게 현재 프로젝트 상태를 파악하기 위한 Obsidian 문서 묶음이다.

## 빠른 요약

- Unity 클라이언트 루트: `C:\ProjectOCH\Client`
- Obsidian vault: `C:\ProjectOCH\Client\ProjectOCH_Client`
- Unity 버전: `6000.3.9f1`
- 서버 참고 경로: `C:\ProjectOCH\Server\GameServer`
- 현재 클라이언트는 서버 접속형 2D SRPG를 목표로 한다.
- 필드 이동은 `Vec2Fixed` 월드 좌표 기반이다.
- 필드 이동 가능 영역은 Unity Tilemap에서 exporter로 JSON을 추출하고, 서버도 같은 JSON을 읽어 검증하는 방향이다.

## 먼저 읽을 문서

1. [[01_Project_Snapshot]]
2. [[02_Addressables_Setup]]
3. [[03_Networking_And_Packets]]
4. [[04_Server_Protocol_Notes]]
5. [[05_Known_Issues_And_TODO]]
6. [[06_Work_Log]]
7. [[07_Architecture_Refactor]]

## 현재 핵심 코드

- 앱 진입점:
  - `Assets/Scripts/App/GameRoot.cs`
  - `Assets/Scripts/App/AppServices.cs`
- 네트워크:
  - `Assets/Scripts/Services/Network/NetworkService.cs`
  - `Assets/Scripts/Packet/PacketHandler.cs`
  - `Assets/Scripts/Packet/Generated/PacketManager.cs`
  - `Assets/Scripts/Packet/GameServerSession.cs`
  - `Assets/Scripts/Packet/ServerCore/*.cs`
- 씬 흐름:
  - `Assets/Scripts/Scenes/TitleSceneFlow.cs`
  - `Assets/Scripts/Scenes/FieldSceneAddressableLoader.cs`
- 필드:
  - `Assets/Scripts/Field/FieldObjectManager.cs`
  - `Assets/Scripts/Field/FieldPawnController.cs`
  - `Assets/Scripts/Field/FieldMapWalkArea.cs`
  - `Assets/Scripts/Field/FieldPositionCodec.cs`
  - `Assets/Scripts/Field/CameraController.cs`
- 에디터 도구:
  - `Assets/Editor/MultiplayerBuildAndRun.cs`
  - `Assets/Editor/FieldWalkMapExporter.cs`
  - `Assets/Editor/AddressablesProjectSetup.cs`

## 현재 주의점

- `Field_001`은 Hexagon Grid다. 서버가 단순 rectangle 공식으로 `world -> cell` 변환하면 클라이언트 `Grid.WorldToCell()`과 어긋난다.
- 프로토콜은 아직 `C_MOVE { Vec2Fixed target }` 형태다. 서버는 `Field_001.walkmap.json` 기반으로 이동 가능 여부를 검증한다.
- 클라이언트는 기본적으로 이동 불가 타일 클릭을 먼저 막는다. 서버 검증 로그를 보고 싶으면 `FieldPawnController.sendBlockedMoveForDebug`를 켠다.
- Addressables의 `Remote.LoadPath`는 개발용 `localhost` 기반이다. 배포 전에 실제 URL 전략이 필요하다.
- 멀티 클라 테스트는 Unity 메뉴 `Tools/Project OCH/Multiplayer`에서 빌드/실행한다.
