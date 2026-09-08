# VContainer → Reflex 마이그레이션 설계

- 상태: 설계 확정
- 작성일: 2026-09-08
- 범위: `Assets/FoundationDI/` 전체(런타임·테스트·샘플), `Assets/Scripts/`(호스트), `Packages/manifest.json`, `Assets/Settings/`, `Assets/Prefabs/`
- 버전: 0.9.7 → 0.10.0 (공개 API 파괴적 변경)
- 대상 Reflex: 14.3.1 — `https://github.com/gustavopsantos/reflex.git?path=/Assets/Reflex/#14.3.1`

---

## 배경 / 목표

FoundationDI는 VContainer를 코어로 삼아 왔다. 그런데 VContainer 타입이 **패키지의 공개 표면까지 새어 나와 있다**:

- `InitializeItem.InitializeAsync(IObjectResolver)` — 사용자가 상속해 구현하는 추상 메서드
- `RegisterXxx(this IContainerBuilder, …)` 확장 11개 — 서비스를 조립하는 유일한 방법
- `PoolManager(IResourceService, IObjectResolver, Transform)` — 공개 생성자

그래서 DI 컨테이너 교체가 "의존성 하나 갈아끼우기"가 아니라 공개 API 변경이 된다. 이번 작업은 컨테이너를 Reflex로 바꾸면서, **같은 일이 다시 일어나지 않도록 리졸버 자리에 패키지 자체 seam을 세우는 것**까지를 목표로 한다.

목표:

- VContainer 의존을 완전히 제거하고 Reflex 14.3.1로 대체한다.
- **런타임 리졸버는 패키지 자체 인터페이스(`IServiceResolver`)로 감싼다.** 공개 시그니처와 테스트에서 DI 프레임워크 타입이 사라진다.
- 마이그레이션 전 구간에서 **컴파일과 전체 테스트가 통과한다**(빅뱅 금지). 근거는 아래 "결정 10".
- Reflex가 이미 하는 일을 우리가 다시 하지 않는다 — 중복 인프라는 포팅하지 않고 삭제한다. 근거는 "결정 3".

비목표:

- **등록(빌더) API의 프레임워크 중립화.** `RegisterXxx`는 Reflex `ContainerBuilder`를 직접 받는다. 근거는 "결정 2".
- **서비스 내부 동작 변경.** MessageService/SoundService/AdService/AnalyticsService/IAPService/TutorialManager의 정책 계층은 전부 생성자 주입 POCO라 **한 줄도 바뀌지 않는다.** 바뀌는 것은 그것들을 등록하는 파일뿐이다.
- **`[SourceGeneratorInjectable]` 도입.** Reflex의 소스 제너레이터 최적화는 이번 범위 밖이다. 근거는 "결정 9".
- **하위 호환 별칭.** `IObjectResolver`를 받는 오버로드를 `[Obsolete]`로 남기지 않는다. 남기려면 VContainer 참조가 남아야 하므로 목적과 모순된다.

---

## Reflex API 사실 확인

설계의 근거가 되는 확인된 동작. 추측이 아니라 14.3.1 문서에서 확인한 내용이다.

- `Reflex.Core.Container`는 **`sealed class`**다(인터페이스 아님). NSubstitute로 모킹할 수 없다.
- 컨테이너는 **자기 자신을 등록**한다 — `MyService(Container c)` 생성자 주입이 성립한다.
- `AttributeInjector.Inject(object, Container)`는 대상이 `IAttributeInjectionContract`를 구현하면 소스 생성 코드로, **아니면 리플렉션으로** 주입한다. 즉 평범한 `[Inject]` 필드/프로퍼티/메서드가 그대로 동작한다.
- `GameObjectInjector.InjectRecursive(go, container)`는 **해당 GameObject와 모든 자손의 전 MonoBehaviour**를 주입한다(비활성 포함).
- `ContainerScope`는 `[DefaultExecutionOrder(-1_000_000_000)]`다. 씬 컨테이너 생성과 **씬 전체 주입이 다른 어떤 `Awake`보다도 먼저** 끝난다.
- 컨테이너는 `Dispose` 시 자신이 만든 `IDisposable` 인스턴스와 자식 컨테이너를 재귀적으로 처분한다. **씬 컨테이너는 씬 언로드 시 Dispose된다.**
- 정적 이벤트 `ContainerScope.OnRootContainerBuilding(ContainerBuilder)` / `OnSceneContainerBuilding(Scene, ContainerBuilder)`로 빌드 직전에 바인딩을 끼워 넣을 수 있다.
- 설정 에셋은 `Resources/ReflexSettings.asset`(`Create > Reflex > Settings`)이며 `RootScopes`는 `List<ContainerScope>`다. `ReflexSettings` 타입 자체는 `internal`이라 우리 코드가 참조할 수 없다 — 에디터에서 만드는 에셋으로만 다룬다.
- Reflex에는 **`IStartable` / `RegisterEntryPoint`에 해당하는 것이 없다.**

