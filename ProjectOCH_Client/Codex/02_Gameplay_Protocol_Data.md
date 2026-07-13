# 게임플레이, 전투, 데이터 계약

> 패킷 계약, 스킬 슬롯, CSV 컬럼을 변경하면 이 문서를 갱신한다.

## 필드

- `FieldObjectManager`가 `S_ENTER_GAME`, `S_SPAWN`, `S_DESPAWN`, `S_MOVE`를 구독해 Pawn을 생성·제거·이동시킨다.
- `FieldPawnController`는 로컬 Pawn의 클릭 이동을 `C_MOVE`로 보낸다. 로컬 이동 가능 여부는 맵 타일로 먼저 확인할 수 있지만, 서버 응답이 최종 상태다.
- 다른 플레이어 Pawn을 클릭하면 `FieldBattleInviteUI`가 전투 초대 확인 UI를 열고 `C_BATTLE_INVITE` 또는 응답 패킷을 보낸다.
- `B` 키의 `C_ENTER_BATTLE`은 개발/디버그용 진입 경로다. 정식 UX로 간주하지 않는다.

## 전투

`BattleObjectManager`가 전투의 클라이언트 상태를 가진다.

- `S_ENTER_BATTLE`의 아군/적 Pawn, 현재 턴 Pawn ID, Battle ID를 기준으로 Pawn을 생성한다.
- 이동: 클릭 → `C_BATTLE_MOVE` → `S_BATTLE_MOVE` → 서버가 확정한 위치·AP·다음 턴 반영
- 스킬: 슬롯 선택 + 타일/Pawn 클릭 → `C_BATTLE_SKILL` → `S_BATTLE_SKILL` → Delta, HP/Armor, 애니메이션, 로그 반영
- 턴 종료: `C_BATTLE_END_TURN` → `S_BATTLE_END_TURN`
- 사망/결과: `S_BATTLE_PAWN_DEAD`, `S_BATTLE_RESULT`; 결과 UI 확인 뒤 `C_BATTLE_RESULT_ACK`

`BattleActionMode.WaitingServer`는 요청을 보낸 뒤 응답 전까지 중복 입력을 막는 상태다.

## 서버 권위 원칙

클라이언트는 다음만 미리 확인한다.

- 선택된 대상이 아군/적/자신/빈 타일인지
- 현재 행동 슬롯과 표시된 AP 등 UI 조건
- 맵상 클릭 가능 여부

클라이언트는 다음을 확정하지 않는다.

- 턴 소유권
- 실제 사거리와 최종 유효성
- 명중, 피해, 방어, 사망, 승패
- AP/이동 가능 상태의 최종값

서버 패킷에 들어 있는 결과를 화면에 반영하는 것이 항상 우선이다.

## 스킬 슬롯 계약

`BattleSceneUI/ActionPanel`과 `C_BATTLE_SKILL.skill_slot`은 아래 표를 공유한다.

| UI 노드 | `BattleActionMode` | skill slot | 용도 |
| --- | --- | ---: | --- |
| `ActionSlot_01` | `Passive` | 1 | 패시브 표시 전용 |
| `ActionSlot_02` | `Skill1` | 2 | 일반 스킬 1 |
| `ActionSlot_03` | `Skill2` | 3 | 일반 스킬 2 |
| `ActionSlot_04` | `Skill3` | 4 | 일반 스킬 3 |
| `ActionSlot_05` | `Skill4` | 5 | 일반 스킬 4 |
| `ActionSlot_06` | `Ultimate` | 6 | 궁극기 |
| `ActionSlot_07` | `SubAction` | 7 | 보조 행동 |
| `ActionSlot_08` | `Move` | 8 | 이동 모드 |

턴 종료는 `TurnExit` 버튼이며 스킬 슬롯이 아니다.

## GameData

`BattleGameDataRepository`는 아래 CSV를 Addressables에서 우선 로드하고, 에디터에서는 `Assets/GameData`를 fallback으로 사용한다.

| 파일 | 책임 |
| --- | --- |
| `ClassKey.csv` | `PawnClass`와 클래스 키 매핑 |
| `PawnTemplate.csv` | Pawn의 기본 전투 템플릿 |
| `BattleSkill.csv` | 슬롯, AP, 사거리, 대상 유형 등 규칙 표시 데이터 |
| `BattleSkillEffect.csv` | 효과 정의 |
| `BattleSkillEffectParam.csv` | 효과별 파라미터 |
| `BattleSkillView.csv` | 아이콘 키, 애니메이션 트리거, VFX/SFX 키 |
| `DisplayText.csv` | 이름, 짧은 설명, 상세 설명 |
| `EnumDef.csv` | 표기용 enum 정의 |

새 캐릭터 또는 스킬을 추가할 때는 CSV, 아이콘 Addressables, Pawn 프리팹 주소, 서버 `PawnClass` / 프로토콜 계약을 함께 점검한다.

## 핵심 패킷

| 흐름 | 클라이언트 → 서버 | 서버 → 클라이언트 |
| --- | --- | --- |
| 입장 | `C_LOGIN`, `C_ENTER_GAME` | `S_LOGIN`, `S_ENTER_GAME`, `S_SPAWN` |
| 필드 | `C_MOVE`, `C_CHAT` | `S_MOVE`, `S_DESPAWN`, `S_CHAT` |
| 초대 | `C_BATTLE_INVITE`, `C_BATTLE_INVITE_RESPONSE` | 초대 요청/수신/결과 패킷 |
| 전투 | `C_ENTER_BATTLE`, `C_BATTLE_MOVE`, `C_BATTLE_SKILL`, `C_BATTLE_END_TURN` | 입장, 이동, 스킬, 턴 종료, 사망, 결과 |
| 전투 종료 | `C_BATTLE_RESULT_ACK` | `S_BATTLE_RESULT_ACK` |
