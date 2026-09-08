# VContainer → Reflex 마이그레이션 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** FoundationDI의 DI 컨테이너를 VContainer에서 Reflex 14.3.1로 교체하고, 그 과정에서 리졸버를 패키지 자체 인터페이스(`IServiceResolver`)로 감싸 공개 API에서 DI 프레임워크 타입을 없앤다.

**Architecture:** 리졸버는 `IServiceResolver` 인터페이스 + `ReflexServiceResolver` 어댑터로 감싼다(테스트 50곳의 NSubstitute 모킹이 그대로 살고, `Container`가 sealed인 문제를 우회한다). 등록 확장 11개는 Reflex `ContainerBuilder`를 직접 받는다(호출부가 이미 `IInstaller` 안이라 감출 게 없다). Reflex가 `ContainerScope`(실행 순서 -1e9)로 씬 전체를 `Awake` 전에 주입하므로 `InjectorService`/`InjectableBehaviour`는 포팅하지 않고 삭제한다. 두 컨테이너를 공존시킨 채 그룹 단위로 옮기고 **마지막 Task에서** VContainer를 뺀다 — 전 구간에서 컴파일과 테스트가 통과한다.

**Tech Stack:** Unity 6000.3.17f1, Reflex 14.3.1, NUnit + NSubstitute 5.3.0, UnityMCP(컴파일/테스트 실행), Addressables, TMP

**Spec:** `docs/superpowers/specs/2026-09-08-vcontainer-to-reflex-design.md`

## Global Constraints

- 네임스페이스는 `DarkNaku.FoundationDI` 단일. 새 파일도 이것을 따른다.
- 테스트 이름은 **한국어 서술형**이다 — 기존 스타일 그대로(`Get은_프리팹_로드를_ResourceService에_위임한다`). `should~`를 영어로 쓰지 않는다.
- **구조적 변경(STRUCTURAL)과 행동적 변경(BEHAVIORAL)을 같은 커밋에 섞지 않는다.** 커밋 제목에 접두어를 단다.
- 브랜치는 `feature/reflex-migration`. 이미 생성돼 있다. worktree는 쓰지 않는다(UnityMCP가 단일 에디터 인스턴스에 붙는다).
- **모든 컴파일·테스트는 UnityMCP로 수행한다.** Unity Editor가 떠 있고 `http://127.0.0.1:8086/mcp`에 연결돼 있어야 한다. CLI 빌드 명령은 없다.
- 스크립트 수정 후에는 `read_console`로 컴파일 에러를 먼저 확인한다. `editor_state.isCompiling == false`가 되어야 새 타입을 쓸 수 있다.
- async 테스트는 `AwaitableTest`(`Tests/Support/`)를 쓴다. EditMode에서 `Awaitable.NextFrameAsync()`/`WaitForSecondsAsync()`는 완료되지 않으므로 이 헬퍼를 우회하지 않는다.
- Reflex UPM URL은 정확히 `https://github.com/gustavopsantos/reflex.git?path=/Assets/Reflex/#14.3.1`.
- 패키지 버전 최종값: `0.9.7` → `0.10.0` (`Assets/FoundationDI/package.json`).
- Task 완료 조건은 매번 동일하다: **컴파일 에러 0 + `FoundationDI.Tests` 전체 통과**.

---

## File Structure

**신규**

| 파일 | 책임 |
|---|---|
| `Runtime/DI/IServiceResolver.cs` | 리졸버 seam. Reflex를 모른다. 공개. |
| `Runtime/DI/ReflexServiceResolver.cs` | 유일한 Reflex 접점. `internal sealed`. |
| `Runtime/DI/ServiceResolverBootstrap.cs` | 루트·씬 컨테이너에 `IServiceResolver`를 자동 등록. `internal static`. |
| `Tests/ServiceResolverTest.cs` | 어댑터 위임 검증(EditMode). |
| `Tests/Runtime/ServiceResolverBootstrapTests.cs` | 씬/루트 양쪽 해석 검증(PlayMode). |
| `Assets/Scripts/Installers/RootInstaller.cs` | 호스트 앱 수명 등록. `RootLifetimeScope` 대체. |
| `Assets/Scripts/Installers/SceneInstaller.cs` | 호스트 씬 수명 등록. `SceneLifetimeScope` 대체. |
| `Assets/Resources/ReflexSettings.asset` | Reflex 설정. `RootScopes`에 루트 프리팹. |
| `Assets/Prefabs/RootScope.prefab` | `ContainerScope` + `RootInstaller`. |

**삭제**

`Runtime/Services/InjectorService/`(3파일) · `Tests/InjectorServiceTest.cs` · `Tests/InjectableBehaviourTest.cs` · `Assets/Settings/VContainerSettings.asset` · `Assets/Prefabs/RootLifetimeScope.prefab` · `Assets/Scripts/LifetimeScopes/`(2파일)

**개명(구조 변경, 내용과 분리)**

`SoundServiceVContainerExtensions.cs` → `SoundServiceRegistration.cs`, 그리고 `HapticServiceVContainerExtensions`/`InitializeServiceVContainerExtensions`/`PoolManagerVContainerExtensions`/`UINavigatorVContainerExtensions`/`InjectorVContainerExtensions` 클래스명 → `*Registration`.

---

### Task 1: Reflex 설치 게이트

Reflex 14.3.1의 공식 지원은 Unity 2021+다. 이 프로젝트는 6000.3.17f1이다. **여기서 막히면 이후 Task가 전부 무의미하므로 첫 Task로 둔다.** 코드는 한 줄도 바꾸지 않는다.

**Files:**
- Modify: `Packages/manifest.json`
- Modify: `Assets/FoundationDI/Runtime/FoundationDI.asmdef`
- Modify: `Assets/FoundationDI/Tests/FoundationDI.Tests.asmdef`
- Modify: `Assets/FoundationDI/Tests/Editor/FoundationDI.Tests.Editor.asmdef`
- Modify: `Assets/FoundationDI/Tests/Runtime/FoundationDI.Tests.Runtime.asmdef`

**Interfaces:**
- Consumes: 없음
- Produces: `Reflex` 어셈블리가 `FoundationDI`와 테스트 3종에서 참조 가능해진다. VContainer 참조는 **그대로 남는다**(공존).

- [ ] **Step 1: manifest에 Reflex를 추가한다**

`Packages/manifest.json`의 `dependencies`에 한 줄 넣는다. VContainer 줄은 **지우지 않는다**.

```json
"com.gustavopsantos.reflex": "https://github.com/gustavopsantos/reflex.git?path=/Assets/Reflex/#14.3.1",
```

- [ ] **Step 2: Unity가 패키지를 받아 컴파일할 때까지 기다린 뒤 콘솔을 본다**

UnityMCP `refresh_unity` → `read_console`.
기대: 컴파일 에러 0. `editor_state.isCompiling == false`.

**여기서 에러가 나면 즉시 멈추고 보고한다.** Unity 6000.3에서 Reflex가 컴파일되지 않으면 이 계획 전체를 재검토해야 한다 — 다른 컨테이너를 고르거나 자체 구현으로 방향을 튼다.

- [ ] **Step 3: Reflex 어셈블리 이름과 GUID를 확인한다**

```bash
find Library/PackageCache -name "*.asmdef" -path "*eflex*" | head
grep -h '"name"' $(find Library/PackageCache -name "Reflex*.asmdef" -path "*eflex*")
```

기대: `Reflex` (그리고 `Reflex.Editor` 등). 실제 이름을 다음 Step에 쓴다.

- [ ] **Step 4: asmdef 4개에 Reflex 참조를 추가한다**

`Runtime/FoundationDI.asmdef`의 `references` 배열 끝에 Step 3에서 확인한 이름(또는 `GUID:...`)을 추가한다. VContainer GUID `b0214a6008ed146ff8f122a6a9c2f6cc`는 **남겨 둔다**.

테스트 asmdef 3개(`FoundationDI.Tests`, `FoundationDI.Tests.Editor`, `FoundationDI.Tests.Runtime`)의 `references`에 `"Reflex"`를 추가한다. 기존 `"VContainer"`는 **남겨 둔다**.

- [ ] **Step 5: 컴파일과 전체 테스트를 확인한다**

UnityMCP `read_console` → 에러 0.
UnityMCP `run_tests` (EditMode, `FoundationDI.Tests`) → **전부 통과**(이 시점 코드 변경이 없으므로 기준선이다).

- [ ] **Step 6: 커밋한다**

```bash
git add Packages/manifest.json Packages/packages-lock.json \
        Assets/FoundationDI/Runtime/FoundationDI.asmdef \
        Assets/FoundationDI/Tests/FoundationDI.Tests.asmdef \
        Assets/FoundationDI/Tests/Editor/FoundationDI.Tests.Editor.asmdef \
        Assets/FoundationDI/Tests/Runtime/FoundationDI.Tests.Runtime.asmdef
git commit -m "[STRUCTURAL] Reflex 14.3.1을 VContainer와 나란히 설치한다"
```

---

### Task 2: `IServiceResolver` seam과 어댑터

**Files:**
- Create: `Assets/FoundationDI/Runtime/DI/IServiceResolver.cs`
- Create: `Assets/FoundationDI/Runtime/DI/ReflexServiceResolver.cs`
- Create: `Assets/FoundationDI/Runtime/DI/ServiceResolverBootstrap.cs`
- Test: `Assets/FoundationDI/Tests/ServiceResolverTest.cs`

**Interfaces:**
- Consumes: Task 1의 Reflex 참조
- Produces:
  - `public interface IServiceResolver { T Resolve<T>(); bool TryResolve<T>(out T resolved); void Inject(object target); void InjectGameObject(GameObject target); }`
  - `internal sealed class ReflexServiceResolver : IServiceResolver`, 생성자 `ReflexServiceResolver(Reflex.Core.Container container)`
  - `IServiceResolver`가 루트·씬 컨테이너 양쪽에 자동 등록된다 — 이후 모든 Task가 `[Inject] IServiceResolver`와 `c.Resolve<IServiceResolver>()`를 전제로 한다.

