# 게임플레이, 패킷, 데이터 계약

> 기준일: 2026-08-04. 서버가 권위 있는 상태를 결정하고, 클라이언트는 수신 snapshot/delta를 표현한다.

## 필드

- `S_ENTER_GAME`, `S_SPAWN`, `S_DESPAWN`, `S_MOVE`는 `FieldObjectManager`가 구독해 필드 Pawn을 생성, 제거, 이동시킨다.
- `Field_001_WalkMap` JSON의 압축 walkable range는 로컬 클릭 프리뷰와 기본 spawn 위치에만 사용한다. `WorldMapRoot`는 시각 표현만 담당하며, 필드 Tilemap 프리팹은 런타임에 로드하지 않는다.
- 로컬 Pawn 클릭 이동은 `C_MOVE`를 전송하며, 서버의 `S_MOVE`로 모든 클라이언트가 실제 이동을 반영한다.
- 다른 플레이어 Pawn을 클릭하면 `FieldBattleInviteUI`가 확인 창을 표시한다. `C_BATTLE_INVITE`와 `C_BATTLE_INVITE_RESPONSE`의 결과가 수락되면 서버가 `S_ENTER_BATTLE`을 전송한다.
- `B` 키의 `C_ENTER_BATTLE`은 개발/디버그 진입 경로다. 실사용 멀티플레이 검증과 구분한다.

## 전투 요청과 응답

| 동작 | 클라이언트 요청 | 서버 응답에서 적용할 상태 |
| --- | --- | --- |
| 입장 | `C_ENTER_BATTLE` 또는 초대 수락 | `S_ENTER_BATTLE` snapshot, 전투 맵, 아군/적 Pawn, 턴 큐 |
| 이동 | `C_BATTLE_MOVE` | 이동, reaction log, Pawn delta, 다음 턴 |
| 스킬 | `C_BATTLE_SKILL` | Pawn delta, tile delta, action log, 턴/행동 상태 |
| 턴 종료 | `C_BATTLE_END_TURN` | Pawn/tile delta, 로그, 다음 턴/턴 큐 |
| 사망 | 없음 | `S_BATTLE_PAWN_DEAD`로 Pawn 사망 표현 |
| 결과 | 없음 | `S_BATTLE_RESULT` 후 결과 UI, `C_BATTLE_RESULT_ACK` |
| 복귀 | `C_BATTLE_RESULT_ACK` | `S_BATTLE_RESULT_ACK` 성공 시 FieldScene |

클라이언트는 성공 응답의 `battle_state_version`만 새 버전으로 적용한다. 과거 또는 중복 버전은 무시한다. 로그는 애니메이션/표시용이며, HP·방어막·상태 등의 최종 값은 언제나 `BattlePawnInfo`/`BattlePawnDelta`에서 얻는다.

## 전투 입력과 미리보기

- 이동 모드는 가중치 없는 BFS로 `MoveRange` 안의 도달 가능한 타일을 계산하고, 지형/Prop/현재 점유 상태를 반영한다.
- ZOC 범위 및 반응 가능 타일도 미리보기로 표시한다. 최종 이동 가능성과 반응은 서버가 판단한다.
- 스킬 미리보기는 `RangeMin/RangeMax`, 대상 종류, `TargetShape`(`RADIUS_1`, `LINE_3` 등), overlay 조건을 기반으로 한다.
- Fire Wall 계열 `LINE_3`은 시작 타일과 인접 방향 타일을 순서대로 고르는 2단계 입력이다.
- Zillian Mace 슬롯 7은 인접 아군을 고른 뒤 위치 교환 여부를 확인한다. 교환을 고르면 `C_BATTLE_SKILL.request_optional_position_swap=true`를 전송하고, 회복만 고르면 해당 필드를 전송하지 않는다.
- 스킬 요청의 권위 있는 대상은 `target_axial`이다. `target_pawn_id`는 클라이언트가 0으로 보내고 서버가 axial에서 실제 Pawn을 찾는다.
- 서버 응답 중 `target_pawn_id == 0`은 타일 전용 결과일 수 있다. 이 경우에도 HP를 직접 계산하지 않고 delta만 적용한다.
- 행동은 AP가 아니라 `pawn_deltas`의 최종 턴 상태로 판단한다. 이동은 `can_move`, 일반 스킬(2~5)은 `used_normal_skill_this_turn == false`, 궁극기(6)는 `used_ultimate == false`, 보조행동(7)은 `used_sub_action_this_turn == false`일 때 사용할 수 있다. `is_action_blocked_this_turn`이면 모든 능동 행동을 막는다.

## 맵과 타일

- `BattleTileInfo`는 axial 좌표, 정적 `tile_type`(NORMAL/WATER), 동적 `overlay_type`(NONE/ICE/FIRE)를 보관한다.
- 입장 snapshot은 Ground 및 CombatOverlay를 초기화한다. 이후 `tile_deltas`는 CombatOverlay만 변경한다.
- 기본 규칙은 NORMAL은 이동 가능, WATER는 ICE일 때만 이동 가능이며, Prop은 별도 이동 차단 요소다. 서버가 최종 판정한다.

