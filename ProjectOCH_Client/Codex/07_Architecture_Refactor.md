# Architecture Refactor

Last updated: 2026-06-30

## 목표

서버가 붙은 2D SRPG 클라이언트로 확장하기 위해 전역 싱글톤 의존을 줄이고, Unity 진입점과 실제 서비스 책임을 분리한다.

## 현재 적용 구조

```text
GameRoot
 -> AppServices
     -> NetworkService
```

`GameRoot`는 허용된 앱 루트 싱글톤이다. 세부 매니저를 전부 static singleton으로 흩뿌리지 않고, `AppServices` 아래에 서비스 객체를 둔다.

## 현재 주요 책임

### GameRoot

파일:

```text
Assets/Scripts/App/GameRoot.cs
```

책임:

- 런타임 시작 전 `@GameRoot` 자동 생성.
- `DontDestroyOnLoad`.
- `Application.runInBackground = true`.
- `AppServices.Initialize`.
- `Update`에서 `Services.Tick`.

### AppServices

파일:

```text
Assets/Scripts/App/AppServices.cs
```

책임:

- 현재는 `NetworkService`만 소유.
- 앞으로 Addressable, Scene, UI 등 앱 단위 서비스를 붙일 수 있는 자리.

### NetworkService

파일:

```text
Assets/Scripts/Services/Network/NetworkService.cs
```

책임:

- 서버 연결 상태.
- 송신 buffer 생성.
- `PacketHandler` event 구독.
- main thread job flush.
- `LastLogin`, `LastEnterGame`, `_knownPlayers` 캐시.

## Field 쪽 구조

```text
FieldSceneAddressableLoader
 -> Field prefab 로드
 -> FieldMapWalkArea
 -> FieldObjectManager
     -> FieldPawnController instances
     -> CameraController target binding
```

`FieldObjectManager`는 필드 씬 단위 오브젝트 관리자다. 전역 singleton으로 두지 않고 Field prefab/scene 생명주기에 묶는다.

## 네트워크 이벤트 흐름

```text
PacketManager
 -> PacketHandler
 -> NetworkService
 -> FieldObjectManager / TitleSceneFlow
```

주의:

- `PacketManager`는 generated parser/dispatcher.
- `PacketHandler`는 사람이 작성하는 packet별 handler.
- `NetworkService`는 packet을 앱 상태로 정리하고 event를 노출한다.

## 싱글톤 판단

허용:

- `GameRoot.Instance`
- generated `PacketManager.Instance`
- `PacketHandler.Instance`

주의:

- scene-specific manager를 global singleton으로 만들지 않는다.
- `FieldObjectManager`는 FieldScene/Field prefab 생명주기에 묶는다.
- Battle 쪽은 나중에 `BattleSession` 단위로 별도 상태를 둔다.

## 향후 구조 제안

```text
GameRoot
 -> AppServices
     -> NetworkService
     -> AddressableService
     -> SceneService
     -> UIService

GameSession
 -> AccountState
 -> LobbyService
 -> FieldSession
 -> BattleSession

FieldSession
 -> FieldState
 -> FieldObjectRegistry
 -> FieldNetworkSync

BattleSession
 -> BattleState
 -> TurnManager
 -> UnitManager
 -> GridMap
 -> CommandSystem
 -> BattleNetworkSync
 -> BattleViewBinder
```

현재 단계에서는 과하게 쪼개지 않고, Field 이동/서버 검증/맵 데이터 파이프라인이 안정된 뒤 다음 리팩터링을 진행하는 것이 좋다.