---

## 결정 사항과 근거

### 1. 리졸버는 자체 `IServiceResolver`로 감싼다

`Runtime/DI/`를 신설하고 인터페이스와 어댑터를 둔다.

```csharp
// Runtime/DI/IServiceResolver.cs — Reflex를 모른다
public interface IServiceResolver
{
    T Resolve<T>();
    bool TryResolve<T>(out T resolved);
    void Inject(object target);
    void InjectGameObject(GameObject target);
}

// Runtime/DI/ReflexServiceResolver.cs — 유일한 Reflex 접점
internal sealed class ReflexServiceResolver : IServiceResolver
{
    private readonly Container _container;
    public ReflexServiceResolver(Container container) => _container = container;

    public T Resolve<T>() => _container.Resolve<T>();

    public bool TryResolve<T>(out T resolved)
    {
        if (_container.HasBinding<T>())
        {
            resolved = _container.Resolve<T>();
            return true;
        }
        resolved = default;
        return false;
    }

    public void Inject(object target) => AttributeInjector.Inject(target, _container);
    public void InjectGameObject(GameObject target) => GameObjectInjector.InjectRecursive(target, _container);
}
```

이유 세 가지:

1. **`Container`가 sealed다.** 지금 테스트가 `Substitute.For<IObjectResolver>()`를 **13개 파일 50곳**에서 쓴다 — `UINavigatorFlowTests` 13, `InitializeServiceTest` 9, `InjectorServiceTest` 8, `InjectableBehaviourTest` 4, `UINavigatorWithOverlayTests` 3, `PoolManagerTest` 3, `UIButtonTest`/`UIStateButtonTest`/`UIScaleButtonTest` 각 2, 나머지 4개 파일 각 1. `Container`를 그대로 노출하면 이 50곳이 전부 실제 컨테이너를 빌드하는 방식으로 재작성돼야 한다. 인터페이스를 두면 **타입 이름만 치환하면 된다.**
2. **CLAUDE.md의 규약과 일치한다.** "외부 의존은 `IXxxProvider` 같은 인터페이스로 추상화하고 EditMode 테스트는 NSubstitute로 이 seam을 대체한다"가 이 리포지토리의 서비스 작성 규약이다. 리졸버만 예외일 이유가 없다.
3. **이 작업 자체가 근거다.** 프레임워크 타입을 공개 시그니처에 넣은 대가를 지금 치르는 중이다. 같은 실수를 반복하지 않는다.

`Resolve`/`TryResolve`/`Inject`/`InjectGameObject` 넷만 노출한다. 현재 코드가 쓰는 전부이고, `Scope()`/`All<T>()` 같은 것은 필요해질 때 추가한다(YAGNI).

### 2. 등록 API는 Reflex `ContainerBuilder`를 직접 받는다

```csharp
public static ContainerBuilder RegisterMessageService(this ContainerBuilder builder)
    => builder.RegisterType(typeof(MessageService), new[] { typeof(IMessageService) },
                            Lifetime.Singleton, Resolution.Lazy);
```

리졸버와 달리 빌더는 감싸지 않는다.

- 호출부가 **이미 Reflex `IInstaller.InstallBindings(ContainerBuilder)` 안**이다. 래퍼를 씌워도 사용자는 같은 파일에서 Reflex를 본다 — 감춰지는 것이 없다.
- 등록 테스트 10여 개는 진짜 컨테이너를 빌드해 `HasBinding`/`Resolve`로 검증할 뿐 **모킹하지 않는다.** 1번과 달리 테스트 압력이 없다.
- 래핑하면 `Lifetime`/`Resolution` 열거형까지 미러링해야 하고, 사용자는 `IInstaller` 안에서 래퍼를 한 번 더 만들어야 한다. 계단만 늘고 얻는 게 없다.

### 3. `InjectorService` · `InjectableBehaviour` · `RegisterInjector`를 삭제한다

