# 게임플레이, 전투, 데이터 계약

> 패킷 계약, 스킬 슬롯, CSV 컬럼 또는 서버-클라이언트 책임이 바뀌면 이 문서를 갱신한다.

## 필드

- `FieldObjectManager`가 `S_ENTER_GAME`, `S_SPAWN`, `S_DESPAWN`, `S_MOVE`를 구독해 Pawn을 생성·제거·이동시킨다.
- `FieldPawnController`는 로컬 Pawn의 클릭 이동을 `C_MOVE`로 보낸다. 클라이언트 타일 판정은 UX 보조이며 서버가 최종 판정한다.
- 다른 플레이어 Pawn 클릭은 `FieldBattleInviteUI`를 열고 전투 초대 패킷을 보낸다.
- `B` 키의 `C_ENTER_BATTLE`은 개발/디버그 진입 경로다.

## 전투 요청과 응답

`BattleObjectManager`가 전투 입력과 서버 응답의 최종 상태 반영을 맡는다.

| 흐름 | 요청 | 응답에서 할 일 |
| --- | --- | --- |
| 입장 | `C_ENTER_BATTLE` | `S_ENTER_BATTLE` 스냅샷으로 기존 Pawn을 제거·재생성하고 전체 타일을 초기화 |
| 이동 | `C_BATTLE_MOVE` | `S_BATTLE_MOVE.pawn_deltas`로 모든 변경 Pawn을 갱신, 위치·Facing·턴 전환 반영 |
| 스킬 | `C_BATTLE_SKILL` | `S_BATTLE_SKILL.pawn_deltas`, `tile_deltas`, logs를 모두 반영 |
| 턴 종료 | `C_BATTLE_END_TURN` | `S_BATTLE_END_TURN.pawn_deltas`, `tile_deltas`, logs를 모두 반영 |
| 사망 / 결과 | - | `S_BATTLE_PAWN_DEAD`, `S_BATTLE_RESULT` 처리 후 결과 ACK |

### 스킬 대상 계약

- 요청의 권위 있는 대상은 항상 `target_axial`이다.
- Pawn 클릭은 해당 Pawn의 axial, Self/SELF_TOGGLE은 caster axial, `TILE_OR_ENEMY`는 빈 타일 axial을 보낸다.
- 호환용 `target_pawn_id`는 클라이언트가 `0`으로 보내고 서버가 axial에서 실제 Pawn을 찾는다.
- 응답의 `target_pawn_id == 0`은 빈 타일 결과다. 대상 Pawn을 찾아서 HP를 직접 바꾸지 않는다.
- 클라이언트의 대상 검사와 사거리 표시는 입력 UX 보조다. 최종 대상·피해·사망·AP는 서버 응답을 따른다.
- `EMPTY_TILE`은 Pawn이 없는 타일만 대상으로 삼는다. `RequiredOverlayType=FIRE`는 해당 타일이 FIRE Overlay인 경우에만 유효하다.
- `TargetShape=RADIUS_1`은 선택 타일과 인접 6칸, `LINE_3`은 서버와 같은 axial 방향으로 선택 타일에서 이어지는 3칸을 프리뷰로 표시한다.
- Overlay Teleport은 `S_BATTLE_SKILL.target_axial`이 서버가 확정한 caster의 새 위치다. `BattlePawnDelta`에는 axial이 없으므로, 성공 응답에서만 이 좌표를 Pawn Transform과 내부 axial에 함께 반영한다.
- 전투 맵 프리뷰는 모든 클래스의 `BattleSkill` 사거리와 대상 계약을 공통으로 표시한다. 이동 모드에서는 현재 Pawn의 `MoveRange` 안에서 서버 타일 상태·Prop·점유를 통과하는 빈 칸을 표시한다.

## 상태 스냅샷과 버전

`BattlePawnInfo`는 입장 전체 상태, `BattlePawnDelta`는 이동·스킬·턴 종료의 최신 상태다. **로그로 수치를 계산하지 않고 Delta를 최종 상태로 적용한다.**

