# InitializeService

**게임 부트스트랩 순차 초기화 서비스**입니다. 초기화 단위를 ScriptableObject(`InitializeItem`)로 정의하고, 여러 항목을 묶은 카탈로그(`InitializeCatalog`)를 `IInitializeService.InitializeAsync(catalog)`에 넘기면 리스트 순서대로 **순차(직렬)** 실행합니다. 서비스 세션(생성~`Dispose`) 동안 아이템·카탈로그 단위로 완료 여부를 기억해 **중복 실행을 방지**합니다.

- **선언형 초기화 항목** — `InitializeItem`을 상속한 SO를 만들고 `InitializeAsync(IObjectResolver)`에 초기화 로직을 작성
- **카탈로그로 묶어서 실행** — `InitializeCatalog` SO 에셋에 항목들을 리스트로 등록, 실행 순서 = 리스트 순서
- **세션 내 중복 방지** — 이미 완료된 아이템/카탈로그는 재호출 시 스킵(카탈로그 전체 완료 시 즉시 반환)
- **예외 즉시 전파 + 실패 지점부터 재개** — 아이템에서 던진 예외는 그대로 호출자에게 전파되고, 실패한 아이템은 미완료로 남아 다음 호출에서 그 지점부터 이어서 실행

---

## 사용법

### 1) 초기화 항목(`InitializeItem`) 작성

`InitializeItem`을 상속하고 `InitializeAsync(IObjectResolver resolver)`를 오버라이드합니다. `resolver.Resolve<T>()`로 DI 컨테이너의 다른 서비스에 접근할 수 있으며, 내부에서 UniTask를 `await`해도 됩니다(UniTask도 어웨이터를 제공하므로 `Awaitable` 반환 메서드 안에서 자유롭게 혼용 가능).

```csharp
using UnityEngine;
using VContainer;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;

[CreateAssetMenu(menuName = "MyGame/Initialize/RemoteConfigInitializeItem")]
public class RemoteConfigInitializeItem : InitializeItem
{
    public override async Awaitable InitializeAsync(IObjectResolver resolver)
    {
        var remoteConfig = resolver.Resolve<IRemoteConfigService>();
        await remoteConfig.FetchAsync(); // UniTask 반환 메서드도 await 가능
    }
}
```

이렇게 만든 항목은 `Create > MyGame > Initialize > RemoteConfigInitializeItem` 메뉴로 SO 에셋을 생성해 사용합니다.

### 2) 카탈로그(`InitializeCatalog`) 에셋 생성

`Create > DarkNaku > InitializeCatalog` 메뉴로 `InitializeCatalog` 에셋을 만들고, 인스펙터의 `Items` 리스트에 위에서 만든 `InitializeItem` SO들을 순서대로 등록합니다. 실행 순서는 이 리스트 순서를 그대로 따릅니다.

### 3) DI 등록

```csharp
// RootLifetimeScope.Configure(IContainerBuilder builder)
builder.RegisterInitializeService();
```

`IObjectResolver`는 VContainer가 자동으로 주입하므로 별도 등록이 필요 없습니다.

### 4) 호출

부트스트랩/플로우 시작 지점에서 생성자로 `IInitializeService`를 주입받아 카탈로그를 넘겨 호출합니다.

```csharp
public class Bootstrap
{
    [SerializeField] private InitializeCatalog _catalog;

    private readonly IInitializeService _initializeService;

    public Bootstrap(IInitializeService initializeService)
    {
        _initializeService = initializeService;
    }

    public async Awaitable RunAsync()
    {
        await _initializeService.InitializeAsync(_catalog);
        // 이후 게임 진입점 코드 (씬 전환, 첫 화면 표시 등)
    }
}
```

---

## API

### `IInitializeService : IDisposable`

| 멤버 | 시그니처 | 설명 |
| --- | --- | --- |
| `InitializeAsync` | `Awaitable InitializeAsync(InitializeCatalog catalog)` | 카탈로그의 `Items`를 리스트 순서대로 순차 실행합니다. 이미 완료된 아이템/카탈로그는 스킵합니다. |
| `Dispose` | `void Dispose()` | 완료 기록(아이템/카탈로그)을 모두 지웁니다. 이후 호출은 처음부터 다시 실행됩니다. |

