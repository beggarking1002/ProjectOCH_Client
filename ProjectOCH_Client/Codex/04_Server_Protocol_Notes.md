# Server Protocol Notes

Last updated: 2026-07-03

## 서버 경로

- Server root: `C:\ProjectOCH\Server`
- GameServer: `C:\ProjectOCH\Server\GameServer`
- 주요 파일:
  - `GameServer.cpp`
  - `ServerPacketHandler.cpp`
  - `Room.cpp`
  - `FieldWalkMapData.cpp`
  - Battle 관련 구현은 서버 현재 상태를 다시 확인해야 한다.

## 현재 클라이언트가 기대하는 서버 동작

### Login

```text
C_LOGIN
 -> S_LOGIN { success = true/false }
```

### Field 입장

```text
C_ENTER_GAME
 -> S_ENTER_GAME { success, player }
 -> S_SPAWN { existing players }
 -> 이후 다른 클라에게 S_SPAWN { new player }
```

클라이언트는 `NetworkService._knownPlayers` 캐시를 유지한다. `S_SPAWN`이 FieldScene 로딩 전에 도착해도 FieldObjectManager 초기화 시 snapshot으로 복구한다.

### Field 이동

```text
C_MOVE { Vec2Fixed target }
 -> 서버 walkmap 검증
 -> 성공: S_MOVE broadcast
 -> 실패: 요청자에게 보정 S_MOVE 또는 실패 처리
```

Field는 `Vec2Fixed` world 좌표 기반이다. 서버 검증은 `Field_001.walkmap.json`을 사용한다.

## Battle 입장

클라이언트는 FieldScene에서 `B` key 입력 시 `C_ENTER_BATTLE`을 보낸다.

서버는 성공 시:

```text
S_ENTER_BATTLE
  success = true
  battle_id
  map_id
  allied_pawns
  enemy_pawns
  current_turn_pawn_id
```

실패 시:

```text
S_ENTER_BATTLE
  success = false
  reason
```

## Battle 이동

클라이언트는 현재 턴인 내 pawn만 `C_BATTLE_MOVE`를 보낸다.

서버가 검증해야 할 것:

- battle id 유효성
- pawn id 유효성
- pawn owner가 요청 session과 일치하는지
- 현재 턴 pawn인지
- target axial이 battle map walkable인지
- 이동 range 안인지
- target이 occupied인지

서버 응답:

```text
S_BATTLE_MOVE
  success
  battle_id
  pawn_id
  start
  target
  next_turn_pawn_id
  result
  reason
```

실패 시에도 클라이언트가 `WaitingServer`에서 빠져나올 수 있도록 `S_BATTLE_MOVE`를 보내는 것이 좋다.

## Battle 스킬

클라이언트는 `Skill1~3` 선택 후 타일 클릭 시 아래를 보낸다.

```text
C_BATTLE_SKILL
  battle_id
  caster_pawn_id
  skill_slot
  target_pawn_id
  target_axial
```

정책:

- `skill_slot = 1, 2, 3`
- `target_pawn_id`는 클릭한 axial에 pawn이 있으면 채우고, 없으면 0.
- `target_axial`은 항상 채움.

서버가 검증해야 할 것:

- battle id 유효성
- caster pawn 유효성
- caster owner 검증
- 현재 턴 검증
- skill slot 유효성
- 스킬별 range/target rule
- target pawn 필요 여부
- target axial walkable/valid 여부

응답:

```text
S_BATTLE_SKILL
  success
  battle_id
  caster_pawn_id
  skill_slot
  target_pawn_id
  target_axial
  damage
  target_hp
  next_turn_pawn_id
  reason
```

현재 대화 기준으로는 서버가 Skill1만 success 처리할 가능성이 있다. 클라이언트는 Skill1~3 모두 보낼 수 있다.

## TurnQueue 관련 proto 필요

현재 클라이언트가 받은 정보는 `next_turn_pawn_id`뿐이다. 사용자가 원하는 UI는 다음과 같다.

```text
TurnQueue 왼쪽 = 현재 턴 캐릭터 초상화
턴 종료 시 현재 초상화가 아래로 떨어지며 소멸
나머지 초상화가 왼쪽으로 한 칸씩 이동
오른쪽에 다음 예측 초상화 추가
```

이를 제대로 하려면 서버가 전체 턴 큐를 보내는 것이 좋다.

추천 proto:

```proto
message S_BATTLE_TURN_QUEUE {
  uint64 battle_id = 1;
  repeated uint64 pawn_ids = 2;
}
```

또는 `S_ENTER_BATTLE`, `S_BATTLE_MOVE`, `S_BATTLE_SKILL`에 `repeated uint64 turn_queue = n;`을 추가해도 된다.

## 서버 빌드 참고

```powershell
cd C:\ProjectOCH\Server
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  .\Server.sln `
  /t:GameServer `
  /p:Configuration=Debug `
  /p:Platform=x64
```
