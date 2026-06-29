# ResourceService 백엔드별 구체 클래스 분리 설계

- 작성일: 2026-06-29
- 대상: `Assets/FoundationDI/Runtime/Services/ResourceService/`
- 관련 규약: CLAUDE.md "SERVICE ARCHITECTURE", VContainer 생성자 선택 규칙

## 1. 목적 / 문제

`Register<IResourceService, ResourceService>(...)`로 등록하면 VContainer가 **매개변수가 가장 많은 생성자**를 선택한다. `ResourceService`는 생성자가 둘(`()` / `(IResourceProvider)`)이라 `ResourceService(IResourceProvider)`가 선택되고, `IResourceProvider`가 미등록이라 resolve에 실패한다.

팩토리 람다(`_ => new ResourceService()`)나 `[Inject]` 어트리뷰트 대신, **백엔드별 단일 생성자 구체 클래스**를 두어 `Register<IResourceService, AddressableResourceService>(...)` 형태로 깔끔히 등록할 수 있게 한다.

## 2. 설계 결정 (확정)

1. **구조**: 코어(`ResourceService`, `IResourceProvider` 단일 생성자) + 백엔드별 sealed 구체(`AddressableResourceService`/`DefaultResourceService`)가 상속.
2. **백엔드 2종**: Addressables(기존 `AddressableResourceProvider`) + Resources.Load(신규 `ResourcesProvider`).
3. **`ResourceService()` 기본 생성자 제거** → 코어는 생성자 하나, greedy 모호성 제거.
4. **`ResourcesProvider.Release`**: `Resources.UnloadAsset` 시도(가능 타입만) + 한계 명시.
5. **`ResourcesProvider` 자동 테스트 생략**: 실제 Resources 에셋 의존이라 PlayMode/수동 검증 대상.

## 3. 아키텍처

### 3.1 코어 + 구체 (상속)

```csharp
// 코어: 캐싱·참조 카운팅 로직 + IResourceProvider seam. 단일 생성자.
public class ResourceService : IResourceService
{
    public ResourceService(IResourceProvider provider) { ... }   // 유일한 생성자
    // 기존 무파라미터 생성자 ResourceService()는 제거
}

public sealed class AddressableResourceService : ResourceService
{
    public AddressableResourceService() : base(new AddressableResourceProvider()) { }
}

public sealed class DefaultResourceService : ResourceService
{
    public DefaultResourceService() : base(new ResourcesProvider()) { }
}
```

- 코어 `ResourceService`는 non-abstract로 유지한다(EditMode 테스트가 `new ResourceService(mockProvider)`로 직접 인스턴스화). 캐싱·참조 카운팅·in-flight 중복 제거 로직은 그대로다.
- 두 구체는 무파라미터 단일 생성자이므로 `Register<IResourceService, XxxResourceService>`가 greedy 모호성 없이 동작한다.

### 3.2 신규 `ResourcesProvider : IResourceProvider`

`AddressableResourceProvider`와 동일한 형태(키→로드 에셋 매핑 + Release). Resources.Load 백엔드.

```csharp
public class ResourcesProvider : IResourceProvider
{
    private readonly Dictionary<string, Object> _assets = new();

    public async UniTask<T> LoadAsync<T>(string key) where T : Object
    {
        var request = Resources.LoadAsync<T>(key);
        await request.ToUniTask();
        var asset = request.asset as T;
        if (asset != null) _assets[key] = asset;
        return asset;
    }

    public T Load<T>(string key) where T : Object
    {
        var asset = Resources.Load<T>(key);
        if (asset != null) _assets[key] = asset;
        return asset;
    }

    public void Release(string key)
    {
        if (!_assets.TryGetValue(key, out var asset)) return;
        _assets.Remove(key);
        // Resources.UnloadAsset은 GameObject/Component에 사용할 수 없다.
        if (asset != null && asset is not GameObject && asset is not Component)
        {
            Resources.UnloadAsset(asset);
        }
    }
}
```

> **한계**: Resources는 Addressables 같은 핸들 해제가 없어 메모리 반환이 제한적이다. `GameObject`(프리팹) 등은 개별 언로드가 불가능하며, 확실한 회수는 `Resources.UnloadUnusedAssets()`(호출자 책임)로만 가능하다. 참조 카운팅(캐시 제거) 자체는 코어가 동일하게 처리한다.

### 3.3 DI 등록

```csharp
builder.Register<IResourceService, AddressableResourceService>(Lifetime.Singleton);
// 또는 Resources 백엔드:
builder.Register<IResourceService, DefaultResourceService>(Lifetime.Singleton);
```

각 단일 무파라미터 생성자 → VContainer 생성자 선택 모호성 없음.

## 4. 영향 / 갱신

- `Tests/Editor/UIManager/DIRegistrationTests.cs`: `_ => new ResourceService()` 람다 → `Register<IResourceService, AddressableResourceService>(Lifetime.Singleton)`.
- README 등록 예제를 `Register<IResourceService, AddressableResourceService>`로 통일:
  - `Assets/FoundationDI/README.md`, 저장소 루트 `README.md`
  - `Runtime/Services/ResourceService/README.md` (+ 백엔드 2종·구조·Resources 한계 설명)
  - `Runtime/Services/SoundService/README.md`
  - `Runtime/Managers/UIManager/README.md` (현재 람다 → 구체 클래스로 통일)
- `ResourceService()` 무파라미터 생성자를 호출하던 다른 코드가 있으면 함께 갱신(구현 시 grep 확인).

## 5. 에러 처리

- `ResourcesProvider.Release`: 캐시에 없는 키는 무시. 언로드 불가 타입은 no-op.
- 코어의 참조 카운팅 규약(로드 1회 ↔ Release 1회, 과다/누락 안전 무시)은 불변.

## 6. 테스트 전략 (EditMode, NSubstitute)

- **코어 `ResourceServiceTest`**: 기존 그대로 통과(코어 로직 불변, `new ResourceService(mockProvider)`).
- **`DIRegistrationTests` 확장**: `Register<IResourceService, AddressableResourceService>(Singleton)` 후 `Resolve<IResourceService>()`가 성공함을 검증(greedy 문제 해결의 회귀 가드). `IResourceProvider`를 등록하지 않아도 resolve되어야 한다.
- **`ResourcesProvider`**: 실제 Resources 폴더 에셋에 의존하므로 EditMode 자동 단위 테스트는 생략하고 PlayMode/수동 검증 대상으로 둔다(코어·등록 경로만 자동 검증).

## 7. 범위 밖

- 추가 백엔드(AssetBundle 등)는 필요 시 같은 패턴으로 확장.
- `ResourcesProvider`의 정교한 메모리 회수(개별 언로드 전략 고도화)는 후속 과제.
