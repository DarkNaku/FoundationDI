# 사운드 카탈로그 + 프리로드 설계

- 작성일: 2026-06-29
- 대상: `Assets/FoundationDI/Runtime/Services/SoundService/`
- 관련 규약: CLAUDE.md "SERVICE ARCHITECTURE", "리소스 로딩은 ResourceService에 위임"

## 1. 목적

사운드를 미리 로드(프리로드)해 첫 재생 지연을 제거하고, 사운드를 **논리 문자열키**로 식별하는 카탈로그를 도입한다. 추후 `SoundButton` 같은 컴포넌트가 이 카탈로그의 키 목록을 드롭다운 소스로 사용할 수 있게 한다.

## 2. 설계 결정 (확정)

1. **카탈로그 역할 범위**: 전체 카탈로그. 모든 사운드를 문자열키로 등록하고 `Play`도 문자열키로 호출한다. 항목별 프리로드 플래그를 둔다.
2. **프리로드 방식**: 비동기 `PreloadAsync()` (UniTask). `IResourceService.LoadAsync`로 병렬 로드한다. 생성자에서 동기 로드하지 않는다(프레임 멈춤 방지).
3. **미등록 키 처리**: 엄격 모드. 카탈로그에 없는 키는 `Debug.LogError` 후 무시한다.

## 3. 아키텍처

### 3.1 카탈로그 추상화 — `ISoundCatalog`

SoundService는 구체 ScriptableObject가 아니라 인터페이스에 의존한다(seam 분리 → NSubstitute EditMode 테스트 가능).

```csharp
public interface ISoundCatalog
{
    bool TryGetResourceKey(string key, out string resourceKey);  // 문자열키 → 리소스키
    IReadOnlyList<string> Keys { get; }                          // SoundButton 드롭다운 소스
    IEnumerable<string> PreloadResourceKeys { get; }             // Preload=true 항목의 리소스키
}
```

### 3.2 데이터 모델 — `SoundCatalog : ScriptableObject, ISoundCatalog`

```csharp
[Serializable]
public struct SoundEntry
{
    public string Key;          // 논리 이름 (Play 인자, 드롭다운 표시). 예: "Jump"
    public string ResourceKey;  // ResourceService 로드 키 (Resources 경로 / Addressables 주소)
    public bool Preload;        // 프리로드 대상 여부
}
```

- `[CreateAssetMenu(fileName = "SoundCatalog", menuName = "DarkNaku/SoundCatalog")]`
- 직렬화 필드는 `List<SoundEntry>`.
- 런타임에 `Dictionary<string,string>`(Key→ResourceKey)을 1회 빌드해 조회에 사용한다.
- 빌드 시 중복 `Key`는 마지막 항목을 채택하고 `Debug.LogWarning`으로 경고한다.
- 문자열키/리소스키 분리 이유: 리소스 경로가 바뀌어도 `Key`는 안정적이며, 드롭다운에 친숙한 이름을 노출할 수 있다.

### 3.3 SoundService 변경

- 생성자: `SoundService(IResourceService resourceService, ISoundCatalog catalog)`.
- `Play(string key)` / `PlayBGM(string key)`:
  1. `catalog.TryGetResourceKey(key, out var resourceKey)` 호출.
  2. 실패 시 `Debug.LogError` 후 return (엄격 모드).
  3. 성공 시 기존 흐름(`_resourceService.Load(resourceKey)` → 재생).
- `UniTask PreloadAsync()` (ISoundService에 추가):
  - `catalog.PreloadResourceKeys`를 `_resourceService.LoadAsync`로 병렬 로드(`UniTask.WhenAll`).
  - 로드한 클립을 `_table`(리소스키 기준 캐시)에 채운다.
- 내부 캐시(`_table`)와 `Dispose`의 `Release`는 기존처럼 **리소스키 기준**으로 유지 → 프리로드/Play/Dispose의 참조 카운팅이 일관된다.

### 3.4 DI 등록 — `RegisterSoundService` 확장 메서드

UIManager(`RegisterUIManager`) 패턴을 따른다.

```csharp
public static void RegisterSoundService(this IContainerBuilder builder, SoundCatalog catalog)
{
    builder.RegisterInstance<ISoundCatalog>(catalog);
    builder.Register<ISoundService, SoundService>(Lifetime.Singleton);
}
```

- 전제: `IResourceService`가 먼저 등록되어 있어야 한다(UIManager와 동일 규약).
- `PreloadAsync()` 호출 시점(부팅 플로우/로딩 씬 등)은 호스트 프로젝트가 결정한다.

## 4. 데이터 흐름

1. 부팅: 호스트가 `RegisterSoundService(catalog)` 등록 → 로딩 시점에 `ISoundService.PreloadAsync()` 호출.
2. `PreloadAsync`: `Preload=true` 항목의 리소스키를 `IResourceService.LoadAsync` 병렬 로드 → `_table` 캐시 채움.
3. 재생: `Play("Jump")` → 카탈로그에서 `"Jump"` → 리소스키 변환 → `_table` 캐시 히트(프리로드된 경우) 또는 `IResourceService.Load` → 재생.
4. 종료: `Dispose` → `_table`의 모든 리소스키 `Release`.

## 5. 에러 처리

- 미등록 키(`Play`/`PlayBGM`): `Debug.LogError` + 무시(엄격 모드).
- 카탈로그 중복 키: 빌드 시 `Debug.LogWarning`, 마지막 항목 채택.
- 프리로드 로드 실패: `IResourceService`의 동작에 위임(현재 ResourceService는 로드 예외 처리 미구현 — 알려진 한계로 그대로 둔다).
- `catalog`가 null인 경우는 등록 규약 위반으로 간주한다(정상 경로에서는 항상 주입됨).

## 6. 테스트 전략 (TDD, EditMode)

`ISoundCatalog`를 NSubstitute로 대체한다.

- 등록된 키 재생 → `IResourceService.Load(resourceKey)` 위임 검증.
- 미등록 키 재생 → `Load` 미호출 + 엄격 모드 동작 검증.
- `PreloadAsync` → `PreloadResourceKeys`의 각 리소스키에 대해 `LoadAsync` 호출 검증.
- 프리로드된 키 재생 시 추가 `Load` 호출 없이 캐시 사용 검증.
- (별도) `SoundCatalog` SO: Key→ResourceKey 매핑, 중복 키 경고, `Keys`/`PreloadResourceKeys` 노출 검증.

## 7. 이번 범위 밖 (향후 확장 지점)

- **SoundButton 컴포넌트** + 문자열키 드롭다운: `SoundCatalog.Keys`를 소스로 하는 커스텀 `PropertyDrawer`. 이번에는 카탈로그가 `Keys`를 노출하는 것까지만 준비한다.
- 그룹/씬별 분할 로드·언로드.
- 동기 프리로드 옵션.

위 3개는 YAGNI 원칙으로 이번 구현에서 제외한다.