> **선행조건(실행 중 발견):** `ContainerBuilder.Build()`는 `ReflexLogger`의 정적 초기화를 건드리고, 거기서 `ReflexSettings.Instance`를 `Assert.IsNotNull`로 단언한다. 즉 **Reflex 컨테이너를 만드는 모든 테스트가 `Assets/Resources/ReflexSettings.asset`을 요구한다.** 원래 Task 10에 있던 이 에셋 생성을 여기로 앞당긴다. `RootScopes`는 비워 두고 `LogLevel`은 `Warning`(=2)으로 둬서 테스트 로그를 오염시키지 않는다.
>
> 또한 NSubstitute 대역을 세울 인터페이스는 **`private` 중첩이면 프록시 생성이 실패한다**(`Can not create proxy for type ... because it is not accessible`). 테스트용 중첩 타입은 `public`으로 둔다.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/ServiceResolverTest.cs`:

```csharp
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using Reflex.Core;
using Reflex.Enums;
using UnityEngine;

public class ServiceResolverTest
{
    private interface IThing { }
    private sealed class Thing : IThing { }

    private sealed class Target
    {
        [Reflex.Attributes.Inject] public IThing Injected;
    }

    private sealed class TargetBehaviour : MonoBehaviour
    {
        [Reflex.Attributes.Inject] public IThing Injected;
    }

    private static Container BuildWithThing(IThing thing)
    {
        return new ContainerBuilder()
            .RegisterValue(thing, new[] { typeof(IThing) })
            .Build();
    }

    [Test]
    public void Resolve는_컨테이너의_인스턴스를_그대로_돌려준다()
    {
        var thing = new Thing();
        using var container = BuildWithThing(thing);

        var resolver = new ReflexServiceResolver(container);

        Assert.AreSame(thing, resolver.Resolve<IThing>());
    }

    [Test]
    public void TryResolve는_등록된_계약에_true와_인스턴스를_돌려준다()
    {
        var thing = new Thing();
        using var container = BuildWithThing(thing);

        var resolver = new ReflexServiceResolver(container);

        Assert.IsTrue(resolver.TryResolve<IThing>(out var resolved));
        Assert.AreSame(thing, resolved);
    }

    [Test]
    public void TryResolve는_미등록_계약에_false와_기본값을_돌려주고_예외를_던지지_않는다()
    {
        using var container = new ContainerBuilder().Build();

        var resolver = new ReflexServiceResolver(container);

        Assert.IsFalse(resolver.TryResolve<IThing>(out var resolved));
        Assert.IsNull(resolved);
    }

    [Test]
    public void Inject는_대상의_Inject_필드를_채운다()
    {
        var thing = new Thing();
        using var container = BuildWithThing(thing);
        var target = new Target();

        new ReflexServiceResolver(container).Inject(target);

        Assert.AreSame(thing, target.Injected);
    }

    [Test]
    public void InjectGameObject는_자손_컴포넌트까지_주입한다()
    {
        var thing = new Thing();
        using var container = BuildWithThing(thing);

        var root = new GameObject("root");
        var child = new GameObject("child");
        child.transform.SetParent(root.transform);
        var behaviour = child.AddComponent<TargetBehaviour>();

        try
        {
            new ReflexServiceResolver(container).InjectGameObject(root);

            Assert.AreSame(thing, behaviour.Injected);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void 인터페이스로만_다뤄도_동작한다()
    {
        var thing = new Thing();
        using var container = BuildWithThing(thing);

        IServiceResolver resolver = new ReflexServiceResolver(container);

        Assert.AreSame(thing, resolver.Resolve<IThing>());
    }

    [Test]
    public void 테스트에서_IServiceResolver를_대역으로_세울_수_있다()
    {
        // 기존 테스트 38곳이 Substitute.For<IObjectResolver>()에서 넘어올 자리다.
        var resolver = Substitute.For<IServiceResolver>();
        var thing = new Thing();
        resolver.Resolve<IThing>().Returns(thing);

        Assert.AreSame(thing, resolver.Resolve<IThing>());
    }
}
```

- [ ] **Step 2: 컴파일 실패를 확인한다**

UnityMCP `read_console`.
기대: `IServiceResolver` / `ReflexServiceResolver` 를 찾을 수 없다는 CS0246.

- [ ] **Step 3: `IServiceResolver`를 만든다**

`Assets/FoundationDI/Runtime/DI/IServiceResolver.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 런타임 의존성 해석과 주입의 seam. DI 프레임워크 타입을 패키지 공개 표면에서 가린다.
    /// 구현은 <see cref="ReflexServiceResolver"/> 하나뿐이며, 테스트는 이 인터페이스를 대역으로 세운다.
    /// </summary>
    public interface IServiceResolver
    {
        /// <summary>등록된 계약을 해석한다. 미등록이면 예외를 던진다.</summary>
        T Resolve<T>();

        /// <summary>등록돼 있을 때만 해석한다. 선택적 의존에 쓴다.</summary>
        bool TryResolve<T>(out T resolved);

        /// <summary>대상 객체의 [Inject] 멤버를 채운다. MonoBehaviour가 아니어도 된다.</summary>
        void Inject(object target);

        /// <summary>GameObject와 모든 자손의 MonoBehaviour를 주입한다(비활성 포함).</summary>
        void InjectGameObject(GameObject target);
    }
}
```

- [ ] **Step 4: `ReflexServiceResolver`를 만든다**

`Assets/FoundationDI/Runtime/DI/ReflexServiceResolver.cs`:

```csharp
using Reflex.Core;
using Reflex.Injectors;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 패키지 안에서 Reflex를 아는 유일한 타입. 컨테이너 교체 비용을 이 파일 하나로 묶어 둔다.
    /// </summary>
    internal sealed class ReflexServiceResolver : IServiceResolver
    {
        private readonly Container _container;

        public ReflexServiceResolver(Container container)
        {
            _container = container;
        }

        public T Resolve<T>() => _container.Resolve<T>();

        public bool TryResolve<T>(out T resolved)
        {
            // Reflex는 미등록 계약에 UnknownContractException을 던진다.
            // 선택적 의존을 예외 없이 다루려면 먼저 물어봐야 한다.
            if (_container.HasBinding<T>())
            {
                resolved = _container.Resolve<T>();
                return true;
            }

            resolved = default;
            return false;
        }

        public void Inject(object target) => AttributeInjector.Inject(target, _container);

        public void InjectGameObject(GameObject target)
            => GameObjectInjector.InjectRecursive(target, _container);
    }
}
```

> `ReflexServiceResolver`가 `internal`이라 테스트에서 직접 `new` 할 수 있어야 한다. `Tests/FoundationDI.Tests.asmdef`는 `FoundationDI`를 참조하지만 `internal` 접근에는 `InternalsVisibleTo`가 필요하다. `Assets/FoundationDI/Runtime/AssemblyInfo.cs`에 이미 있는지 확인하고, 없으면 다음 Step에서 추가한다.

- [ ] **Step 5: `InternalsVisibleTo`를 확인하고 없으면 추가한다**

```bash
grep -rn "InternalsVisibleTo" Assets/FoundationDI/Runtime/
```

`FoundationDI.Tests`가 없으면 `Assets/FoundationDI/Runtime/AssemblyInfo.cs`에 추가한다(파일이 없으면 만든다):

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FoundationDI.Tests")]
[assembly: InternalsVisibleTo("FoundationDI.Tests.Editor")]
[assembly: InternalsVisibleTo("FoundationDI.Tests.Runtime")]
```

- [ ] **Step 6: 컴파일과 테스트를 확인한다**

UnityMCP `read_console` → 에러 0.
UnityMCP `run_tests` (EditMode, 필터 `ServiceResolverTest`) → 7개 전부 PASS.

- [ ] **Step 7: 커밋한다**

```bash
git add Assets/FoundationDI/Runtime/DI Assets/FoundationDI/Tests/ServiceResolverTest.cs \
        Assets/FoundationDI/Runtime/AssemblyInfo.cs
git commit -m "[BEHAVIORAL] 리졸버 seam IServiceResolver와 Reflex 어댑터를 추가한다"
```

---

### Task 3: `IServiceResolver` 자동 등록 부트스트랩

`[Inject] IServiceResolver`가 미등록이면 `AttributeInjector`가 `UnknownContractException`을 던진다. 사용자가 등록을 잊을 수 있는 구조를 만들지 않는다.

**Files:**
- Create: `Assets/FoundationDI/Runtime/DI/ServiceResolverBootstrap.cs`
- Test: `Assets/FoundationDI/Tests/Runtime/ServiceResolverBootstrapTests.cs`

**Interfaces:**
- Consumes: Task 2의 `IServiceResolver`, `ReflexServiceResolver`
- Produces: `internal static class ServiceResolverBootstrap`에 `internal static void Register(ContainerBuilder builder)` — 테스트가 직접 부를 수 있도록 `internal`로 연다. 이후 모든 Task가 `IServiceResolver` 등록을 전제한다.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/Runtime/ServiceResolverBootstrapTests.cs`:

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;
using Reflex.Core;

public class ServiceResolverBootstrapTests
{
    private interface IScopedThing { }
    private sealed class ScopedThing : IScopedThing { }

    [Test]
    public void 부트스트랩을_건_컨테이너는_IServiceResolver를_해석한다()
    {
        var builder = new ContainerBuilder();
        ServiceResolverBootstrap.Register(builder);

        using var container = builder.Build();

        Assert.IsNotNull(container.Resolve<IServiceResolver>());
    }

    [Test]
    public void 해석된_리졸버는_자기_컨테이너를_감싼다()
    {
        var thing = new ScopedThing();
        var builder = new ContainerBuilder();
        ServiceResolverBootstrap.Register(builder);
        builder.RegisterValue(thing, new[] { typeof(IScopedThing) });

        using var container = builder.Build();
        var resolver = container.Resolve<IServiceResolver>();

        // 부모 컨테이너의 리졸버를 물려받으면 이 바인딩을 못 본다.
        Assert.IsTrue(resolver.TryResolve<IScopedThing>(out var resolved));
        Assert.AreSame(thing, resolved);
    }

    [Test]
    public void 같은_컨테이너에서_두_번_해석해도_같은_인스턴스다()
    {
        var builder = new ContainerBuilder();
        ServiceResolverBootstrap.Register(builder);

        using var container = builder.Build();

        Assert.AreSame(container.Resolve<IServiceResolver>(), container.Resolve<IServiceResolver>());
    }
}
```

- [ ] **Step 2: 컴파일 실패를 확인한다**

UnityMCP `read_console`.
기대: `ServiceResolverBootstrap`을 찾을 수 없다는 CS0103/CS0246.

- [ ] **Step 3: `ServiceResolverBootstrap`을 만든다**

