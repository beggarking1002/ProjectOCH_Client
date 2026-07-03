# Project Snapshot

Last updated: 2026-07-03

## 환경

- Project root: `C:\ProjectOCH\Client`
- Unity: `6000.3.9f1`
- Render pipeline: Universal RP / 2D Renderer
- Input: Input System package
- Protobuf DLL: `Assets/Libs/Google.Protobuf.dll`
- Addressables package: `com.unity.addressables` `2.9.1`

## 전체 흐름

```text
TitleScene
 -> GameStartButton
 -> C_ENTER_GAME
 -> S_ENTER_GAME success
 -> FieldScene
 -> FieldSceneAddressableLoader
 -> Field 자유 이동
 -> B key
 -> C_ENTER_BATTLE
 -> S_ENTER_BATTLE success
 -> BattleScene
 -> BattleSceneAddressableLoader
 -> BattleField_001 / battle pawns / battle UI
```

## 앱 루트

```text
GameRoot
 -> AppServices
     -> NetworkService
```

`GameRoot`는 `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`로 `@GameRoot`를 자동 생성한다. `DontDestroyOnLoad`로 유지되며 `Application.runInBackground = true`를 설정한다.

## 주요 폴더

- `Assets/Scripts/App`
  - 앱 루트와 서비스 컨테이너.
- `Assets/Scripts/Services/Network`
  - 실질적인 서버 연결, 송신, 수신 이벤트 캐시.
- `Assets/Scripts/Packet`
  - generated proto, packet id dispatcher, handler, socket session, ServerCore 포팅 코드.
- `Assets/Scripts/Scenes`
  - Title/Field/Battle scene flow와 Addressables loader.
- `Assets/Scripts/Field`
  - Field pawn, field object sync, camera, walk area, fixed-point 좌표 codec.
- `Assets/Scripts/Battle`
  - Battle map grid, object manager, pawn controller, action mode, 임시 UI controller.
- `Assets/Editor`
  - Addressables setup, multiplayer build/run, walkmap exporter.
- `Assets/@Resources/Prefab/UI`
  - 사용자가 만든 `Canvas_BattleUI.prefab` 전투 UI 목업.

## Field 상태

- Field는 타일 기반 이동이 아니라 마우스로 찍은 월드 좌표로 이동한다.
- 패킷은 `Vec2Fixed` fixed-point 좌표를 사용한다.
- 클라이언트는 선택적으로 로컬 walkable 검증을 먼저 할 수 있다.
- 서버는 `Field_001.walkmap.json`을 사용해 최종 검증한다.
- Field pawn은 현재 `Pawn_Beige_Ice`를 사용하도록 맞춰졌다.
- 이동 중 animator bool은 `isMoving`을 사용한다.

## Battle 상태

- Battle은 axial 좌표계 기반 hex tile 전투로 간다.
- `BattleScene` 진입 시 Addressable `BattleField_001`을 로드한다.
- 서버가 내려준 `S_ENTER_BATTLE`의 allied/enemy pawn 정보를 사용해 pawn을 spawn한다.
- `PawnClass`와 Addressable prefab 주소를 매핑한다.
- 현재 턴 pawn 머리 위에는 `TURN` TextMesh indicator가 뜬다.
- 현재 턴이 아닌 pawn은 서버 전투에서 조작하지 못한다.
- `C_BATTLE_MOVE`, `S_BATTLE_MOVE`, `C_BATTLE_SKILL`, `S_BATTLE_SKILL` 연결이 되어 있다.

## 현재 UI 상태

- 런타임 Battle UI는 아직 코드 생성형 `BattleUIController`가 만든다.
- 사용자가 별도로 `Canvas_BattleUI.prefab` 목업을 만들었다.
- 이 prefab은 `ActionPanel`, `TurnQueue`, 좌우 pawn panel, tile info, turn panel 등을 갖고 있다.
- 다음 작업은 코드 생성 UI를 prefab 바인딩 방식으로 교체하는 것이다.

## 빌드/멀티클라 테스트

메뉴:

```text
Tools/Project OCH/Multiplayer/Build Client Only
Tools/Project OCH/Multiplayer/Build And Run/2 Players
Tools/Project OCH/Multiplayer/Build And Run/3 Players
Tools/Project OCH/Multiplayer/Build And Run/4 Players
Tools/Project OCH/Multiplayer/Run Existing/1~4 Players
Tools/Project OCH/Multiplayer/Stop Launched Players
```

빌드 결과 예:

```text
Builds/Win64/Client/Client.exe
Builds/Win64/Logs/Client_1.log
Builds/Win64/Logs/Client_2.log
```
