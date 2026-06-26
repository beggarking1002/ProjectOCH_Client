# Project Snapshot

Last updated: 2026-06-20

## 환경

- Project root: `C:\ProjectOCH\Client`
- Unity: `6000.3.9f1`
- Render pipeline: Universal RP / 2D Renderer 설정 존재
- Protobuf 런타임 DLL: `Assets/Libs/Google.Protobuf.dll`
- Addressables package: `com.unity.addressables` `2.9.1`

## 주요 폴더

- `Assets/Editor`
  - 에디터 전용 자동 설정 코드.
- `Assets/AddressableAssetsData`
  - Addressables 설정, 그룹, 데이터 빌더.
- `Assets/Scripts/Network`
  - 기존 코드 호환용 `GameServerConnection` wrapper.
- `Assets/Scripts/App`
  - `GameRoot`, `AppServices`.
- `Assets/Scripts/Services/Network`
  - 실제 게임 서버 연결을 담당하는 `NetworkService`.
- `Assets/Scripts/Packet`
  - 클라이언트 패킷 핸들러, 게임 서버 세션, generated proto.
- `Assets/Scripts/Packet/ServerCore`
  - TCP 소켓, 패킷 세션, 수신 버퍼.
- `Assets/Libs`
  - `Google.Protobuf.dll`.

## 현재 주요 코드 역할

### `GameRoot`

경로: `Assets/Scripts/App/GameRoot.cs`

- 유일한 앱 진입점 역할.
- `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`로 `@GameRoot` 자동 생성.
- `AppServices`를 생성하고 `Update()`에서 `Services.Tick()` 호출.
- 현재는 `NetworkService`만 소유한다.

### `NetworkService`

경로: `Assets/Scripts/Services/Network/NetworkService.cs`

- 기존 `GameServerConnection`의 실제 네트워크 책임을 이동한 순수 C# 서비스.
- 연결 상태, `Connect`, `Disconnect`, `Send`, `SendLogin`, `EnterGame`, `SendChat` 담당.
- `GameServerSession`과 `ClientPacketHandler`를 사용한다.
- 메인 스레드 job queue를 flush한다.

### `GameServerConnection`

경로: `Assets/Scripts/Network/GameServerConnection.cs`

- 기존 참조 호환용 wrapper.
- 직접 소켓/패킷 큐를 소유하지 않는다.
- 내부 호출은 `GameRoot.Instance.Network`로 위임한다.

### `ClientPacketHandler`

경로: `Assets/Scripts/Packet/ClientPacketHandler.cs`

- 패킷 ID와 Protobuf parser를 등록.
- 서버 패킷을 소켓 스레드에서 파싱한 뒤 메인 스레드 큐에 넣는다.
- `Flush()`는 `GameServerConnection.Update()`에서 호출된다.
- 클라이언트 송신 패킷은 `[size(2)][packetId(2)][payload]` 형식으로 만든다.

### `GameServerSession`

경로: `Assets/Scripts/Packet/GameServerSession.cs`

- `PacketSession` 구현체.
- 수신 패킷은 `ClientPacketHandler.Instance.HandlePacket(this, buffer)`로 전달.
- 연결/해제/전송 이벤트를 외부로 노출.

### `ServerCore`

경로: `Assets/Scripts/Packet/ServerCore`

- `Connector`: 비동기 TCP connect.
- `Session`: send/recv loop.
- `PacketSession`: 패킷 헤더 기반으로 여러 패킷을 분리.
- `RecvBuffer`: 수신 버퍼 read/write cursor 관리.

## Git 상태 메모

2026-06-20 확인 시 `ProjectOCH_Client/` 옵시디언 볼트가 untracked로 보였다. 이 폴더를 Git에 포함할지 여부는 별도 결정 필요.