`Assets/FoundationDI/Runtime/DI/ServiceResolverBootstrap.cs`:

```csharp
using Reflex.Core;
using Reflex.Enums;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// IServiceResolver를 루트·씬 컨테이너 양쪽에 자동 등록한다.
    /// 사용자가 등록을 잊으면 [Inject] IServiceResolver가 UnknownContractException으로 터지므로,
    /// 잊을 수 있는 구조를 아예 만들지 않는다.
    /// </summary>
    internal static class ServiceResolverBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            ContainerScope.OnRootContainerBuilding += Register;
            ContainerScope.OnSceneContainerBuilding += (_, builder) => Register(builder);
        }

        /// <summary>테스트가 직접 부를 수 있도록 internal로 연다.</summary>
        internal static void Register(ContainerBuilder builder)
        {
            // 팩토리여야 한다 — 어댑터는 '빌드된' 컨테이너를 필요로 하는데
            // 등록 시점에는 아직 없다. RegisterFactory의 콜백이 그것을 넘겨 준다.
            //
            // 씬 컨테이너에도 거는 이유: 부모(루트)의 리졸버를 물려받으면
            // 씬 스코프에만 있는 바인딩을 해석하지 못한다.
            builder.RegisterFactory<IServiceResolver>(
                c => new ReflexServiceResolver(c), Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
```

- [ ] **Step 4: 컴파일과 테스트를 확인한다**

UnityMCP `read_console` → 에러 0.
UnityMCP `run_tests` (EditMode, 필터 `ServiceResolverBootstrapTests`) → 3개 PASS.

> `ContainerScope.OnRootContainerBuilding`의 정확한 시그니처가 `Action<ContainerBuilder>`인지 확인한다. 다르면 람다를 맞춘다 — 스펙의 "Reflex API 사실 확인" 절이 근거다.

- [ ] **Step 5: 커밋한다**

```bash
git add Assets/FoundationDI/Runtime/DI/ServiceResolverBootstrap.cs \
        Assets/FoundationDI/Tests/Runtime/ServiceResolverBootstrapTests.cs
git commit -m "[BEHAVIORAL] IServiceResolver를 루트/씬 컨테이너에 자동 등록한다"
```

---

### Task 4: 등록 확장 — 의존 없는 서비스 4개

`MessageService` · `SoundService` · `HapticService` · `InitializeService`. 서비스 본체는 건드리지 않는다.

**Files:**
- Modify: `Runtime/Services/MessageService/MessageServiceRegistration.cs`
- Modify: `Runtime/Services/SoundService/SoundServiceVContainerExtensions.cs`
- Modify: `Runtime/Services/HapticService/HapticService.cs:124-131`(확장부만)
- Modify: `Runtime/Services/InitializeService/InitializeService.cs:46-56`(확장부만)
- Test: `Tests/MessageServiceTest.cs:221-224`, `Tests/HapticServiceTest.cs:201-203`

**Interfaces:**
- Consumes: Task 1 Reflex 참조
- Produces:
  - `RegisterMessageService(this ContainerBuilder) → ContainerBuilder`
  - `RegisterSoundService(this ContainerBuilder, SoundServiceSettings) → ContainerBuilder`
  - `RegisterHapticService(this ContainerBuilder) → ContainerBuilder`
  - `RegisterInitializeService(this ContainerBuilder) → ContainerBuilder`
  - 넷 다 `ContainerBuilder`를 돌려주어 체이닝 가능. (기존에 `void`였던 셋도 반환형을 맞춘다.)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Tests/MessageServiceTest.cs`의 기존 등록 테스트를 Reflex로 옮긴다. **테스트 이름과 단언은 그대로 둔다** — 이것은 포팅이지 재작성이 아니다. `using VContainer;` → `using Reflex.Core;`만 바꾸면 `new ContainerBuilder()`/`Build()`/`Resolve<T>()`가 그대로 컴파일된다:

```csharp
    [Test]
    public void RegisterMessageService로_등록하면_IMessageService를_싱글턴으로_해석할_수_있다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterMessageService();

        var container = builder.Build();

        var service = container.Resolve<IMessageService>();

        Assert.IsNotNull(service);
        Assert.IsInstanceOf<MessageService>(service);
        Assert.AreSame(service, container.Resolve<IMessageService>());

        Assert.DoesNotThrow(() => container.Dispose());
    }
```

그리고 `SoundService`의 다중 계약 단일 인스턴스를 검증하는 테스트를 새로 넣는다.

`Tests/SoundServiceRegistrationTest.cs`를 새로 만든다:

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;
using Reflex.Core;
using UnityEngine;

public class SoundServiceRegistrationTest
{
    private SoundServiceSettings _settings;

    [SetUp]
    public void SetUp() => _settings = ScriptableObject.CreateInstance<SoundServiceSettings>();

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_settings);

    [Test]
    public void ISoundService와_ISoundEngine은_같은_인스턴스를_돌려준다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterSoundService(_settings);

        using var container = builder.Build();

        // 빌더 API와 내부 seam이 같은 서비스를 봐야 한다.
        // 계약마다 별도 인스턴스가 생기면 재생 상태가 둘로 갈린다.
        Assert.AreSame(container.Resolve<ISoundService>(), container.Resolve<ISoundEngine>());
    }

    [Test]
    public void 설정_에셋이_그대로_해석된다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterSoundService(_settings);

        using var container = builder.Build();

        Assert.AreSame(_settings, container.Resolve<SoundServiceSettings>());
    }

    [Test]
    public void 볼륨_저장소는_PlayerPrefs_기본_구현으로_등록된다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterSoundService(_settings);

        using var container = builder.Build();

        Assert.IsInstanceOf<PlayerPrefsVolumeStorage>(container.Resolve<ISoundVolumeStorage>());
    }
}
```

- [ ] **Step 2: 컴파일 실패를 확인한다**

UnityMCP `read_console`.
기대: `RegisterMessageService`/`RegisterSoundService`가 `Reflex.Core.ContainerBuilder`에 대한 확장이 아니라는 CS1929/CS1061.

- [ ] **Step 3: `MessageServiceRegistration`을 옮긴다**

```csharp
using Reflex.Core;
using Reflex.Enums;

namespace DarkNaku.FoundationDI
{
    public static class MessageServiceRegistration
    {
        // 루트 IInstaller의 InstallBindings에서 호출한다.
        //   builder.RegisterMessageService();
        // 컨테이너가 Dispose될 때 Reflex가 MessageService.Dispose를 호출해 구독을 정리한다.
        public static ContainerBuilder RegisterMessageService(this ContainerBuilder builder)
        {
            return builder.RegisterType(
                typeof(MessageService), new[] { typeof(IMessageService) },
                Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
```

- [ ] **Step 4: `SoundServiceVContainerExtensions`를 옮긴다**

파일 내용을 이렇게 바꾼다(파일명 변경은 Task 11에서 한다 — 구조 변경을 섞지 않는다):

```csharp
using Reflex.Core;
using Reflex.Enums;

namespace DarkNaku.FoundationDI
{
    public static class SoundServiceVContainerExtensions
    {
        /// <summary>
        /// SoundService를 컨테이너에 등록한다. 볼륨 영속화는 PlayerPrefs 기본 구현을 사용한다.
        /// </summary>
        /// <param name="settings">데이터 컬렉션과 오클루전 설정을 담은 에셋.</param>
        public static ContainerBuilder RegisterSoundService(this ContainerBuilder builder,
                                                            SoundServiceSettings settings)
        {
            builder.RegisterValue(settings);
            builder.RegisterType(
                typeof(PlayerPrefsVolumeStorage), new[] { typeof(ISoundVolumeStorage) },
                Lifetime.Singleton, Resolution.Lazy);

            // 계약 둘이 한 바인딩을 공유한다 — 싱글턴이므로 같은 인스턴스가 나온다.
            return builder.RegisterType(
                typeof(SoundService), new[] { typeof(ISoundService), typeof(ISoundEngine) },
                Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
```

- [ ] **Step 5: `HapticServiceVContainerExtensions`를 옮긴다**

`Runtime/Services/HapticService/HapticService.cs`의 확장 클래스만 바꾼다. 파일 상단 `using VContainer;`는 `[Inject]` 생성자가 아직 남아 있으므로 **Task 8까지 유지한다**.

```csharp
    public static class HapticServiceVContainerExtensions
    {
        /// <summary>HapticService를 컨테이너에 등록한다. 외부 리소스 의존이 없어 추가 인자는 불필요하다.</summary>
        public static Reflex.Core.ContainerBuilder RegisterHapticService(
            this Reflex.Core.ContainerBuilder builder)
        {
            return builder.RegisterType(
                typeof(HapticService), new[] { typeof(IHapticService) },
                Reflex.Enums.Lifetime.Singleton, Reflex.Enums.Resolution.Lazy);
        }
    }
```

> 이 파일은 전환 기간 동안 `VContainer`(속성)와 `Reflex`(빌더)를 둘 다 본다. `using` 충돌을 피하려고 Reflex 쪽은 **정규화된 이름**으로 쓴다. Task 8에서 `[ReflexConstructor]`로 바꿀 때 `using`을 정리한다.

- [ ] **Step 6: `InitializeServiceVContainerExtensions`를 옮긴다**

`Runtime/Services/InitializeService/InitializeService.cs`의 확장 클래스만 바꾼다. 파일 상단 `using VContainer;`는 `IObjectResolver` 생성자가 아직 남아 있으므로 **Task 6까지 유지한다**.

```csharp
    public static class InitializeServiceVContainerExtensions
    {
        /// <summary>
        /// InitializeService를 컨테이너에 싱글턴으로 등록한다.
        /// IServiceResolver는 ServiceResolverBootstrap이 자동 등록한다.
        /// </summary>
        public static Reflex.Core.ContainerBuilder RegisterInitializeService(
            this Reflex.Core.ContainerBuilder builder)
        {
            return builder.RegisterType(
                typeof(InitializeService), new[] { typeof(IInitializeService) },
                Reflex.Enums.Lifetime.Singleton, Reflex.Enums.Resolution.Lazy);
        }
    }
```

- [ ] **Step 7: `HapticServiceTest`의 등록 테스트를 옮긴다**

`Tests/HapticServiceTest.cs:201-203`의 `new ContainerBuilder()`를 Reflex 것으로 바꾼다. 파일 상단에 `using Reflex.Core;`를 추가하고, 해석 단언을 `container.Resolve<IHapticService>()`로 맞춘다.

