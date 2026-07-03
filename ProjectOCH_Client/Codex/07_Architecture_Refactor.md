# Architecture Refactor

Last updated: 2026-07-03

## 목표

서버가 붙은 2D SRPG 클라이언트로 확장하기 위해 전역 singleton 남용을 줄이고, scene lifecycle과 서버 session 상태를 분리한다.

## 현재 구조

```text
GameRoot
 -> AppServices
     -> NetworkService

PacketManager
 -> PacketHandler
 -> NetworkService
 -> Scene/Object manager
```

허용 중인 singleton:

- `GameRoot.Instance`
- generated `PacketManager.Instance`
- `PacketHandler.Instance`

scene-specific manager는 전역 singleton으로 만들지 않는 방향이다.

## GameRoot

파일:

```text
Assets/Scripts/App/GameRoot.cs
```

책임:

- `@GameRoot` 자동 생성
- `DontDestroyOnLoad`
- `Application.runInBackground = true`
- `AppServices.Initialize`
- 매 프레임 `Services.Tick()`

## AppServices

파일:

```text
Assets/Scripts/App/AppServices.cs
```

현재는 `NetworkService` 중심이다. 나중에 필요하면 아래 서비스를 추가할 수 있다.

```text
AddressableService
SceneService
UIService
AudioService
```

단, 지금 단계에서 과한 추상화는 하지 않는다.

## NetworkService

파일:

```text
Assets/Scripts/Services/Network/NetworkService.cs
```

책임:

- 서버 연결 상태
- 송신 buffer 생성
- `PacketHandler` event 구독
- main thread job flush
- `LastLogin`, `LastEnterGame`, `LastEnterBattle`
- Field player snapshot cache
- Battle packet send/receive event

## Field 구조

```text
FieldSceneAddressableLoader
 -> Field prefab
 -> FieldMapWalkArea
 -> FieldObjectManager
     -> FieldPawnController instances
     -> CameraController target binding
```

Field는 자유 이동이다. `Vec2Fixed` world 좌표를 서버에 보내고, 서버가 walkmap으로 검증한다.

## Battle 구조

```text
BattleSceneFlow
 -> S_ENTER_BATTLE success
 -> BattleScene load
 -> BattleSceneAddressableLoader
     -> BattleField_001
     -> BattleMapGrid
     -> BattleObjectManager
         -> BattlePawnController instances
         -> C_BATTLE_MOVE / C_BATTLE_SKILL
         -> S_BATTLE_MOVE / S_BATTLE_SKILL
     -> BattleUIController
```

Battle은 axial tile 기반이다. 현재 `BattleObjectManager`가 전투 입력, pawn spawn, packet response 적용까지 많이 담당하고 있다.

## 앞으로 분리하면 좋은 단위

Battle 쪽이 커지면 아래로 나누는 것이 좋다.

```text
BattleSession
 -> BattleState
 -> BattlePawnRegistry
 -> BattleInputController
 -> BattleNetworkSync
 -> BattleHudController
 -> BattleTurnQueueView
```

하지만 현재는 전투 루프를 먼저 세우는 단계라서 무리하게 파일을 쪼개지 않는 것이 낫다.

## UI 방향

현재 `BattleUIController`는 임시 코드 생성 UI다.

최종 방향:

```text
Canvas_BattleUI.prefab
 -> BattleHudController
     -> ActionSlot views
     -> TurnQueue views
     -> Selected pawn panel
     -> Enemy pawn panel
     -> Tile info panel
```

UI는 턴을 결정하지 않는다. 서버가 내려준 battle state를 보여주고, 사용자의 명령을 packet으로 보내는 역할만 한다.

## 서버 권위 원칙

클라이언트:

- 현재 턴인지, 클릭 가능한지 정도는 UX를 위해 먼저 막을 수 있다.
- 하지만 최종 이동/스킬 성공 여부는 서버 응답을 따른다.

서버:

- owner 검증
- turn 검증
- map walkable 검증
- range 검증
- occupancy 검증
- skill rule 검증

이 원칙은 Field와 Battle 모두 동일하다.
