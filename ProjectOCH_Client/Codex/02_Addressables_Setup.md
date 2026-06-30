# Addressables Setup

Last updated: 2026-06-30

## 패키지와 기본 설정

- `Packages/manifest.json`
  - `com.unity.addressables`: `2.9.1`
- Addressables 설정 위치:
  - `Assets/AddressableAssetsData`
- Build Settings config object:
  - `ProjectSettings/EditorBuildSettings.asset`

자동 초기화 도구:

```text
Assets/Editor/AddressablesProjectSetup.cs
Tools/Project OCH/Addressables/Initialize
```

## 현재 Addressable 사용

`FieldSceneAddressableLoader`가 FieldScene 진입 시 Addressables로 프리팹을 로드한다.

현재 코드 기준 주소:

```text
Field
WorldMapRoot
Field_Pawn
```

역할:

- `Field`
  - 필드 맵 루트. `FieldMapWalkArea`, `FieldObjectManager`가 붙거나 런타임에 추가된다.
- `WorldMapRoot`
  - Field 위에 올라가는 시각용 월드맵 이미지/오브젝트.
- `Field_Pawn`
  - 플레이어 pawn 프리팹. `FieldObjectManager`가 Addressables로 생성한다.

## FieldScene 로딩 흐름

```text
FieldScene 로드
 -> FieldSceneAddressableLoader.LoadFieldMap()
 -> Addressables.InstantiateAsync("Field")
 -> FieldMapWalkArea 초기화
 -> FieldObjectManager 초기화
 -> Addressables.InstantiateAsync("WorldMapRoot")
```

`WorldMapRoot`의 `SpriteRenderer.sortingOrder`는 로드 후 offset을 더해 Field 타일맵 위에 보이게 한다.

## Addressables 빌드

멀티 클라 빌드 스크립트가 클라이언트 빌드 전에 Addressables content build를 실행한다.

```text
Assets/Editor/MultiplayerBuildAndRun.cs
AddressableAssetSettings.BuildPlayerContent(...)
```

## 현재 프로필 값

개발용 기본값:

```text
Remote.BuildPath = ServerData/[BuildTarget]
Remote.LoadPath = http://localhost/[BuildTarget]
```

주의:

- `localhost`는 개발용이다.
- 다른 PC/기기 배포 시 클라이언트 자신의 localhost를 보기 때문에 remote asset을 찾지 못한다.
- 배포 전 CDN, 파일 서버, 또는 내장 로컬 번들 전략을 결정해야 한다.

## 관련 경고

`ProfileValueReference: GetValue called with empty id` 경고는 Addressables profile path id가 비어 있을 때 발생할 수 있다. 현재 초기화 스크립트는 Remote build/load path id가 비어 있으면 변수 참조를 다시 설정한다.
