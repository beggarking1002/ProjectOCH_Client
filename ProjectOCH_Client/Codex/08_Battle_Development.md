# Battle Development

Last updated: 2026-07-03

## 현재 목표

Battle room은 axial 좌표계 기반의 hex SRPG 전투로 구현한다.

Field와 차이:

- Field: 마우스 클릭 위치로 자유 이동.
- Battle: 마우스로 찍은 타일의 중앙으로 이동.
- Battle 이동/스킬은 서버 검증을 거쳐 확정.

## 현재 구현된 흐름

```text
FieldScene
 -> B key
 -> C_ENTER_BATTLE
 -> S_ENTER_BATTLE
 -> BattleScene
 -> BattleField_001 load
 -> allied/enemy pawn spawn
 -> current turn 표시
 -> Move 또는 Skill 선택
 -> tile click
 -> C_BATTLE_MOVE 또는 C_BATTLE_SKILL
 -> S_BATTLE_MOVE 또는 S_BATTLE_SKILL
 -> pawn / hp / next turn 반영
```

## 핵심 파일

```text
Assets/Scripts/Scenes/BattleSceneFlow.cs
Assets/Scripts/Scenes/BattleSceneAddressableLoader.cs
Assets/Scripts/Battle/AxialCoord.cs
Assets/Scripts/Battle/BattleMapGrid.cs
Assets/Scripts/Battle/BattleObjectManager.cs
Assets/Scripts/Battle/BattlePawnController.cs
Assets/Scripts/Battle/BattleActionMode.cs
Assets/Scripts/Battle/BattleUIController.cs
```

## BattleSceneAddressableLoader

현재 동작:

```text
BattleScene 진입
 -> Addressables.InstantiateAsync("BattleField_001")
 -> BattleMapGrid 초기화
 -> BattleObjectManager 초기화
 -> LastEnterBattle가 있으면 서버 pawn 정보로 spawn
 -> 없으면 debug pawn 2개 spawn
 -> BattleUIController 초기화
```

주의:

- `BattleUIController`는 아직 `Canvas_BattleUI.prefab`을 쓰지 않는다.
- 다음 작업에서 UI prefab 로드/바인딩 방식으로 바꿔야 한다.

## BattleObjectManager

책임:

- battle pawn addressable spawn
- pawn id -> controller dictionary
- local pawn id set
- current turn pawn id
- action mode
- mouse input -> axial 변환
- `C_BATTLE_MOVE` 송신
- `C_BATTLE_SKILL` 송신
- `S_BATTLE_MOVE` 적용
- `S_BATTLE_SKILL` 적용
- current turn indicator 갱신

현재 규칙:

- 서버 battle에서는 현재 턴인 내 pawn만 조작 가능.
- 이동/스킬 패킷 송신 후 `WaitingServer`.
- 응답을 받으면 `Move` 상태로 복귀.

## BattleActionMode

```csharp
public enum BattleActionMode
{
    Move,
    Skill1,
    Skill2,
    Skill3,
    WaitingServer,
}
```

UI에서 mode를 바꾸고, 맵 클릭 시 mode에 따라 move 또는 skill packet을 보낸다.

## Battle pawn prefab mapping

`BattleObjectManager.GetPawnAddress(BattlePawnInfo info)` 기준:

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

fallback은 `Pawn_Beige_Ice`.

## Current turn indicator

`BattlePawnController`가 `TurnIndicator` child object를 만들고 `TextMesh`로 `TURN`을 표시한다.

`BattleObjectManager.RefreshTurnIndicators()`가 현재 턴 pawn만 켠다.

## 현재 Battle UI 상태

### 임시 런타임 UI

`BattleUIController`가 코드로 아래 버튼을 만든다.

```text
Move
Skill1
Skill2
Skill3
End Turn
```

이 UI는 기능 확인용이다.

### 사용자가 만든 UI prefab

위치:

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

현재 상태:

- 목업으로 사용 가능.
- `ActionPanel`, `TurnQueue`에 `HorizontalLayoutGroup`이 있음.
- 아직 Button/Text/TMP 바인딩은 정리되지 않음.
- 아직 실제 BattleScene에 연결되지 않음.

정리 필요:

```text
ActionIcon1 (1) 등 -> ActionSlot_01~08
TurnQueue 자식      -> TurnPortraitSlot_01~08
TrunExit            -> TurnExit
```

## 다음 작업 제안

### Step 1. UI prefab 정리

- `Canvas_BattleUI.prefab` 이름 정리
- `ActionSlot_01~03`에 Button 추가
- 가능하면 `BattleActionSlot` 개별 prefab으로 분리

### Step 2. BattleHudController 작성

기존 `BattleUIController`를 교체하거나 내부를 바꾼다.

역할:

- `Canvas_BattleUI` prefab instance 찾기/로드
- `ActionSlot_01~03` 클릭 연결
- 현재 mode 표시
- 현재 턴 표시
- local turn인지에 따라 버튼 활성화

### Step 3. BattleSceneAddressableLoader 수정

현재:

```text
BattleUIController가 코드 생성 UI를 만듦
```

변경:

```text
Canvas_BattleUI prefab 로드
BattleHudController.Initialize(objectManager)
```

### Step 4. TurnQueue 데이터 확장

현재는 `next_turn_pawn_id`만 있으므로 전체 큐 애니메이션이 어렵다.

서버/proto에 전체 큐 추가 필요:

```proto
repeated uint64 turn_queue = n;
```

또는 별도:

```proto
message S_BATTLE_TURN_QUEUE {
  uint64 battle_id = 1;
  repeated uint64 pawn_ids = 2;
}
```

### Step 5. 스킬 결과 연출

- damage text
- HP bar
- skill animation trigger
- death 처리
- target highlight

## 검증 방법

1. 서버 실행
2. Unity Editor에서 TitleScene 시작
3. GameStartButton 클릭
4. FieldScene 진입
5. `B` key 입력
6. BattleScene 진입
7. allied/enemy pawn spawn 확인
8. 현재 턴 pawn 머리 위 `TURN` 확인
9. Move 버튼 또는 mode에서 타일 클릭
10. Skill1 선택 후 target tile 클릭
11. Console에서 `C_BATTLE_SKILL`, `S_BATTLE_SKILL` 로그 확인

현재 서버 구현에 따라 Skill1만 success일 수 있다.
