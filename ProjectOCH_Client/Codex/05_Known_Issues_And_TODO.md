# Known Issues And TODO

Last updated: 2026-07-03

## 최우선 TODO

### 1. Canvas_BattleUI prefab을 실제 BattleScene에 연결

현재:

- `Assets/@Resources/Prefab/UI/Canvas_BattleUI.prefab`이 존재한다.
- `BattleSceneAddressableLoader`는 아직 이 prefab을 로드하지 않는다.
- `BattleUIController`가 런타임에 임시 UI를 코드로 만든다.

해야 할 일:

1. prefab 이름 정리
   - `ActionIcon1 (1)` 등 -> `ActionSlot_01~08`
   - `TurnQueue` 자식 -> `TurnPortraitSlot_01~08`
   - `TrunExit` -> `TurnExit`
2. `ActionSlot_01~03`에 Button 또는 클릭 처리 추가
3. prefab을 Addressables에 등록할지 Resources로 로드할지 결정
4. `BattleUIController`를 prefab 바인딩 방식으로 변경
5. 기존 코드 생성 UI 제거

### 2. 전투 스킬 UI와 BattleActionMode 연결

현재:

- `BattleActionMode`는 `Move`, `Skill1`, `Skill2`, `Skill3`, `WaitingServer`.
- `BattleObjectManager.SetActionMode()`가 존재한다.
- Skill mode에서 타일 클릭 시 `C_BATTLE_SKILL`을 보낸다.

해야 할 일:

- `ActionSlot_01` -> Skill1
- `ActionSlot_02` -> Skill2
- `ActionSlot_03` -> Skill3
- 선택된 슬롯 highlight 표시
- `WaitingServer` 상태에서는 슬롯 클릭 비활성화 또는 무시
- 스킬 실패 시 UI 상태를 `Move`로 복구

### 3. TurnQueue proto와 UI

현재:

- 클라이언트는 `next_turn_pawn_id`만 받는다.
- 현재 턴 pawn 머리 위 `TURN` 표시는 된다.
- 전체 턴 큐 UI를 정확히 만들 데이터는 부족하다.

해야 할 일:

- 서버와 proto에 전체 턴 큐를 추가할지 결정.
- 추천:

```proto
message S_BATTLE_TURN_QUEUE {
  uint64 battle_id = 1;
  repeated uint64 pawn_ids = 2;
}
```

또는 `S_ENTER_BATTLE`, `S_BATTLE_MOVE`, `S_BATTLE_SKILL`에 `repeated uint64 turn_queue` 추가.

### 4. End Turn 패킷

현재:

- `BattleUIController.DebugEndTurn()`은 로그만 찍는다.
- 서버 end turn 패킷이 없다.

해야 할 일:

- `C_BATTLE_END_TURN`, `S_BATTLE_TURN_CHANGED` 또는 유사 패킷 설계.
- 또는 move/skill 사용 후 항상 턴이 끝나는 구조로 갈지 결정.

## 중간 우선순위

### Skill 결과 연출

현재:

- `S_BATTLE_SKILL` 성공 시 target hp 값만 local info에 반영한다.
- 실제 HP bar, damage text, animation, death 처리 없음.

해야 할 일:

- `BattlePawnController.ApplyHp()` 이후 HP UI 갱신
- damage floating text
- skill animation trigger
- hp <= 0 사망 처리
- 사망 pawn 점유 해제는 서버 결과와 맞춰 처리

### Battle walkmap / prop tile

전투 맵은 axial 기반이다. Ground tile 위에 Prop tile을 얹는 구조에서는 기본적으로 prop tile을 점유/이동 불가로 봐야 한다.

나중에 prop이 파괴되어 이동 가능해지는 경우:

- 서버가 battle map의 동적 blocked state를 관리해야 한다.
- 클라이언트는 서버가 내려준 상태를 표시만 한다.
- 초기에는 정적 walkmap으로 충분하다.

### Battle UI prefab 구조 개선

`ActionSlot`과 `TurnPortraitSlot`은 개별 prefab으로 분리하는 것이 좋다.

추천:

```text
BattleActionSlot.prefab
  Frame Image
  SkillIcon Image
  CooldownOverlay Image
  CooldownText TMP_Text
  SelectionHighlight Image
  Button

BattleTurnPortraitSlot.prefab
  Portrait Image
  Frame Image
  TeamColor Image
  CurrentTurnHighlight Image
```

## 낮은 우선순위 / 정리

- `System.Net.Http` version conflict warning 정리.
- `Google.Protobuf.dll` 버전 문서화.
- 기존 Field hex-grid 서버 좌표계 이슈 재검토.
- Addressables remote path를 배포 환경에 맞게 결정.
- `Canvas_BattleUI`를 Addressables 그룹에 넣을지 확인.

## 주의할 점

- UI EventSystem은 `InputSystemUIInputModule`을 써야 한다.
- `StandaloneInputModule`이 남아 있으면 Input System only 설정에서 오류가 난다.
- 서버 전투에서는 현재 턴이 아닌 pawn을 클라이언트에서 조작하지 못하게 막고 있다.
- 그래도 최종 권위 검증은 항상 서버에서 해야 한다.
