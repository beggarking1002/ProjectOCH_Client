# Work Log

## 2026-06-20

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

