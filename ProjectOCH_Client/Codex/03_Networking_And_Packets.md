# Networking And Packets

Last updated: 2026-06-30

## 연결 흐름

```text
GameRoot.Awake
 -> AppServices.Initialize
 -> NetworkService.Initialize
 -> PacketHandler event 구독

GameRoot.Start
 -> NetworkService.Connect
 -> Connector.Connect(127.0.0.1:7777)
 -> C_LOGIN 전송
 -> S_LOGIN 수신
 -> Verified 상태
```

기본 서버 주소:

```text
127.0.0.1:7777
```

## 패킷 포맷

클라이언트와 서버 모두 기본 패킷 형식은 같다.

```text
[size: ushort][packetId: ushort][protobuf payload]
```

클라이언트 송신 위치:

```text
NetworkService.MakeSendBuffer(...)
```

수신 분리:

```text
PacketSession.OnRecv(...)
GameServerSession.OnRecvPacket(...)
PacketManager.Instance.OnRecvPacket(...)
PacketHandler
```

## 현재 프로토콜 요약

현재 generated C# 기준 주요 메시지:

```proto
message Vec2Fixed
{
    sint32 x = 1;
    sint32 y = 2;
}

message ObjectInfo
{
    uint64 object_id = 1;
    ObjectType object_type = 2;
    CreatureType creature_type = 3;
    Vec2Fixed position = 4;
}

message C_LOGIN {}
message S_LOGIN { bool success = 1; }

message C_ENTER_GAME { uint64 playerIndex = 1; }
message S_ENTER_GAME
{
    bool success = 1;
    ObjectInfo player = 2;
}

message S_SPAWN { repeated ObjectInfo players = 1; }
message S_DESPAWN { repeated uint64 object_ids = 1; }

message C_MOVE { Vec2Fixed target = 1; }
message S_MOVE
{
    uint64 object_id = 1;
    Vec2Fixed start = 2;
    Vec2Fixed target = 3;
    uint32 duration_ms = 4;
}
```

## PacketManager / PacketHandler 관계

- `PacketManager`
  - generated 코드.
  - packet id와 protobuf parser를 매핑한다.
  - 수신 buffer를 구체 packet으로 변환한다.
- `PacketHandler`
  - 사람이 작성하는 handler.
  - `S_LOGINHandler`, `S_ENTER_GAMEHandler`, `S_SPAWNHandler`, `S_MOVEHandler` 등.
  - 수신 packet을 main thread job queue에 넣고 event로 전달한다.
- `NetworkService`
  - `PacketHandler` event를 구독한다.
  - 상태 캐시와 상위 서비스 event를 관리한다.

## 입장 흐름

```text
TitleScene GameStartButton
 -> NetworkService.EnterGame(playerIndex)
 -> C_ENTER_GAME
 -> S_ENTER_GAME success
 -> FieldScene 로드
 -> S_SPAWN으로 기존/신규 플레이어 동기화
```

주의:

- 서버는 `S_ENTER_GAME`과 `S_SPAWN`을 별도 패킷으로 보낸다.
- `S_SPAWN`이 FieldScene 로드 전에 도착할 수 있다.
- 이를 위해 `NetworkService`가 `_knownPlayers` 캐시를 유지하고, `FieldObjectManager`가 FieldScene 초기화 시 snapshot을 사용한다.

## 이동 흐름

```text
클라 클릭
 -> C_MOVE target(Vec2Fixed world)
 -> 서버 walkmap 검증
 -> 성공: S_MOVE start/target/duration_ms broadcast
 -> 실패: 해당 클라에게 S_MOVE start=target=현재 위치, duration_ms=0
```

클라 `FieldPawnController`는 기본적으로 로컬 walkable 검사를 먼저 한다. 서버 검증 로그 확인이 필요하면 `sendBlockedMoveForDebug`를 켠다.
