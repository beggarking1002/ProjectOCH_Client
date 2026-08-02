# 현재 상태와 작업 워크플로

> 기준일: 2026-08-02

## 구현된 범위

### 공통/필드

- `GameRoot`가 전역 네트워크 서비스와 UI 폰트를 초기화하고 씬 전환에도 유지된다.
- 타이틀 배경, 장면 전환 오버레이, `Field_001_WalkMap` JSON, WorldMapRoot/Pawn Addressables 로드가 구현돼 있다. Field Tilemap 프리팹은 런타임에 로드하지 않는다.
- 필드 Pawn의 입장/생성/제거/서버 이동 동기화가 구현돼 있다.
- 다른 플레이어 클릭 기반 전투 초대 확인/수락/거절 UI와 패킷 흐름이 구현돼 있다.

### 전투

- 서버 입장 snapshot으로 전투 맵, 아군/적 Pawn, 타일, 현재 턴, upcoming turn queue를 초기화한다.
- `battle_state_version`으로 중복/과거 응답을 방지하고, 모든 Pawn delta와 tile delta를 서버 값으로 적용한다.
- 이동 경로/ZOC 및 스킬 범위/형태/overlay 기반의 입력 미리보기가 구현돼 있다.
- 이동 반응, 일반 스킬, ZOC 반응, 사망, 상태 tick을 순서 있는 프레젠테이션으로 표현한다.
- 턴 큐 초상화, 현재 턴 초상화, Pawn 상태 패널/상태 아이콘, 행동 툴팁, 결과 오버레이와 ACK 복귀 흐름이 구현돼 있다.
- Suen Axe/Parvis, Alen Shield/Spear, Zillian Longbow/Mace의 상태 기반 슬롯/애니메이션 표현이 추가돼 있다.

## 아직 검증 또는 보완이 필요한 항목

1. 실제 서버와 두 클라이언트를 사용한 모든 클래스/스킬 조합의 통합 검증
2. 지원 클래스 전체에 전용 visual/행동/상태 프레젠테이션을 연결할지 결정
3. 각 Pawn visual 프리팹의 Team Ring/상태 UI 위치를 실제 스프라이트 기준으로 조정
4. Addressables 콘텐츠 빌드에 최신 Pawn, 초상화, 아이콘, CSV가 포함됐는지 확인
5. EditMode/PlayMode 자동 테스트 추가
6. `BattleObjectManager`, `BattleUIController`, `BattleGameDataRepository`의 큰 책임을 장기적으로 분리

## 변경 작업 규칙

- 전투 결과값을 로컬에서 추론하거나 로그로 HP를 계산하지 않는다. 서버 snapshot/delta만 상태에 적용한다.
- Axial/Tilemap 좌표 변환은 `BattleMapGrid` 경계에서만 한다.
- 새 Pawn 클래스를 추가하면 ClassKey CSV, visual Addressable, `PawnVisualAddressByClass`, 필요 시 `AddPawnBehavior`, 아이콘/초상화 매핑을 함께 점검한다.
- 새 스킬/상태를 추가하면 GameData CSV, UI 아이콘 주소, tooltip/상태 아이콘 표현, 서버 계약을 함께 점검한다.
- Addressable 주소를 바꾸면 로더의 상수, 그룹 등록, 콘텐츠 빌드를 모두 확인한다.
- 필드 walkmap을 변경하면 `Field_001_WalkMap` Addressable과 서버가 사용하는 동일한 JSON 산출물을 함께 갱신한다. WorldMapRoot는 시각 표현이며 이동 판정 원본이 아니다.
- 생성 패킷 코드는 수정하지 않는다.

## 검증 절차

### 코드 변경

```powershell
dotnet build Assembly-CSharp.csproj --no-restore
dotnet build Assembly-CSharp-Editor.csproj --no-restore
```

Unity 패키지 참조 경고가 발생할 수 있으므로, 최종 판단은 Unity Editor Console과 Play Mode에서도 확인한다.

### 콘텐츠 변경

1. Unity Editor Console 오류를 확인한다.
2. 프리팹, Sprite, CSV가 올바른 Addressables 그룹과 주소에 등록됐는지 확인한다.
3. `Window > Asset Management > Addressables > Groups > Build > New Build > Default Build Script`를 실행한다.
4. Play Mode 또는 Player Build에서 실제 로드와 화면을 확인한다.

### 멀티플레이 전투 스모크 테스트

1. 서버를 실행하고 클라이언트 둘을 서로 다른 `-playerIndex` 값으로 실행한다.
2. 두 Pawn이 FieldScene에서 생성되고 이동이 서로 동기화되는지 확인한다.
3. 초대, 수락/거절, 같은 battleId로 BattleScene 진입하는 흐름을 확인한다.
4. 이동, ZOC 반응, 스킬, 상태/overlay/턴 큐 변화가 두 클라이언트에서 일치하는지 확인한다.
5. 사망과 결과 UI를 확인하고, ACK 후 FieldScene으로 한 번만 복귀하는지 확인한다.

## 문서 갱신 기준

- 런타임 수명, 씬, 로더, 좌표 경계가 바뀌면 `01_Architecture.md`를 갱신한다.
- 패킷, 스킬 규칙, Pawn 상태, CSV/UI 계약이 바뀌면 `02_Gameplay_Protocol_Data.md`를 갱신한다.
- 구현 완료 범위, 검증 결과, 후속 작업이 바뀌면 이 문서를 갱신한다.
