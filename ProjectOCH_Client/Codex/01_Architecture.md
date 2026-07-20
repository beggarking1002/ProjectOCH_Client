# 아키텍처와 실행 흐름

> 코드 구조, 장면 흐름, 좌표계 또는 프리팹 조합 방식이 바뀌면 이 문서를 갱신한다.

## 기술 구성

| 영역 | 사용 기술 |
| --- | --- |
| 엔진 / 렌더링 | Unity 6.3, URP 2D |
| UI | uGUI (`UnityEngine.UI`) |
| 입력 | Unity Input System |
| 콘텐츠 로딩 | Addressables 2.9 |
| 네트워크 | TCP Socket 기반 자체 `ServerCore` |
| 직렬화 | Google Protobuf |
| 전투 좌표 | 서버: pure axial, Unity 맵: Point Top Odd-R offset cell |

## 장면 흐름

```text
TitleScene
  └─ GameStartButton → C_ENTER_GAME → S_ENTER_GAME
       └─ FieldScene
            ├─ Field_001 / WorldMapRoot / field pawn Addressables 로드
            ├─ C_BATTLE_INVITE 또는 C_ENTER_BATTLE(개발용 B 키)
            └─ S_ENTER_BATTLE → BattleScene
                 ├─ BattleField_001 / battle pawns / BattleSceneUI 로드
                 ├─ C_BATTLE_MOVE / C_BATTLE_SKILL / C_BATTLE_END_TURN
                 └─ S_BATTLE_RESULT → C_BATTLE_RESULT_ACK → FieldScene
```

빌드 설정의 활성 장면은 `TitleScene`, `FieldScene`, `BattleScene`이다. 실제 맵·Pawn·UI 콘텐츠 대부분은 Addressables 프리팹이다.

## 지속 객체와 책임

| 객체 | 생명주기 | 책임 |
| --- | --- | --- |
| `GameRoot` | 게임 시작부터 종료까지 | `AppServices`, 서버 연결, 매 프레임 서비스 Tick |
| `TitleSceneFlow` | `DontDestroyOnLoad` | 타이틀 버튼과 입장 응답 처리 |
| `FieldSceneAddressableLoader` | `DontDestroyOnLoad` | 필드 맵 / 월드맵 로드 및 해제 |
| `BattleSceneFlow` | `DontDestroyOnLoad` | 전투 입장·결과 ACK에 따른 장면 전환 |
| `BattleSceneAddressableLoader` | `DontDestroyOnLoad` | 전투 맵, Pawn, UI 준비; 맵 Tile 에셋 로드 후 스폰 |
| `SceneTransitionOverlay` | 필요 시 생성 | 장면 전환 중 화면 차단 |

## 네트워크 경로

```text
Socket thread
  → GameServerSession
  → Generated PacketManager
  → PacketHandler queue
  → GameRoot.Update()
  → NetworkService.Tick()
  → NetworkService event
  → Field / Battle / Scene controller
```

`PacketHandler`는 수신 패킷을 큐에 넣고 Unity 메인 스레드에서 이벤트를 실행한다. `NetworkService`는 앱 수준 이벤트와 최신 상태(`LastEnterBattle` 등)를 보관한다.

## 전투 맵과 좌표 경계

`BattleField_001`의 Grid는 Point Top hex이며 Unity 내부 표현은 **Odd-R offset cell**이다. 반면 서버·프로토콜의 `Axial`은 pure axial이다.

```text
Unity cell (col, row) → axial (q, r)
q = col - ((row - (row & 1)) / 2)
r = row

axial (q, r) → Unity cell (col, row)
col = q + ((r - (r & 1)) / 2)
row = r
```

- 이 변환은 `BattleMapGrid.CellToAxial` / `AxialToCell`에만 둔다.
- 클릭, 이동·스킬 패킷, Pawn 배치, Tile Delta, Aura 범위 표시는 모두 이 경계를 거친다.
- 서버 axial을 Unity cell로 직접 사용하면 인접 6칸, 사거리, Aura, 우박 범위가 어긋난다.

타일맵은 역할별 3계층이다.

| Tilemap | 역할 | 전투 중 변경 |
| --- | --- | --- |
| `Ground_Tilemap` | 원본 NORMAL/WATER 지형 | 스냅샷 초기화 때만 갱신 |
| `Prop_Tilemap` | 정적 벽·오브젝트·이동 불가 요소 | 변경하지 않음 |
| `CombatOverlay_Tilemap` | ICE 등 동적 전투 효과 | `tile_deltas`로 갱신 |

`BattleMapGrid.LoadVisualTilesAsync`는 Addressables 맵 번들에 직렬화 Tile 참조가 빠진 경우에도 `Tile/grass`, `Tile/water`, `Tile/ice` 주소에서 시각 Tile을 보충한다.

## Pawn 조합과 시각 표현

전투 Pawn은 다음처럼 조합한다.

```text
PawnBase (BattlePawn, Status UI, visual root)
  └─ visual
       └─ PawnClass별 Addressable 시각 프리팹
            └─ PawnTeamRing
```

- 서버의 `BattlePawn` 상속 구조를 클라이언트에 복제하지 않는다. `PawnClass → Addressable 시각 프리팹` 매핑만 유지한다.
- `PawnTeamRing`은 더 이상 `PawnBase`에 있지 않다. 각 클래스 시각 프리팹에 독립적으로 들어 있으므로, 캐릭터별 발 위치에 맞춰 Local Position을 조절할 수 있다. 현재 기본 위치는 `(0, 0, 0)`이다.
- 같은 시각 프리팹은 FieldScene에서도 사용한다. 팀 링은 기본 비활성이고 `BattlePawnController`가 전투 초기화 시에만 활성화한다.
- `BattlePawn`은 서버 스냅샷 반영, 이동, 공통 상태 UI, 팀 링 같은 공통 표현만 맡는다. 클래스별 표현과 로컬 보조 기능은 상속 클래스로 분리한다.
- 현재 계층은 `BattlePawn → Beige → BeigeIce`다. `BeigeIce`는 자신의 `BEIGE_ICE_STORM_CENTER` Aura 상태만 읽어 Pawn 부착형 반경 VFX를 갱신한다.

## 디렉터리 가이드

```text
Assets/
  Scripts/
    App/         앱 루트와 서비스 조합
    Scenes/      장면 흐름과 Addressables 로더
    Services/    네트워크 앱 서비스
    Packet/      Socket 기반, 핸들러, 생성된 프로토콜 코드
    Field/       필드 맵, 이동, 캐릭터, 전투 초대
    Battle/      전투 맵, Pawn, 입력, UI, GameData
  @Resources/    Addressables 맵, Pawn, UI, 아트 프리팹
  GameData/      CSV 원본 데이터
  AddressableAssetsData/ Addressables 설정과 그룹
```

## 설계 경계

- 새 기능은 원칙적으로 `GameRoot.Instance.Network`를 사용한다. `GameServerConnection`은 MonoBehaviour 호환 래퍼다.
- `Field.AxialCoord`와 `Battle.AxialCoord`는 별도 타입이다. namespace 혼동에 유의한다.
- Addressables 주소를 변경하면 로더와 그룹 등록을 함께 수정한다.
- `Assets/Scripts/Packet/Generated/`는 생성물이다. 직접 수정하지 않는다.
