# 현재 상태와 작업 절차

## 기준 시점

2026-07-13 기준. 이 문서는 코드 변경 시 함께 갱신한다. 날짜별 작업 로그가 필요하면 이 문서의 하단에 링크를 추가하고, 이 문서에는 현재 사실만 유지한다.

## 현재 진행 중인 작업

BEIGE_ICE를 시작점으로 전투 스킬 UI와 GameData 연동을 확장 중이다.

- `BattleGameDataRepository` 및 `BattleSkillIconCache`가 새로 추가됨
- CSV GameData와 스킬 아이콘 Addressables 그룹이 추가됨
- `BattleUIController`가 선택 Pawn의 클래스·스킬 데이터를 읽어 슬롯, 아이콘, 툴팁, 활성화를 갱신함
- `BattleObjectManager`가 `TargetType` 기반의 1차 입력 검증과 스킬 애니메이션 트리거를 처리함
- 현재 변경은 아직 커밋되지 않았음

신규 전투 상태 프로토콜 반영도 진행 중이다.

- 생성된 C#은 `BattleResourceType`, Resource/Barrier/Status 상태, `battle_state_version`, `shield_current` / `shield_max`, Barrier `max_value`를 포함함
- `BattlePawnController`는 Pawn별 리소스·보호막·상태 로컬 모델과 UI용 Shield 값을 보관하며, 입장 정보와 Delta의 목록을 전체 교체함
- `BattleObjectManager`는 `S_ENTER_BATTLE`에서 기존 Pawn을 재생성하고, 성공 Delta의 상태·로그·연출을 더 높은 `battle_state_version`에서만 적용함. 낮은 버전은 과거 패킷, 같은 버전은 중복 패킷으로 구분해 무시함
- 전투 Pawn 표현은 `PawnClass → Addressables 시각 프리팹` 매핑으로 결정한다. 서버의 `BattlePawn` 상속 구조는 복제하지 않으며, GameData에만 존재하는 새 PawnClass는 매핑 누락 경고와 기본 프리팹 fallback으로 안전하게 처리함
- `BattleUIController`와 `PawnStatusWorldUI`는 `shield_current` / `shield_max`를 하나의 보호 바로 표시한다. COLD 리소스는 해당 Pawn에만 표시하며 Status는 키·스택·남은 턴을 상세 패널에 표시함
- 상태 아이콘은 현재 런타임 생성 칩(약어/스택/턴)이다. 아트 확정 후 `status_key`별 Sprite 매핑으로 교체한다.
- `BattleActionLog`는 피해 숫자·MISS 등의 연출 전용이다. Pawn의 HP/AP/Armor 등 최종 수치는 언제나 `pawn_deltas`만 적용한다.
- 스킬 요청은 `target_axial`만 권위 있는 대상으로 사용하며, NetworkService가 호환 필드 `target_pawn_id`를 항상 `0`으로 전송한다. 빈 타일 스킬 결과는 Pawn 피해 숫자를 표시하지 않는다.

## 알려진 개발 단계 항목

- 자동화된 EditMode/PlayMode 테스트가 없다.
- `BattleObjectManager`, `BattleUIController`, `BattleGameDataRepository`는 책임이 큰 클래스다. 기능 안정 후 입력/상태 반영/UI/데이터 파싱을 분리할 후보다.
- Addressables 콘텐츠 변경은 C# 컴파일 성공만으로 빌드에 반영되지 않는다. Addressables 콘텐츠 빌드가 필요하다.
- 스킬별 피해 예측, 전체 턴 큐, 모든 `TargetType`의 상세 검증은 서버 프로토콜/정책의 추가 결정이 필요할 수 있다.
- 로컬 Pawn fallback, 디버그 전투 Pawn, B 키 전투 진입은 서버 연결 문제를 가릴 수 있으므로 실제 멀티플레이 검증 시 사용 여부를 구분한다.

## 검증 절차

### 코드 변경 후

```powershell
dotnet build Assembly-CSharp.csproj --no-restore
dotnet build Assembly-CSharp-Editor.csproj --no-restore
```

현재 두 빌드는 성공한다. Unity Addressables Editor 참조로 인한 `System.Net.Http` 버전 충돌 경고는 남아 있다.

### 콘텐츠 변경 후

1. Unity Editor에서 컴파일 오류가 없는지 확인한다.
2. 새 프리팹, Sprite, CSV가 Addressables 그룹에 등록됐는지 확인한다.
3. Addressables 콘텐츠를 다시 빌드한다.
4. 실제 빌드 또는 최신 Addressables가 적용된 Play Mode에서 로드 여부를 확인한다.

### 멀티플레이 전투 검증

1. 두 클라이언트를 각기 다른 `-playerIndex` 값으로 실행한다.
2. 두 플레이어가 FieldScene에서 보이고 이동 동기화되는지 확인한다.
3. 한 플레이어가 초대하고, 상대가 수락하여 같은 전투에 진입하는지 확인한다.
4. 이동, 각 스킬의 타깃 제한, 턴 종료, 사망, 결과 ACK 후 필드 복귀를 확인한다.

## 새 세션용 요청 문구

새 Codex 세션에서는 아래처럼 요청한다.

> `ProjectOCH_Client/Codex/00_README.md`부터 연결된 문서를 읽고, 현재 작업 트리의 변경을 보존한 채 작업을 시작해줘.

## 문서 갱신 규칙

- 새 기능이 시스템 경계를 바꾸면 `01_Architecture.md`를 갱신한다.
- 게임 규칙, 패킷, 데이터 컬럼, UI 슬롯 계약이 바뀌면 `02_Gameplay_And_Data.md`를 갱신한다.
- 진행 중인 기능, 검증 결과, 알려진 문제의 상태가 바뀌면 이 문서를 갱신한다.
- 세션에서 내린 임시 결정은 작업 로그에 남기되, 확정된 사실만 이 가이드 문서에 승격한다.