- [ ] **Step 8: 컴파일과 테스트를 확인한다**

UnityMCP `read_console` → 에러 0.
UnityMCP `run_tests` (EditMode, `FoundationDI.Tests` 전체) → 전부 PASS.

- [ ] **Step 9: 커밋한다**

```bash
git add Assets/FoundationDI/Runtime/Services/MessageService \
        Assets/FoundationDI/Runtime/Services/SoundService \
        Assets/FoundationDI/Runtime/Services/HapticService \
        Assets/FoundationDI/Runtime/Services/InitializeService \
        Assets/FoundationDI/Tests/MessageServiceTest.cs \
        Assets/FoundationDI/Tests/HapticServiceTest.cs \
        Assets/FoundationDI/Tests/SoundServiceRegistrationTest.cs
git commit -m "[BEHAVIORAL] Message/Sound/Haptic/Initialize 등록을 Reflex 빌더로 옮긴다"
```

---

### Task 5: 등록 확장 — 팩토리 서비스 3개

`AdService` · `AnalyticsService` · `IapService`. 셋 다 팩토리 콜백 안에서 컨테이너를 다시 쓴다. `IapService`는 `container.TryResolve<T>(out …)`도 쓴다 — Reflex에는 없으므로 `HasBinding` + `Resolve`로 푼다.

**Files:**
- Modify: `Runtime/Services/AdService/AdServiceRegistration.cs`
- Modify: `Runtime/Services/AnalyticsService/AnalyticsServiceRegistration.cs`
- Modify: `Runtime/Services/IAPService/IapServiceRegistration.cs`
- Test: `Tests/AdServiceRegistrationTest.cs`, `Tests/AnalyticsServiceRegistrationTest.cs`, `Tests/AnalyticsProviderSettingsTest.cs:111`, `Tests/IapServiceRegistrationTest.cs`

**Interfaces:**
- Consumes: Task 1
- Produces: `RegisterAdService(this ContainerBuilder, AdServiceSettings) → ContainerBuilder`, `RegisterAnalyticsService(this ContainerBuilder, AnalyticsServiceSettings) → ContainerBuilder`, `RegisterIapService(this ContainerBuilder, IapServiceSettings) → ContainerBuilder`. **셋 다 settings가 null이면 등록하지 않고 builder를 그대로 돌려주는 기존 동작을 유지한다.**

- [ ] **Step 1: 기존 테스트를 Reflex로 옮긴다(먼저 실패시킨다)**

네 테스트 파일에서 `using VContainer;` → `using Reflex.Core;`, `new ContainerBuilder()`는 그대로(타입만 바뀜), 단언은 이렇게 맞춘다:

```csharp
    [Test]
    public void 설정이_null이면_서비스를_등록하지_않는다()
    {
        LogAssert.Expect(LogType.Error, new Regex("AdServiceSettings가 null"));

        var builder = new ContainerBuilder();
        builder.RegisterAdService(null);

        using var container = builder.Build();

        Assert.IsFalse(container.HasBinding<IAdService>());
    }
```

`IapServiceRegistrationTest`에는 선택 등록 폴백을 지키는 테스트가 있어야 한다:

```csharp
    [Test]
    public void 지급_핸들러를_등록하지_않으면_AutoConfirmFulfillment로_폴백한다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterIapService(_settings);

        using var container = builder.Build();

        // 폴백이 깨지면 지급이 확정되지 않아 스토어가 영원히 재전달한다.
        Assert.DoesNotThrow(() => container.Resolve<IIapService>());
    }
```

- [ ] **Step 2: 컴파일 실패를 확인한다**

UnityMCP `read_console`.
기대: `RegisterAdService` 등이 `Reflex.Core.ContainerBuilder`의 확장이 아니라는 CS1929.

- [ ] **Step 3: `AdServiceRegistration`을 옮긴다**

```csharp
using Reflex.Core;
using Reflex.Enums;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public static class AdServiceRegistration
    {
        // 루트 IInstaller의 InstallBindings에서 호출한다.
        //   builder.RegisterAdService(_adServiceSettings);
        public static ContainerBuilder RegisterAdService(this ContainerBuilder builder,
                                                         AdServiceSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[AdService] AdServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterValue(settings);
            builder.RegisterType(typeof(PlayerPrefsAdRemovalStorage), new[] { typeof(IAdRemovalStorage) },
                                 Lifetime.Singleton, Resolution.Lazy);
            builder.RegisterType(typeof(UnityAdDispatcher), new[] { typeof(IAdDispatcher) },
                                 Lifetime.Singleton, Resolution.Lazy);
            builder.RegisterType(typeof(AdProviderFactory), new[] { typeof(IAdProviderFactory) },
                                 Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterFactory<IAdService>(container =>
            {
                var factory = container.Resolve<IAdProviderFactory>();
                var dispatcher = container.Resolve<IAdDispatcher>();
                var storage = container.Resolve<IAdRemovalStorage>();

                var forceDummy = settings.ForceDummyInEditor && Application.isEditor;
                var provider = factory.Create(settings.Provider, settings.DummyOptions, forceDummy);

                return new AdService(provider, dispatcher, settings.ToOptions(), storage);
            }, Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
```

- [ ] **Step 4: `AnalyticsServiceRegistration`을 옮긴다**

```csharp
using Reflex.Core;
using Reflex.Enums;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public static class AnalyticsServiceRegistration
    {
        // 루트 IInstaller의 InstallBindings에서 호출한다.
        //   builder.RegisterAnalyticsService(_analyticsServiceSettings);
        public static ContainerBuilder RegisterAnalyticsService(this ContainerBuilder builder,
                                                                AnalyticsServiceSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[AnalyticsService] AnalyticsServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterValue(settings);
            builder.RegisterType(typeof(AnalyticsProviderFactory), new[] { typeof(IAnalyticsProviderFactory) },
                                 Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterFactory<IAnalyticsService>(container =>
            {
                var factory = container.Resolve<IAnalyticsProviderFactory>();
                var options = settings.ToOptions();
                var types = settings.ResolveProviders(Application.isEditor);
                var providers = factory.CreateAll(types, options, settings.ProviderSettings);

                return new AnalyticsService(providers, options);
            }, Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
```

- [ ] **Step 5: `IapServiceRegistration`을 옮긴다**

`container.TryResolve<T>(out var x)` 세 곳이 `HasBinding` + `Resolve`로 바뀌는 것이 유일한 실질 변경이다.

```csharp
using Reflex.Core;
using Reflex.Enums;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public static class IapServiceRegistration
    {
        // 루트 IInstaller의 InstallBindings에서 호출한다.
        //   builder.RegisterIapService(_iapServiceSettings);
        //
        // 지급 핸들러를 쓰려면 같은 InstallBindings 어디서든(순서 무관) 함께 등록한다.
        //   builder.RegisterType(typeof(MyFulfillment), new[] { typeof(IIapFulfillment) },
        //                        Lifetime.Singleton, Resolution.Lazy);
        public static ContainerBuilder RegisterIapService(this ContainerBuilder builder,
                                                          IapServiceSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[IAPService] IapServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterValue(settings);
            builder.RegisterType(typeof(IapProviderFactory), new[] { typeof(IIapProviderFactory) },
                                 Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterFactory<IIapService>(container =>
            {
                var factory = container.Resolve<IIapProviderFactory>();

                var forceDummy = settings.ForceDummyInEditor && Application.isEditor;
                var provider = factory.Create(settings.Provider, settings.DummyOptions, forceDummy);

                // 셋 다 선택 등록이다. 게임이 등록하지 않았으면 기본 구현으로 폴백하므로
                // 등록 순서에 의존하지 않는다.
                // Reflex에는 TryResolve가 없어 HasBinding으로 먼저 묻는다 —
                // 미등록 계약에 Resolve를 부르면 UnknownContractException이 난다.
                var fulfillment = container.HasBinding<IIapFulfillment>()
                    ? container.Resolve<IIapFulfillment>()
                    : new AutoConfirmFulfillment();

                var validator = container.HasBinding<IReceiptValidator>()
                    ? container.Resolve<IReceiptValidator>()
                    : IapReceiptValidatorRegistry.ResolveOrDefault();

                var entitlements = container.HasBinding<IEntitlementStorage>()
                    ? container.Resolve<IEntitlementStorage>()
                    : new PlayerPrefsEntitlementStorage();

                return new IapService(provider, settings.ToOptions(), fulfillment, validator, entitlements);
            }, Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
```

- [ ] **Step 6: 컴파일과 테스트를 확인한다**

UnityMCP `read_console` → 에러 0.
UnityMCP `run_tests` (EditMode, `FoundationDI.Tests` 전체) → 전부 PASS.

- [ ] **Step 7: 커밋한다**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/AdServiceRegistration.cs \
        Assets/FoundationDI/Runtime/Services/AnalyticsService/AnalyticsServiceRegistration.cs \
        Assets/FoundationDI/Runtime/Services/IAPService/IapServiceRegistration.cs \
        Assets/FoundationDI/Tests/AdServiceRegistrationTest.cs \
        Assets/FoundationDI/Tests/AnalyticsServiceRegistrationTest.cs \
        Assets/FoundationDI/Tests/AnalyticsProviderSettingsTest.cs \
        Assets/FoundationDI/Tests/IapServiceRegistrationTest.cs
