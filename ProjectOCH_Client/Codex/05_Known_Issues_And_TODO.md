# Known Issues And TODO

Last updated: 2026-06-20

## 높은 우선순위

### 수신 처리 예외 시 recv loop 중단 가능

파일: `Assets/Scripts/Packet/ServerCore/Session.cs`

2026-06-20 리팩터링에서 예외 시 `Disconnect()`하도록 수정했다.

남은 확인:

- 실제 서버 연결 상태에서 비정상 패킷/서버 종료/재접속 시나리오 테스트 필요.

### 잘못된 패킷 크기 처리

파일: `Assets/Scripts/Packet/ServerCore/Session.cs`

2026-06-20 리팩터링에서 `dataSize < HeaderSize`이면 `-1`을 반환하고 연결 종료되도록 수정했다.

### 자동 접속 정책

파일: `Assets/Scripts/Network/GameServerConnection.cs`

2026-06-20 리팩터링 이후 `GameServerConnection`의 자동 생성은 제거했고, `GameRoot`가 앱 진입점으로 자동 생성된다.

남은 결정:

- `GameRoot.connectToGameServerOnStart` 기본값을 계속 true로 둘지 결정 필요.
- 로그인 씬에서 명시적으로 연결하게 바꾸면 기본값 false가 더 적합하다.

### 이전 세션 callback이 새 세션 상태를 덮을 수 있음

파일: `Assets/Scripts/Network/GameServerConnection.cs`

2026-06-20 리팩터링에서 `NetworkService.CreateSession()`에 `ReferenceEquals(_session, createdSession)` 검사를 추가했다.

### `SendLogin()` 상태 처리

파일: `Assets/Scripts/Network/GameServerConnection.cs`

2026-06-20 리팩터링에서 `Send(...)` 성공 시에만 `Verifying`으로 변경하도록 수정했다.

## 중간 우선순위

### Protobuf 원본 관리

클라이언트에는 generated C#만 있고 `.proto` 원본은 없다.

개선:

- proto 원본을 공통 repo 또는 명확한 shared 위치에서 관리.
- 클라/서버 generated 산출물 재생성 절차 문서화.

### Google.Protobuf.dll 직접 관리

`Assets/Libs/Google.Protobuf.dll`로 직접 포함되어 있다.

개선:

- DLL 버전 기록.
- 서버/툴링 protoc 버전과 호환성 확인.
- 가능하면 Unity package 방식 또는 명확한 외부 의존성 문서화.

### 주석 인코딩 깨짐

`Assets/Scripts/Packet/ServerCore/Session.cs`, `Connector.cs` 일부 주석이 깨져 있다.

개선:

- 파일 인코딩을 UTF-8로 통일.
- 깨진 주석은 의미 확인 후 복구 또는 제거.

## Addressables TODO

- 실제 Addressable 에셋 등록.
- CDN/파일 서버 URL 결정.
- `Remote.LoadPath`를 `localhost`에서 실제 URL로 변경.
- content build 실행 절차 문서화.
- 런타임 로드 코드 작성.
