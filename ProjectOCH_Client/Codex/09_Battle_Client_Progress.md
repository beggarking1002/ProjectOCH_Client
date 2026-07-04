# Battle Client Progress

작성일: 2026-07-04

## 목적

전투 UI와 서버 상태 연동 준비 현황을 정리한다. 이후 서버 전투 시스템 구현 결과가 들어오면, 이 문서를 기준으로 클라이언트에서 어떤 필드를 소비하고 어떤 패킷을 추가로 맞춰야 하는지 확인한다.

## 현재 완료된 작업

### BattleSceneUI 로딩

- Addressables 그룹 `UI`에 등록된 `BattleSceneUI`를 우선 로드한다.
- 에디터 환경에서는 Addressables 로드 실패 시 `Assets/@Resources/Prefab/UI/BattleSceneUI.prefab`를 fallback으로 로드한다.
- 런타임에서 `EventSystem`과 `InputSystemUIInputModule`을 보장한다.

### ActionSlot 바인딩

`BattleUIController`가 `ActionSlot_01`부터 `ActionSlot_08`까지 버튼 컴포넌트를 바인딩한다.

| 슬롯 | 역할 |
| --- | --- |
| `ActionSlot_01` | Move |
| `ActionSlot_02` | Skill1 |
| `ActionSlot_03` | Skill2 |
| `ActionSlot_04` | Skill3 |
| `ActionSlot_05` | Skill4 |
| `ActionSlot_06` | Ultimate |
| `ActionSlot_07` | SubAction |
| `ActionSlot_08` | Wait / EndTurn |

- `TurnExit`에도 버튼 컴포넌트를 붙이고 턴 넘기기 수단으로 연결했다.
- UI 버튼 클릭이 타일맵 클릭으로 전파되는 문제는 `BattleObjectManager.IsPointerOverUi()` 체크로 방지한다.

### Action Mode

현재 클라이언트 액션 모드는 다음 값을 사용한다.

- `Move`
- `Skill1`
- `Skill2`
- `Skill3`
- `Skill4`
- `Ultimate`
- `SubAction`
- `WaitingServer`

주의: 보조행동 이름은 `Assist`가 아니라 `SubAction`으로 사용한다.

### 서버 패킷 연동

현재 클라이언트는 다음 전투 패킷을 송신한다.

- `C_BATTLE_MOVE`
- `C_BATTLE_SKILL`
- `C_BATTLE_END_TURN`

현재 클라이언트는 다음 전투 패킷을 수신 처리한다.

- `S_BATTLE_START`
- `S_BATTLE_MOVE`
- `S_BATTLE_SKILL`
- `S_BATTLE_END_TURN`

`C_BATTLE_SKILL`의 스킬 슬롯은 다음 규칙으로 보낸다.

- `Skill1` ~ `Skill4`: `1` ~ `4`
- `Ultimate`: `5`

아직 별도 `C_BATTLE_SUB_ACTION` / `S_BATTLE_SUB_ACTION` 패킷은 없다. 서버가 별도 패킷을 추가하면 클라이언트도 송신/수신 핸들러를 추가해야 한다.

### Pawn 상태 모델

`BattlePawnController`는 서버가 내려주는 전투 상태를 보관한다.

- `Hp`
- `MaxHp`
- `Armor`
- `MaxArmor`
- `CurrentAp`
- `MoveRange`
- `CanMove`
- `UsedSubActionThisTurn`
- `UsedUltimate`
- `IsShieldUnit`
- `IsMelee`

`BattlePawnDelta` 수신 시 다음 상태를 갱신한다.

- `Hp`
- `Armor`
- `CurrentAp`
- `CanMove`
- `UsedSubActionThisTurn`
- `UsedUltimate`

`S_BATTLE_MOVE`, `S_BATTLE_SKILL`, `S_BATTLE_END_TURN`의 `PawnDeltas`와 응답 상태 필드를 적용할 수 있는 구조가 들어가 있다.

### Pawn Panel 표시

`Left_SelectedPawnPanel`과 `Right_EnemyPawnPanel`에 런타임 텍스트를 붙여 선택 아군과 대상 적 정보를 표시한다.

표시 내용:

- Pawn ID
- 소유 진영
- Pawn Class
- 근접/원거리 타입
- 현재 axial 좌표
- `HP / MaxHP`
- `Armor / MaxArmor`
- `AP`
- 이동 가능 여부
- SubAction 사용 여부
- Ultimate 사용 여부

### 버튼 disable 조건

버튼 활성화 여부는 클라이언트 추정값이 아니라 서버에서 받은 Pawn 상태를 기준으로 결정하도록 준비되어 있다.

기본 조건:

- 서버 응답 대기 중(`WaitingServer`)이면 모든 액션 버튼 비활성화
- 현재 턴의 로컬 Pawn이 아니면 모든 액션 버튼 비활성화

액션별 조건:

- Move: `CanMove == true`일 때만 활성화
- Skill1 ~ Skill4: `CurrentAp > 0`일 때만 활성화
- Ultimate: `UsedUltimate == false`일 때만 활성화
- SubAction: `UsedSubActionThisTurn == false`일 때만 활성화
- Wait / TurnExit: 현재 로컬 턴이면 활성화

주의: 스킬별 실제 AP 비용, 사거리, 대상 조건은 아직 서버 검증 결과를 받아야 한다. 현재 클라이언트는 버튼 활성화의 1차 조건만 준비한 상태다.

### Battle Log 표시

`BattleActionLog`를 받아 최근 로그를 UI에 표시할 수 있다.

현재 표시되는 주요 정보:

- 액션 타입
- 공격자 Pawn
- 방어자 Pawn
- 피해량
- 결과 HP
- 결과 Armor
- 크리티컬, 회피, 방어, 완전방어, 반격 플래그

## 확인된 검증 상태

- `Assembly-CSharp` 빌드 성공
- `Assembly-CSharp-Editor` 빌드 성공
- 남아 있는 경고: `System.Net.Http` 어셈블리 버전 충돌 경고
- Unity Play Mode에서 최종 UI 시각 검증은 별도 필요

## 남은 작업

### 서버 계약 확정 후 진행

- `SubAction`을 `C_BATTLE_SKILL`로 처리할지, 별도 `C_BATTLE_SUB_ACTION`으로 처리할지 확정해야 한다.
- 스킬별 AP 비용과 사용 가능 조건을 서버 응답으로 받을지, 정적 데이터로 클라이언트도 들고 있을지 정해야 한다.
- 버튼 비활성화 사유를 표시하려면 서버가 사유 코드 또는 액션별 가능 상태를 내려주는 구조가 필요하다.

### 클라이언트 구현 후보

- Pawn Panel을 텍스트에서 HP/Armor/AP 바 형태로 개선
- 스킬/이동 사거리 preview
- 타겟 선택 가능/불가능 타일 하이라이트
- 데미지, 회피, 방어, 반격 로그의 전투 연출
- 서버 권위 판정 실패 시 UI rollback 또는 상태 재동기화
- 전투 종료 패킷과 결과 UI

## 다음 추천 순서

1. 서버에서 `SubAction` 패킷 또는 처리 방식을 확정한다.
2. 서버가 Pawn별 액션 가능 상태를 내려주는 응답 구조를 확정한다.
3. 클라이언트는 그 구조에 맞춰 버튼별 disable 사유와 tooltip을 붙인다.
4. 이후 HP/Armor/AP 표시를 바 UI로 개선하고 전투 연출을 추가한다.