### `InitializeItem`

```csharp
public abstract class InitializeItem : ScriptableObject
{
    public abstract Awaitable InitializeAsync(IObjectResolver resolver);
}
```

초기화 단위 1개를 나타내는 추상 SO입니다. `resolver`는 `InitializeService` 생성 시 주입된 `IObjectResolver`(루트 컨테이너)가 그대로 전달됩니다.

### `InitializeCatalog`

```csharp
[CreateAssetMenu(fileName = "InitializeCatalog", menuName = "DarkNaku/InitializeCatalog")]
public class InitializeCatalog : ScriptableObject
{
    public IReadOnlyList<InitializeItem> Items { get; }
}
```

`InitializeItem` SO들을 순서대로 보유하는 SO 에셋입니다. 실행 순서 = `Items` 리스트 순서입니다.

### 생성자

```csharp
public InitializeService(IObjectResolver resolver); // VContainer가 자동 주입
```

`RegisterInitializeService()`로 등록하면 `IObjectResolver`는 VContainer가 자동으로 채워주므로 직접 생성할 일은 거의 없습니다.

---

## 매뉴얼

### 중복 방지(dedup)

- **아이템 단위** — 한 번 성공적으로 완료된 `InitializeItem`(참조 동일성 기준)은 이후 어떤 카탈로그를 통해 다시 만나도 재실행되지 않습니다. 여러 카탈로그가 같은 아이템을 공유해도 실행은 1회입니다.
- **카탈로그 단위** — 카탈로그의 모든 아이템이 성공적으로 끝나면 그 카탈로그는 완료로 기록됩니다. 완료된 카탈로그로 `InitializeAsync`를 다시 호출하면 아이템 순회조차 하지 않고 즉시 반환합니다.
- **null 아이템** — 리스트에 빈 슬롯(`null`)이 있으면 조용히 건너뜁니다.
- 이 dedup은 **서비스 인스턴스의 세션(생성 ~ `Dispose`) 동안만** 유지됩니다. 영구 저장(예: `PlayerPrefs`)되지 않습니다.

### 실패 처리와 재개

- 아이템의 `InitializeAsync`가 예외를 던지면 그 예외는 `await` 지점을 통해 그대로 호출자에게 전파됩니다. 실패한 아이템은 완료로 기록되지 않으며, 그 이후 아이템들은 이번 호출에서 실행되지 않습니다.
- 카탈로그도 완료로 기록되지 않으므로, 같은 카탈로그로 `InitializeAsync`를 다시 호출하면 **처음부터가 아니라 실패했던 아이템부터** 이어서 실행됩니다(이전에 이미 성공한 아이템들은 dedup으로 자동 스킵).
- 재시도 횟수 제한, 백오프, 에러 UI 표시 등의 재시도 전략은 호출자(부트스트랩 코드) 책임입니다.

### 정리(Dispose)

- `Dispose()`는 아이템·카탈로그 완료 기록을 모두 지웁니다. 이후 같은 카탈로그로 `InitializeAsync`를 호출하면 처음부터 다시 실행됩니다.
- VContainer가 `Singleton` 수명을 관리하면 컨테이너 Dispose 시 자동 호출됩니다.

### 범위 밖 (Out of scope)

- **병렬 실행 미지원** — 아이템은 항상 리스트 순서대로 순차 실행되며, 동시 실행/병렬화는 하지 않습니다.
- **우선순위 없음** — 아이템 간 우선순위 개념이 없습니다. 실행 순서는 카탈로그 인스펙터의 리스트 순서가 전부입니다.
- **진행률 보고 없음** — 몇 번째 아이템이 실행 중인지, 몇 %가 끝났는지 알려주는 API가 없습니다.
- **취소(CancellationToken) 미지원** — 실행 중인 `InitializeAsync`를 외부에서 취소할 수 없습니다.
- **스레드 안전성 없음** — 다른 FoundationDI 서비스와 동일하게 Unity 메인 스레드 사용을 전제로 합니다.
- **자동 실행 없음** — 게임 진입점에서 카탈로그를 자동으로 찾아 실행해주는 기능은 포함하지 않습니다. 부트스트랩 코드에서 명시적으로 `InitializeAsync`를 호출해야 합니다.