포팅하지 않고 **없앤다.**

`InjectorService`의 존재 이유는 정적 `_pending` 큐다. VContainer에서는 씬에 배치된 컴포넌트의 `Awake`가 컨테이너 생성보다 먼저 돌 수 있어서, 컨테이너가 없으면 자신을 큐에 넣어 두고 `IStartable.Start()`에서 일괄 주입했다.

Reflex에는 그 레이스가 없다. `ContainerScope`가 `-1_000_000_000` 실행 순서로 돌면서 씬 컨테이너를 만들고 `SceneInjector`가 씬 전체를 재귀 주입하는 일이 **모든 `Awake`보다 먼저** 끝난다. 큐를 포팅하면 언제나 비어 있는 큐를 유지하는 코드만 남는다.

**단, `InjectorService`는 일을 두 개 한다.** 하나는 위의 지연이고, 다른 하나는 **주입 실패를 대상 하나에 가두는 것**(`TryInject`의 try/catch)이다. Reflex는 첫 번째만 해결하고 두 번째는 하지 않는다 — 소스로 확인했다:

```csharp
// Reflex GameObjectInjector.InjectRecursiveMany — 컴포넌트별 try/catch가 없다
for (var j = 0; j < monoBehaviours.Count; j++)
    if (monoBehaviour != null)
        AttributeInjector.Inject(monoBehaviour, container);
```

그리고 `FieldInjector.Inject`는 해석 실패를 `FieldInjectorException`으로 다시 던진다. 따라서 **미등록 서비스를 요구하는 컴포넌트 하나가 그 뒤 순번 전체의 씬 주입을 막는다.** 지금은 `InjectorService`가 이것을 잡아 주고 있다.

그래서 삭제와 **함께** `[Inject]` 필드로 서비스를 직접 받던 씬 컴포넌트 넷을 리졸버 방식으로 바꾼다 — `UIButton`이 이미 쓰는 그 패턴이다.

| 컴포넌트 | 지금 | 이후 |
|---|---|---|
| `TutorialTarget` | `[Inject] ITutorialTargetRegistry _registry` | `[Inject] Construct(IServiceResolver)` + `TryResolve` |
| `TutorialSequenceBehaviour` | `[Inject] ITutorialManager _tutorial` | 〃 |
| `MusicZone` | `[Inject] ISoundService _soundService` | 〃 |
| `OutputVolumeSlider` | `[Inject] ISoundService _soundService` | 〃 |

`IServiceResolver`는 결정 4로 항상 등록되므로 이 경로는 **절대 던지지 않는다.** 예외가 없으니 격리 인프라도 필요 없다. 넷 다 이미 널 가드(`_x == null`)를 갖고 있어 미등록 시 동작은 지금과 같다.

연쇄 결과:

- `UIButton.EnsureInjected()`와 `_requested` 플래그가 사라진다. `[Inject] public void Construct(IServiceResolver)` 하나로 끝난다.
- 위 네 컴포넌트의 `EnsureInjected()` 호출과 `InjectableBehaviour` 상속이 사라진다. **주입 지연에 대비한 `Update` 폴링은 남긴다** — 제거는 이번 범위 밖이다.
- 런타임 생성물(PoolManager 인스턴스, UINavigator의 Presenter/View)은 지금처럼 `InjectGameObject`/`Inject`로 명시 주입한다. `PoolManager`는 자체 try/catch가 있어 격리가 유지된다.
- **자가 주입 능력은 사라진다.** `InjectableBehaviour`는 런타임 `Instantiate` 시에도 스스로 주입을 요청했다(`Awake` → `Request(this)` → 컨테이너가 준비돼 있으면 즉시 주입). Reflex에는 대응물이 없다 — `GameObjectSelfInjector`가 있지만 `internal`이라 소비자가 쓸 수 없다. 따라서 **씬에 미리 배치되지 않은 오브젝트는 생성한 쪽이 주입 책임을 진다.** 패키지 안에서는 `PoolManager`와 `UINavigator.CreateRoot`가 그 책임을 지고, 소비자 코드에는 README로 안내한다(공개 자가 주입 컴포넌트를 새로 만드는 것은 이번 범위 밖이다).

**공개 타입 2개가 사라지는 파괴적 변경**이다(`InjectorService`, `InjectableBehaviour`). 0.9.x 단계이고 README에 마이그레이션 안내를 싣는 조건으로 감수한다. 버전을 0.10.0으로 올린다.