## Pawn 상태

`BattlePawn`은 다음 상태를 유지한다.

- 기본: HP, Armor, Shield, MoveRange, 이동/행동 가능 여부, 현재 턴, 사망, 방향
- 전체 교체 snapshot: Resources, Barriers, Statuses, Auras
- 표현: 클래스별 animator/VFX, 팀 링, 월드 상태 UI, 피격/스킬/사망 애니메이션

Resources/Barriers/Statuses/Auras는 부분 병합이 아니라 전달된 배열 전체로 교체한다. 빈 배열은 기존 상태를 비운다.

## 현재 데이터 클래스와 표현

CSV `ClassKey.csv`에는 Beige Ice/Fire, Suen Axe/Parvis, Alen Shield/Spear, Zillian Longbow/Mace가 정의돼 있다. 현재 전용 클라이언트 표현은 Beige Ice/Fire, Suen Axe, Suen Parvis, Alen Shield/Spear, Zillian Longbow/Mace에 연결돼 있으며, 나머지는 공통 fallback 표현을 사용한다.

- `SuenAxe`: `SUEN_AXE_AXE_OFF` 상태로 장비 animator와 슬롯 아이콘/이름을 변경한다.
- `SuenParvis`: `SUEN_PARVIS_OFF` 상태에 따라 슬롯 2는 Install Parvis/Sit Shot으로 전환하고, 슬롯 3은 항상 Stand Shot을 유지하며 animator 상태를 전환한다.
- `AlenSpear`: Sentinel/Charge Command 관련 상태 라벨과 아이콘을 제공한다.
- `AlenSwordShield`: 카르바스/황실 방패술, 책임감, 도발, 결투의 대가 자기 강화 상태 라벨과 아이콘을 제공하며, 자존심 성공 시 서버가 보낸 caster 위치 delta로 적의 이전 타일까지 전진을 표현한다.
- `ZillianLongbow`: 슬롯별 아이콘/명칭과 longbow animator 제스처를 제공한다.
- `ZillianMace`: 근접 단일 공격, 1~4칸 `RADIUS_1` 실명, 인접 아군 회복, 전체 보호막, 선택 위치 교환을 서버의 모든 Pawn delta로 표현한다. 슬롯 3은 서버 delta에 `STUN`이 있을 때만 제어 성공 연출과 행동 불가 상태를 표시하며, 어지러움은 별도 누적 상태로 표시하지 않는다.
- `BeigeIce`: Aura 상태에 따른 주변 VFX와 COLD/상태 UI를 표현한다.

## 전투 UI

- 8개 행동 슬롯: passive(1), skill 1~4(2~5), ultimate(6), sub action(7), move(8)
- `TurnQueue`는 서버의 upcoming turn snapshot/resync를 초상화 순서와 애니메이션으로 표현한다.
- 현재 턴 Pawn의 초상화, 양쪽 Pawn 상태 패널, HP/Shield/Resource/Morale bar, 상태 아이콘, 툴팁을 제공한다.
- `S_BATTLE_RESULT` 수신 후 약 2초 뒤 결과 오버레이를 표시하고, 확인 버튼이 ACK를 한 번만 전송한다.

## CSV GameData

| 파일 | 책임 |
| --- | --- |
| `ClassKey.csv` | 클래스 key와 `PawnClass` 매핑 |
| `PawnTemplate.csv` | Pawn 역할 및 기본 능력치 |
| `BattleSkill.csv` | 슬롯, 사거리, 대상/형태/overlay 요구 조건 |
| `BattleSkillEffect.csv` | 효과 그룹과 실행 순서 |
| `BattleSkillEffectParam.csv` | 효과 파라미터/상태 modifier |
| `BattleSkillView.csv` | 애니메이션 trigger, VFX/SFX, 아이콘 주소, 발사체 키 |
| `BattleZoc.csv` | ZOC 범위, 전방 arc, 반응 제한/트리거 |
| `DisplayText.csv` | 다국어 표시 텍스트 |
| `EnumDef.csv` | enum/상태 정의 |
| `BattleConfig.csv`, `BattleMapTile.csv` | 전투 설정과 맵 타일 원본 |

`BattleGameDataRepository`는 런타임에서 Addressables CSV를 우선 사용하고, 에디터 환경에서는 `Assets/GameData`를 fallback으로 사용한다.

`BattleSkillView.ProjectileKey`가 비어 있지 않은 스킬은 서버 `BattleActionLog` 순서에 따라 공격자에서 대상 Pawn(또는 `target_axial`)까지 발사체를 재생한다. 피해·회피·상태·사망은 발사체가 아닌 서버 로그와 `pawn_deltas`만으로 판정한다.