| 상태 | 클라이언트 보관 / UI 규칙 |
| --- | --- |
| 기본 | HP, Armor, AP, 이동 가능, 행동 사용 여부, 사망, Facing |
| Resource | `BattleResourceType → value/max_value`. 목록이 비면 자원 UI를 숨긴다. Beige Ice만 COLD 게이지 사용 |
| Barrier | armor는 기본 장갑, barriers는 ID별 임시 보호막. 보호 바는 재계산하지 않고 `shield_current/shield_max` 사용 |
| Status | `status_key → stacks/remaining_owner_turns` |
| Aura | `source_skill_key → radius` |

- `Resources`, `Barriers`, `Statuses`, `Auras`는 모두 **부분 병합이 아닌 전체 교체 스냅샷**이다. 빈 배열은 기존 상태를 비운다.
- `S_BATTLE_SKILL`과 `S_BATTLE_END_TURN`의 `pawn_deltas`는 caster/primary target만 가정하지 말고 전부 순회한다. 강화 실드·우박·Aura는 여러 Pawn을 바꿀 수 있다.
- `battle_state_version`은 성공 패킷에 대해 단조 증가한다. 현재 버전보다 큰 경우만 상태와 연출을 적용한다. 낮은 버전은 과거 패킷, 같은 버전은 중복 패킷으로 무시한다.
- `BattleActionLog`는 피해 숫자·애니메이션용이다. HP·보호막·자원·상태의 최종 수치는 항상 Pawn Delta다.

## 타일 동기화와 이동 규칙

- `BattleTileInfo` = `axial`, 원본 `tile_type`(NORMAL/WATER), 동적 `overlay_type`(NONE/ICE/FIRE).
- `S_ENTER_BATTLE.tiles`는 전투 맵 전체 스냅샷이다.
- 스킬·턴 종료의 `tile_deltas`는 변경 타일만 전달하며 Ground/Prop을 바꾸지 않고 `CombatOverlay_Tilemap`만 갱신한다.
- 이동 표현 규칙: `NORMAL + NONE/ICE/FIRE` 가능, `WATER + NONE/FIRE` 불가, `WATER + ICE` 가능. Prop은 별도로 이동을 막는다.

## 스킬 슬롯 계약

| UI 노드 | `BattleActionMode` | slot | 용도 |
| --- | --- | ---: | --- |
| `ActionSlot_01` | `Passive` | 1 | 패시브 표시 전용 |
| `ActionSlot_02` | `Skill1` | 2 | 일반 스킬 1 |
| `ActionSlot_03` | `Skill2` | 3 | 일반 스킬 2 |
| `ActionSlot_04` | `Skill3` | 4 | 일반 스킬 3 |
| `ActionSlot_05` | `Skill4` | 5 | 일반 스킬 4 |
| `ActionSlot_06` | `Ultimate` | 6 | 궁극기 |
| `ActionSlot_07` | `SubAction` | 7 | 보조 행동 |
| `ActionSlot_08` | `Move` | 8 | 이동 모드 |

턴 종료는 `TurnExit` 버튼이며 스킬 슬롯이 아니다. 궁극기는 `UsedUltimate`, 보조 행동은 `UsedSubActionThisTurn`으로 UI에서 비활성화한다.

스킬 슬롯의 루트 Image는 기본 더미가 아니라 클래스 스킬 아이콘으로 직접 교체된다. 아이콘 위에 별도 `Icon` 오브젝트를 겹치거나 스킬 이름 `Label`을 표시하지 않는다. 상세 이름·설명은 툴팁이 담당한다.

## Beige Ice 현재 스킬 계약