부수 효과 하나는 **개선**이다. 지금은 `ContainerScope`(구 `LifetimeScope`) 없는 씬의 컴포넌트가 `_pending`에 쌓인 채 영영 비워지지 않는다 — 조용히 주입 안 된 상태로 산다. 삭제 후에는 그냥 주입되지 않고, `UIButton`처럼 널 가드가 있는 곳은 세션당 한 번 경고한다.

### 4. `IServiceResolver`는 패키지가 자동으로 등록한다

`[Inject] IServiceResolver`가 미등록이면 `AttributeInjector`가 `UnknownContractException`을 던진다. 사용자가 등록을 잊을 수 있는 구조를 만들지 않는다.

```csharp
// Runtime/DI/ServiceResolverBootstrap.cs
internal static class ServiceResolverBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Install()
    {
        ContainerScope.OnRootContainerBuilding += Register;
        ContainerScope.OnSceneContainerBuilding += (_, builder) => Register(builder);
    }

    private static void Register(ContainerBuilder builder)
        => builder.RegisterFactory<IServiceResolver>(
               c => new ReflexServiceResolver(c), Lifetime.Singleton, Resolution.Lazy);
}
```

- **팩토리**여야 한다. 어댑터는 *빌드된* 컨테이너를 필요로 하는데 등록 시점에는 없다. `RegisterFactory`의 콜백은 컨테이너를 받는다.
- **루트와 씬 양쪽**에 건다. 씬 컨테이너에서 해석되는 `IServiceResolver`는 그 씬 컨테이너를 감싸야 한다(부모 것을 물려받으면 씬 스코프 바인딩을 못 본다).
- `SubsystemRegistration`은 Reflex의 `ReflexSettings.InitializeReflex()`와 같은 단계다. 구독은 도메인 리로드마다 새로 걸린다.

### 5. `IStartable`은 `MonoBehaviour.Start()`로 내린다

Reflex에 엔트리포인트가 없다. 현재 `IStartable` 사용처는 셋이다.

- `InjectorService` — 결정 3으로 삭제된다.
- 샘플 5종의 `*Demo : IStartable` — 전부 씬 데모다. `MonoBehaviour`로 바꿔 씬에 놓고 `Start()`에서 같은 일을 한다. 씬 주입이 `Awake` 전에 끝나므로 `Start()`에서 의존성은 이미 채워져 있다.
- 호스트 `TestHubBootstrap : IStartable` — 위와 동일.

생성 시점 보장이 필요한 등록(현재 없음)은 `Resolution.Eager`를 쓴다. `Build()`가 즉시 인스턴스화한다.

### 6. 컴포지션 루트는 `IInstaller` + `ContainerScope`로 옮긴다

| 현재 | 이후 |
|---|---|
| `Assets/Settings/VContainerSettings.asset` | `Assets/Resources/ReflexSettings.asset` |
| `Assets/Prefabs/RootLifetimeScope.prefab` | `Assets/Prefabs/RootScope.prefab` (`ContainerScope` + `RootInstaller`) |
| `RootLifetimeScope : LifetimeScope` | `RootInstaller : MonoBehaviour, IInstaller` |
| `SceneLifetimeScope : LifetimeScope` | 씬의 `ContainerScope` GO + `SceneInstaller : MonoBehaviour, IInstaller` |
| `LifetimeScope.Find<T>().Container.Inject(this)` | `gameObject.scene.GetSceneContainer()` + `AttributeInjector.Inject` |

`VContainerSettings`가 매 Play 세션에 루트 스코프를 자동 생성하던 동작은 `ReflexSettings.RootScopes`가 그대로 대체한다. 루트 스코프 프리팹은 씬에 배치하지 않는다 — 지금과 같다.

**CLAUDE.md의 `RegisterTutorialManager` 주의사항이 없어진다.** "`InjectorService`가 정적 컨테이너 참조 하나를 공유하는 단일 컨테이너 모델이라 씬 스코프에 두면 조용히 실패한다"는 경고는 `InjectorService`가 사라지면서 함께 사라진다. Reflex는 씬 컨테이너가 부모를 제대로 상속하고, 씬 주입도 씬 컨테이너로 수행한다. `RegisterTutorialManager`는 문서대로 **씬 인스톨러**에 두면 된다.

### 7. `WithParameter`가 없으므로 `RegisterPoolManager`는 팩토리로 바뀐다

