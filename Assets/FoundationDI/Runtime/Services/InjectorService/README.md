# InjectorService

씬에 디자이너가 직접 배치한(=`LifetimeScope`가 생성하지 않은) MonoBehaviour에 VContainer 의존성을 주입하는 **인프라**입니다. 컴포넌트는 정적 진입점에 자신을 등록하고, 컨테이너가 준비되면 주입받습니다. `UIButton`이 첫 사용처입니다.

- **위치·계층·순서 무관** — 컴포넌트는 정적 `InjectorService.Request(this)`만 호출. 컨테이너 준비 전 요청은 보류했다가 일괄 주입
- **이벤트 드리븐** — 폴링 없음. 컨테이너 준비 시 1회 flush, 준비 후 요청은 즉시 주입
- **베이스 클래스** — `InjectableBehaviour`가 `Awake`에서 멱등 self-request를 캡슐화
- **동적 생성 대응** — 런타임에 생성되는 컴포넌트도 동일 경로로 주입

---

## 사용법

### 1) DI 등록 (VContainer)

루트 `LifetimeScope`에서 한 번만 등록합니다.

```csharp
using VContainer;
using VContainer.Unity;
using DarkNaku.FoundationDI;

public class RootLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterInjector();
        // 주입 대상이 소비할 서비스들도 함께 등록
    }
}
```

> **반드시 루트 스코프에서 한 번만** 호출합니다. `InjectorService`는 정적 컨테이너 참조를 공유하므로(단일 컨테이너 모델), 자식 스코프에서 중복 등록하면 루트의 주입이 깨질 수 있습니다.

### 2) 주입받는 컴포넌트 작성

`InjectableBehaviour`를 상속하고 `[Inject]` 필드를 선언합니다.

```csharp
using UnityEngine;
using VContainer;
using DarkNaku.FoundationDI;

public sealed class HudWidget : InjectableBehaviour
{
    [Inject] private ISoundService _sound;

    // Awake를 오버라이드하면 base.Awake() 호출 필수(self-request 보장)
    protected override void Awake()
    {
        base.Awake();
        // 추가 초기화
    }

    public void OnButton() => _sound.Play("Click");
}
```

주입 완료 시점은 컨테이너 준비 시점에 달려 있으므로, 주입된 필드는 클릭/입력 등 **런타임 이벤트 시점**에 사용합니다(생성자/`Awake` 즉시 사용 금지).

### 3) `InjectableBehaviour`를 못 쓸 때

`InjectableBehaviour`는 `MonoBehaviour`를 직접 상속하므로, 이미 다른 클래스를 상속하고 있는
컴포넌트는 쓸 수 없습니다. `UIButton`이 그 예입니다 — uGUI `Button`을 상속해야 하므로
`InjectableBehaviour`에 올라탈 수 없고, `Awake`에서 `InjectorService.Request(this)`를 직접
호출합니다.

```csharp
public class UIButton : Button
{
    private bool _requested;

    [Inject]
    public void Construct(IObjectResolver resolver)
    {
        resolver.TryResolve(out _soundService);
        resolver.TryResolve(out _hapticService);
    }

    protected override void Awake()
    {
        base.Awake();
        EnsureInjected();
    }

    private void EnsureInjected()
    {
        if (_requested) return;
        _requested = true;
        InjectorService.Request(this);
    }
}
```

`[Inject]` 필드 대신 `IObjectResolver`를 받아 `TryResolve`하는 이유는 별개의 설계 결정입니다 —
미등록 서비스를 `[Inject]` 필드로 요구하면 VContainer가 예외를 던지고, 그 컴포넌트는 결국 의존성
없이 남기 때문입니다. 자세한 내용은 [Components README](../../Components/README.md)를 참고하세요.

---

## 주입 실패 격리

주입 중 예외가 나도 **그 대상 하나에만 가둡니다.** `Start()`의 일괄 주입과 `Request()`의 즉시 주입
양쪽 모두 대상마다 `try/catch`로 감싸고 `Debug.LogException`으로 남긴 뒤 계속 진행합니다.

