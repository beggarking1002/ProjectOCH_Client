# Known Issues And TODO

Last updated: 2026-06-20

## 높은 우선순위

### 수신 처리 예외 시 recv loop 중단 가능

파일: `Assets/Scripts/Packet/ServerCore/Session.cs`

`OnRecvCompleted`에서 예외가 나면 로그만 찍고 `Disconnect()`도 하지 않고 `RegisterRecv()`도 다시 호출하지 않는다.

결과:

- 소켓은 살아 있는 것처럼 보일 수 있음.
- 실제 수신 루프는 멈출 수 있음.
- 잘못된 패킷/Protobuf 파싱 오류 한 번으로 네트워크가 멈춘 상태가 될 수 있음.

개선:

- 예외 발생 시 연결을 명확히 끊거나, 복구 가능한 예외만 판단해서 recv를 재등록.

### 잘못된 패킷 크기 처리

파일: `Assets/Scripts/Packet/ServerCore/Session.cs`

`PacketSession.OnRecv`에서 `dataSize < HeaderSize`이면 `break`만 한다.

결과:

- 잘못된 바이트가 버퍼에 남음.
- 이후 수신이 계속 막힐 수 있음.

개선:

- `dataSize < HeaderSize`이면 비정상 패킷으로 보고 연결 종료.

### 자동 접속 정책

파일: `Assets/Scripts/Network/GameServerConnection.cs`

현재 씬에 컴포넌트가 없어도 자동 생성되고 `connectOnStart = true`라서 모든 씬에서 `127.0.0.1:7777` 접속을 시도한다.

결과:

- 메뉴/테스트 씬에서도 접속 실패 로그 발생.
- 빌드 환경에서 localhost 접속을 시도할 수 있음.

개선:

- 자동 생성 여부를 설정으로 분리.
- 로그인 씬 또는 부트스트랩 씬에서 명시적으로 생성.
- 개발/운영 서버 주소를 ScriptableObject 또는 config로 분리.

### 이전 세션 callback이 새 세션 상태를 덮을 수 있음

파일: `Assets/Scripts/Network/GameServerConnection.cs`

`CreateSession()`에서 disconnect callback이 `_session = null`을 직접 수행한다. 이전 세션의 늦은 callback이 새 세션을 null로 덮을 수 있다.

개선:

```csharp
GameServerSession createdSession = new GameServerSession();
_session = createdSession;
createdSession.Disconnected += _ => EnqueueMainThread(() =>
{
    if (ReferenceEquals(_session, createdSession) == false)
        return;

    _session = null;
    SetState(GameServerConnectionState.Disconnected);
});
```

### `SendLogin()` 상태 처리

파일: `Assets/Scripts/Network/GameServerConnection.cs`

`SendLogin()`은 먼저 `Verifying`으로 상태를 바꾸고 `Send(...)` 실패 여부를 무시한다.

개선:

- `Send(...)` 성공 시에만 `Verifying`으로 변경.
- 실패 시 `LastError`와 상태를 명확히 처리.

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

