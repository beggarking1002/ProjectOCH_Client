# Server Protocol Notes

Last updated: 2026-06-30

## 관련 서버 경로

- 서버 루트: `C:\ProjectOCH\Server`
- C++ 게임 서버: `C:\ProjectOCH\Server\GameServer`
- packet handler:
  - `C:\ProjectOCH\Server\GameServer\ServerPacketHandler.cpp`
  - `C:\ProjectOCH\Server\GameServer\ServerPacketHandler.h`
- room:
  - `C:\ProjectOCH\Server\GameServer\Room.cpp`
  - `C:\ProjectOCH\Server\GameServer\Room.h`
- walkmap:
  - `C:\ProjectOCH\Server\GameServer\FieldWalkMapData.cpp`
  - `C:\ProjectOCH\Server\GameServer\FieldWalkMapData.h`
- proto:
  - `Protocol.proto`
  - `Struct.proto`
  - `Enum.proto`

## 현재 서버 동작 요약

### C_LOGIN

`Handle_C_LOGIN`은 현재 DB 없이 `S_LOGIN.success = true`를 보낸다.

### C_ENTER_GAME

```text
Handle_C_ENTER_GAME
 -> ObjectUtils::CreatePlayer
 -> GRoom->DoAsync(Room::HandleEnterPlayer)
 -> Room::EnterRoom
 -> SendEnterGame(player, true)
 -> SendExistingPlayers(player)
 -> SendSpawn(object, exceptId = new player's id)
```

신규 클라:

```text
S_ENTER_GAME
S_SPAWN { existing players }
```

기존 클라:

```text
S_SPAWN { new player }
```

### C_MOVE

서버는 `C_MOVE.target`을 `Vec2Fixed` 월드 좌표로 받는다.

현재 목표 구조:

```text
C_MOVE.target
 -> FieldWalkMapData.FixedToCell
 -> walkable_ranges 검사
 -> 성공이면 player->position 갱신 후 S_MOVE broadcast
 -> 실패이면 position 갱신 없이 duration_ms=0 보정
```

## Walkmap JSON

Unity exporter가 생성한다.

클라이언트 기본 출력:

```text
Assets/GameData/Maps/Field_001.walkmap.json
```

서버 복사 위치:

```text
C:\ProjectOCH\Server\Data\Maps\Field_001.walkmap.json
```

현재 JSON 핵심 필드:

```json
{
  "map_id": "Field_001",
  "fixed_point_scale": 100,
  "cell_size": { "x": 0.95, "y": 1.0 },
  "origin_world": { "x": 0.0, "y": 0.0 },
  "bounds": {},
  "walkable_ranges": [
    { "y": -20, "x_min": 4, "x_max": 6 }
  ],
  "debug_walkable_cells": []
}
```

`walkable_ranges`는 inclusive range다.

```text
x_min <= cellX <= x_max
```

## 중요한 좌표계 이슈

`Field_001` 프리팹의 Grid는 Hexagon layout이다.

프리팹 설정:

```text
Grid.m_CellSize = { x: 0.95, y: 1.0, z: 1 }
Grid.m_CellLayout = 1
Grid.m_CellSwizzle = 0
Tilemap.m_TileAnchor = { x: 0, y: 0, z: 0 }
```

따라서 서버에서 단순 rectangle 공식으로 변환하면 클라 `Grid.WorldToCell()`과 어긋난다.

문제 공식:

```cpp
cellX = floor((worldX - originX) / cellSizeX);
cellY = floor((worldY - originY) / cellSizeY);
```

Hex Grid에서는 y row stride와 row offset을 반영해야 한다. 현재 서버 좌표 동기화 문제를 볼 때 `FieldWalkMapData::FixedToCell`과 `CellToFixed`가 가장 우선 확인 대상이다.

## 서버 빌드 팁

GameServer만 확인할 때:

```powershell
cd C:\ProjectOCH\Server
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  .\Server.sln `
  /t:GameServer `
  /p:Configuration=Debug `
  /p:Platform=x64
```

`GameServer.vcxproj`를 직접 빌드하면 PreBuild 상대 경로가 틀어질 수 있다. 솔루션 타겟으로 빌드하는 편이 안전하다.
