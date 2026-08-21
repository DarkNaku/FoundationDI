# MessageService

**타입을 채널로 삼는 인-메모리 pub-sub 서비스**입니다. 발행자와 구독자가 서로를 참조하지 않고 메시지 타입 하나로만 연결됩니다. 외부 메시징 라이브러리에 의존하지 않는 순수 C# 구현입니다.

- **타입 = 채널** — `Publish<T>`로 보내면 `Subscribe<T>`한 핸들러만 받습니다
- **메시지 타입 제약 없음** — `struct`/`class` 모두 발행할 수 있습니다
- **`IDisposable` 구독 토큰** — `Subscribe`가 반환하는 토큰을 버리면 해제됩니다. 핸들러 참조를 보관했다가 `RemoveListener`로 짝을 맞출 필요가 없습니다
- **발행 중 구독 변경 안전** — 발행은 시작 시점의 스냅샷으로 완주합니다
- **핸들러 예외 격리** — 한 구독자가 던진 예외가 뒤따르는 구독자를 건너뛰지 않습니다
- **메인 스레드 전제** — 잠금이 없습니다. 여러 스레드에서 동시에 호출하면 안 됩니다

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
        builder.RegisterMessageService();
    }
}
```

싱글턴으로 등록되며, 컨테이너가 Dispose될 때 `MessageService.Dispose`가 호출되어 남은 구독이 모두 정리됩니다.

### 2) 메시지 정의

메시지는 그냥 타입입니다. 상속하거나 속성을 붙일 필요가 없습니다.

```csharp
public readonly struct ScoreChanged
{
    public readonly int Value;
    public ScoreChanged(int value) => Value = value;
}
```

### 3) 발행

```csharp
public class ScoreModel
{
    private readonly IMessageService _message;

    public ScoreModel(IMessageService message) => _message = message;

    public void Add(int amount)
    {
        _score += amount;
        _message.Publish(new ScoreChanged(_score));
    }
}
```

구독자가 없는 타입을 발행해도 아무 일도 일어나지 않습니다.

### 4) 구독과 해제

```csharp
private IDisposable _subscription;

private void OnEnable()
{
    _subscription = _message.Subscribe<ScoreChanged>(OnScoreChanged);
}

private void OnDisable()
{
    _subscription?.Dispose();
}

private void OnScoreChanged(ScoreChanged m) => _label.text = m.Value.ToString();
```

### 5) MonoBehaviour 수명에 묶기 (R3)

구독 토큰이 `IDisposable`이므로 R3의 `AddTo`를 그대로 쓸 수 있습니다. 해제 코드를 따로 쓰지 않아도 오브젝트가 파괴될 때 함께 해제됩니다.

```csharp
using R3;

private void Start()
{
    _message.Subscribe<ScoreChanged>(OnScoreChanged).AddTo(this);
    _message.Subscribe<LevelUp>(OnLevelUp).AddTo(this);
}
```

여러 구독을 한 번에 끊고 싶으면 `CompositeDisposable`에 모읍니다.

```csharp
private readonly CompositeDisposable _disposables = new();

private void OnEnable()
{
    _message.Subscribe<ScoreChanged>(OnScoreChanged).AddTo(_disposables);
    _message.Subscribe<LevelUp>(OnLevelUp).AddTo(_disposables);
}

private void OnDisable() => _disposables.Clear();
```

---

## API

| 멤버 | 설명 |
| --- | --- |
| `void Publish<T>(T message)` | `T`를 구독한 모든 핸들러를 등록 순서대로 호출한다. 구독자가 없으면 아무 일도 하지 않는다 |
| `IDisposable Subscribe<T>(Action<T> handler)` | 핸들러를 등록하고 해제 토큰을 반환한다. `handler`가 `null`이면 `ArgumentNullException` |
| `void Dispose()` | 모든 구독을 해제한다. 이후 `Publish`/`Subscribe`는 `ObjectDisposedException`을 던진다 |

같은 핸들러를 두 번 구독하면 두 번 호출됩니다. 이때 토큰 하나를 `Dispose`하면 등록 하나만 빠집니다. 토큰의 중복 `Dispose`와 서비스의 중복 `Dispose`는 모두 무해합니다.

---

## 설계 노트

**발행은 스냅샷으로 진행한다.** `Publish`는 호출 시점의 구독자 목록을 떠서 순회합니다. 따라서 핸들러 안에서 다른 구독을 해제해도 그 구독자는 **이번 발행까지는 호출되고**, 핸들러 안에서 새로 구독하면 **다음 발행부터** 호출됩니다. 발행 도중 목록이 바뀌어 순회가 깨지는 일이 없습니다.

**핸들러 예외는 격리한다.** 핸들러를 하나씩 호출하고 예외는 `Debug.LogException`으로 흘립니다. 멀티캐스트 델리게이트를 한 번에 `Invoke`하면 앞선 핸들러의 예외가 뒤따르는 핸들러를 조용히 건너뛰는데, 이 문제를 막기 위한 선택입니다. 대가로 발행마다 호출 목록 배열이 하나 할당됩니다.

**메인 스레드 전제다.** 내부는 잠금 없는 `Dictionary<Type, Delegate>` 하나입니다(`ResourceService`와 같은 전제). 백그라운드 스레드에서 발행해야 한다면 호출자가 메인 스레드로 마샬링해야 합니다.

**정적 전역이 아니다.** 서비스 인스턴스가 구독을 소유하므로 스코프가 사라지면 구독도 사라집니다. Domain Reload를 끈 상태에서 플레이 모드를 반복해도 이전 세션의 핸들러가 살아남지 않습니다.

## 알려진 범위 외

- 비동기 발행/구독 없음 — 필요해지면 `Awaitable` 기반으로 추가한다
- 구독 우선순위·필터·버퍼링(마지막 값 재생) 없음
- 스레드 안전성 없음