예외를 삼키는 것이 아니라 **치명적이지 않게** 만드는 것입니다 — 콘솔에는 그대로 에러로 뜹니다.
`MessageService`의 핸들러 예외 격리, `UINavigator`의 `OperationQueue`와 같은 방침입니다.

격리가 없으면 피해가 번집니다.

- `Start()`에서 예외가 빠져나가면 **뒤 순번의 모든 보류분이 주입을 못 받고** 보류 큐도 비워지지
  않습니다. VContainer는 `EntryPointExceptionHandler`가 등록되어 있지 않으면 EntryPoint 예외를
  그대로 다시 던지므로, 컨테이너 시작 자체가 깨질 수 있습니다.
- `Request()`의 즉시 주입은 보통 컴포넌트의 `Awake` 안에서 호출됩니다. 여기서 예외가 나가면
  `Instantiate` 호출자에게까지 전파됩니다.

> 격리는 "조용한 실패"와 다릅니다. 주입받지 못한 컴포넌트는 의존성이 `null`인 채로 남으므로,
> 콘솔의 예외 로그를 무시하면 안 됩니다.

---

## API

### `InjectorService : IStartable, IDisposable`

| 멤버 | 시그니처 | 설명 |
| --- | --- | --- |
| `Request` | `static void Request(MonoBehaviour target)` | 컴포넌트를 주입 대상으로 등록. 컨테이너가 준비됐으면 즉시 주입, 아니면 보류. `null`은 무시 |
| `Start` | `void Start()` | (EntryPoint) 컨테이너를 정적 참조에 바인딩하고 보류분을 일괄 주입 |
| `Dispose` | `void Dispose()` | 정적 상태(컨테이너 참조·보류 큐)를 초기화. 도메인 리로드 비활성화 환경 대비 |

### `InjectableBehaviour : MonoBehaviour` (abstract)

| 멤버 | 시그니처 | 설명 |
| --- | --- | --- |
| `Awake` | `protected virtual void Awake()` | `EnsureInjected()` 호출. 오버라이드 시 `base.Awake()` 필수 |
| `EnsureInjected` | `protected void EnsureInjected()` | 멱등 self-request. 아직 요청 전이면 `InjectorService.Request(this)` 호출 |

### DI 등록

```csharp
public static void RegisterInjector(this IContainerBuilder builder);
```
`InjectorService`를 EntryPoint로 등록합니다.

---

## 매뉴얼

### 이벤트 드리븐 flush

- `Request` 시 컨테이너가 준비됐으면 즉시 `Inject`, 아니면 보류 큐에 쌓습니다.
- `InjectorService.Start()`(EntryPoint) 시점에 컨테이너를 바인딩하고 보류분을 한 번에 주입합니다. 이후의 `Request`는 즉시 주입됩니다.
- 매 프레임 폴링이 없습니다. 동적 생성 컴포넌트는 런타임(컨테이너 준비 완료)에 `Request`하므로 즉시 주입됩니다.

### 초기화 순서

- VContainer `LifetimeScope`는 `Awake`에서 컨테이너를 빌드합니다. 같은 씬의 컴포넌트 `Awake`와 순서가 보장되지 않지만, 보류 큐가 이를 흡수하므로 순서에 무관합니다.

### 단일 컨테이너 모델

- 정적 컨테이너 참조를 전제로 합니다. 이 패키지는 루트 단일 `LifetimeScope` + `DontDestroyOnLoad`를 가정합니다. 자식 스코프에서 `RegisterInjector`를 중복 등록하지 마세요.
  씬 스코프에 등록한 서비스(예: `IUINavigator`)는 이 정적 리졸버로 해결되지 않습니다. 씬 배치 컴포넌트가 그런 서비스를 요구하면 `RegisterInjector`를 같은 씬 스코프에 두어야 합니다.

### 테스트

- EditMode 단위 테스트(`Tests/InjectorServiceTest.cs`, `Tests/InjectableBehaviourTest.cs`)는 `IObjectResolver`를 NSubstitute로 대체해 보류/flush/즉시 주입/멱등을 검증합니다. EditMode에서는 `AddComponent`가 `Awake`를 자동 호출하지 않으므로, 테스트는 `Awake`를 명시적으로 트리거합니다.
