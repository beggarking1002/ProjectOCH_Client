# Networking And Packets

Last updated: 2026-07-03

## 연결 흐름

```text
GameRoot.Awake
 -> AppServices.Initialize
 -> NetworkService.Initialize
 -> PacketHandler event 구독

GameRoot.Start
 -> NetworkService.Connect
 -> Connector.Connect(127.0.0.1:7777)
 -> C_LOGIN
 -> S_LOGIN
 -> Verified
```

기본 서버:

```text
127.0.0.1:7777
```

## 패킷 포맷

클라이언트와 서버 모두 아래 포맷을 사용한다.

```text
[size: ushort][packetId: ushort][protobuf payload]
```

송신 위치:

```text
NetworkService.MakeSendBuffer(...)
```

수신 흐름:

```text
PacketSession.OnRecv(...)
GameServerSession.OnRecvPacket(...)
PacketManager.Instance.OnRecvPacket(...)
PacketHandler
NetworkService
Scene/Object manager
```

## PacketManager / PacketHandler / NetworkService 역할

- `PacketManager`
  - generated dispatcher.
  - packet id와 protobuf parser를 매핑한다.
  - `S_BATTLE_SKILL`까지 등록되어 있다.
- `PacketHandler`
  - packet별 static handler.
  - main thread queue에 event 호출을 넣는다.
  - 현재 주요 이벤트:
    - `LoginReceived`
    - `EnterGameReceived`
    - `SpawnReceived`
    - `MoveReceived`
    - `EnterBattleReceived`
    - `BattleMoveReceived`
    - `BattleSkillReceived`
- `NetworkService`
  - `PacketHandler` 이벤트를 구독한다.
  - 송신 API를 제공한다.
  - `LastLogin`, `LastEnterGame`, `LastEnterBattle`, `_knownPlayers`를 관리한다.

## 주요 client send API

```csharp
NetworkService.SendLogin()
NetworkService.EnterGame(ulong playerIndex = 0)
NetworkService.EnterBattle()
NetworkService.SendBattleMove(ulong battleId, ulong pawnId, int q, int r)
NetworkService.SendBattleSkill(ulong battleId, ulong casterPawnId, int skillSlot, ulong targetPawnId, int q, int r)
NetworkService.SendChat(string message)
```

`GameServerConnection`에도 같은 wrapper가 있다.

## Field 패킷

현재 Field는 `Vec2Fixed` fixed-point 좌표 기반이다.

```proto
message C_MOVE {
  Vec2Fixed target = 1;
}

message S_MOVE {
  uint64 object_id = 1;
  Vec2Fixed start = 2;
  Vec2Fixed target = 3;
  uint32 duration_ms = 4;
}
```

흐름:

```text
마우스 클릭
 -> FieldPawnController
 -> C_MOVE target(Vec2Fixed)
 -> 서버 walkmap 검증
 -> S_MOVE broadcast
 -> FieldObjectManager / FieldPawnController 위치 갱신
```

## Battle 입장 패킷

```proto
message C_ENTER_BATTLE {}

message S_ENTER_BATTLE {
  bool success = 1;
  uint64 battle_id = 2;
  string map_id = 3;
  repeated BattlePawnInfo allied_pawns = 4;
  repeated BattlePawnInfo enemy_pawns = 5;
  uint64 current_turn_pawn_id = 6;
  string reason = 7;
}
```

흐름:

```text
FieldScene에서 B key
 -> C_ENTER_BATTLE
 -> S_ENTER_BATTLE success
 -> BattleSceneFlow가 BattleScene 로드
 -> BattleSceneAddressableLoader가 BattleField_001 로드
 -> BattleObjectManager.SpawnFromEnterBattle()
```

## Battle 이동 패킷

```proto
message C_BATTLE_MOVE {
  uint64 battle_id = 1;
  uint64 pawn_id = 2;
  AxialCoord target = 3;
}

message S_BATTLE_MOVE {
  bool success = 1;
  uint64 battle_id = 2;
  uint64 pawn_id = 3;
  AxialCoord start = 4;
  AxialCoord target = 5;
  uint64 next_turn_pawn_id = 6;
  BattleMoveResult result = 7;
  string reason = 8;
}
```

클라이언트 처리:

- 현재 턴이 내 pawn일 때만 이동 요청 가능.
- 이동 요청을 보내면 `BattleActionMode.WaitingServer`.
- `S_BATTLE_MOVE.Success=true`면 pawn axial 갱신, 다음 턴 갱신, mode를 `Move`로 복귀.
- 실패해도 mode는 `Move`로 복귀하고 warning log를 남긴다.

## Battle 스킬 패킷

```proto
message C_BATTLE_SKILL {
  uint64 battle_id = 1;
  uint64 caster_pawn_id = 2;
  int32 skill_slot = 3;
  uint64 target_pawn_id = 4;
  AxialCoord target_axial = 5;
}

message S_BATTLE_SKILL {
  bool success = 1;
  uint64 battle_id = 2;
  uint64 caster_pawn_id = 3;
  int32 skill_slot = 4;
  uint64 target_pawn_id = 5;
  AxialCoord target_axial = 6;
  int32 damage = 7;
  int32 target_hp = 8;
  uint64 next_turn_pawn_id = 9;
  string reason = 10;
}
```

클라이언트 처리:

- `Skill1`, `Skill2`, `Skill3` 버튼은 각각 `skill_slot = 1, 2, 3`으로 보낸다.
- 클릭한 axial에 pawn이 있으면 `target_pawn_id`를 채운다.
- 없으면 `target_pawn_id = 0`이고 `target_axial`은 항상 보낸다.
- `S_BATTLE_SKILL.Success=true`면 target hp와 다음 턴을 반영한다.
- 현재 서버 구현에 따라 Skill1만 성공할 수 있다. 클라이언트는 1/2/3 모두 전송 가능하다.
