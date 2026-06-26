# ResourceService

Addressables 기반의 **범용 에셋 로더 서비스**입니다. 임의 타입 `T`의 에셋을 키로 로드/해제하는 단일 진입점을 제공하고, **참조 카운팅**으로 핸들 생명주기를 안전하게 관리합니다.

- **Addressables 전용** — `Resources.Load` 폴백 없음
- **비동기/동기** 로드 모두 지원 (`LoadAsync<T>` / `Load<T>`)
- **키 단위 캐싱 + 참조 카운팅** — 참조가 0이 되면 실제 Addressables 핸들 해제
- **진행 중(in-flight) 중복 제거** — 같은 키를 동시에 로드해도 실제 로드는 1회

---

## 사용법

### 1) DI 등록 (VContainer)

```csharp
using VContainer;
using VContainer.Unity;
using DarkNaku.FoundationDI;

public class RootLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // 기본 생성자가 AddressableResourceProvider를 주입
        builder.Register<IResourceService, ResourceService>(Lifetime.Singleton);
    }
}
```

### 2) 비동기 로드 / 해제

```csharp
public class Example
{
    private readonly IResourceService _resource;

    public Example(IResourceService resource) => _resource = resource;

    public async UniTask ShowIconAsync(Image target)
    {
        // Addressables 주소(또는 키)로 로드
        var sprite = await _resource.LoadAsync<Sprite>("ui_icon_coin");
        target.sprite = sprite;

        // 사용이 끝나면 같은 키로 해제 (참조 카운트 감소)
        _resource.Release("ui_icon_coin");
    }
}
```

### 3) 동기 로드

```csharp
// WaitForCompletion 기반 — 블로킹이 허용되는 초기화 등에서 사용
var prefab = _resource.Load<GameObject>("enemy_slime");
var instance = Object.Instantiate(prefab);
_resource.Release("enemy_slime");
```

### 4) 일괄 정리

```csharp
// 서비스 종료/씬 정리 시 남은 모든 핸들 해제
(_resource as IDisposable)?.Dispose();
// DI 컨테이너가 Singleton 수명을 관리하면 컨테이너 Dispose 시 자동 호출됨
```

---

## API

### `IResourceService : IDisposable`

| 멤버 | 시그니처 | 설명 |
| --- | --- | --- |
| `LoadAsync<T>` | `UniTask<T> LoadAsync<T>(string key) where T : Object` | 키에 해당하는 에셋을 비동기로 로드해 반환합니다. 이미 캐시에 있으면 즉시 캐시본을 반환하고 참조 카운트를 1 증가시킵니다. |
| `Load<T>` | `T Load<T>(string key) where T : Object` | 동기(`WaitForCompletion`) 로드입니다. 캐싱·참조 카운팅 규약은 `LoadAsync`와 동일합니다. |
| `Release` | `void Release(string key)` | 키의 참조 카운트를 1 감소시킵니다. 0이 되면 캐시에서 제거하고 실제 Addressables 핸들을 해제합니다. |
| `Dispose` | `void Dispose()` | 캐시에 남은 모든 키의 핸들을 일괄 해제하고 캐시를 비웁니다. |

**매개변수 / 제약**

- `key` — Addressables 주소 또는 키 문자열.
- `T` — `UnityEngine.Object` 파생 타입 (`Sprite`, `GameObject`, `AudioClip`, `ScriptableObject` 등).

**반환값**

- `LoadAsync<T>` / `Load<T>` — 로드된 에셋. 캐시 히트 시 동일 인스턴스를 반환합니다.

### `IResourceProvider`

실제 로딩 백엔드를 추상화한 seam입니다. `ResourceService`는 이 인터페이스에만 의존하므로, 테스트에서 대체 구현(mock)을 주입해 Addressables 없이 검증할 수 있습니다.

```csharp
public interface IResourceProvider
{
    UniTask<T> LoadAsync<T>(string key) where T : Object;
    T Load<T>(string key) where T : Object;
    void Release(string key);
}
```

`ResourceService`는 **첫 로드(참조 0→1)** 일 때만 `LoadAsync`/`Load`를, **마지막 해제(참조 1→0)** 일 때만 `Release`를 위임합니다.

