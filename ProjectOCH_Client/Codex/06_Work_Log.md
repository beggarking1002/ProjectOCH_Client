# Work Log

## 2026-06-20

### GameRoot / NetworkService 리팩터링

수행 내용:

- `GameRoot` 추가.
  - 앱 전체 진입점.
  - `@GameRoot` 자동 생성.
  - `AppServices` 생성 및 `Services.Tick()` 호출.
- `AppServices` 추가.
  - 현재는 `NetworkService`만 소유.
- `NetworkService` 추가.
  - 기존 `GameServerConnection`의 연결/상태/송신/로그인 검증 책임 이동.
  - `Connector`, `GameServerSession`, `ClientPacketHandler` 재사용.
  - 이전 session callback이 현재 session을 덮지 않도록 `ReferenceEquals` 검사 추가.
  - `SendLogin()`은 전송 성공 시에만 `Verifying` 상태로 전환.
- `GameServerConnection` 축소.
  - 자동 생성 제거.
  - 기존 코드 호환을 위한 wrapper로 유지.
- `Session.cs` 보강.
  - `dataSize < HeaderSize`이면 비정상 패킷으로 처리.
  - send/recv 예외 시 disconnect.
  - disconnect 시 null socket, 이미 닫힌 socket 방어.

검증:

- Unity가 새 파일을 csproj에 포함함.
- `dotnet build`는 Unity가 `obj/Debug/Assembly-CSharp.dll`을 점유 중이라 file lock으로 실패. 코드 오류 검증용 결과로는 사용하지 않음.
- Unity Editor log에서 새 파일 import 확인. 명시적 `error CS`는 확인되지 않음.

### 프로젝트 코드 구성 리뷰

확인한 내용:

- Unity 클라이언트는 네트워크 자동 접속 구조가 들어가 있음.
- `GameServerConnection`이 런타임 자동 생성되고 `127.0.0.1:7777`에 자동 접속.
- `ClientPacketHandler`는 Protobuf packet parser와 메인 스레드 dispatch 역할.
- `ServerCore.Session` 수신 루프에 예외 처리/비정상 패킷 처리 리스크가 있음.
- Addressables 기본 설정은 되어 있으나 실제 에셋 등록과 content build는 아직 없음.

주요 발견:

- 로그인 로그의 `players=3`은 서버 `Handle_C_LOGIN`에서 하드코딩한 테스트 데이터.
- 서버 handler 위치: `C:\ProjectOCH\Server\GameServer\ServerPacketHandler.cpp`
- 코드:

```cpp
for (int32 i = 0; i < 3; i++)
{
    Protocol::ObjectInfo* player = loginPkt.add_players();
    ...
}
```

### Obsidian 문서화

생성한 문서:

- [[00_Start_Here]]
- [[01_Project_Snapshot]]
- [[02_Addressables_Setup]]
- [[03_Networking_And_Packets]]
- [[04_Server_Protocol_Notes]]
- [[05_Known_Issues_And_TODO]]
- [[06_Work_Log]]

## 2026-06-15

### Addressables 설정

수행 내용:

- Unity Addressables package `2.9.1` 설치.
- `Assets/AddressableAssetsData` 생성.
- `Default Local Group`, `Remote Content` 생성.
- `Remote.BuildPath`: `ServerData/[BuildTarget]`.
- `Remote.LoadPath`: `http://localhost/[BuildTarget]`.
- `Assets/Editor/AddressablesProjectSetup.cs` 추가.
- `Tools/Project OCH/Addressables/Initialize` 메뉴 추가.
- `.gitignore`에 `ServerData` 제외 추가.

검증:

- Unity 컴파일 성공.
- Addressables 초기화 로그 확인.
- `Remote Content` group 생성 확인.
- `Remote.LoadPath` 저장 확인.

남은 일:

- 실제 Addressable 에셋 지정.
- 배포 URL 결정.
- content build 실행.