`RegisterPoolManager(this IContainerBuilder, Transform root = null)`은 VContainer의 `registration.WithParameter(root)`로 생성자 인자 하나만 덮어쓰고 나머지는 컨테이너가 채우게 한다. **Reflex에는 파라미터 오버라이드가 없다.**

```csharp
public static ContainerBuilder RegisterPoolManager(this ContainerBuilder builder, Transform root = null)
    => builder.RegisterFactory<IPoolManager>(
           c => new PoolManager(c.Resolve<IResourceService>(), c.Resolve<IServiceResolver>(), root),
           Lifetime.Singleton, Resolution.Lazy);
```

`root`가 null이어도 같은 경로를 탄다 — `PoolManager`의 생성자가 이미 null을 "부모 없이 생성"으로 처리한다(`PoolManagerTest.부모_Transform이_없으면_풀_루트를_부모없이_생성한다`). 분기가 사라져 오히려 단순해진다.

### 8. 생성자 모호성은 `[ReflexConstructor]`로 표시한다

`UnityAdDispatcher`와 `HapticService`는 생성자가 둘이고, VContainer가 파라미터 많은 쪽을 고르는 것을 `[Inject]`로 막고 있었다. Reflex의 대응물은 `[ReflexConstructor]`다. 의미와 위치가 같아 1:1 치환이다. 대상은 `UnityAdDispatcher`의 `(bool)` 아닌 생성자와 `HapticService`의 무인자 생성자 둘이다.

### 9. `[SourceGeneratorInjectable]`은 도입하지 않는다

Reflex는 `partial class` + `[SourceGeneratorInjectable]`로 리플렉션을 걷어내는 최적화를 제공한다. 이번에는 쓰지 않는다.

- 리플렉션 폴백이 정상 경로다 — 동작에 차이가 없다.
- 도입하면 `[Inject]`를 쓰는 모든 타입이 `partial`이 되어야 하고, 마이그레이션 diff에 성능 최적화가 섞인다. 구조 변경과 행동 변경을 섞지 않는다는 이 리포지토리의 원칙과도 어긋난다.
- 프로파일링 근거 없이 하는 최적화다.

마이그레이션이 끝난 뒤 별도 작업으로 검토한다.

### 10. 두 컨테이너를 공존시킨 뒤 VContainer를 마지막에 뺀다

Unity 프로젝트는 어셈블리 하나가 깨지면 전체가 깨진다. VContainer 참조를 먼저 지우면 100여 개 파일이 동시에 컴파일 에러를 내고, 그 상태에서는 **테스트를 한 줄도 돌릴 수 없다.** RED→GREEN 사이클이 성립하지 않는다.

그래서 Reflex를 **먼저 추가**하고, 전환 기간 동안 `FoundationDI.asmdef`가 둘 다 참조한다. 파일 그룹 단위로 옮기며 매 단계 컴파일·전체 테스트를 통과시키고, **맨 마지막 단계**에서 VContainer 참조와 manifest 항목을 제거한다.

단계는 아래 "마이그레이션 순서"에 있다.

---

## API 대응표

| VContainer | Reflex |
|---|---|
| `IObjectResolver` | `IServiceResolver` (자체 seam) → `Reflex.Core.Container` |
| `IContainerBuilder` | `Reflex.Core.ContainerBuilder` |
| `builder.Register<IFoo, Foo>(Lifetime.Singleton)` | `builder.RegisterType(typeof(Foo), new[]{typeof(IFoo)}, Lifetime.Singleton, Resolution.Lazy)` |
| `builder.Register<Foo>(Lifetime.Singleton).As<IFoo>()` | 위와 동일 |
| `builder.Register<T>(c => …, Lifetime.Singleton)` | `builder.RegisterFactory<T>(c => …, Lifetime.Singleton, Resolution.Lazy)` |
| `builder.RegisterInstance(x)` | `builder.RegisterValue(x)` |
| `builder.RegisterInstance(x).As<IX>()` | `builder.RegisterValue(x, new[]{ typeof(IX) })` |
| `builder.Register<…>(…).WithParameter(x)` | **없음** → `RegisterFactory`로 인자를 손으로 넘긴다 |
| `builder.RegisterEntryPoint<T>()` | 없음 → `MonoBehaviour.Start()` 또는 `Resolution.Eager` |
| `resolver.Resolve<T>()` | `container.Resolve<T>()` |
| `resolver.TryResolve<T>(out var x)` | `container.HasBinding<T>()` + `Resolve<T>()` (어댑터가 흡수) |
| `resolver.Inject(obj)` | `AttributeInjector.Inject(obj, container)` |
| `resolver.InjectGameObject(go)` | `GameObjectInjector.InjectRecursive(go, container)` |
| `LifetimeScope` + `Configure` | `ContainerScope` + `IInstaller.InstallBindings` |
| `LifetimeScope.Find<T>().Container` | `scene.GetSceneContainer()` |
| `[Inject]` 필드 / 메서드 | `[Inject]` (`Reflex.Attributes`) — 동일 |
| `[Inject]` 생성자 | `[ReflexConstructor]` |
| `IStartable` | 없음 (결정 5) |

