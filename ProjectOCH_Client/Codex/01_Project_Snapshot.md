# Project Snapshot

Last updated: 2026-06-30

## 환경

- Project root: `C:\ProjectOCH\Client`
- Unity: `6000.3.9f1`
- Render pipeline: Universal RP / 2D Renderer
- Input: Input System package
- Protobuf DLL: `Assets/Libs/Google.Protobuf.dll`
- Addressables package: `com.unity.addressables` `2.9.1`

## 주요 폴더

- `Assets/Editor`
  - Addressables 초기화, 멀티 클라 빌드/실행, 필드 walkmap exporter.
- `Assets/AddressableAssetsData`
  - Addressables 설정과 그룹.
- `Assets/Scripts/App`
  - `GameRoot`, `AppServices`.
- `Assets/Scripts/Services/Network`
  - 실제 네트워크 서비스인 `NetworkService`.
- `Assets/Scripts/Packet`
  - generated proto, `PacketManager`, `PacketHandler`, session, ServerCore 포팅 코드.
- `Assets/Scripts/Scenes`
  - `TitleSceneFlow`, `FieldSceneAddressableLoader`.
- `Assets/Scripts/Field`
  - 필드 pawn, 카메라, 좌표 codec, walk area, object manager.
- `Assets/GameData/Maps`
  - exporter가 생성하는 walkmap JSON.

## 현재 런타임 구조

```text
GameRoot
 -> AppServices
     -> NetworkService
         -> Connector
         -> GameServerSession
         -> PacketManager / PacketHandler
```

`GameRoot`는 `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`로 `@GameRoot`를 자동 생성한다. `Application.runInBackground = true`를 설정해서 멀티 클라 테스트 중 포커스가 없는 창도 네트워크 tick을 계속 돈다.

## 씬 흐름

```text
TitleScene
 -> GameStartButton 클릭
 -> C_ENTER_GAME 전송
 -> S_ENTER_GAME success
 -> FieldScene 로드
 -> FieldSceneAddressableLoader가 Field, WorldMapRoot 로드
 -> FieldObjectManager가 내 pawn과 기존/신규 player pawn 관리
```

`TitleSceneFlow`는 실행 인자 `-playerIndex`를 읽어서 `C_ENTER_GAME.PlayerIndex`에 넣는다.

## 필드 구성

- `FieldSceneAddressableLoader`
  - Addressable `Field` 프리팹 로드.
  - Addressable `WorldMapRoot` 로드.
  - `FieldMapWalkArea`, `FieldObjectManager`를 붙이거나 초기화.
- `FieldObjectManager`
  - `S_ENTER_GAME`, `S_SPAWN`, `S_DESPAWN`, `S_MOVE` 이벤트를 받아 pawn 생성/삭제/이동.
  - `NetworkService.GetKnownPlayersSnapshot()`을 이용해 씬 전환 중 받은 player 상태를 복구.
- `FieldPawnController`
  - 마우스 클릭으로 `C_MOVE` 요청.
  - 서버 `S_MOVE`를 받아 이동.
  - 왼쪽 이동 시 sprite flip.
  - `Animator.IsMoving` 제어.
- `CameraController`
  - 런타임에 MainCamera에 붙고 내 pawn을 따라간다.

## 빌드/테스트

멀티 클라 테스트 메뉴:

```text
Tools/Project OCH/Multiplayer/Build Client Only
Tools/Project OCH/Multiplayer/Build And Run/2 Players
Tools/Project OCH/Multiplayer/Build And Run/3 Players
Tools/Project OCH/Multiplayer/Build And Run/4 Players
Tools/Project OCH/Multiplayer/Run Existing/1~4 Players
Tools/Project OCH/Multiplayer/Stop Launched Players
```

빌드 결과:

```text
Builds/Win64/Client/Client.exe
Builds/Win64/Logs/Client_1.log
Builds/Win64/Logs/Client_2.log
```
