# Project OCH Client

Unity 6로 제작한 **서버 권위형 온라인 전술 RPG 프로토타입 클라이언트**입니다. 필드 탐색과 마을 콘텐츠, 계정 생존 자원, 클래스 선택과 두 플레이어가 Pawn 4개씩 조작하는 4대4 육각 타일 전투를 제공합니다.

- Server repository: [ProjectOCH_Server](https://github.com/beggarking1002/ProjectOCH_Server)

## 주요 기능

- Google OAuth 2.0 Authorization Code + PKCE 로그인
- 서버가 확정한 필드 경로 재생과 동일 walkmap 기반 A* 시각 보정
- 마을 상점, 플레이어별 재고, 인벤토리와 퀘스트 UI
- 포만도, 행복도와 갈증 상태 표시 및 물 타일 상호작용
- 육각 타일 이동·대상 선택과 8종 Pawn 클래스 전투 표현
- 서버 상태 버전 검증과 `BattleActionLog` 기반 순차 연출
- Addressables 기반 필드·전투 Pawn, UI와 시각 자산 로딩

## 구조

```text
GameRoot (DontDestroyOnLoad)
    ↓
NetworkService · PacketHandler
    ↓
SceneFlow (Title → Field ⇄ Battle)
    ↓
FieldObjectManager · BattleObjectManager · UI
```

네트워크 연결과 최신 서버 상태는 씬과 독립적으로 유지합니다. 클라이언트는 서버가 확정한 Pawn·타일 상태를 먼저 적용하고, 행동 로그를 입력이 잠긴 연출 큐에서 순서대로 재생합니다.

## 기술 스택

- Unity `6000.3.9f1`, C#
- Universal Render Pipeline 2D, Tilemap
- Addressables, Input System
- TCP, Protocol Buffers

## 로컬 실행

1. Unity Hub에서 Unity `6000.3.9f1`로 프로젝트를 엽니다.
2. `Assets/StreamingAssets/GoogleAuth.json.example`을 `GoogleAuth.json`으로 복사해 서버와 같은 Google Desktop OAuth 앱의 `client_id`를 입력합니다.
3. [서버 저장소](https://github.com/beggarking1002/ProjectOCH_Server)의 MySQL과 인증 설정을 완료하고 `GameServer`를 실행합니다.
4. `Assets/Scenes/TitleScene.unity`를 열어 Play합니다.

기본 서버 주소는 `127.0.0.1:7777`이며 `GameRoot`의 Inspector 설정에서 변경할 수 있습니다. 서버와 클라이언트의 Protocol Buffers 정의 및 생성 코드는 서로 일치해야 합니다. 실제 OAuth 정보가 들어가는 `Assets/StreamingAssets/GoogleAuth.json`은 Git에서 제외됩니다.

## 주요 디렉터리

```text
Assets/Scripts/App/       앱 생명주기와 공용 서비스
Assets/Scripts/Services/  네트워크와 인증
Assets/Scripts/Field/     필드 이동, 상호작용과 UI
Assets/Scripts/Battle/    전투 상태, 입력과 연출
Assets/Scenes/            Title, Field, Battle 씬
Assets/@Resources/        Addressables 프리팹과 아트 자산
```

## 현재 범위

서로 다른 Google 계정의 클라이언트 2개와 로컬 서버 환경에서 로그인, 필드 동기화, 전투 초대·진행·종료와 필드 복귀를 수동 검증했습니다. 전투 중 재접속과 자동 회귀 테스트는 후속 범위입니다.

