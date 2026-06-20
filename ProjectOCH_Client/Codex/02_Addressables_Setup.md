# Addressables Setup

Last updated: 2026-06-20

## 완료된 작업

- `Packages/manifest.json`에 `com.unity.addressables`: `2.9.1` 추가.
- Unity가 `Packages/packages-lock.json`에 Addressables와 의존성 `com.unity.scriptablebuildpipeline`을 해석.
- Addressables 데이터 생성:
  - `Assets/AddressableAssetsData/AddressableAssetSettings.asset`
  - `Assets/AddressableAssetsData/DefaultObject.asset`
  - `Assets/AddressableAssetsData/AssetGroups/Default Local Group.asset`
  - `Assets/AddressableAssetsData/AssetGroups/Remote Content.asset`
  - Data builders: Fast Mode, Packed Mode, Packed Play Mode
- `ProjectSettings/EditorBuildSettings.asset`에 Addressables settings config object 등록.
- `.gitignore`에 `ServerData` 제외 추가.

## 자동 초기화 코드

경로: `Assets/Editor/AddressablesProjectSetup.cs`

역할:

- Unity 에디터 로드 후 Addressables settings가 없거나 원격 그룹/경로가 비어 있으면 초기화.
- 메뉴 제공:
  - `Tools/Project OCH/Addressables/Initialize`
- `Remote Content` 그룹 생성.
- `Remote Content` 그룹 schema:
  - Build path: `Remote.BuildPath`
  - Load path: `Remote.LoadPath`
  - Bundle mode: `PackTogether`
  - Include in build: true
  - Content update static content: false

## 현재 프로필 경로

- `Local.BuildPath`: `[UnityEngine.AddressableAssets.Addressables.BuildPath]/[BuildTarget]`
- `Local.LoadPath`: `{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]`
- `Remote.BuildPath`: `ServerData/[BuildTarget]`
- `Remote.LoadPath`: `http://localhost/[BuildTarget]`

## 주의점

- `Remote.LoadPath`의 `localhost`는 개발용이다.
- 실제 배포 빌드에서는 각 클라이언트 자기 PC/기기의 localhost를 보게 되므로 에셋을 못 찾는다.
- 배포 전 `Remote.LoadPath`를 CDN 또는 파일 서버 URL로 바꿔야 한다.
- 아직 Addressable로 지정된 실제 에셋은 없다.
- 콘텐츠 빌드는 아직 하지 않았다.

## 다음 작업 후보

- Addressable로 관리할 에셋 기준 정하기.
- 로컬/원격 그룹 분리 규칙 정하기.
- 실제 CDN/파일 서버 URL 결정.
- Addressables content build 파이프라인 정리.
- 런타임 로딩 코드 작성.

