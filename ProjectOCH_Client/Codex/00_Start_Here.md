# ProjectOCH Client - Codex Start Here

Last updated: 2026-06-20

이 문서는 Codex 새 대화를 시작할 때 먼저 읽기 위한 인덱스다.

## 빠른 요약

- Unity 클라이언트 프로젝트 경로: `C:\ProjectOCH\Client`
- Obsidian 정리 볼트: `C:\ProjectOCH\Client\ProjectOCH_Client`
- Unity 버전: `6000.3.9f1`
- 주요 런타임 코드:
  - `Assets/Scripts/Network/GameServerConnection.cs`
  - `Assets/Scripts/Packet/ClientPacketHandler.cs`
  - `Assets/Scripts/Packet/GameServerSession.cs`
  - `Assets/Scripts/Packet/ServerCore/*.cs`
- Protobuf 생성 코드:
  - `Assets/Scripts/Packet/Generated/Enum.cs`
  - `Assets/Scripts/Packet/Generated/Struct.cs`
  - `Assets/Scripts/Packet/Generated/Protocol.cs`
- 서버 참고 경로:
  - `C:\ProjectOCH\Server\GameServer`

## 읽는 순서

1. [[01_Project_Snapshot]]
2. [[02_Addressables_Setup]]
3. [[03_Networking_And_Packets]]
4. [[04_Server_Protocol_Notes]]
5. [[05_Known_Issues_And_TODO]]
6. [[06_Work_Log]]
7. [[07_Architecture_Refactor]]

## 현재 가장 중요한 주의점

- `GameServerConnection`은 런타임에 자동 생성되고 기본값으로 `127.0.0.1:7777`에 자동 접속한다.
- 2026-06-20 리팩터링 이후 앱 진입점은 `GameRoot`, 실제 네트워크 처리는 `NetworkService`가 담당한다. `GameServerConnection`은 기존 코드 호환용 wrapper로 축소했다.
- 로그인 응답의 `players=3`은 클라이언트 계산값이 아니라 서버 `Handle_C_LOGIN`에서 `for (int32 i = 0; i < 3; i++)`로 더미 플레이어 3개를 넣기 때문에 나온다.
- Addressables 기본 세팅은 되어 있지만 `Remote.LoadPath`가 개발용 `http://localhost/[BuildTarget]`다. 배포 전 실제 CDN/서버 URL로 변경해야 한다.
- 클라이언트 generated proto와 서버 proto 원본/생성물이 어긋날 가능성이 있다. 패킷 구조 변경 시 반드시 서버/클라를 같은 proto에서 재생성해야 한다.