git commit -m "[BEHAVIORAL] Ad/Analytics/IAP 등록을 Reflex 빌더로 옮긴다"
```

---

### Task 6: 등록 확장 — 매니저 3개 (`WithParameter` 제거 포함)

`PoolManager` · `UINavigator` · `TutorialManager`. `RegisterPoolManager`의 `WithParameter`는 Reflex에 대응물이 없어 팩토리로 푼다.

**Files:**
- Modify: `Runtime/Managers/PoolManager/PoolManager.cs:250-270`(확장부만)
- Modify: `Runtime/Managers/UINavigator/UINavigator.cs:408-421`(확장부만)
- Modify: `Runtime/Managers/TutorialManager/TutorialManagerRegistration.cs`
- Test: `Tests/TutorialManagerRegistrationTest.cs`, `Tests/Editor/UINavigator/DIRegistrationTests.cs`

**Interfaces:**
- Consumes: Task 2의 `IServiceResolver`(팩토리가 `c.Resolve<IServiceResolver>()`로 꺼낸다), Task 3의 자동 등록
- Produces:
  - `RegisterPoolManager(this ContainerBuilder, Transform root = null) → ContainerBuilder`
  - `RegisterUINavigator(this ContainerBuilder, UINavigatorSettings) → ContainerBuilder`
  - `RegisterTutorialManager(this ContainerBuilder, string saveKey = "default") → ContainerBuilder`
  - `RegisterTutorialManager(this ContainerBuilder, ITutorialProgressStorage) → ContainerBuilder`

> **주의:** `RegisterPoolManager`의 팩토리는 `PoolManager` 생성자가 `IServiceResolver`를 받도록 바뀐 뒤에야 컴파일된다. 그 변경은 Task 7이다. 그래서 이 Task에서는 팩토리를 **`c.Resolve<IObjectResolver>()` 대신 임시로 `null`을 넘기지 않는다** — 대신 Task 7과 함께 실행하거나, 아래 Step 3의 순서를 그대로 따른다(생성자 변경을 이 Task 안에서 먼저 한다).

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Tests/TutorialManagerRegistrationTest.cs`를 Reflex로 옮기고(`using Reflex.Core;`), `PoolManager`의 root 인자 전달을 지키는 테스트를 `Tests/PoolManagerTest.cs`에 추가한다:

```csharp
    [Test]
    public void RegisterPoolManager에_넘긴_root가_풀_루트의_부모가_된다()
    {
        var root = new GameObject("scene-root").transform;
        var resource = Substitute.For<IResourceService>();

        var builder = new ContainerBuilder();
        builder.RegisterValue(resource, new[] { typeof(IResourceService) });
        ServiceResolverBootstrap.Register(builder);
        builder.RegisterPoolManager(root);

        using var container = builder.Build();

        try
        {
            // WithParameter가 사라졌으므로 팩토리가 root를 제대로 넘기는지가 회귀 지점이다.
            var pool = container.Resolve<IPoolManager>();
            Assert.IsNotNull(pool);
            Assert.AreEqual(1, root.childCount);
        }
        finally
        {
            Object.DestroyImmediate(root.gameObject);
        }
    }
```

- [ ] **Step 2: 컴파일 실패를 확인한다**

UnityMCP `read_console`.
기대: `RegisterPoolManager`가 `Reflex.Core.ContainerBuilder`의 확장이 아니라는 CS1929.

- [ ] **Step 3: `PoolManager`의 생성자와 필드를 `IServiceResolver`로 바꾼다**

`Runtime/Managers/PoolManager/PoolManager.cs`:

- `using VContainer;` / `using VContainer.Unity;` 제거
- `private readonly IObjectResolver _resolver;` → `private readonly IServiceResolver _resolver;`
- `public PoolManager(IResourceService resourceService, IObjectResolver resolver, Transform parent = null)` → `IServiceResolver resolver`
- `_resolver?.InjectGameObject(go);`(159행) — **그대로 둔다.** 시그니처가 같다.

- [ ] **Step 4: `PoolManagerVContainerExtensions`를 팩토리로 바꾼다**

```csharp
    public static class PoolManagerVContainerExtensions
    {
        /// <summary>
        /// PoolManager를 컨테이너에 등록한다.
        /// 씬 IInstaller에서 호출하면 풀이 씬과 수명을 함께하여, 씬 언로드 시
        /// 컨테이너 Dispose로 풀과 로드한 에셋(IResourceService.Release)이 자동 정리된다.
        /// <paramref name="root"/>(보통 ContainerScope의 transform)를 넘기면 풀 루트가
        /// 활성 씬이 아니라 그 transform이 속한 씬에 확실히 귀속된다(additive 로드 안전).
        /// 전제: 부모(루트) 컨테이너에 <see cref="IResourceService"/>가 이미 등록되어 있어야 한다.
        /// </summary>
        public static ContainerBuilder RegisterPoolManager(this ContainerBuilder builder,
                                                           Transform root = null)
        {
            // Reflex에는 VContainer의 WithParameter(파라미터 오버라이드)가 없다.
            // root를 넘기는 길은 팩토리뿐이다. root가 null이면 PoolManager 생성자가
            // 이미 '부모 없이 생성'으로 처리하므로 분기가 필요 없다.
            return builder.RegisterFactory<IPoolManager>(
                c => new PoolManager(c.Resolve<IResourceService>(), c.Resolve<IServiceResolver>(), root),
                Lifetime.Singleton, Resolution.Lazy);
        }
    }
```

파일 상단에 `using Reflex.Core;`와 `using Reflex.Enums;`를 추가한다.

- [ ] **Step 5: `UINavigatorVContainerExtensions`를 옮긴다**

```csharp
    public static class UINavigatorVContainerExtensions
    {
        /// <summary>
        /// UINavigator를 컨테이너에 등록한다.
        /// 전제: 호출 전에 <see cref="IResourceService"/>가 이미 등록되어 있어야 한다
        /// (UINavigator 전용 풀과 UIInstanceFactory가 이를 사용).
        /// </summary>
        public static ContainerBuilder RegisterUINavigator(this ContainerBuilder builder,
                                                           UINavigatorSettings settings)
        {
            builder.RegisterValue(settings);
            builder.RegisterType(typeof(UIInstanceFactory), Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterType(
                typeof(UINavigator), new[] { typeof(IUINavigator) },
                Lifetime.Singleton, Resolution.Lazy);
        }
    }
```

- [ ] **Step 6: `TutorialManagerRegistration`을 옮긴다**

```csharp
using Reflex.Core;
using Reflex.Enums;

namespace DarkNaku.FoundationDI
{
    public static class TutorialManagerRegistration
    {
        /// <summary>
        /// 씬 IInstaller에서 호출한다.
        /// 전제: 부모(루트) 컨테이너에 IMessageService가 등록돼 있어야 한다.
        /// saveKey는 진행도 PlayerPrefs 키의 네임스페이스다.
        /// </summary>
        public static ContainerBuilder RegisterTutorialManager(this ContainerBuilder builder,
                                                               string saveKey = "default")
        {
            builder.RegisterFactory<ITutorialProgressStorage>(
                _ => new PlayerPrefsTutorialProgressStorage(saveKey), Lifetime.Singleton, Resolution.Lazy);

            return RegisterCore(builder);
        }

        /// <summary>진행도 저장소를 직접 붙일 때(서버 동기화 등) 쓴다.</summary>
        public static ContainerBuilder RegisterTutorialManager(this ContainerBuilder builder,
                                                               ITutorialProgressStorage storage)
        {
            builder.RegisterValue(storage, new[] { typeof(ITutorialProgressStorage) });

            return RegisterCore(builder);
        }

        private static ContainerBuilder RegisterCore(ContainerBuilder builder)
        {
            builder.RegisterType(typeof(TutorialClock), new[] { typeof(ITutorialClock) },
                                 Lifetime.Singleton, Resolution.Lazy);
            builder.RegisterType(typeof(TutorialTargetRegistry), new[] { typeof(ITutorialTargetRegistry) },
                                 Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterType(typeof(TutorialManager), new[] { typeof(ITutorialManager) },
                                        Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
```

- [ ] **Step 7: `PoolManagerTest`의 리졸버 대역을 바꾼다**

`Tests/PoolManagerTest.cs`의 `Substitute.For<IObjectResolver>()` 3곳을 `Substitute.For<IServiceResolver>()`로 바꾸고 `using VContainer;`를 지운다. 단언 `resolver.Received().InjectGameObject(...)`는 시그니처가 같아 그대로다.

- [ ] **Step 8: 컴파일과 테스트를 확인한다**

UnityMCP `read_console` → 에러 0.
UnityMCP `run_tests` (EditMode, `FoundationDI.Tests` 전체) → 전부 PASS.

- [ ] **Step 9: 커밋한다**

```bash
git add Assets/FoundationDI/Runtime/Managers Assets/FoundationDI/Tests/PoolManagerTest.cs \
        Assets/FoundationDI/Tests/TutorialManagerRegistrationTest.cs \
        Assets/FoundationDI/Tests/Editor/UINavigator/DIRegistrationTests.cs
git commit -m "[BEHAVIORAL] Pool/UINavigator/Tutorial 등록을 Reflex로 옮기고 WithParameter를 팩토리로 푼다"
```

---

### Task 7: 리졸버 소비자 전환

`InitializeService`/`InitializeItem` · `UIInstanceFactory`. (`PoolManager`는 Task 6에서 끝났다.)

**Files:**
- Modify: `Runtime/Services/InitializeService/InitializeItem.cs`
- Modify: `Runtime/Services/InitializeService/InitializeService.cs`
- Modify: `Runtime/Managers/UINavigator/Controllers/UIInstanceFactory.cs`
- Modify: `Runtime/Managers/UINavigator/UINavigator.cs:120`
- Test: `Tests/InitializeServiceTest.cs`(9곳), `Tests/Editor/UINavigator/UIInstanceFactoryTests.cs`(1곳), `Tests/Runtime/UINavigator/*`(19곳)

**Interfaces:**
- Consumes: Task 2의 `IServiceResolver`
- Produces:
  - `public abstract Awaitable InitializeItem.InitializeAsync(IServiceResolver resolver)` — **공개 파괴적 변경**
  - `InitializeService(IServiceResolver resolver)`
  - `internal UIInstanceFactory(IServiceResolver resolver)`, `internal IServiceResolver Resolver { get; }`

- [ ] **Step 1: 테스트의 대역 타입을 먼저 바꾼다(실패시킨다)**

11개 파일 38곳을 일괄 치환한다:

```bash
cd /Users/chakyounghoon/Projects/FoundationDI
grep -rl "Substitute.For<IObjectResolver>" Assets/FoundationDI/Tests \
  | grep -v "InjectorServiceTest\|InjectableBehaviourTest" \
  | xargs sed -i '' 's/Substitute\.For<IObjectResolver>/Substitute.For<IServiceResolver>/g'
```

그런 다음 같은 파일들에서 `using VContainer;`를 지운다(다른 VContainer 심볼이 남아 있지 않은 파일만).

- [ ] **Step 2: 컴파일 실패를 확인한다**

UnityMCP `read_console`.
기대: `InitializeService`/`UIInstanceFactory` 생성자가 `IServiceResolver`를 받지 않는다는 CS1503.