---

## 파일별 변경

### 신규

- `Runtime/DI/IServiceResolver.cs`
- `Runtime/DI/ReflexServiceResolver.cs`
- `Runtime/DI/ServiceResolverBootstrap.cs`
- `Assets/Scripts/LifetimeScopes/` → `Assets/Scripts/Installers/RootInstaller.cs`, `SceneInstaller.cs`
- `Assets/Resources/ReflexSettings.asset`, `Assets/Prefabs/RootScope.prefab`

### 삭제

- `Runtime/Services/InjectorService/` 전체 (`InjectorService.cs`, `InjectableBehaviour.cs`, `README.md`)
- `Assets/Settings/VContainerSettings.asset`, `Assets/Prefabs/RootLifetimeScope.prefab`
- `Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs`, `SceneLifetimeScope.cs`
- `Tests/InjectorServiceTest.cs`, `Tests/InjectableBehaviourTest.cs`

### 등록 파일만 바뀌는 것 (서비스 본체 무변경)

`MessageServiceRegistration.cs` · `SoundServiceVContainerExtensions.cs` · `AdServiceRegistration.cs` · `AnalyticsServiceRegistration.cs` · `IapServiceRegistration.cs` · `TutorialManagerRegistration.cs` · `HapticService.cs`(확장부) · `InitializeService.cs`(확장부) · `PoolManager.cs`(확장부) · `UINavigator.cs`(확장부)

`*VContainerExtensions` 클래스명은 `*Registration`으로 통일한다(구조 변경, 별도 커밋).

### 시그니처가 바뀌는 것

| 파일 | 변경 |
|---|---|
| `InitializeItem.cs` | `InitializeAsync(IObjectResolver)` → `(IServiceResolver)` — **공개 파괴적** |
| `InitializeService.cs` | 생성자 `IObjectResolver` → `IServiceResolver` |
| `PoolManager.cs` | 생성자 `IObjectResolver` → `IServiceResolver`, `_resolver?.InjectGameObject(go)` 유지 |
| `UIInstanceFactory.cs` | 생성자·`Resolver` 프로퍼티 `IObjectResolver` → `IServiceResolver` |
| `UINavigator.cs:120` | `new PoolManager(_resource, _factory.Resolver, …)` — 타입만 바뀜 |
| `UIButton.cs` | `Construct(IServiceResolver)`, `EnsureInjected`/`_requested` 삭제 |
| `UnityAdDispatcher.cs` · `HapticService.cs` | `[Inject]` 생성자 → `[ReflexConstructor]` |

### `[Inject]` 필드를 리졸버 주입으로 바꾸는 것

`TutorialSequenceBehaviour.cs` · `TutorialTarget.cs` · `MusicZone.cs` · `OutputVolumeSlider.cs` — **넷 다 `InjectableBehaviour`를 상속하고 `[Inject]` 필드로 서비스를 직접 받는다.** `MonoBehaviour`로 내리고, 필드 주입을 `[Inject] Construct(IServiceResolver)` + `TryResolve`로 바꾼다(결정 3의 표). `EnsureInjected()` 호출과 `base.Awake()`를 지운다.

샘플의 `SoundSampleDemo.cs`도 같은 상속을 쓰므로 8단계에서 같이 내린다.

### asmdef

- `Runtime/FoundationDI.asmdef` — VContainer GUID 제거, Reflex 추가
- `Tests/FoundationDI.Tests.asmdef`, `Tests/Editor/…`, `Tests/Runtime/…` — `"VContainer"` → `"Reflex"`

### 호스트 / 샘플

