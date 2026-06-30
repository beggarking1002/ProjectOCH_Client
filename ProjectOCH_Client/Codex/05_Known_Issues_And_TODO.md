# Known Issues And TODO

Last updated: 2026-06-30

## 높은 우선순위

### 서버와 클라의 Hex Grid 좌표 동기화

`Field_001`은 Unity Hexagon Grid다. 서버가 rectangle 공식으로 `FixedToCell`을 계산하면 경계나 row offset에서 클라와 다르게 판정한다.

확인 대상:

- 서버 `FieldWalkMapData::FixedToCell`
- 서버 `FieldWalkMapData::CellToFixed`
- 클라 `FieldMapWalkArea.IsWalkable`
- exporter `FieldWalkMapExporter`

가능한 해결:

- 서버가 Unity Hex Grid 변환을 정확히 구현한다.
- 또는 프로토콜에 클라가 계산한 target cell을 추가한다.

현재는 프로토콜 변경 없이 서버 변환을 맞추려는 방향이다.

### Walkmap JSON 재생성 규칙

현재 `FieldWalkMapExporter`는 기본적으로 `Ground_Tilemap` 타일을 이동 가능 영역으로 본다.

옵션:

- `Block Tilemap`
  - `Block_Tilemap` 또는 `Prop_Tilemap` 자동 감지.
- `Subtract Block Tilemap`
  - 켜면 `Ground - Block`으로 추출.
  - 꺼두면 Ground 기준만 사용.

주의:

- `Block_Tilemap`이 배경/표시용으로 넓게 깔린 경우 이 옵션을 켜면 walkable이 0개가 될 수 있다.
- 서버 검증용 JSON을 갱신하면 서버 `Data\Maps` 쪽도 같은 파일로 맞춰야 한다.

### 서버 검증 로그 확인

기본 클라 동작은 이동 불가 타일 클릭을 먼저 막는다.

서버에서 `walkable=0` 로그를 보고 싶으면 `FieldPawnController`에서:

```text
validateLocallyBeforeSend = true
sendBlockedMoveForDebug = true
```

또는 모든 클릭을 서버로 보내려면:

```text
validateLocallyBeforeSend = false
```

### S_ENTER_GAME / S_SPAWN 스냅샷 구조

현재 서버는 `S_ENTER_GAME`과 기존 플레이어 목록용 `S_SPAWN`을 별도 패킷으로 보낸다.

클라에서 씬 전환 중 `S_SPAWN`을 놓치지 않도록 `NetworkService._knownPlayers` 캐시를 사용한다.

장기적으로는 아래처럼 정리하는 편이 더 명확하다.

```proto
message S_ENTER_GAME
{
    bool success = 1;
    ObjectInfo player = 2;
    repeated ObjectInfo players = 3;
}
```

## 중간 우선순위

### FieldMapWalkArea 런타임 설정

`useBlockTilemap`은 현재 기본 false다. 실제 gameplay에서 Block 레이어를 클라 사전 검증에도 쓸지 결정해야 한다.

### 이동 보정과 예측

현재 클라는 기본적으로 서버 `S_MOVE` 승인 후 이동한다. 즉 강한 서버 권위 구조에 가깝다.

나중에 조작감을 개선하려면:

- 클라 예측 이동
- 서버 거부 시 rollback/reconciliation
- 이동 중 새 명령 처리

을 설계해야 한다.

### 스폰 위치 중복

서버는 walkable cell에서 랜덤 스폰한다. 현재 같은 cell에 여러 플레이어가 겹치는지 여부는 별도 점유 검증이 필요하다.

### Addressables 배포 경로

`Remote.LoadPath = http://localhost/[BuildTarget]`는 개발용이다. 배포 전 실제 remote path 전략이 필요하다.

## 낮은 우선순위

- `System.Net.Http` 버전 충돌 warning 정리.
- `Google.Protobuf.dll` 버전 관리 문서화.
- 기존 axial 관련 스크립트는 field에서 분리되었지만 battle용으로 남아 있다. battle 구현 시 재검토.
