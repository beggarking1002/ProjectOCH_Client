# Addressables Setup

Last updated: 2026-07-03

## 기본 설정

- Package: `com.unity.addressables` `2.9.1`
- Settings path: `Assets/AddressableAssetsData`
- Setup tool:

```text
Assets/Editor/AddressablesProjectSetup.cs
Tools/Project OCH/Addressables/Initialize
```

## 현재 사용하는 주요 Addressable

Field:

```text
Field
WorldMapRoot
Pawn_Beige_Ice
```

Battle:

```text
BattleField_001
Pawn_Beige_Ice
Pawn_Beige_Fire
Pawn_Suen_AxeSword
Pawn_Suen_Parvis
Pawn_Zillian_Longbow
Pawn_Zillian_Mace
Pawn_Alen_Spear
Pawn_Alen_SwordShield
Pawn_Sera_Necromancer
Pawn_Sera_Warlock
Pawn_Darkhand_Sword
```

UI:

```text
Assets/@Resources/Prefab/UI/Canvas_BattleUI.prefab
```

`Canvas_BattleUI.prefab`은 아직 Addressables에 등록된 것으로 확인하지 않았다. 다음 작업에서 `Resources.Load`로 쓸지 Addressables로 등록할지 결정해야 한다. 장기적으로는 Addressables 등록을 권장한다.

## FieldScene 로드

`FieldSceneAddressableLoader`가 FieldScene 진입 시 Addressables로 Field 관련 prefab을 로드한다.

```text
FieldScene
 -> Addressables.InstantiateAsync("Field")
 -> FieldMapWalkArea 초기화
 -> FieldObjectManager 초기화
 -> Addressables.InstantiateAsync("WorldMapRoot")
 -> Field pawn spawn
```

현재 Field pawn은 `Pawn_Beige_Ice`를 사용한다.

## BattleScene 로드

`BattleSceneAddressableLoader`가 BattleScene 진입 시 `BattleField_001`을 로드한다.

```text
BattleScene
 -> Addressables.InstantiateAsync("BattleField_001")
 -> BattleMapGrid.InitializeIfNeeded()
 -> BattleObjectManager.Initialize()
 -> S_ENTER_BATTLE가 있으면 서버 pawn 정보로 spawn
 -> 없으면 debug pawn spawn
 -> BattleUIController.Initialize()
```

주의: `BattleUIController`는 현재 `Canvas_BattleUI.prefab`을 쓰지 않고 런타임에 임시 UI를 만든다. 교체 필요.

## Addressables 빌드

멀티플레이어 빌드 스크립트가 클라이언트 빌드 전에 Addressables content build를 수행하도록 구성되어 있다.

```text
Assets/Editor/MultiplayerBuildAndRun.cs
AddressableAssetSettings.BuildPlayerContent(...)
```

## Remote path 주의

개발 기본값은 localhost 기반이다.

```text
Remote.BuildPath = ServerData/[BuildTarget]
Remote.LoadPath = http://localhost/[BuildTarget]
```

실제 배포 시 `localhost`는 각 클라이언트 자기 자신을 의미하므로 CDN, 파일 서버, 내장 번들 중 하나로 정책을 정해야 한다.

## 자주 본 경고

`ProfileValueReference: GetValue called with empty id`

Addressables profile path id가 비어 있을 때 발생할 수 있다. `AddressablesProjectSetup`이 Remote build/load path id를 다시 잡아주도록 구성되어 있다.
