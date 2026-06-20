# Server Protocol Notes

Last updated: 2026-06-20

## 관련 서버 경로

- 서버 루트: `C:\ProjectOCH\Server`
- C++ 게임 서버: `C:\ProjectOCH\Server\GameServer`
- 서버 packet handler:
  - `C:\ProjectOCH\Server\GameServer\ServerPacketHandler.cpp`
  - `C:\ProjectOCH\Server\GameServer\ServerPacketHandler.h`
- 서버 proto:
  - `C:\ProjectOCH\Server\GameServer\Protocol.proto`
  - `C:\ProjectOCH\Server\GameServer\Struct.proto`
  - `C:\ProjectOCH\Server\GameServer\Enum.proto`
- 서버 generated protobuf:
  - `Protocol.pb.cc/.h`
  - `Struct.pb.cc/.h`
  - `Enum.pb.cc/.h`

## 현재 확인된 불일치 가능성

`C:\ProjectOCH\Server\GameServer\Protocol.proto`에서 본 내용은 `S_LOGIN`이 아래처럼 보였다.

```proto
message S_LOGIN
{
    bool success = 1;
    repeated Player players = 2;
}
```

하지만 실제 서버 C++ handler와 generated code, 클라이언트 generated C#은 `ObjectInfo` 기반으로 동작한다.

```cpp
Protocol::ObjectInfo* player = loginPkt.add_players();
```

클라이언트 generated C#도 다음 구조다.

```csharp
public pbc::RepeatedField<global::Protocol.ObjectInfo> Players
```

즉 proto 원본과 generated 코드가 같은 시점의 산출물인지 의심해야 한다.

## 패킷 변경 시 원칙

- proto 원본 하나를 기준으로 삼는다.
- 서버 C++ generated 파일과 클라이언트 C# generated 파일을 같은 proto에서 다시 생성한다.
- 패킷 ID enum도 서버/클라이언트가 같은 값을 쓰는지 확인한다.
- `S_LOGIN.players` 타입을 바꾸면 서버 handler, 클라이언트 UI/로직, generated 파일 모두 함께 맞춘다.

## 서버 로그인 처리 현재 의미

- `C_LOGIN`을 받으면 DB 조회 없이 테스트 데이터 생성.
- `S_LOGIN.success = true`.
- `players`에 랜덤 위치를 가진 `ObjectInfo` 3개 추가.
- 이 응답은 캐릭터 선택 목록 또는 테스트 플레이어 목록처럼 쓰일 수 있지만, 현재는 하드코딩 더미다.