- `Assets/Scripts/` — 인스톨러 2개 신규, 스모크 테스트 3개(`scene.GetSceneContainer()`), UI 프레젠터 3개(`using` 교체), `TestHubBootstrap`(→ `MonoBehaviour`)
- `Samples~/` — `SampleLifetimeScope` → `SampleInstaller`, 각 샘플 `*Scope.cs`(5) / `*Presenters.cs`(5), `*Demo : IStartable` → `MonoBehaviour`

### 문서

`CLAUDE.md`(결정 6의 `RegisterTutorialManager` 경고 삭제 포함), 루트 `README.md`, 서비스별 README 12개, `plan.md`, `package.json`(0.10.0)

---

## 마이그레이션 순서

각 단계 끝에서 **컴파일 통과 + 전체 테스트 통과**가 조건이다.

1. **Reflex 설치.** `manifest.json`에 추가. `FoundationDI.asmdef`·테스트 asmdef 3개가 VContainer와 Reflex를 **둘 다** 참조. 이 단계에서는 코드 변경 없음. (STRUCTURAL)
2. **`Runtime/DI/` seam 추가.** `IServiceResolver` + `ReflexServiceResolver` + 부트스트랩. 아직 아무도 안 쓴다. 어댑터 단위 테스트를 먼저 쓴다(RED→GREEN). (BEHAVIORAL)
3. **`ReflexSettings` + `RootScope.prefab` + 인스톨러 2개 생성.** 구 `RootLifetimeScope`와 **병존**시키되 씬에서는 아직 안 쓴다. (STRUCTURAL)
4. **등록 확장 11개를 Reflex `ContainerBuilder`로 전환**하고 등록 테스트를 함께 옮긴다. 서비스별로 나눠 진행. (BEHAVIORAL)
5. **리졸버 소비자 전환** — `InitializeService`/`InitializeItem`, `PoolManager`, `UIInstanceFactory`, `UIButton`. 테스트의 `Substitute.For<IObjectResolver>()`를 `IServiceResolver`로 치환. (BEHAVIORAL)
6. **`[Inject]` 컴포넌트 4개 + `[ReflexConstructor]` 2개.** (BEHAVIORAL)
7. **`InjectorService` / `InjectableBehaviour` 삭제**와 해당 테스트 2개 삭제. (BEHAVIORAL — 동작이 사라지므로)
8. **호스트·샘플 전환**, 씬에 `ContainerScope` 배치, 구 프리팹/에셋 삭제. (BEHAVIORAL)
9. **VContainer 제거** — asmdef 참조 4곳, `manifest.json`, `packages-lock.json`. (STRUCTURAL)
10. **문서 갱신 + 0.10.0.** (STRUCTURAL)

`plan.md`에는 각 단계의 테스트 항목을 `[ ]`로 풀어 쓴다.

---

## 테스트 계획

### 신규

- `ServiceResolverTest` (EditMode) — 어댑터가 `Resolve`/`TryResolve`(등록·미등록 양쪽)/`Inject`/`InjectGameObject`를 Reflex 컨테이너에 올바로 위임한다
- `ServiceResolverBootstrapTest` (PlayMode) — 루트·씬 컨테이너 양쪽에서 `IServiceResolver`가 해석되고, **씬 컨테이너의 것이 씬 스코프 바인딩을 본다**

### 삭제

- `InjectorServiceTest` (6 케이스), `InjectableBehaviourTest` — 대상이 사라진다

### 타입 치환만 (동작 검증 그대로)

`Substitute.For<IObjectResolver>()` → `Substitute.For<IServiceResolver>()`. 삭제되는 두 파일을 뺀 **11개 파일 38곳**: `UINavigatorFlowTests`(13) · `InitializeServiceTest`(9) · `UINavigatorWithOverlayTests`(3) · `PoolManagerTest`(3) · `UIButtonTest`(2) · `UIStateButtonTest`(2) · `UIScaleButtonTest`(2) · `UINavigatorViewInjectionTests` · `UINavigatorSceneLifetimeTests` · `UINavigatorRootPrefabTests` · `UIInstanceFactoryTests`(각 1)

단, 버튼 테스트 3종의 `new InjectorService(Substitute.For<IObjectResolver>()).Dispose()`(정적 상태 리셋용 셋업, 6곳)는 **치환이 아니라 삭제**한다 — 7단계에서 `InjectorService`가 사라지면 리셋할 정적 상태가 없다.

### 컨테이너 빌드 방식 변경

