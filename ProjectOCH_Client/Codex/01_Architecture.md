# 아키텍처와 실행 흐름

> 코드 구조나 장면 흐름이 변경되면 이 문서를 갱신한다.

## 기술 구성

| 영역 | 사용 기술 |
| --- | --- |
| 엔진 / 렌더링 | Unity 6.3, URP 2D |
| UI | uGUI (`UnityEngine.UI`) |
| 입력 | Unity Input System |
| 콘텐츠 로딩 | Addressables 2.9 |
| 네트워크 | TCP Socket 기반 자체 `ServerCore` |
| 직렬화 | Google Protobuf |
| 좌표계 | 필드 / 전투 모두 axial hex 좌표를 사용 |

## 장면 흐름

```text
TitleScene
  └─ GameStartButton → C_ENTER_GAME → S_ENTER_GAME
       └─ FieldScene
            ├─ Field_001 / WorldMapRoot / field pawn Addressables 로드
            ├─ C_BATTLE_INVITE 또는 C_ENTER_BATTLE(개발용 B 키)
            └─ S_ENTER_BATTLE → BattleScene
                 ├─ BattleField_001 / battle pawns / BattleSceneUI 로드
                 ├─ C_BATTLE_MOVE / C_BATTLE_SKILL / C_BATTLE_END_TURN
                 └─ S_BATTLE_RESULT → C_BATTLE_RESULT_ACK → FieldScene
```

빌드 설정에서 활성화된 장면은 `TitleScene`, `FieldScene`, `BattleScene`이다. 각 장면의 실제 콘텐츠는 장면 파일보다 Addressables 프리팹이 담당하는 비중이 크다.

## 지속 객체와 책임

| 객체 | 생명주기 | 책임 |
| --- | --- | --- |
| `GameRoot` | 게임 시작부터 종료까지 | `AppServices`, 서버 연결, 매 프레임 서비스 Tick |
| `TitleSceneFlow` | `DontDestroyOnLoad` | 타이틀 버튼과 입장 응답 처리 |
| `FieldSceneAddressableLoader` | `DontDestroyOnLoad` | 필드 맵 / 월드맵 로드 및 해제 |
| `BattleSceneFlow` | `DontDestroyOnLoad` | 전투 입장·결과 ACK에 따른 장면 전환 |
| `BattleSceneAddressableLoader` | `DontDestroyOnLoad` | 전투 맵, Pawn, UI 준비와 전환 오버레이 제어 |
| `SceneTransitionOverlay` | 필요 시 생성 | 장면 전환 중 화면 차단 |

## 네트워크 경로

```text
Socket thread
  → GameServerSession
  → Generated PacketManager
  → PacketHandler queue
  → GameRoot.Update()
  → NetworkService.Tick()
  → NetworkService event
  → Field / Battle / Scene controller
```

`PacketHandler`는 수신 패킷을 큐에 넣고 Unity 메인 스레드에서 이벤트를 실행한다. `NetworkService`는 패킷을 앱 수준 이벤트와 최신 상태(`LastEnterBattle` 등)로 정리한다. 장면이 로드된 뒤에도 최신 상태를 이용해 초기 화면을 구성할 수 있다.

## 디렉터리 가이드

```text
Assets/
  Scripts/
    App/         앱 루트와 서비스 조합
    Scenes/      장면 흐름과 Addressables 로더
    Services/    네트워크 앱 서비스
    Network/     레거시/MonoBehaviour 네트워크 래퍼
    Packet/      Socket 기반, 핸들러, 생성된 프로토콜 코드
    Field/       필드 맵, 이동, 캐릭터, 전투 초대
    Battle/      전투 맵, Pawn, 입력, UI, GameData
  @Resources/    Addressables로 쓰이는 맵, Pawn, UI, 아트 프리팹
  GameData/      CSV 원본 데이터
  AddressableAssetsData/ Addressables 설정과 그룹
  Editor/        에디터 자동 설정 / 빌드 보조 도구
```

## 주의할 설계 경계

- `GameServerConnection`은 `GameRoot.Network`를 감싸는 MonoBehaviour 호환 래퍼다. 새 기능은 원칙적으로 `GameRoot.Instance.Network`를 사용한다.
- `Field.AxialCoord`와 `Battle.AxialCoord`는 이름과 구현이 비슷하지만 별도 타입이다. 두 영역 간 코드를 옮길 때 namespace 혼동에 유의한다.
- 장면 콘텐츠는 주소 문자열(`Field_001`, `BattleField_001`, `BattleSceneUI` 등)에 의존한다. 주소를 변경하면 로더와 Addressables 등록을 함께 수정한다.
- 전투 Pawn은 서버의 `PawnClass`만으로 공통 `PawnBase`와 클래스별 시각 프리팹을 조합한다. 서버의 `BattlePawn` 상속 구조를 클라이언트에 복제하지 않으며, 스킬 UI도 같은 `PawnClass`를 GameData 키로 사용한다.