| slot | 스킬 | 대상 | 서버 권위 결과 / 클라이언트 표현 |
| ---: | --- | --- | --- |
| 1 | 냉기 축적 | Passive | COLD 최대치·감소·역류 규칙 |
| 2 | 아이스 볼트 | 적 단일 | 피해와 COLD +1 Delta |
| 3 | 아이스 실드 | 아군 단일 | Barrier / Shield Delta, 강화 시 인접 추가 대상 가능 |
| 4 | 우박 | 타일 또는 적 | 빈 타일 ICE Overlay, 물은 인접 6칸 ICE, 적 타일은 동상; 강화 시 삼각 3칸 가능 |
| 5 | 폭풍의 중심 | Self Toggle | `BEIGE_ICE_STORM_CENTER` Aura. Pawn 부착형 반경 VFX, Aura tick은 로그와 모든 Pawn Delta로 반영 |
| 6 | 차가운 노력가 | Self | 3 owner turns 면역 `IMM`과 강화 `EMP`; 강화 Aura는 서버가 radius 2를 전송 |
| 7 | 해동 포션 | Self | COLD를 floor(절반)으로 감소, `DMG` 상태 2 owner turns, 턴당 1회 |

강화/포션의 실제 피해 배율, 추가 대상, 대상 모양, 지속시간은 서버와 Delta가 권위 있다. 현재 클라이언트에는 스킬별 범위 미리보기는 없으며 Aura VFX만 서버 Aura radius를 직접 표현한다.

## GameData와 아이콘

`BattleGameDataRepository`는 Addressables CSV를 우선 로드하고, 에디터에서는 `Assets/GameData` fallback을 사용한다.

| 파일 | 책임 |
| --- | --- |
| `ClassKey.csv` | `PawnClass`와 클래스 키 매핑 |
| `PawnTemplate.csv` | Pawn 기본 전투 템플릿 |
| `BattleSkill.csv` | 슬롯, AP, 사거리, 대상 유형 |
| `BattleSkillEffect.csv` | 효과 정의 |
| `BattleSkillEffectParam.csv` | 효과별 파라미터·상태 키·강화 modifier |
| `BattleSkillView.csv` | 아이콘 키, 애니메이션 트리거, VFX/SFX 키 |
| `DisplayText.csv` | 툴팁 이름·짧은 설명·상세 설명 |
| `EnumDef.csv` | 표기용 enum / 상태 키 정의 |

`SkillIcon` Addressables 그룹에는 Beige Ice의 `icon_beige_ice_passive`, `skill1`~`skill4`, `ulti`, `sub` 주소가 등록돼 있다. 새 아이콘 또는 CSV를 플레이어 빌드에 반영하려면 Addressables 콘텐츠 빌드가 필요하다.

`BattleSkill.csv`의 `TargetShape`, `RequiredOverlayType`은 서버 데이터 계약과 동기화한다. 현재 Beige Fire는 HEAT 자원을 사용하며 Fireball(단일), Explosion(`RADIUS_1`), Fire Wall(`LINE_3` + FIRE), Teleport(`EMPTY_TILE` + FIRE), Ambitious, Cooling Potion을 제공한다.

## 핵심 패킷

| 흐름 | 클라이언트 → 서버 | 서버 → 클라이언트 |
| --- | --- | --- |
| 입장 | `C_LOGIN`, `C_ENTER_GAME` | `S_LOGIN`, `S_ENTER_GAME`, `S_SPAWN` |
| 필드 | `C_MOVE`, `C_CHAT` | `S_MOVE`, `S_DESPAWN`, `S_CHAT` |
| 초대 | `C_BATTLE_INVITE`, `C_BATTLE_INVITE_RESPONSE` | 초대 요청/수신/결과 |
| 전투 | `C_ENTER_BATTLE`, `C_BATTLE_MOVE`, `C_BATTLE_SKILL`, `C_BATTLE_END_TURN` | 입장, 이동, 스킬, 턴 종료, 사망, 결과 |
| 전투 종료 | `C_BATTLE_RESULT_ACK` | `S_BATTLE_RESULT_ACK` |
