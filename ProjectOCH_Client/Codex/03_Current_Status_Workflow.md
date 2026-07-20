# 현재 상태와 작업 절차

## 기준 시점

2026-07-20 기준. 이 문서는 완료된 사실과 다음 검증 항목만 유지한다. 세션별 임시 메모는 별도 작업 로그에 남긴다.

## 현재 구현 상태

### 전투 프로토콜 / 상태 반영

- 최신 생성 C# 패킷이 Resource, Barrier, Status, Aura, Shield, Tile Overlay, `battle_state_version`을 포함한다.
- `BattlePawnController`는 Pawn별 Resources / Barriers / Statuses / Auras를 보관하고 모든 배열을 전체 교체한다.
- `BattleObjectManager`는 `S_ENTER_BATTLE`, 이동, 스킬, 턴 종료에서 모든 `pawn_deltas`를 적용한다.
- Shield UI는 `armor`가 아닌 서버 `shield_current / shield_max`를 표시한다.
- 로그는 연출 전용이며, Delta 외의 방식으로 수치를 계산하지 않는다.
- 중복·과거 성공 패킷은 `battle_state_version`으로 무시한다.

### 타일 / 좌표

- `BattleField_001`은 Ground / Prop / Combat Overlay 3계층이다.
- 입장 스냅샷은 Ground와 Overlay를 초기화하고, `tile_deltas`는 Overlay만 바꾼다.
- Unity Odd-R cell과 서버 pure axial 사이의 변환을 `BattleMapGrid` 경계에 적용했다. Aura·이동·우박·Pawn 배치가 같은 규약을 사용한다.
- 오래된 Addressables 맵 번들에 Tile 참조가 누락됐을 때 시각 Tile을 주소에서 로드하는 fallback이 있다.

### Beige Ice

- 패시브부터 보조 행동까지 슬롯 1~7의 GameData·아이콘·대상 입력이 연결돼 있다.
- 스킬 4 폭풍의 중심은 서버 Aura 상태를 읽어 Pawn 주변에 반경 VFX를 표시한다. 강화 중 서버 radius 2가 오면 같은 VFX가 확장된다.
- 궁극기 상태는 `EMP x1 T3`(금색) / `IMM x1 T3`(하늘색)으로 표시한다.
- 해동 포션의 피해 감소 상태는 `DMG x1 T2`(갈색)으로 표시한다. 반복 사용은 서버 상태의 남은 턴 갱신을 따른다.
- 스킬 슬롯은 더미 위에 아이콘을 겹치지 않고 루트 슬롯 Image를 교체한다. 스킬 이름은 슬롯에 표시하지 않고 툴팁에서만 제공한다.

### Beige Fire

- 서버 GameData의 BattleSkill, Effect, EffectParam, EnumDef를 동기화했다. `TargetShape`, `RequiredOverlayType`, HEAT, FIRE Overlay를 포함한다.
- FIRE는 Combat Overlay로만 표시되며 일반 지형의 이동 가능 여부를 바꾸지 않는다.
- Teleport은 사거리 내 FIRE Overlay의 빈 타일만 유효 대상이며, Explosion/Fire Wall은 서버와 같은 RADIUS_1/LINE_3 프리뷰를 표시한다.

### Pawn 프리팹

- `PawnTeamRing`을 `PawnBase`에서 제거하고 모든 클래스별 시각 프리팹에 넣었다.
- 기본 위치는 `(0, 0, 0)`이다. 캐릭터 발 위치가 다르면 해당 시각 프리팹의 `PawnTeamRing` Local Position만 조절한다.
- Ring은 전투 초기화 때만 켜므로 FieldScene에는 표시되지 않는다.

### Pawn 클래스 계층

- 공용 `BattlePawn`은 서버 스냅샷, 이동, 공통 상태 UI와 팀 링만 관리한다.
- 캐릭터 고유의 클라이언트 표현은 상속 클래스에 둔다. 현재 Beige 계열은 `BattlePawn → Beige → BeigeIce` 구조이며, 폭풍의 중심 Aura VFX는 `BeigeIce`가 전담한다.

