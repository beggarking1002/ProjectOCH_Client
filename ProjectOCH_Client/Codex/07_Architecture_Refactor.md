# Architecture Refactor

Last updated: 2026-06-20

## 목표

서버가 붙은 2D SRPG 클라이언트로 확장하기 위해 전역 싱글톤을 줄이고, Unity 진입점과 실제 서비스 책임을 분리한다.

## 현재 적용한 구조

```text
GameRoot
 └─ AppServices
     └─ NetworkService
         ├─ Connector
         ├─ GameServerSession
         └─ ClientPacketHandler
```

## 새 파일

- `Assets/Scripts/App/GameRoot.cs`
- `Assets/Scripts/App/AppServices.cs`
- `Assets/Scripts/Services/Network/NetworkService.cs`
- `Assets/Scripts/Services/Network/GameServerConnectionState.cs`

## 변경된 파일

- `Assets/Scripts/Network/GameServerConnection.cs`
  - 직접 네트워크 처리 제거.
  - `GameRoot.Instance.Network`로 위임하는 wrapper로 축소.
- `Assets/Scripts/Packet/ServerCore/Session.cs`
  - 비정상 packet size 처리 보강.
  - send/recv 예외 시 disconnect 처리.
  - disconnect 시 null/이미 닫힌 socket 방어.

## 설계 방향

- 허용하는 싱글톤:
  - `GameRoot.Instance`
- 피할 것:
  - `NetworkService.Instance`
  - `BattleSession.Instance`
  - `UnitManager.Instance`
  - 모든 곳에서 `Managers.X`를 호출하는 구조
- 앞으로 붙일 구조:

```text
GameRoot
 └─ AppServices
     ├─ NetworkService
     ├─ AddressableService
     ├─ SceneService
     └─ UIService

GameSession
 ├─ AccountState
 ├─ LobbyService
 └─ BattleSession

BattleSession
 ├─ BattleState
 ├─ TurnManager
 ├─ UnitManager
 ├─ GridMap
 ├─ CommandSystem
 ├─ BattleNetworkSync
 └─ BattleViewBinder
```

## 다음 리팩터링 후보

1. `GameRoot.connectToGameServerOnStart` 기본값 검토.
2. `ClientPacketHandler` singleton을 `NetworkService` 소유 객체로 변경.
3. `NetworkService`에 재접속/서버 변경 흐름 추가.
4. `AddressableService` 추가.
5. 로그인/로비/전투를 `GameSession` 단위로 분리.

