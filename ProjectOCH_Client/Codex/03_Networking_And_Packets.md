# Networking And Packets

Last updated: 2026-06-20

## 연결 흐름

1. Unity 씬 로드 후 `GameServerConnection.Bootstrap()` 실행.
2. 씬에 `GameServerConnection`이 없으면 새 GameObject 생성.
3. `Awake()`에서 싱글톤 설정 및 `ClientPacketHandler.Init()`.
4. `Start()`에서 `connectOnStart`가 true면 `Connect()`.
5. `Connector.Connect()`로 `127.0.0.1:7777` TCP 연결 시도.
6. 연결 성공 후 `GameServerConnection.SendLogin()` 호출.
7. `C_LOGIN` 송신.
8. 서버에서 `S_LOGIN` 수신.
9. `ClientPacketHandler`가 `S_LOGIN` parser로 파싱.
10. 메인 스레드 큐에 `LoginReceived` 이벤트 enqueue.
11. `GameServerConnection.Update()`에서 queue flush.
12. `OnLoginReceived()`에서 `Verified` 또는 `Failed` 상태로 변경.

## 패킷 포맷

클라이언트와 서버 모두 기본적으로 아래 형식을 쓴다.

```text
[size: ushort][packetId: ushort][protobuf payload]
```

클라이언트 생성 위치:

- `ClientPacketHandler.MakeSendBuffer(...)`

수신 분리 위치:

- `PacketSession.OnRecv(...)`

## 패킷 ID

경로: `Assets/Scripts/Packet/ClientPacketHandler.cs`

```text
C_LOGIN      = 1000
S_LOGIN      = 1001
C_ENTER_GAME = 1002
S_ENTER_GAME = 1003
C_LEAVE_GAME = 1004
S_LEAVE_GAME = 1005
S_SPAWN      = 1006
S_DESPAWN    = 1007
C_MOVE       = 1008
S_MOVE       = 1009
C_CHAT       = 1010
S_CHAT       = 1011
```

## 로그인 응답

클라이언트 generated 코드 기준:

- `Protocol.S_LOGIN`
  - `bool Success`
  - `RepeatedField<ObjectInfo> Players`

로그 위치:

```csharp
Debug.Log($"Game server verification: success={pkt.Success}, players={pkt.Players.Count}");
```

여기서 `Players.Count`는 서버가 보낸 `S_LOGIN.players` repeated field의 개수다.

## 서버가 players=3을 보내는 이유

서버 경로:

- `C:\ProjectOCH\Server\GameServer\ServerPacketHandler.cpp`

핵심 코드:

```cpp
bool Handle_C_LOGIN(PacketSessionRef& session, Protocol::C_LOGIN& pkt)
{
    Protocol::S_LOGIN loginPkt;

    for (int32 i = 0; i < 3; i++)
    {
        Protocol::ObjectInfo* player = loginPkt.add_players();
        Protocol::PosInfo* posInfo = player->mutable_pos_info();
        posInfo->set_x(Utils::GetRandom(0.f, 100.f));
        posInfo->set_y(Utils::GetRandom(0.f, 100.f));
        posInfo->set_z(Utils::GetRandom(0.f, 100.f));
        posInfo->set_yaw(Utils::GetRandom(0.f, 45.f));
    }

    loginPkt.set_success(true);
    SEND_PACKET(loginPkt);
    return true;
}
```

따라서 `players=3`은 현재 접속자 수가 아니라 서버 로그인 핸들러의 테스트 더미 개수다.

## 메인 스레드 처리

- `ClientPacketHandler.Register` 내부에서 parser로 패킷을 파싱.
- 이벤트 호출은 `_mainThreadJobs.Enqueue(...)`로 메인 스레드로 넘김.
- `GameServerConnection.Update()`가 `ClientPacketHandler.Instance.Flush()` 호출.

Unity API를 소켓 스레드에서 직접 호출하지 않기 위한 구조다.