- [ ] **Step 3: `InitializeItem`을 바꾼다**

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public abstract class InitializeItem : ScriptableObject
    {
        public abstract Awaitable InitializeAsync(IServiceResolver resolver);
    }
}
```

- [ ] **Step 4: `InitializeService`를 바꾼다**

`using VContainer;`를 지우고, 필드·생성자 타입을 바꾼다. `await item.InitializeAsync(_resolver);`는 그대로다.

```csharp
        private readonly IServiceResolver _resolver;

        public InitializeService(IServiceResolver resolver)
        {
            _resolver = resolver;
        }
```

- [ ] **Step 5: `UIInstanceFactory`를 바꾼다**

```csharp
using System;

namespace DarkNaku.FoundationDI
{
    internal sealed class UIInstanceFactory
    {
        private readonly IServiceResolver _resolver;

        // UINavigator 전용 풀도 같은 컨테이너로 View 계층을 주입해야 하므로 노출한다.
        internal IServiceResolver Resolver => _resolver;

        public UIInstanceFactory(IServiceResolver resolver)
        {
            _resolver = resolver;
        }

        // Host만 미리 설정하고 View 바인딩은 나중에 (BindView) 한다.
        // UINavigator 내부에서 Pool.Get 전에 presenter를 반환해야 할 때 사용.
        internal UIPresenter CreatePresenter(Type presenterType, IUIElementHost host)
        {
            var presenter = (UIPresenter)Activator.CreateInstance(presenterType);
            _resolver.Inject(presenter);
            presenter.BindHost(host);
            return presenter;
        }
    }
}
```

`UINavigator.cs:5`의 `using VContainer;`를 지운다. 120행 `new PoolManager(_resource, _factory.Resolver, Root.GO.transform)`는 타입만 바뀌므로 그대로다.

- [ ] **Step 6: 컴파일과 테스트를 확인한다**

UnityMCP `read_console` → 에러 0.
UnityMCP `run_tests` (EditMode, `FoundationDI.Tests`) → 전부 PASS.
UnityMCP `run_tests` (PlayMode, `FoundationDI.Tests.Runtime`) → 전부 PASS.

- [ ] **Step 7: 커밋한다**

```bash
git add Assets/FoundationDI/Runtime/Services/InitializeService \
        Assets/FoundationDI/Runtime/Managers/UINavigator Assets/FoundationDI/Tests
git commit -m "[BEHAVIORAL] 리졸버 소비자를 IServiceResolver로 옮긴다"
```

---

### Task 8: 씬 컴포넌트 주입과 생성자 속성

**Files:**
- Modify: `Runtime/Components/UIButton.cs`
- Modify: `Runtime/Managers/TutorialManager/Authoring/TutorialSequenceBehaviour.cs`
- Modify: `Runtime/Managers/TutorialManager/Targets/TutorialTarget.cs`
- Modify: `Runtime/Services/SoundService/Components/MusicZone.cs`
- Modify: `Runtime/Services/SoundService/Components/OutputVolumeSlider.cs`
- Modify: `Runtime/Services/AdService/Dispatch/UnityAdDispatcher.cs:46-48`
- Modify: `Runtime/Services/HapticService/HapticService.cs:36`
- Test: `Tests/UIButtonTest.cs`, `Tests/UIStateButtonTest.cs`, `Tests/UIScaleButtonTest.cs`

**Interfaces:**
- Consumes: Task 2·3
- Produces: `UIButton.Construct(IServiceResolver)`가 `[Reflex.Attributes.Inject]` 메서드 주입 지점이 된다. `InjectorService.Request` 호출이 사라져 Task 9의 삭제가 가능해진다.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Tests/UIButtonTest.cs`의 컨테이너 셋업을 Reflex로 바꾸고, 주입이 `Construct`로 들어오는지 직접 검증한다:

```csharp
    [Test]
    public void Construct는_등록된_사운드_서비스를_받아_클릭시_재생한다()
    {
        var sound = Substitute.For<ISoundService>();
        var go = new GameObject("btn", typeof(RectTransform));
        var button = go.AddComponent<UIButton>();

        try
        {
            var builder = new ContainerBuilder();
            builder.RegisterValue(sound, new[] { typeof(ISoundService) });
            using var container = builder.Build();

            // 씬 주입 경로와 같은 방식으로 주입한다.
            new ReflexServiceResolver(container).Inject(button);

            button.PlayFeedback();

            sound.ReceivedWithAnyArgs().Play(default);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 서비스가_하나도_등록되지_않아도_클릭이_예외를_내지_않는다()
    {
        var go = new GameObject("btn", typeof(RectTransform));
        var button = go.AddComponent<UIButton>();

        try
        {
            using var container = new ContainerBuilder().Build();

            // IServiceResolver만 있으면 되고, 개별 서비스는 없어도 된다 —
            // 이게 [Inject] 필드가 아니라 리졸버를 받는 이유다.
            new ReflexServiceResolver(container).Inject(button);

            Assert.DoesNotThrow(() => button.PlayFeedback());
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
```

> 위 두 번째 테스트가 성립하려면 `Construct`가 받는 `IServiceResolver`도 컨테이너에 있어야 한다. `new ContainerBuilder()` 뒤에 `ServiceResolverBootstrap.Register(builder);`를 넣는다.

- [ ] **Step 2: 컴파일 실패를 확인한다**

UnityMCP `read_console`.
기대: `Construct(IObjectResolver)`와 `IServiceResolver` 인자 불일치 CS1503.

- [ ] **Step 3: `UIButton`을 바꾼다**

`using VContainer;` → `using Reflex.Attributes;`. `Construct`의 주석과 시그니처를 바꾸고 `EnsureInjected`/`_requested`/`Awake`의 호출을 지운다.

```csharp
        /// <summary>
        /// 개별 서비스가 아니라 리졸버를 받는다. [Inject] 필드로 서비스를 직접 받으면
        /// 미등록 시 Reflex의 AttributeInjector가 UnknownContractException을 던지는데,
        /// 이 예외를 흡수할 곳이 없다 — 씬 주입 경로(SceneInjector)와 PoolManager의
        /// InjectGameObject 둘 다 try/catch가 없어 View가 통째로 안 뜬다.
        /// IServiceResolver는 ServiceResolverBootstrap이 루트·씬 양쪽에 항상 등록한다.
        /// </summary>
        [Inject]
        public void Construct(IServiceResolver resolver)
        {
            if (resolver == null) return;

            resolver.TryResolve(out _soundService);
            resolver.TryResolve(out _hapticService);
        }

        protected override void Awake()
        {
            base.Awake();

            // 주입은 Reflex가 담당한다 — 씬 배치분은 ContainerScope가 Awake 전에,
            // 런타임 생성분은 PoolManager/UINavigator가 InjectGameObject로 채운다.

            // Button.Press()가 OnPointerClick/OnSubmit 양쪽에서 호출되므로
            // 리스너 하나로 마우스·터치·게임패드 Submit이 전부 커버된다.
            onClick.AddListener(PlayFeedback);
        }
```

`private bool _requested;` 필드와 `EnsureInjected()` 메서드를 통째로 지운다.

- [ ] **Step 4: 씬 컴포넌트 4개를 리졸버 주입으로 바꾼다**

> **왜 필드 주입을 그냥 두지 않는가:** Reflex의 `GameObjectInjector.InjectRecursiveMany`에는 컴포넌트별 try/catch가 없고 `FieldInjector.Inject`는 해석 실패를 `FieldInjectorException`으로 다시 던진다. 미등록 서비스를 요구하는 컴포넌트 하나가 **그 뒤 순번 전체의 씬 주입을 막는다.** 지금은 `InjectorService.TryInject`가 이걸 잡아 주고 있고, Task 9에서 그게 사라진다. `IServiceResolver`는 항상 등록되므로 리졸버 주입은 던지지 않는다.

넷 다 공통 변경:

- `using VContainer;` → `using Reflex.Attributes;`
- `: InjectableBehaviour` → `: MonoBehaviour`
- `[Inject] private IXxx _field;` → 평범한 `private IXxx _field;` + 아래 `Construct`
- `EnsureInjected()` 호출 제거, `base.Awake()` 제거(`protected override void Awake` → `private void Awake`)
- **`Update` 폴링과 널 가드는 남긴다** — 제거는 이번 범위 밖이다

`TutorialTarget.cs`:

```csharp
        private ITutorialTargetRegistry _registry;

        [Inject]
        public void Construct(IServiceResolver resolver)
        {
            if (resolver == null) return;

            resolver.TryResolve(out _registry);
        }

        private void OnEnable()
        {
            TryRegister();
        }
```

`TutorialSequenceBehaviour.cs`:

```csharp
        private ITutorialManager _tutorial;

        [Inject]
        public void Construct(IServiceResolver resolver)
        {
            if (resolver == null) return;

            resolver.TryResolve(out _tutorial);
        }
```

`MusicZone.cs` — 클래스 선언의 XML 주석에서 `InjectableBehaviour` 언급도 고친다:

```csharp
    /// <summary>
    /// 구/박스 영역 안에서만 들리는 음악 존. 영역 밖 페이드 구간에서 거리에 비례해 볼륨이 줄어든다.
    /// 씬에 배치하는 컴포넌트이므로 <see cref="IServiceResolver"/>로 <see cref="ISoundService"/>를
    /// 선택 주입받는다 — 미등록 시 던지면 같은 씬의 다른 컴포넌트 주입까지 막힌다.
    /// </summary>
    public class MusicZone : MonoBehaviour
    {
        private ISoundService _soundService;

        [Inject]
        public void Construct(IServiceResolver resolver)
        {
            if (resolver == null) return;

            resolver.TryResolve(out _soundService);
        }
```

`OutputVolumeSlider.cs` — `Awake`에서 `base.Awake()`를 지우고, `Start`/`ChangeVolume`의 `EnsureInjected()`를 지운다:

```csharp
        private ISoundService _soundService;

        [Inject]
        public void Construct(IServiceResolver resolver)
        {
            if (resolver == null) return;

            resolver.TryResolve(out _soundService);
        }

        private void Awake()
        {
            _volumeSlider = GetComponent<Slider>();
            _volumeSlider.onValueChanged.AddListener(ChangeVolume);
        }

        private void Start()
        {
            if (_soundService == null)
            {
                Debug.LogError("[OutputVolumeSlider] ISoundService가 주입되지 않았습니다.");
                return;
            }
            ...
        }

        public void ChangeVolume(float volume)
        {
            if (_soundService == null) return;
            ...
        }
```

