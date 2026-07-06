# Client Work Summary - 2026-07-06

## 목적

현재까지 Unity 클라이언트에서 진행한 전투/필드/주소 지정 리소스 작업을 다음 개발자가 바로 이어받을 수 있도록 정리한다.

관련 핵심 문서:

- [[00_Start_Here]]
- [[08_Battle_Development]]
- [[09_Battle_Client_Progress]]

## 현재 큰 흐름

클라이언트는 서버 권위 구조를 유지한다.

- Field에서는 서버가 플레이어 위치와 전투 신청 흐름을 관리한다.
- Battle에서는 서버가 턴, 이동, 스킬, HP/Armor/AP, 죽음, 승패를 판정한다.
- 클라이언트는 서버 패킷을 받아 UI와 연출을 갱신한다.
- 전투 UI와 주요 overlay는 가능한 한 prefab/addressable 기반으로 관리한다.

## Addressables / Prefab 정책

현재 방향은 "코드로 UI를 새로 생성하지 않고, prefab/addressable을 우선 사용"이다.

주요 Addressable:

- `BattleSceneUI`
- `FieldBattleInviteUI`
- `BattleResultUI`
- `SceneTransitionOverlay`
- `PawnBase`
- `PawnStatusWorldUI`
- `PawnTeamRing`
- 각 캐릭터 pawn visual prefab

주의:

- `PawnStatusWorldUI`와 `PawnTeamRing`은 캐릭터 prefab 하위에 직접 넣지 않는다.
- 두 UI는 `PawnBase.prefab` 하위에 둔다.
- 캐릭터 pawn prefab은 visual 전용으로 유지한다.
- 런타임에는 `PawnBase/visual` 아래에 실제 캐릭터 prefab을 넣는다.

## Battle Pawn 구조

현재 런타임 battle pawn 구조:

```text
PawnBase
  PawnTeamRing
  PawnStatusWorldUI
  visual
    Pawn_*
```

역할:

- `PawnBase`: 전투 pawn의 공통 root
- `PawnTeamRing`: 아군/적군 표시 ring
- `PawnStatusWorldUI`: 머리 위 HP/Armor bar
- `visual`: 실제 캐릭터 sprite/animator prefab이 들어가는 자리

현재 정책:

- 아군 발 아래 ring은 파란색
- 적군 발 아래 ring은 빨간색
- 적 pawn 자체를 붉게 tint 처리하지 않는다
- 죽은 pawn은 destroy하지 않고 dead state/비활성화 상태로 유지한다

관련 파일:

- `Assets/Scripts/Battle/BattleObjectManager.cs`
- `Assets/Scripts/Battle/BattlePawnController.cs`
- `Assets/Scripts/Battle/PawnTeamRing.cs`
- `Assets/Scripts/Battle/PawnStatusWorldUI.cs`
- `Assets/@Resources/Prefab/Pawn/PawnBase.prefab`
- `Assets/@Resources/Prefab/UI/PawnStatusWorldUI.prefab`
- `Assets/@Resources/Prefab/UI/PawnTeamRing.prefab`

## PawnStatusWorldUI 재생성 이슈

문제:

- 캐릭터 prefab 하위에서 `PawnStatusWorldUI`를 삭제해도 Addressables 빌드/에디터 실행 뒤 다시 붙는 현상이 있었다.

원인:

- `Assets/Editor/PawnStatusWorldUIPrefabSetup.cs`가 `[InitializeOnLoadMethod]`로 에디터 리로드 시 자동 실행되면서 `Pawn_*.prefab` 전부에 `PawnStatusWorldUI`를 다시 붙이고 있었다.

조치:

- 해당 editor setup 스크립트가 이제 `Pawn_*.prefab`을 건드리지 않도록 수정했다.
- 자동 구성 대상은 `PawnStatusWorldUI.prefab`과 `PawnBase.prefab`만이다.
- 모든 `Pawn_*.prefab`에서 `PawnStatusWorldUI`, `PawnTeamRing`, nested prefab instance 참조를 제거했다.

검증:

- `Pawn_*.prefab`에는 `PawnStatusWorldUI` / `PawnTeamRing` 참조가 없어야 한다.
- `PawnBase.prefab`에는 두 UI가 남아 있어야 한다.

## Battle UI

`BattleSceneUI`는 Addressables `UI` group에 `BattleSceneUI` 주소로 등록되어 있다.

ActionSlot 매핑:

| Slot | Action |
| --- | --- |
| `ActionSlot_01` | Move |
| `ActionSlot_02` | Skill1 |
| `ActionSlot_03` | Skill2 |
| `ActionSlot_04` | Skill3 |
| `ActionSlot_05` | Skill4 |
| `ActionSlot_06` | Ultimate |
| `ActionSlot_07` | SubAction |
| `ActionSlot_08` | Wait / EndTurn |

추가 연결:

- `TurnExit`도 버튼으로 연결되어 턴 넘기기 수단으로 동작한다.
- UI 버튼 클릭이 뒤쪽 tile click으로 전달되던 문제는 `EventSystem.IsPointerOverGameObject()` 체크로 방지했다.
- Pawn panel에는 HP/Armor/AP 등 서버 상태 기반 정보가 표시된다.
- HP/Armor 수치는 panel bar 내부와 world bar 내부에 표시된다.

관련 파일:

- `Assets/Scripts/Battle/BattleUIController.cs`
- `Assets/@Resources/Prefab/UI/BattleSceneUI.prefab`

## Battle 이동 연출

현재 이동 처리:

- `C_BATTLE_MOVE`를 서버에 보낸다.
- 서버가 `S_BATTLE_MOVE`를 양쪽 클라이언트에 보낸다.
- 클라이언트는 `S_BATTLE_MOVE` 수신 시 즉시 teleport하지 않고, target tile까지 보간 이동한다.
- 이동 중 visual animator의 `isMoving` bool을 `true`로 켠다.
- 도착하면 `isMoving`을 `false`로 끈다.
- 이동 연출이 끝난 뒤 `nextTurnPawnId`를 반영하고 턴 인디케이터를 갱신한다.

이동 중 입력 잠금:

- 이동 중에는 `ActionSlot` 버튼 비활성화
- 이동 중에는 `TurnExit` 비활성화
- 이동 중에는 tile/skill 입력도 무시
- 상대 pawn 이동도 동일하게 연출된다

관련 파일:

- `Assets/Scripts/Battle/BattlePawnController.cs`
- `Assets/Scripts/Battle/BattleObjectManager.cs`
- `Assets/Scripts/Battle/BattleUIController.cs`

주의:

- Animator bool parameter 이름은 `isMoving` 소문자 기준이다.

## Battle 상태/패킷 처리

현재 클라이언트가 전송하는 주요 battle packet:

- `C_BATTLE_MOVE`
- `C_BATTLE_SKILL`
- `C_BATTLE_END_TURN`
- `C_BATTLE_RESULT_ACK`

현재 클라이언트가 처리하는 주요 battle packet:

- `S_ENTER_BATTLE`
- `S_BATTLE_MOVE`
- `S_BATTLE_SKILL`
- `S_BATTLE_END_TURN`
- `S_BATTLE_PAWN_DEAD`
- `S_BATTLE_RESULT`
- `S_BATTLE_RESULT_ACK`

상태 반영:

- `BattlePawnDelta`를 통해 HP, Armor, AP, CanMove, SubAction/Ultimate 사용 여부, death state를 갱신한다.
- 아군 대상 공격은 클라이언트에서도 1차로 막는다.
- 서버가 최종 권위자이므로 클라이언트 검사는 UX용 방어선이다.

## PvP 전투 신청 흐름

서버 작업에 맞춰 클라이언트에서 battle invite UI와 packet handler를 연결했다.

흐름:

1. Field에서 플레이어1이 플레이어2를 클릭한다.
2. 플레이어1에게 전투 신청 Yes/No UI가 뜬다.
3. Yes를 누르면 서버에 신청 packet을 보낸다.
4. 플레이어1 UI는 대기 상태가 된다.
5. 플레이어2에게 수락/거절 UI가 뜬다.
6. 거절 시 양쪽에 결과 UI가 표시된다.
7. 수락 시 양쪽 field pawn은 despawn/disable되고 같은 battle room으로 들어간다.
8. `S_ENTER_BATTLE`을 통해 각자 allied/enemy 관점으로 pawn을 받는다.

관련 prefab/addressable:

- `FieldBattleInviteUI`

관련 파일:

- `Assets/Scripts/Field/FieldBattleInviteUI.cs`
- `Assets/Scripts/Field/FieldObjectManager.cs`

## Battle 결과 UI

서버 승패 packet 흐름:

```text
S_BATTLE_RESULT
 -> 2초 대기
 -> BattleResultUI 표시
 -> OK 클릭
 -> C_BATTLE_RESULT_ACK
 -> S_BATTLE_RESULT_ACK
 -> FieldScene 복귀
```

현재 동작:

- 승리 시 `You Win!`
- 패배 시 `You Lose!`
- OK 버튼을 누르면 `C_BATTLE_RESULT_ACK`를 보낸다.
- `S_BATTLE_RESULT_ACK(success=true)` 수신 후 FieldScene으로 돌아간다.

관련 prefab/addressable:

- `BattleResultUI`

관련 파일:

- `Assets/Scripts/Battle/BattleUIController.cs`
- `Assets/Scripts/Scenes/BattleSceneFlow.cs`

## Scene 전환 부드럽게 처리

문제:

- Title -> Field, Battle -> Field, Field -> Battle 전환 시 map이 먼저 보이고 pawn/camera/UI가 한 박자 늦게 나타났다.

조치:

- `SceneTransitionOverlay` prefab/addressable을 사용한다.
- scene load 뒤 필요한 map/pawn/UI 준비가 끝날 때까지 overlay를 유지한다.
- 준비 완료 후 overlay를 숨긴다.

적용 대상:

- TitleScene -> FieldScene
- BattleScene -> FieldScene
- FieldScene -> BattleScene

관련 파일:

- `Assets/Scripts/Scenes/SceneTransitionOverlay.cs`
- `Assets/Scripts/Scenes/FieldSceneAddressableLoader.cs`
- `Assets/Scripts/Scenes/BattleSceneAddressableLoader.cs`
- `Assets/Scripts/Scenes/TitleSceneFlow.cs`
- `Assets/Scripts/Scenes/BattleSceneFlow.cs`

## 전투 기획 현재안

현재 논의된 전투 규칙:

- 한 턴 기본 행동력은 2
- 행동력 2를 모두 쓰면 이동 불가
- 일부 스킬은 AP 1만 소모하므로 사용 후 이동 가능
- 일반적인 스킬은 AP 2 소모
- SubAction은 AP를 소모하지 않으며 조건 충족 시 턴마다 1회 사용 가능
- Ultimate는 전투 중 1회만 사용 가능하고 AP를 소모하지 않음
- 근접 공격에 대해 회피/완전방어 성공 시 반격
- 반격은 한쪽이 실패할 때까지 이어질 수 있음
- 원거리 캐릭터는 반격 불가
- 방패 캐릭터는 Armor 수치를 가진다
- Armor는 매 턴 시작 시 잃은 Armor의 절반 회복
- 피격 시 HP보다 Armor가 먼저 피해를 받음
- Critical은 일반 피해의 1.5배
- Critical 성공 시 상대는 회피/완전방어 불가

구현 방향:

- 규칙 판정은 서버 우선
- 클라이언트는 서버가 내려준 상태/로그/연출 정보를 표시
- 버튼 disable은 서버 상태 기반으로 결정

## 현재 검증 상태

확인한 것:

- `Assembly-CSharp.csproj` 빌드 성공
- `Assembly-CSharp-Editor.csproj` 빌드 성공
- 기존 `System.Net.Http` 버전 충돌 warning은 남아 있음
- 이 warning은 Unity/Addressables 쪽 기존 warning으로 보이며 현재 battle 작업과 직접 관련은 낮다

Unity Editor에서 추가 확인할 것:

- Addressables 빌드 후 `Pawn_*.prefab`에 `PawnStatusWorldUI`가 다시 붙지 않는지
- `S_BATTLE_MOVE` 수신 시 양쪽 클라이언트에서 이동 애니메이션이 보이는지
- 이동 중 ActionSlot/TurnExit가 비활성화되는지
- Animator parameter `isMoving`이 이동 중 켜졌다가 도착 후 꺼지는지
- BattleResultUI OK 이후 FieldScene 복귀가 정상인지

## 다음 추천 작업

1. Unity Editor에서 실제 PvP 2클라로 이동 연출 검증
2. 서버와 skill result log를 더 풍부하게 맞춰 damage/guard/counter 연출 준비
3. 스킬별 사거리/타겟 preview UI 추가
4. 선택한 pawn/target pawn highlight 추가
5. Turn queue 전체 표시용 서버 packet 또는 `S_ENTER_BATTLE` 확장 논의
6. SubAction을 별도 packet으로 분리할지, `C_BATTLE_SKILL` slot으로 유지할지 확정
7. Death 연출, result 연출, damage text 추가

