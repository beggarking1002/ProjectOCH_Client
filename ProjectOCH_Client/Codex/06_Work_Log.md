# Work Log

Last updated: 2026-07-03

## 2026-07-03

### Battle skill packet client integration

추가/수정:

- `Assets/Scripts/Packet/PacketHandler.cs`
  - `BattleSkillReceived` 이벤트 추가
  - `S_BATTLE_SKILLHandler` 추가
- `Assets/Scripts/Services/Network/NetworkService.cs`
  - `SendBattleSkill(...)` 추가
  - `C_BATTLE_SKILL` 송신 case 추가
  - `S_BATTLE_SKILL` 수신 처리 추가
- `Assets/Scripts/Network/GameServerConnection.cs`
  - `SendBattleSkill(...)` wrapper 추가
- `Assets/Scripts/Battle/BattleObjectManager.cs`
  - Skill1/2/3 mode에서 타일 클릭 시 `C_BATTLE_SKILL` 송신
  - 클릭한 axial 위 pawn을 찾아 `target_pawn_id` 채움
  - `S_BATTLE_SKILL` 성공 시 target hp와 next turn 반영
- `Assets/Scripts/Battle/BattlePawnController.cs`
  - `ApplyHp(int hp)` 추가

검증:

- MSBuild 기준 C# 컴파일 통과.
- 기존 `System.Net.Http` warning만 남음.

### Battle action state and turn indicator

추가/수정:

- `Assets/Scripts/Battle/BattleActionMode.cs`
  - `Move`, `Skill1`, `Skill2`, `Skill3`, `WaitingServer`
- `BattleObjectManager`
  - 현재 턴 pawn만 조작 가능
  - move/skill 송신 후 `WaitingServer`
  - 서버 응답 후 `Move` 복귀
- `BattlePawnController`
  - 현재 턴 pawn 머리 위에 `TURN` TextMesh indicator 표시
- `BattleUIController`
  - 임시 코드 생성 UI
  - Move/Skill1/Skill2/Skill3/End Turn 버튼
  - 현재 턴 pawn과 mode 표시
  - EventSystem을 Input System 기준으로 보정

### Canvas_BattleUI prefab 확인

사용자가 생성:

```text
Assets/@Resources/Prefab/UI/Canvas_BattleUI.prefab
```

확인된 구조:

```text
Canvas_BattleUI
  TurnPanel
  MenuDropDown
  TurnQueue
  Right_EnemyPawnPanel
  TileInfo
  Setting
  Question
  Left_SelectedPawnPanel
  TrunExit
  ActionPanel
```

상태:

- 목업으로 충분히 사용 가능.
- `ActionPanel`, `TurnQueue`에 `HorizontalLayoutGroup`이 들어가 있다.
- 아직 코드와 연결되어 있지 않다.
- 다음 작업은 prefab 바인딩형 UI controller 작성.

## 2026-07-02

### Battle scene entry and pawn spawning

추가/수정:

- FieldScene에서 `B` key를 누르면 `C_ENTER_BATTLE` 송신.
- `S_ENTER_BATTLE.success` 수신 시 `BattleScene` 로드.
- `BattleSceneAddressableLoader`가 `BattleField_001` 로드.
- 서버의 allied/enemy pawn 목록 기반으로 battle pawn spawn.
- `PawnClass`와 Addressable prefab 주소 매핑.

PawnClass 매핑:

```text
SuenAxeSword      -> Pawn_Suen_AxeSword
SuenParvis        -> Pawn_Suen_Parvis
BeigeFire         -> Pawn_Beige_Fire
BeigeIce          -> Pawn_Beige_Ice
ZillianLongbow    -> Pawn_Zillian_Longbow
ZillianMace       -> Pawn_Zillian_Mace
AlenSpear         -> Pawn_Alen_Spear
AlenSwordShield   -> Pawn_Alen_SwordShield
SeraNecromancer   -> Pawn_Sera_Necromancer
SeraWarlock       -> Pawn_Sera_Warlock
DarkhandSword     -> Pawn_Darkhand_Sword
```

### Battle movement

- Battle은 axial 좌표계 사용.
- 마우스로 타일을 클릭하면 tile center로 이동.
- 서버 전투에서는 `C_BATTLE_MOVE` 송신 후 `S_BATTLE_MOVE`를 기다린다.
- Debug battle에서는 서버 없이 local 이동 가능.

## 2026-06-30

### Multiplayer build/run tool

추가:

- `Assets/Editor/MultiplayerBuildAndRun.cs`

기능:

- Addressables content build
- Windows x64 client build
- 같은 exe를 여러 개 실행
- 각 클라이언트에 `-playerIndex` 전달
- 로그 분리

### Run in background

적용:

- `ProjectSettings/ProjectSettings.asset`
- `Assets/Editor/MultiplayerBuildAndRun.cs`
- `Assets/Scripts/App/GameRoot.cs`

목적:

- 여러 클라이언트 실행 시 포커스 없는 창도 네트워크 tick이 계속 돌도록 함.

### Field movement and map validation

- Field는 `Vec2Fixed` world 좌표 기반 이동.
- 서버는 walkmap JSON으로 이동 가능 여부 검증.
- 클라이언트는 옵션으로 로컬 검증을 먼저 할 수 있음.
- `FieldWalkMapExporter`로 tilemap에서 walkmap JSON 생성.

## 2026-06-20 이전

### GameRoot / NetworkService refactor

- 기존 대형 Managers singleton 구조 대신 `GameRoot -> AppServices -> NetworkService` 구조로 정리.
- `PacketHandler`는 main thread event dispatch 역할.
- `NetworkService`는 상태 캐시와 송신 API 담당.

### Addressables setup

- Addressables package 설치.
- `Assets/AddressableAssetsData` 생성.
- Local/Remote group 구성.
