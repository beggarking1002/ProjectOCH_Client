# Work Log

Last updated: 2026-06-30

## 2026-06-30

### 멀티 클라 빌드/실행 도구

추가:

- `Assets/Editor/MultiplayerBuildAndRun.cs`

메뉴:

```text
Tools/Project OCH/Multiplayer/Build Client Only
Tools/Project OCH/Multiplayer/Build And Run/2 Players
Tools/Project OCH/Multiplayer/Build And Run/3 Players
Tools/Project OCH/Multiplayer/Build And Run/4 Players
Tools/Project OCH/Multiplayer/Run Existing/1~4 Players
Tools/Project OCH/Multiplayer/Stop Launched Players
```

동작:

- Addressables content build.
- Windows x64 클라이언트 1회 빌드.
- 같은 exe를 여러 개 실행.
- 각 클라에 `-playerIndex` 전달.
- 각 클라 로그를 `Builds/Win64/Logs/Client_N.log`로 분리.

### Run In Background 설정

문제:

- 포커스 없는 클라이언트가 네트워크 갱신을 하지 않다가 창을 클릭하면 한꺼번에 반영됨.

수정:

- `ProjectSettings/ProjectSettings.asset`
  - `runInBackground: 1`
- `Assets/Editor/MultiplayerBuildAndRun.cs`
  - `PlayerSettings.runInBackground = true`
- `Assets/Scripts/App/GameRoot.cs`
  - `Application.runInBackground = true`

### TitleScene 입장 흐름

수정:

- `Assets/Scripts/Scenes/TitleSceneFlow.cs`
  - `GameStartButton` 클릭 시 `C_ENTER_GAME` 전송.
  - 실행 인자 `-playerIndex`를 읽어 `C_ENTER_GAME.PlayerIndex`에 설정.
  - `S_ENTER_GAME.success` 수신 시 `FieldScene` 로드.

### FieldScene Addressables 로딩

수정/추가:

- `Assets/Scripts/Scenes/FieldSceneAddressableLoader.cs`
- `Assets/Scripts/Field/FieldObjectManager.cs`
- `Assets/Scripts/Field/FieldPositionCodec.cs`
- `Assets/Scripts/Field/CameraController.cs`

내용:

- FieldScene 진입 시 Addressable `Field` 로드.
- `WorldMapRoot`를 Field 위에 로드.
- `Field_Pawn`은 `FieldObjectManager`가 생성.
- 카메라는 내 pawn을 따라감.

### Field 이동

수정:

- `Assets/Scripts/Field/FieldPawnController.cs`

내용:

- 마우스 클릭 위치를 `Vec2Fixed`로 변환해 `C_MOVE` 전송.
- 서버 `S_MOVE`를 받아 이동.
- 왼쪽 이동 시 sprite flip.
- 이동 중 `Animator.IsMoving = true`.
- 로컬 사전 검증 옵션 추가:
  - `validateLocallyBeforeSend`
  - `sendBlockedMoveForDebug`

### 기존 플레이어 표시 문제

문제:

- 3번 클라가 입장할 때 기존 1번/2번이 안 보이다가, 그들이 이동해야 보임.

원인:

- `S_SPAWN`이 FieldScene 로드 전에 도착해 `FieldObjectManager`가 소비하지 못함.

수정:

- `NetworkService`가 `_knownPlayers` 캐시 유지.
- `FieldObjectManager.Initialize`에서 `GetKnownPlayersSnapshot()`을 사용해 기존 player pawn 생성.

### Walkmap Exporter

추가/수정:

- `Assets/Editor/FieldWalkMapExporter.cs`
- `Assets/Scripts/Field/FieldWalkMapData.cs`

기능:

- 프리팹 에셋을 직접 넣어 walkmap JSON 추출.
- 예: `Assets/@Resources/Prefab/Maps/Field_001.prefab`
- `Ground_Tilemap` 기준 이동 가능 영역 추출.
- row range 압축:

```json
{ "y": -16, "x_min": -5, "x_max": -4 }
```

- `Include Debug Cells` 옵션으로 개별 cell 목록도 선택 출력 가능.
- `Block_Tilemap` 또는 `Prop_Tilemap` 자동 감지.
- `Subtract Block Tilemap`을 켠 경우에만 block tilemap을 제외.

### 서버 참고 사항 확인

서버 쪽 확인:

- `GameServer.cpp`
  - `GFieldWalkMapData.LoadFromFile("C:\\ProjectOCH\\Server\\Data\\Maps\\Field_001.walkmap.json")`
- `FieldWalkMapData.cpp`
  - JSON 로드 및 `walkable_ranges` 검사.
- `Room.cpp`
  - `C_MOVE`에서 walkmap 검증 후 `S_MOVE`.

발견:

- `Field_001`은 Hexagon Grid인데 서버 `FixedToCell`이 rectangle 공식이면 좌표계가 어긋날 수 있다.

## 2026-06-20

### GameRoot / NetworkService 리팩터링

추가/정리:

- `GameRoot`
- `AppServices`
- `NetworkService`
- `GameServerConnection`은 호환 wrapper로 축소.
- `PacketHandler` event 기반 main thread dispatch.

### Addressables 초기 설정

추가:

- `Assets/Editor/AddressablesProjectSetup.cs`
- `Tools/Project OCH/Addressables/Initialize`

## 2026-06-15

### Addressables 패키지 설치

- Unity Addressables package `2.9.1` 설치.
- `Assets/AddressableAssetsData` 생성.
- Local/Remote group 생성.