### `AddressableResourceProvider`

`IResourceProvider`의 기본 구현(Addressables 어댑터)입니다. `ResourceService()` 기본 생성자가 자동으로 주입합니다.

- `Addressables.LoadAssetAsync<T>` / `WaitForCompletion()` / `Addressables.Release` 를 감쌉니다.
- 키→`AsyncOperationHandle` 매핑을 보관하고, `Release` 시 `IsValid()`로 확인 후 핸들을 해제합니다.

### 생성자

```csharp
public ResourceService();                          // 기본: AddressableResourceProvider 주입
public ResourceService(IResourceProvider provider); // 테스트/커스텀 백엔드 주입
```

---

## 매뉴얼

### 참조 카운팅 규약

- `LoadAsync`/`Load` 1회 호출은 참조 카운트를 1 증가시킵니다. 같은 키를 N번 로드하면 카운트는 N입니다.
- `Release` 1회 호출은 참조 카운트를 1 감소시킵니다. **N번 로드했다면 N번 Release해야** 실제 핸들이 해제됩니다.
- **로드 1회 ↔ 해제 1회**의 짝을 맞추는 것이 원칙입니다. 짝이 맞지 않으면 핸들이 조기 해제되거나(과다 Release) 메모리에 남습니다(Release 누락).
- 캐시에 없는 키를 `Release`하거나 보유 참조보다 많이 `Release`해도 안전하게 무시됩니다(예외 없음).

### 캐싱

- 키 단위로 로드된 에셋과 참조 카운트를 캐시합니다. 캐시 히트 시 동일 인스턴스를 반환하므로, 같은 키의 반복 로드는 추가 Addressables 호출을 일으키지 않습니다.
- 참조 카운트가 0이 되어 해제된 키를 다시 로드하면 Addressables에서 새로 로드합니다.

### 동시 로드 중복 제거 (in-flight)

- 같은 키의 `LoadAsync`가 완료되기 전에 다시 호출되면, 진행 중인 로드를 공유하여 Addressables 호출을 1회로 묶습니다.
- 동시에 대기한 호출자 각각이 참조 카운트를 1씩 올립니다. 예) 같은 키를 동시에 2번 로드 → 참조 카운트 2, Addressables 로드는 1회.

### 비동기 vs 동기

- `LoadAsync<T>` — 프레임 드롭 없는 비동기 로드. 일반적인 런타임 로딩에 권장합니다.
- `Load<T>` — `WaitForCompletion` 기반 동기 로드. 블로킹이 허용되는 초기화 단계 등에서만 사용하세요. 큰 에셋을 메인 스레드에서 동기 로드하면 프레임이 멈출 수 있습니다.
- 두 메서드는 같은 캐시·참조 카운트를 공유하므로, 한 키를 `Load`로 올리고 `Release`로 내리거나 그 반대도 일관되게 동작합니다.

### 정리(Dispose)

- `Dispose()`는 남아 있는 모든 키의 핸들을 해제합니다. DI 컨테이너가 `Singleton` 수명을 관리하면 컨테이너 Dispose 시 자동 호출됩니다.

### 테스트

- EditMode 단위 테스트(`Assets/FoundationDI/Tests/ResourceServiceTest.cs`)는 `IResourceProvider`를 NSubstitute로 대체하여, 실제 Addressables 빌드 없이 참조 카운팅·캐싱·중복 제거 로직을 검증합니다.
- 실제 Addressables 연동(`AddressableResourceProvider`)은 PlayMode 검증 대상입니다.

### 한계 / 후속 과제

- **에러 처리 미구현(범위 외)** — `LoadAsync` 진행 중 provider가 예외를 던지면 대기 중인 호출자가 완료되지 않을 수 있습니다. 에러 전파는 후속 과제입니다.
- **스레드 안전성 없음** — Unity 메인 스레드 사용을 전제로 합니다.
- **기존 서비스 위임** — PoolService/SoundService가 이 로더를 사용하도록 전환하는 작업은 별도 계획으로 진행됩니다.
