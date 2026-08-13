# 아키텍처와 실행 흐름

> 기준일: 2026-07-29

## 기술 구성

| 영역 | 구성 |
| --- | --- |
| 엔진/렌더링 | Unity 6.3, URP 2D, Tilemap |
| UI/입력 | uGUI, TextMesh Pro, Unity Input System |
| 콘텐츠 | Addressables 2.9, CSV GameData |
| 통신 | TCP Socket 기반 `ServerCore`, Google Protobuf |
| 전투 좌표 | 서버 pure axial, Unity Point-Top Odd-R offset cell |

## 씬 및 런타임 흐름

```text
GameRoot (DontDestroyOnLoad)
  └─ AppServices / NetworkService

TitleScene
  └─ GameStartButton → C_ENTER_GAME → S_ENTER_GAME → FieldScene

FieldScene
  ├─ Field_001_WalkMap JSON / WorldMapRoot / Field Pawn Addressables
  ├─ 다른 Pawn 클릭 → 전투 초대 UI → 초대/수락 패킷 → 클래스 선택 UI
	└─ 양측 선택 잠금 → S_ENTER_BATTLE → BattleScene

BattleScene
  ├─ BattleField_001 / PawnBase + 클래스별 visual / BattleSceneHUD Addressables
  ├─ C_BATTLE_MOVE / C_BATTLE_SKILL / C_BATTLE_END_TURN
  └─ S_BATTLE_RESULT → 결과 UI → C_BATTLE_RESULT_ACK → FieldScene
```

`GameRoot`는 시작 전에 자동 생성되어 전 씬에서 유지된다. 폰트 Addressable을 제공하고 `AppServices.Tick()`을 매 프레임 호출한다. `TitleSceneFlow`, `FieldSceneAddressableLoader`, `BattleSceneFlow`, `BattleSceneAddressableLoader`, `SceneTransitionOverlay`도 각자의 런타임 부트스트랩으로 씬 전환을 관찰한다.

## 네트워크 스레드 경계

```text
Socket thread
  → GameServerSession / Generated PacketManager
  → PacketHandler main-thread job queue
  → GameRoot.Update()
  → NetworkService.Tick()
  → Field / Battle / Scene 이벤트 구독자
```

`NetworkService`는 연결 상태와 최근 수신 패킷을 보관하고, `EnterGameReceived`, `MoveReceived`, `EnterBattleReceived`, 전투/초대/결과 이벤트를 Unity 메인 스레드에서 발행한다. `GameServerConnection`은 이 서비스를 MonoBehaviour 형태로 노출하는 호환 래퍼다.

## 디렉터리 책임

```text
Assets/
  Scripts/
    App/       부트스트랩과 서비스 조합
    Scenes/    씬 흐름, 전환 오버레이, Addressables 로더
    Services/  네트워크 서비스
    Network/   씬에서 쓰는 네트워크 MonoBehaviour 래퍼
    Packet/    소켓 세션, 패킷 디스패치, Protobuf 생성 코드
    Field/     필드 맵, 이동, Pawn 동기화, 전투 초대
    Battle/    전투 맵, Pawn, 입력, 상태 반영, UI, CSV 저장소
  @Resources/              Addressable 등록 원본 프리팹/아트
  GameData/                CSV 원본
  AddressableAssetsData/   그룹과 주소 설정
  Scenes/                  Title/Field/Battle/Develop 씬
  Editor/                  맵/프리팹/Addressables 설정 보조 도구
Packages/                  Unity 패키지 의존성
ProjectSettings/           Unity 프로젝트/빌드 설정
Builds/                    산출물
Portfolio/                 포트폴리오 문서 산출물
```

## Addressables 경계

| 그룹 | 주요 주소 |
| --- | --- |
| Maps | `WorldMapRoot`, `BattleField_001` |
| Pawn | `PawnBase`와 클래스별 visual Pawn |
| UI | `BattleSceneHUD`, `BattleResultUI`, `FieldBattleInviteUI`, `SceneTransitionOverlay`, 폰트/초상화 |
| Tile | `Tile/grass`, `Tile/water`, `Tile/ice`, `Tile/fire` 등 |
| SkillIcon | 클래스별 passive/skill/ultimate/sub 아이콘 |
| GameData | 클래스, Pawn, 스킬, 효과, ZOC, 문구 CSV, `Field_001_WalkMap` JSON |

필드 씬은 `Field_001` Tilemap 프리팹을 런타임에 인스턴스화하지 않는다. `Field_001_WalkMap`의 압축 walkable range가 로컬 클릭 프리뷰와 기본 spawn 위치를 제공하고, `WorldMapRoot`가 시각 표현을 담당한다. 실제 이동 결과는 서버의 `S_MOVE`가 권위 있다.

전투 Pawn은 `PawnBase`를 먼저 인스턴스화한 뒤 `visual` 자식에 클래스별 visual 프리팹을 붙이는 조합 구조다. 서버의 `PawnClass`에 따라 `BattlePawn` 또는 `BeigeIce`, `BeigeFire`, `SuenAxe`, `SuenParvis`, `AlenSpear`, `ZillianLongbow` 컴포넌트를 추가한다. 지원되지 않는 표현 클래스는 fallback visual과 공통 `BattlePawn`으로 처리한다.

## 전투 맵 좌표 경계

`BattleField_001`은 Ground/Prop/CombatOverlay의 3 계층 Tilemap이다. 서버의 pure axial `(q, r)`과 Unity Odd-R cell `(col, row)` 변환은 다음과 같으며, `BattleMapGrid`만 이 변환을 담당한다.

```text
q = col - ((row - (row & 1)) / 2)
r = row

col = q + ((r - (r & 1)) / 2)
row = r
```

`CombatOverlay_Tilemap`만 서버의 `tile_deltas`로 갱신된다. Ground와 Prop은 정적 시각 레이어다.