- [ ] **Step 5: 생성자 속성 2개를 바꾼다**

`Runtime/Services/AdService/Dispatch/UnityAdDispatcher.cs`:

```csharp
        // [ReflexConstructor]가 없으면 Reflex가 파라미터가 더 많은 (bool) 생성자를 고른다.
        [ReflexConstructor]
```

`using VContainer;` → `using Reflex.Attributes;`.

`Runtime/Services/HapticService/HapticService.cs:36`도 같은 방식으로 `[Inject]` → `[ReflexConstructor]`, `using VContainer;` → `using Reflex.Attributes;`. Task 4에서 정규화된 이름으로 써 둔 `Reflex.Core.ContainerBuilder`/`Reflex.Enums.*`를 `using`으로 정리한다.

- [ ] **Step 6: 버튼 테스트 3종의 `InjectorService` 리셋 셋업을 지운다**

`Tests/UIButtonTest.cs` · `UIStateButtonTest.cs` · `UIScaleButtonTest.cs`에서 다음 형태의 줄 6곳을 **삭제**한다(치환이 아니다):

```csharp
new InjectorService(Substitute.For<IServiceResolver>()).Dispose();
```

Task 9에서 `InjectorService`가 사라지므로 리셋할 정적 상태가 없다.

- [ ] **Step 7: 컴파일과 테스트를 확인한다**

UnityMCP `read_console` → 에러 0.
UnityMCP `run_tests` (EditMode + PlayMode) → 전부 PASS.

- [ ] **Step 8: 커밋한다**

```bash
git add Assets/FoundationDI/Runtime/Components Assets/FoundationDI/Runtime/Managers/TutorialManager \
        Assets/FoundationDI/Runtime/Services/SoundService/Components \
        Assets/FoundationDI/Runtime/Services/AdService/Dispatch/UnityAdDispatcher.cs \
        Assets/FoundationDI/Runtime/Services/HapticService/HapticService.cs \
        Assets/FoundationDI/Tests
git commit -m "[BEHAVIORAL] 씬 컴포넌트 주입을 Reflex 속성으로 옮긴다"
```

---

### Task 9: `InjectorService` · `InjectableBehaviour` 삭제

**Files:**
- Delete: `Runtime/Services/InjectorService/InjectorService.cs`(+`.meta`)
- Delete: `Runtime/Services/InjectorService/InjectableBehaviour.cs`(+`.meta`)
- Delete: `Runtime/Services/InjectorService/README.md`(+`.meta`)
- Delete: `Tests/InjectorServiceTest.cs`(+`.meta`)
- Delete: `Tests/InjectableBehaviourTest.cs`(+`.meta`)

**Interfaces:**
- Consumes: Task 8이 마지막 호출부(`UIButton.EnsureInjected`)를 제거해 둔 상태
- Produces: `InjectorService`/`InjectableBehaviour`/`RegisterInjector`가 사라진다. 이후 Task는 이들을 참조하지 않는다.

- [ ] **Step 1: 남은 참조가 없는지 확인한다**

```bash
cd /Users/chakyounghoon/Projects/FoundationDI
grep -rn "InjectorService\|InjectableBehaviour\|RegisterInjector" \
  --include="*.cs" Assets | grep -v "Assets/FoundationDI/Runtime/Services/InjectorService/"
```

기대 출력: `Assets/FoundationDI/Samples~/05-Sound/Scripts/SoundSampleDemo.cs`(Task 10에서 처리) 외에는 없음. 다른 것이 나오면 먼저 그것부터 정리한다.

- [ ] **Step 2: 삭제한다**

```bash
git rm -r Assets/FoundationDI/Runtime/Services/InjectorService \
          Assets/FoundationDI/Tests/InjectorServiceTest.cs \
          Assets/FoundationDI/Tests/InjectorServiceTest.cs.meta \
          Assets/FoundationDI/Tests/InjectableBehaviourTest.cs \
          Assets/FoundationDI/Tests/InjectableBehaviourTest.cs.meta
```

- [ ] **Step 3: 컴파일과 테스트를 확인한다**

UnityMCP `refresh_unity` → `read_console` → 에러 0.
UnityMCP `run_tests` (EditMode + PlayMode) → 전부 PASS. **테스트 수가 12개 줄어드는 것이 정상이다**(`InjectorServiceTest` 8 + `InjectableBehaviourTest` 4 케이스 기준, 실제 수는 실행 결과로 확인).

- [ ] **Step 4: 커밋한다**

```bash
git commit -m "[BEHAVIORAL] InjectorService와 InjectableBehaviour를 삭제한다

Reflex의 ContainerScope가 실행 순서 -1e9로 씬 전체를 어떤 Awake보다도 먼저
주입하므로, 컨테이너 생성 전 Awake를 받아 두던 정적 보류 큐가 할 일이 없다."
```

---

### Task 10: 호스트 프로젝트와 샘플

> **스펙과의 순서 차이(의도적):** 스펙의 마이그레이션 순서는 인스톨러 생성을 3단계에 두었지만, 인스톨러는 `Register*` 확장이 Reflex `ContainerBuilder`를 받도록 바뀐 뒤에야 컴파일된다. 그래서 이 계획은 그것을 Task 4~6 뒤로 미룬다. "매 단계 컴파일이 통과한다"는 스펙의 요구는 이 순서라야 지켜진다.


**Files:**
- Create: `Assets/Scripts/Installers/RootInstaller.cs`, `Assets/Scripts/Installers/SceneInstaller.cs`
- Delete: `Assets/Scripts/LifetimeScopes/`(2파일+meta)
- Modify: `Assets/Scripts/AdServiceSmokeTest.cs`, `AnalyticsServiceSmokeTest.cs`, `IapServiceSmokeTest.cs`
- Modify: `Assets/Scripts/UI/GamePagePresenter.cs`, `MenuPagePresenter.cs`, `TestHubPresenters.cs`(`TestHubBootstrap` → MonoBehaviour)
- Modify: `Samples~/Common/SampleLifetimeScope.cs` → `SampleInstaller.cs`, 각 샘플 `*Scope.cs`(5) / `*Presenters.cs`(5), `05-Sound/Scripts/SoundSampleDemo.cs`
- Modify: `Assets/Resources/ReflexSettings.asset`(RootScopes 채우기) / Create: `Assets/Prefabs/RootScope.prefab`
- Delete: `Assets/Settings/VContainerSettings.asset`(+meta), `Assets/Prefabs/RootLifetimeScope.prefab`(+meta)
- Modify: 호스트 씬들(`ContainerScope` 배치)

**Interfaces:**
- Consumes: Task 4·5·6의 `Register*` 확장 전부
- Produces: 실행 가능한 호스트 앱. PlayMode 테스트가 이 배선을 검증한다.

- [ ] **Step 1: `RootInstaller`를 만든다**

`Assets/Scripts/Installers/RootInstaller.cs`. 구 `RootLifetimeScope.Configure`를 그대로 옮기되 **두 가지가 바뀐다**: `builder.RegisterInjector()`가 사라지고, `builder.RegisterTutorialManager()`가 씬으로 내려간다(스펙 결정 6 — 그것을 루트에 붙들어 두던 `InjectorService` 제약이 없어졌다).

```csharp
using DarkNaku.FoundationDI;
using Reflex.Core;
using Reflex.Enums;
using UnityEngine;

public class RootInstaller : MonoBehaviour, IInstaller
{
    // 인스펙터에서 Assets/Settings/AdServiceSettings.asset 을 연결한다.
    [SerializeField] private AdServiceSettings _adServiceSettings;

    // 인스펙터에서 Assets/Settings/AnalyticsServiceSettings.asset 을 연결한다.
    [SerializeField] private AnalyticsServiceSettings _analyticsServiceSettings;

    // 인스펙터에서 Assets/Settings/IapServiceSettings.asset 을 연결한다.
    [SerializeField] private IapServiceSettings _iapServiceSettings;

    public void InstallBindings(ContainerBuilder builder)
    {
        // 프리팹 로드는 Resources 백엔드 ResourceService에 위임한다.
        // 백엔드 교체는 이 provider 등록 한 줄만 바꾼다 (예: AddressablesProvider).
        builder.RegisterType(typeof(ResourcesProvider), new[] { typeof(IResourceProvider) },
                             Lifetime.Singleton, Resolution.Lazy);
        builder.RegisterType(typeof(ResourceService), new[] { typeof(IResourceService) },
                             Lifetime.Singleton, Resolution.Lazy);
        builder.RegisterMessageService();

        // builder.RegisterInjector() 는 사라졌다 — 씬 배치 컴포넌트의 주입은
        // ContainerScope가 Awake 전에 직접 한다.

        builder.RegisterHapticService();
        builder.RegisterInitializeService();
        builder.RegisterAdService(_adServiceSettings);
        builder.RegisterAnalyticsService(_analyticsServiceSettings);
        builder.RegisterIapService(_iapServiceSettings);

        // RegisterTutorialManager는 SceneInstaller로 내려갔다.
        // 루트에 두던 이유(InjectorService의 정적 리졸버 공유)가 사라졌다.
    }
}
```

- [ ] **Step 2: `SceneInstaller`를 만들고 `TestHubBootstrap`을 내린다**

구 `SceneLifetimeScope`는 `RegisterUINavigator`와 `RegisterEntryPoint<TestHubBootstrap>()` 둘을 등록했다. Reflex에 엔트리포인트가 없으므로 `TestHubBootstrap`은 `MonoBehaviour`가 되어 씬에 놓인다.

`Assets/Scripts/Installers/SceneInstaller.cs`:

```csharp
using DarkNaku.FoundationDI;
using Reflex.Core;
using UnityEngine;

/// <summary>
/// 씬 수명 컴포넌트를 등록한다. UINavigator는 씬 컨테이너가 소유하므로
/// 씬이 언로드되면 캔버스·풀·프리젠터가 함께 파괴된다.
/// IResourceService 등 앱 수명 서비스는 부모(루트) 컨테이너에서 해결된다.
/// </summary>
public class SceneInstaller : MonoBehaviour, IInstaller
{
    // 인스펙터에서 Assets/Settings/UINavigatorSettings.asset 을 연결한다.
    [SerializeField] private UINavigatorSettings _uiNavigatorSettings;

    public void InstallBindings(ContainerBuilder builder)
    {
        builder.RegisterUINavigator(_uiNavigatorSettings);

        // 루트에서 내려왔다. 씬 수명이 원래 자리다.
        builder.RegisterTutorialManager();
    }
}
```