`MessageServiceTest` · `HapticServiceTest` · `AdServiceRegistrationTest` · `AnalyticsServiceRegistrationTest` · `AnalyticsProviderSettingsTest` · `IapServiceRegistrationTest` · `TutorialManagerRegistrationTest` · `InitializeServiceTest` · `Editor/UINavigator/DIRegistrationTests` · `Editor/UINavigator/UIInstanceFactoryTests` — `new ContainerBuilder()…Build()`를 Reflex 것으로, 단언은 `HasBinding`/`Resolve`로

### PlayMode

`Tests/Runtime/`의 UINavigator 5종과 `TutorialManagerSmokeTests`는 씬에 `LifetimeScope`를 세우는 셋업이 `ContainerScope` + 인스톨러로 바뀐다. **검증 내용은 그대로다.**

---

## 커밋 분할

STRUCTURAL과 BEHAVIORAL을 섞지 않는다. 위 마이그레이션 순서의 각 단계가 최소 한 커밋이며, 4·5단계는 서비스/소비자 단위로 더 쪼갠다. 클래스 개명(`*VContainerExtensions` → `*Registration`)은 내용 변경과 분리해 STRUCTURAL 단독 커밋으로 낸다.

---

## 마이그레이션 안내 (README에 실을 내용)

0.9.x → 0.10.0 사용자가 해야 할 일:

1. `manifest.json`에서 VContainer를 빼고 Reflex 14.3.1을 넣는다.
2. `Resources/ReflexSettings.asset`을 만들고 루트 스코프 프리팹을 `RootScopes`에 등록한다.
3. `LifetimeScope` 서브클래스를 `MonoBehaviour, IInstaller`로 바꾸고 `Configure` → `InstallBindings`.
4. 씬 스코프가 있던 자리에 `ContainerScope` 컴포넌트를 놓는다.
5. `InjectableBehaviour` 상속을 `MonoBehaviour`로 내린다. `[Inject]` 필드는 그대로 두면 된다 — Reflex가 씬 로드 시 자동 주입한다.
6. `InitializeItem` 서브클래스의 `InitializeAsync(IObjectResolver)`를 `(IServiceResolver)`로 바꾼다.
7. `using VContainer` → `using Reflex.Attributes`(속성) / `using Reflex.Core`(빌더).
8. `builder.RegisterInjector()` 호출을 지운다.

---

## 리스크

- **Reflex 14.3.1은 Unity 2021+ 지원을 표방한다.** 이 프로젝트는 6000.3.17f1이다. 1단계에서 설치 직후 컴파일과 기존 테스트를 돌려 확인한다. 여기서 막히면 이후 단계가 의미 없으므로 **1단계가 사실상 게이트**다.
- **씬 자동 주입 범위.** Reflex는 `ContainerScope`가 있는 씬만 주입한다. 호스트 씬에 배치하는 것을 8단계에서 빠뜨리면 `[Inject]` 필드가 조용히 null이 된다. PlayMode 테스트가 이를 잡는다.
- **`DontDestroyOnLoad` 오브젝트.** 씬 소속이 아니므로 자동 주입 대상이 아니다. 현재 그런 대상은 `[AdService] Runner`(`HideAndDontSave`)뿐이고 주입을 받지 않으므로 영향 없다.
- **씬 주입에는 컴포넌트별 격리가 없다.** `AttributeInjector.Inject`가 던지면 그 뒤 컴포넌트가 전부 주입되지 않는다. 결정 3으로 `[Inject]` 서비스 필드를 전부 없앴으므로 패키지 안에는 던질 경로가 남지 않지만, **이 패키지를 쓰는 프로젝트가 자기 컴포넌트에 `[Inject]` 필드를 쓰면 같은 함정에 빠진다.** README에 명시한다. 다만 `UIButton`이 서비스를 직접 `[Inject]` 필드로 받지 않는 현재 설계는 **그대로 유지해야 한다**(README의 근거 문단도 Reflex 기준으로 다시 쓴다).
- **`link.xml` / `SdkDefineTable`은 영향 없다.** 어댑터 보존은 SDK 어셈블리 기준이고 DI와 무관하다.

---

## 덜어낸 것

- `IObjectResolver` 호환 오버로드 — VContainer 참조가 남아야 하므로 목적과 모순
- 빌더 래퍼 `IServiceRegistry` (결정 2)
- 소스 제너레이터 최적화 (결정 9)
- `InjectorService`의 호환 껍데기 (결정 3)
- `IServiceResolver`의 `Scope()`/`All<T>()`/`Single<T>()` — 현재 사용처 없음
