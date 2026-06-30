# PoolManager

키(string) 기반 **GameObject 오브젝트 풀 매니저**입니다. 자주 생성·파괴되는 오브젝트(투사체, 이펙트, 적 등)를 미리 만들어 재사용해 `Instantiate`/`Destroy` 비용과 GC 부담을 줄입니다.

- **씬 수명** — 보통 씬 `LifetimeScope`에 등록해 풀이 씬과 수명을 함께합니다. 씬 언로드 시 `scope.Dispose()`로 풀과 로드한 에셋이 자동 정리됩니다(전역 풀이 필요하면 루트 스코프에 등록).
- **로딩 위임** — 프리팹 로드는 직접 `Resources`/`Addressables`를 호출하지 않고 [`IResourceService`](../../Services/ResourceService/README.md)에 위임합니다(핸들·참조 카운팅을 한 곳에서 관리).
- **Unity ObjectPool 기반** — 키마다 `UnityEngine.Pool.ObjectPool<IPoolItem>`을 두고, `PoolData`가 풀 상태를, `PoolItem`이 항목 생명주기 콜백과 지연 반환을 담당합니다.

---

## 사용법

### 1) DI 등록 (VContainer)

`RegisterPoolManager`를 **씬 LifetimeScope**에서 호출합니다. `transform`을 넘기면 풀 루트가 활성 씬이 아니라 그 transform이 속한 씬에 확실히 귀속됩니다(additive 로드 안전).

```csharp
using VContainer;
using VContainer.Unity;
using DarkNaku.FoundationDI;

public class SceneLifetimeScope : LifetimeScope   // 씬에 배치
{
    protected override void Configure(IContainerBuilder builder)
    {
        // 전제: 부모(루트) 스코프에 IResourceService가 이미 등록되어 있어야 한다.
        builder.RegisterPoolManager(transform);
    }
}
```

루트 스코프(전역 수명)에 등록하면 풀이 게임 전체 수명을 가집니다.

### 2) 가져오기 / 반환

```csharp
public class Weapon
{
    private readonly IPoolManager _pool;

    public Weapon(IPoolManager pool) => _pool = pool;   // 생성자 주입

    public void Fire(Transform muzzle)
    {
        // key = ResourceService 로드 키(Resources 경로 또는 Addressables 주소/키)
        var bullet = _pool.Get("Bullet", muzzle);   // GameObject 반환
        // 컴포넌트로 바로 받기: var b = _pool.Get<Bullet>("Bullet", muzzle);

        _pool.Release(bullet, 2f);   // 2초 뒤 풀로 반환 (delay 생략 시 즉시)
    }
}
```

- `Get`은 키가 처음이면 `IResourceService.Load`로 프리팹을 로드하고 풀을 자동 생성합니다.
- 부모를 생략하면 풀 루트(`[PoolManager]`) 아래로 들어갑니다.
- 반환은 `Destroy`가 아니라 `Release`로 합니다.

### 3) 항목 생명주기 콜백

프리팹에 `PoolItem`을 상속한 컴포넌트를 붙이면 콜백을 오버라이드할 수 있습니다(없으면 `Get` 시점에 자동으로 `PoolItem`이 `AddComponent`됩니다).

```csharp
public class Bullet : PoolItem
{
    public override void OnGetItem()       // 풀에서 꺼낼 때 (기본: SetActive(true))
    {
        base.OnGetItem();
        // 속도·수명 초기화 등
    }

    public override void OnReleaseItem()   // 풀로 되돌릴 때 (기본: SetActive(false))
    {
        base.OnReleaseItem();
    }
}
```

콜백 순서: `OnCreateItem`(최초 1회) → `OnGetItem`(꺼낼 때마다) → `OnReleaseItem`(반환할 때마다) → `OnDestroyItem`(풀 정리 시).

---

## API

### `IPoolManager : IDisposable`

| 멤버 | 시그니처 | 설명 |
| --- | --- | --- |
| `Get` | `GameObject Get(string key, Transform parent = null)` | 키에 해당하는 인스턴스를 풀에서 가져옵니다(없으면 로드·생성). `parent` 생략 시 풀 루트 아래로. |
| `Get<T>` | `T Get<T>(string key, Transform parent = null) where T : class` | `Get` 후 `GetComponent<T>()` 결과를 반환합니다. |
| `Release` | `void Release(GameObject item, float delay = 0f)` | 인스턴스를 풀로 되돌립니다. `delay > 0`이면 그 시간 뒤 반환(`UniTask`). |
| `Dispose` | `void Dispose()` | 모든 풀을 정리하고, 로드한 에셋을 키마다 `IResourceService.Release`하며, 풀 루트를 파괴합니다. |

### 생성자

```csharp
public PoolManager(IResourceService resourceService, Transform parent = null);
```

- `resourceService` — 프리팹 로드를 위임할 리소스 서비스(필수, DI 주입).
- `parent` — 풀 루트(`[PoolManager]`)를 둘 부모. 보통 씬 `LifetimeScope`의 transform. `null`이면 활성 씬에 생성됩니다.

---

## 매뉴얼

### 씬 수명과 메모리 정리

- 풀 루트는 `DontDestroyOnLoad`로 두지 않습니다. `parent`(씬 스코프 transform) 아래에 붙어 해당 씬과 함께 파괴됩니다.
- 씬 언로드 → 씬 `LifetimeScope`의 `scope.Dispose()` → `PoolManager.Dispose()` → 풀 정리 + 로드한 키마다 `IResourceService.Release()`. 참조가 0이 되면 에셋이 언로드됩니다.
- 파괴 순서(씬의 GameObject 파괴 ↔ `Dispose`)는 보장되지 않으므로, 이미 파괴된 항목·루트는 fake-null 가드로 건너뜁니다.

### 로딩 위임 (ResourceService)

- 프리팹은 `IResourceService.Load<GameObject>(key)`로 로드합니다. 단일 백엔드만 사용하므로(Resources **또는** Addressables) 키가 등록된 백엔드를 따릅니다 — 한 서비스 인스턴스 안에서 Resources↔Addressables 폴백은 없습니다.
- 로드 1회 ↔ `Dispose`의 `Release` 1회로 참조 카운팅 짝을 맞춥니다.
- **로드 실패(null)**: `Get`은 에러 로그를 남기고 `null`을 반환합니다. 실패한 로드는 ResourceService가 캐시·카운트하지 않으므로 별도의 보상 `Release`는 하지 않습니다.

### 테스트

- EditMode 단위 테스트(`Assets/FoundationDI/Tests/PoolManagerTest.cs`)는 `IResourceService`를 NSubstitute로 대체해 로드 위임·부모 귀속·실패 처리·`Dispose` 시 키 Release를 검증합니다.
- `DontDestroyOnLoad` 제거의 실효(씬 전환 시 실제 정리)는 EditMode에서 검증되지 않으므로 PlayMode/수동 확인 대상입니다.

### 한계 / 후속 과제

- **스레드 안전성 없음** — Unity 메인 스레드 사용을 전제로 합니다.
- **동기 로드** — 첫 `Get`은 `IResourceService.Load`(`WaitForCompletion`) 기반이라 큰 에셋은 프레임이 멈출 수 있습니다. 비동기 선로딩이 필요하면 미리 `IResourceService.LoadAsync`로 데워두는 방식을 고려하세요.
- **Addressables 키 예외** — 잘못된 Addressables 키는 `IResourceService`에서 예외가 전파될 수 있습니다(에러 처리는 ResourceService의 후속 과제).