`Assets/Scripts/UI/TestHubPresenters.cs`의 `TestHubBootstrap`(92행 부근)을 이렇게 바꾼다:

```csharp
    /// 앱 시작 시 메인 테스트 페이지를 띄우는 부트스트랩.
    /// 씬에 배치한다 — Reflex가 Awake 전에 주입하므로 Start에서 _ui는 이미 채워져 있다.
    public class TestHubBootstrap : MonoBehaviour
    {
        [Inject] private IUINavigator _ui;

        private void Start() => _ui.Page<MenuPage>();
    }
```

파일 상단에서 `using VContainer;`/`using VContainer.Unity;`를 지우고 `using Reflex.Attributes;`와 `using UnityEngine;`을 넣는다. 씬의 `ContainerScope` GameObject에 `TestHubBootstrap` 컴포넌트를 붙인다.

- [ ] **Step 3: `ReflexSettings`와 `RootScope.prefab`을 만든다**

`ReflexSettings.asset`은 **Task 2에서 이미 만들었다**(테스트가 요구했다). 여기서는 `RootScopes`를 채운다.

Unity 에디터에서:
1. 빈 GameObject에 `ContainerScope` + `RootInstaller` 컴포넌트를 붙이고 `Assets/Prefabs/RootScope.prefab`으로 저장
2. 구 `RootLifetimeScope.prefab`의 `[SerializeField]` 에셋 참조를 새 프리팹에 그대로 옮긴다
3. `ReflexSettings.RootScopes`에 `RootScope.prefab`을 추가

- [ ] **Step 4: 씬에 `ContainerScope`를 배치한다**

호스트 씬마다 `SceneLifetimeScope`가 있던 GameObject에 `ContainerScope` + `SceneInstaller`를 붙이고, 구 컴포넌트를 제거한다. `[SerializeField]` 참조를 옮긴다.

- [ ] **Step 5: 스모크 테스트 3개를 바꾼다**

`Assets/Scripts/AdServiceSmokeTest.cs` 등에서:

```csharp
using DarkNaku.FoundationDI;
using Reflex.Attributes;
using Reflex.Core;
using Reflex.Extensions;
using UnityEngine;

public class AdServiceSmokeTest : MonoBehaviour
{
    [Inject] private IAdService _ads;

    private void Awake()
    {
        // 이 오브젝트가 ContainerScope가 있는 씬에 놓여 있으면 Reflex가 Awake 전에
        // 이미 주입했다. DontDestroyOnLoad 등 씬 밖에 있을 때만 아래가 필요하다.
        if (_ads == null)
        {
            Reflex.Injectors.AttributeInjector.Inject(this, gameObject.scene.GetSceneContainer());
        }
    }
}
```

- [ ] **Step 6: 프레젠터와 `IStartable`을 바꾼다**

`Assets/Scripts/UI/*.cs`: `using VContainer;` → `using Reflex.Attributes;`. `[Inject]` 필드는 그대로.

`TestHubBootstrap`은 Step 2에서 이미 바꿨다. 여기서는 `GamePagePresenter`/`MenuPagePresenter`와 `TestHubPresenters.cs`의 나머지 프레젠터만 다룬다.

- [ ] **Step 7: 샘플 6개 파일군을 바꾼다**

> **`Samples~`는 물결표 폴더라 Unity가 컴파일하지 않는다.** 여기 실수는 컴파일러도 테스트도 잡아 주지 않는다 — UPM으로 샘플을 임포트한 사용자에게서만 드러난다. 바꾼 뒤 `grep -rn "VContainer\|IStartable\|InjectableBehaviour" Assets/FoundationDI/Samples~`로 **직접 확인한다.**


- `Samples~/Common/SampleLifetimeScope.cs` → `SampleInstaller.cs`: `LifetimeScope` → `MonoBehaviour, IInstaller`, `Configure` → `InstallBindings`
- `01`~`05`의 `*Scope.cs` 5개: `SampleLifetimeScope` → `SampleInstaller` 상속, `Configure` → `InstallBindings`
- `*Presenters.cs` 5개: `using VContainer;` → `using Reflex.Attributes;`, `using VContainer.Unity;` 제거, `*Demo : IStartable` → `MonoBehaviour` + `Start()`
- `05-Sound/Scripts/SoundSampleDemo.cs`: `InjectableBehaviour` → `MonoBehaviour`

- [ ] **Step 8: 구 에셋과 스코프를 지운다**

```bash
git rm Assets/Settings/VContainerSettings.asset Assets/Settings/VContainerSettings.asset.meta \
       Assets/Prefabs/RootLifetimeScope.prefab Assets/Prefabs/RootLifetimeScope.prefab.meta
git rm -r Assets/Scripts/LifetimeScopes
```

- [ ] **Step 9: 컴파일·테스트·실행을 확인한다**

UnityMCP `read_console` → 에러 0.
UnityMCP `run_tests` (EditMode + PlayMode) → 전부 PASS.
**Play 모드로 호스트 씬을 직접 띄워** UI가 뜨고 버튼 사운드가 나는지 확인한다 — PlayMode 테스트가 `ContainerScope` 배치 누락을 다 잡지는 못한다.

- [ ] **Step 10: 커밋한다**

```bash
git add -A Assets/Scripts Assets/FoundationDI/Samples~ Assets/Resources Assets/Prefabs Assets/Settings
git commit -m "[BEHAVIORAL] 호스트와 샘플의 컴포지션 루트를 Reflex IInstaller로 옮긴다"
```

---

### Task 11: VContainer 제거와 마무리

**Files:**
- Modify: `Packages/manifest.json`, `Packages/packages-lock.json`
- Modify: asmdef 4개
- Rename: `SoundServiceVContainerExtensions.cs` → `SoundServiceRegistration.cs`, 클래스명 5개
- Modify: `Assets/FoundationDI/package.json`, `CLAUDE.md`, `README.md`, 서비스 README 12개, `plan.md`

**Interfaces:**
- Consumes: Task 1~10 전부
- Produces: VContainer 의존 0. 패키지 0.10.0.

- [ ] **Step 1: VContainer 잔존 참조를 확인한다**

```bash
cd /Users/chakyounghoon/Projects/FoundationDI
grep -rn "VContainer\|IObjectResolver\|IContainerBuilder\|LifetimeScope\|IStartable" \
  --include="*.cs" Assets | grep -v "VContainerExtensions"
```

기대: **출력 없음**. 남아 있으면 그것부터 고친다.

- [ ] **Step 2: asmdef 4개에서 VContainer를 뺀다**

`Runtime/FoundationDI.asmdef`에서 GUID `b0214a6008ed146ff8f122a6a9c2f6cc` 줄을 지운다.
테스트 asmdef 3개에서 `"VContainer",` 줄을 지운다.

- [ ] **Step 3: manifest에서 VContainer를 뺀다**

`Packages/manifest.json`의 `"jp.hadashikick.vcontainer"` 줄을 지운다. Unity가 `packages-lock.json`을 스스로 갱신한다.

- [ ] **Step 4: 컴파일과 테스트를 확인한다**

UnityMCP `refresh_unity` → `read_console` → 에러 0.
UnityMCP `run_tests` (EditMode + PlayMode) → 전부 PASS.

- [ ] **Step 5: 커밋한다**

```bash
git add Packages/manifest.json Packages/packages-lock.json Assets/FoundationDI/Runtime/FoundationDI.asmdef \
        Assets/FoundationDI/Tests/FoundationDI.Tests.asmdef \
        Assets/FoundationDI/Tests/Editor/FoundationDI.Tests.Editor.asmdef \
        Assets/FoundationDI/Tests/Runtime/FoundationDI.Tests.Runtime.asmdef
git commit -m "[STRUCTURAL] VContainer 의존을 제거한다"
```

- [ ] **Step 6: `*VContainerExtensions` 클래스명을 정리한다**

이름만 바꾼다. 내용은 건드리지 않는다.

- `SoundServiceVContainerExtensions` → `SoundServiceRegistration` (파일명도 `SoundServiceRegistration.cs`로, `git mv` + `.meta` 동반)
- `HapticServiceVContainerExtensions` → `HapticServiceRegistration`
- `InitializeServiceVContainerExtensions` → `InitializeServiceRegistration`
- `PoolManagerVContainerExtensions` → `PoolManagerRegistration`
- `UINavigatorVContainerExtensions` → `UINavigatorRegistration`

확장 메서드라 호출부(`builder.RegisterXxx()`)는 바뀌지 않는다. 컴파일·테스트 확인 후 커밋:

```bash
git commit -m "[STRUCTURAL] 등록 확장 클래스명에서 VContainer를 지운다"
```

- [ ] **Step 7: 문서를 갱신한다**

- `Assets/FoundationDI/package.json`: `"version": "0.10.0"`
- `CLAUDE.md`: "VContainer를 코어로" → Reflex. **`RegisterTutorialManager`의 `RegisterInjector` 주의사항 문단을 삭제한다**(결정 6). `InjectorService` 언급 전부 제거. `UIButton`의 선택적 주입 근거를 Reflex 기준으로 다시 쓴다.
- 루트 `README.md` + 서비스 README 12개: `LifetimeScope`/`Configure` → `IInstaller`/`InstallBindings`, `IObjectResolver` → `IServiceResolver`
- 루트 `README.md`에 **마이그레이션 안내 8단계**를 싣는다(스펙의 "마이그레이션 안내" 절 그대로)
- `plan.md`: 이번 작업 항목을 `[x]`로 채운 섹션 추가

- [ ] **Step 8: 최종 확인 후 커밋한다**

UnityMCP `run_tests` (EditMode + PlayMode) → 전부 PASS.

```bash
git add -A
git commit -m "[STRUCTURAL] Reflex 전환을 문서에 적고 0.10.0으로 올린다"
```

---

## 실행 후 확인 목록

- [ ] `grep -rn "VContainer" --include="*.cs" --include="*.asmdef" --include="*.json" Assets Packages` → 출력 없음
- [ ] EditMode 전체 통과 / PlayMode 전체 통과
- [ ] 호스트 씬 Play → UI 표시, 버튼 사운드·햅틱 동작
- [ ] `Assets/Resources/ReflexSettings.asset`의 `RootScopes`에 `RootScope.prefab`이 들어 있다
- [ ] `package.json` 0.10.0
