# InjectorService

씬에 디자이너가 직접 배치한(=`LifetimeScope`가 생성하지 않은) MonoBehaviour에 VContainer 의존성을 주입하는 **인프라**입니다. 컴포넌트는 정적 진입점에 자신을 등록하고, 컨테이너가 준비되면 주입받습니다. `SoundButton`이 첫 사용처입니다.

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

### 테스트

- EditMode 단위 테스트(`Tests/InjectorServiceTest.cs`, `Tests/InjectableBehaviourTest.cs`)는 `IObjectResolver`를 NSubstitute로 대체해 보류/flush/즉시 주입/멱등을 검증합니다. EditMode에서는 `AddComponent`가 `Awake`를 자동 호출하지 않으므로, 테스트는 `Awake`를 명시적으로 트리거합니다.
