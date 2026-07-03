# ProjectOCH Client - Start Here

Last updated: 2026-07-03

이 문서는 새 Codex 대화를 시작할 때 가장 먼저 읽기 위한 인수인계 문서다.

## 빠른 요약

- Unity client root: `C:\ProjectOCH\Client`
- Obsidian vault: `C:\ProjectOCH\Client\ProjectOCH_Client`
- Unity version: `6000.3.9f1`
- Server reference root: `C:\ProjectOCH\Server`
- 현재 목표: 서버 권위 구조의 2D SRPG. Field는 자유 클릭 이동, Battle은 axial hex tile 기반 전투.
- 현재 작업 초점: BattleScene 전투 루프, 스킬 패킷, 전투 UI prefab 연결.

## 먼저 읽을 문서

1. [[01_Project_Snapshot]]
2. [[03_Networking_And_Packets]]
3. [[08_Battle_Development]]
4. [[02_Addressables_Setup]]
5. [[05_Known_Issues_And_TODO]]
6. [[06_Work_Log]]
7. [[04_Server_Protocol_Notes]]
8. [[07_Architecture_Refactor]]

## 현재 핵심 코드

- 앱 루트
  - `Assets/Scripts/App/GameRoot.cs`
  - `Assets/Scripts/App/AppServices.cs`
- 네트워크
  - `Assets/Scripts/Services/Network/NetworkService.cs`
  - `Assets/Scripts/Network/GameServerConnection.cs`
  - `Assets/Scripts/Packet/PacketHandler.cs`
  - `Assets/Scripts/Packet/Generated/PacketManager.cs`
  - `Assets/Scripts/Packet/Generated/Protocol.cs`
- Field
  - `Assets/Scripts/Scenes/FieldSceneAddressableLoader.cs`
  - `Assets/Scripts/Field/FieldObjectManager.cs`
  - `Assets/Scripts/Field/FieldPawnController.cs`
  - `Assets/Scripts/Field/FieldMapWalkArea.cs`
  - `Assets/Scripts/Field/FieldPositionCodec.cs`
  - `Assets/Scripts/Field/CameraController.cs`
- Battle
  - `Assets/Scripts/Scenes/BattleSceneFlow.cs`
  - `Assets/Scripts/Scenes/BattleSceneAddressableLoader.cs`
  - `Assets/Scripts/Battle/BattleMapGrid.cs`
  - `Assets/Scripts/Battle/BattleObjectManager.cs`
  - `Assets/Scripts/Battle/BattlePawnController.cs`
  - `Assets/Scripts/Battle/BattleActionMode.cs`
  - `Assets/Scripts/Battle/BattleUIController.cs`
- Editor tools
  - `Assets/Editor/MultiplayerBuildAndRun.cs`
  - `Assets/Editor/FieldWalkMapExporter.cs`
  - `Assets/Editor/AddressablesProjectSetup.cs`

## 현재 바로 이어서 할 일

가장 자연스러운 다음 작업은 `Canvas_BattleUI.prefab`을 실제 BattleScene UI로 연결하는 것이다.

현재는 `BattleUIController`가 런타임에 임시 UI를 코드로 생성한다. 하지만 사용자가 `Assets/@Resources/Prefab/UI/Canvas_BattleUI.prefab` 목업을 만들어 둔 상태다. 따라서 다음 순서는:

1. `Canvas_BattleUI.prefab`의 오브젝트 이름 정리
   - `ActionIcon1 (1)` 같은 이름을 `ActionSlot_01` 형식으로 변경
   - `TurnQueue` 자식은 `TurnPortraitSlot_01` 형식으로 변경
   - `TrunExit` 오타를 `TurnExit`로 변경
2. `Canvas_BattleUI`를 Addressables 또는 Resources 로드 대상으로 결정
3. 기존 코드 생성형 `BattleUIController`를 prefab 바인딩형으로 교체
4. `ActionSlot_01~03` 클릭 시 `BattleObjectManager.SetActionMode(Skill1~3)` 호출
5. `TurnExit` 클릭 시 서버용 end turn 패킷 설계 또는 임시 로그 유지
6. `TurnQueue`는 현재 `next_turn_pawn_id`만으로는 전체 큐 애니메이션이 부족하므로 서버 proto 확장이 필요

## 검증 메모

- MSBuild 기준 클라이언트 C# 컴파일은 최근 통과했었다.
- 반복적으로 보이는 `System.Net.Http` version conflict warning은 기존 Unity/Addressables 쪽 경고이며, 최근 전투 코드 변경과 직접 관련은 없다.
- Unity Input은 Input System package 기준이다. UI EventSystem에는 `InputSystemUIInputModule`을 써야 한다.