## 남은 검증 / 다음 우선순위

1. 실제 서버와 두 클라이언트로 Beige Ice 전 스킬을 검증한다.
   - 우박: NORMAL/WATER/적 타일, ICE Overlay와 이동 가능 여부
   - 폭풍의 중심: 화면상 인접 6칸, Aura on/off, 턴 시작 피해·동상, 강화 반경 2
   - 궁극기: EMP/IMM 3턴 감소, 강화 볼트·실드·우박·Aura
   - 해동 포션: 홀수 COLD floor 반감, DMG 2턴, 턴당 1회, 재사용 시 지속시간만 갱신
2. 각 Pawn 시각 프리팹의 Team Ring 발 위치를 실제 스프라이트 기준으로 조정한다.
3. SkillIcon 및 변경된 맵/프리팹/CSV가 포함되도록 Addressables 콘텐츠를 빌드한다.
4. 스킬/상태 UI 아트가 확정되면 현재 런타임 텍스트 칩을 Sprite 기반 아이콘으로 교체한다.

## 알려진 개발 단계 항목

- 자동화된 EditMode/PlayMode 테스트가 없다.
- `BattleObjectManager`, `BattleUIController`, `BattleGameDataRepository`의 책임이 크다. 안정화 후 입력·상태·UI·데이터 파싱을 분리할 후보가 있다.
- 현재는 스킬별 범위 미리보기를 제공하지 않는다. 추가한다면 서버 규칙을 재구현하는 최종 판정이 아니라 연출용 표시로만 둔다.
- Addressables 콘텐츠 변경은 C# 컴파일 성공만으로 플레이어 빌드에 반영되지 않는다.
- 로컬 Pawn fallback, 디버그 전투 Pawn, B 키 진입은 실제 멀티플레이 검증과 구분한다.

## 검증 절차

### 코드 변경 후

```powershell
dotnet build Assembly-CSharp.csproj --no-restore
dotnet build Assembly-CSharp-Editor.csproj --no-restore
```

최근 `Assembly-CSharp` 빌드는 성공했다. Unity Addressables Editor 참조로 인한 `System.Net.Http` 버전 충돌 경고는 기존 경고다.

### 콘텐츠 변경 후

1. Unity Editor의 Console 오류를 확인한다.
2. 새 프리팹, Sprite, CSV가 올바른 Addressables 그룹/주소에 등록됐는지 확인한다.
3. `Window > Asset Management > Addressables > Groups > Build > New Build > Default Build Script`를 실행한다.
4. 최신 콘텐츠를 포함한 Play Mode 또는 Player Build에서 로드를 확인한다.

### 멀티플레이 전투 검증

1. 두 클라이언트를 서로 다른 `-playerIndex`로 실행한다.
2. FieldScene에서 서로 보이고 이동이 동기화되는지 확인한다.
3. 초대·수락으로 동일한 전투에 진입하는지 확인한다.
4. 위 Beige Ice 항목, 턴 종료, 사망, 결과 ACK 후 FieldScene 복귀를 확인한다.

## 새 세션용 요청 문구

새 Codex 세션에서는 아래처럼 요청한다.

> `ProjectOCH_Client/Codex/00_README.md`부터 연결된 문서를 읽고, 현재 작업 트리의 변경을 보존한 채 작업을 시작해줘.

## 문서 갱신 규칙

- 장면·로더·좌표·프리팹 조합 경계가 바뀌면 `01_Architecture.md`를 갱신한다.
- 패킷·전투 규칙·CSV·UI 슬롯 계약이 바뀌면 `02_Gameplay_Protocol_Data.md`를 갱신한다.
- 완료 상태·검증 결과·다음 작업이 바뀌면 이 문서를 갱신한다.
