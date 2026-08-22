# IAPService Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 게임 코드가 `IIapService` 하나로 Google Play / App Store의 소모성·비소모성 상품을 구매·복원할 수 있게 한다.

**Architecture:** AdService와 같은 3계층 — `IIapProvider`(SDK seam) → `IapService`(정책: 검증·지급·확정·소유 상태) → `IIapService`(게임이 보는 표면). Unity IAP는 `FOUNDATIONDI_UNITYIAP` 심볼이 걸린 옵셔널 어셈블리에 격리하고, 코어는 Dummy provider만으로 완전히 동작한다.

**Tech Stack:** Unity 6000.3.17f1, `com.unity.purchasing` 5.4.2, VContainer, `UnityEngine.Awaitable`, NUnit + NSubstitute(EditMode).

**Spec:** `docs/superpowers/specs/2026-08-23-iapservice-design.md`

## Global Constraints

- 네임스페이스는 `DarkNaku.FoundationDI` 하나.
- 런타임 코드는 `Assets/FoundationDI/Runtime/Services/IAPService/`, 테스트는 `Assets/FoundationDI/Tests/`(플랫 배치).
- 신규 async는 `UnityEngine.Awaitable`. 테스트 래핑만 `UniTask.ToCoroutine`을 쓴다(기존 테스트 관례).
- **await 뒤에 그 `Awaitable`의 `.IsCompleted`를 읽지 않는다** — Unity 6의 Awaitable은 단일 사용/풀 반환이라 detached 상태가 된다. 테스트는 await 이전에 단언할 것을 단언한다.
- 테스트 이름은 한국어 `should~` 의도 서술. 파일 수정은 Write로 통째 교체(UnityMCP 관례).
- 코어 asmdef(`FoundationDI`)는 `com.unity.purchasing`를 **참조하지 않는다.**
- 구조적 변경과 행동적 변경을 같은 커밋에 섞지 않는다. 제목에 `[STRUCTURAL]`/`[BEHAVIORAL]` 접두어.
- 매 태스크 종료 시 `run_tests(EditMode)` 전체를 돌리고 초록일 때만 커밋한다.

## File Structure

| 경로 | 책임 |
| --- | --- |
| `IAPService/IapTypes.cs` | 값 타입 전부 (`IapProduct`, `IapPurchase`, 결과/실패 struct, enum) |
| `IAPService/IIapService.cs` | 공개 계약 |
| `IAPService/IapService.cs` | 정책 계층 — 초기화·구매 파이프라인·소유 상태·이벤트 |
| `IAPService/IapServiceRegistration.cs` | `builder.RegisterIapService(settings)` |
| `IAPService/Fulfillment/IIapFulfillment.cs` `AutoConfirmFulfillment.cs` | 지급 seam + 기본 구현 |
| `IAPService/Validation/IReceiptValidator.cs` `NoopReceiptValidator.cs` | 검증 seam + 기본 구현 |
| `IAPService/Entitlements/IEntitlementStorage.cs` `PlayerPrefsEntitlementStorage.cs` | 비소모성 소유 캐시 |
| `IAPService/Providers/IIapProvider.cs` `IIapProviderFactory.cs` `IapProviderFactory.cs` `IapProviderRegistry.cs` | SDK seam + 선택 |
| `IAPService/Providers/Dummy/DummyIapProvider.cs` `DummyIapOptions.cs` | 에디터용 가짜 스토어 |
| `IAPService/Providers/UnityIAP/*` | `FoundationDI.UnityIAP` asmdef — 실제 SDK 어댑터 + 로컬 검증기 |
| `IAPService/Settings/IapProviderType.cs` `IapProductId.cs` `IapProductEntry.cs` `IapServiceSettings.cs` | 설정 SO |
| `Editor/IAPService/IapProductConstantsGenerator.cs` | 상수 클래스 생성기 |
| `Tests/IapTestDoubles.cs` | `FakeIapProvider`, `FakeFulfillment`, `FakeReceiptValidator`, `FakeEntitlementStorage` |
| `Tests/IapTypesTest.cs` `IapServiceTest.cs` `IapProviderFactoryTest.cs` `IapServiceRegistrationTest.cs` | 테스트 |

## 확정 시그니처 (모든 태스크가 이것에 맞춘다)

```csharp
public enum IapProductType { Consumable, NonConsumable }

public enum IapPurchaseOutcome
{
    Purchased, Restored, AlreadyOwned, UserCancelled, Deferred, NotReady, InvalidReceipt, Failed,
}

public readonly struct IapError
{
    public int Code { get; }
    public string Message { get; }
    public IapError(int code, string message);
    public override string ToString();          // "(코드) 메시지"
}

public readonly struct IapProductDefinition
{
    public string Id { get; }          // 게임이 쓰는 공용 ID
    public string StoreId { get; }     // 플랫폼 해석이 끝난 스토어 ID
    public IapProductType Type { get; }
    public IapProductDefinition(string id, string storeId, IapProductType type);
}

public readonly struct IapProduct
{
    public string Id { get; }
    public string StoreId { get; }
    public IapProductType Type { get; }
    public string Title { get; }
    public string Description { get; }
    public string LocalizedPrice { get; }
    public double Price { get; }
    public string CurrencyCode { get; }
    public bool IsAvailable { get; }
    public IapProduct(string id, string storeId, IapProductType type, string title, string description,
                      string localizedPrice, double price, string currencyCode, bool isAvailable);
}

public readonly struct IapPurchase
{
    public string ProductId { get; }
    public IapProductType Type { get; }
    public string TransactionId { get; }
    public string Receipt { get; }
    public double Price { get; }
    public string CurrencyCode { get; }
    public bool IsRestored { get; }
    public IapPurchase(string productId, IapProductType type, string transactionId, string receipt,
                       double price, string currencyCode, bool isRestored);
}

public readonly struct IapPurchaseResult
{
    public IapPurchaseOutcome Outcome { get; }
    public IapPurchase Purchase { get; }
    public IapError Error { get; }
    public bool IsSuccess { get; }   // Purchased | Restored | AlreadyOwned

    public static IapPurchaseResult Purchased(IapPurchase purchase);
    public static IapPurchaseResult Restored(IapPurchase purchase);
    public static IapPurchaseResult AlreadyOwned(IapPurchase purchase);
    public static IapPurchaseResult UserCancelled();
    public static IapPurchaseResult Deferred();
    public static IapPurchaseResult NotReady();
    public static IapPurchaseResult InvalidReceipt(IapError error);
    public static IapPurchaseResult Failed(IapError error);
}

public readonly struct IapRestoreResult
{
    public bool Success { get; }
    public int RestoredCount { get; }
    public IapError Error { get; }
    public static IapRestoreResult Ok(int restoredCount);
    public static IapRestoreResult Fail(IapError error);
}

// provider → 정책 계층
public readonly struct IapPendingPurchase
{
    public string StoreId { get; }
    public string TransactionId { get; }
    public string Receipt { get; }
    public bool IsRestored { get; }
    public IapPendingPurchase(string storeId, string transactionId, string receipt, bool isRestored);
}

public readonly struct IapPurchaseFailure
{
    public string StoreId { get; }
    public bool IsUserCancelled { get; }
    public IapError Error { get; }
    public IapPurchaseFailure(string storeId, bool isUserCancelled, IapError error);
}

public readonly struct IapProviderContext
{
    public IReadOnlyList<IapProductDefinition> Products { get; }
    public bool VerboseLogging { get; }
    public IapProviderContext(IReadOnlyList<IapProductDefinition> products, bool verboseLogging);
}

public interface IIapProvider : IDisposable
{
    string Name { get; }
    Awaitable<bool> InitializeAsync(IapProviderContext context);
    IReadOnlyList<IapProduct> Products { get; }
    bool Purchase(string storeId);
    void Confirm(string transactionId);
    Awaitable<bool> RestoreAsync();

    event Action<IapPendingPurchase> PurchasePending;
    event Action<IapPurchaseFailure> PurchaseFailed;
    event Action<string> PurchaseDeferred;      // storeId
}

public interface IIapFulfillment { Awaitable<bool> FulfillAsync(IapPurchase purchase); }
public interface IReceiptValidator { bool Validate(IapPurchase purchase, out IapError error); }

public interface IEntitlementStorage
{
    bool IsOwned(string productId);
    void SetOwned(string productId, bool owned);
}

public readonly struct IapServiceOptions
{
    public IReadOnlyList<IapProductDefinition> Products { get; }
    public bool VerboseLogging { get; }
    public IapServiceOptions(IReadOnlyList<IapProductDefinition> products, bool verboseLogging);
}

public sealed class IapService : IIapService
{
    public IapService(IIapProvider provider, IapServiceOptions options,
                      IIapFulfillment fulfillment = null,
                      IReceiptValidator validator = null,
                      IEntitlementStorage entitlements = null);
}

public enum IapProviderType { Dummy, UnityIAP }
```

**구매 파이프라인의 확정 순서:** 검증 → 지급(`FulfillAsync`) → `Confirm` → 소유 기록 → `Purchased` 이벤트 → 대기 중인 `PurchaseAsync` 완료.

---

## Task 1: 값 타입 + 플랫폼 ID

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/IAPService/IapTypes.cs`
- Create: `Assets/FoundationDI/Runtime/Services/IAPService/Settings/IapProductId.cs`
- Test: `Assets/FoundationDI/Tests/IapTypesTest.cs`

**Interfaces:**
- Produces: 위 "확정 시그니처"의 모든 값 타입 + `IapProductId`.

`IapProductId`는 `AdUnitId`와 같은 패턴이되, **비어 있으면 공용 ID로 폴백**하는 점이 다르다:

```csharp
[Serializable]
public struct IapProductId
{
    [SerializeField] private string _android;
    [SerializeField] private string _ios;

    public IapProductId(string android, string ios);
    public string Android { get; }
    public string iOS { get; }

    // 현재 빌드 타깃의 오버라이드. 비어 있으면 null이 아니라 빈 문자열.
    public string Current { get; }

    // 오버라이드가 비면 공용 ID를 그대로 쓴다. 대부분의 게임은 양 스토어에 같은 ID를 올린다.
    public string Resolve(string fallbackId) => string.IsNullOrEmpty(Current) ? fallbackId : Current;
}
```

- [ ] **Step 1: 실패하는 테스트 작성** — `Tests/IapTypesTest.cs`

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class IapTypesTest
{
    [Test]
    public void 플랫폼_오버라이드가_비면_공용_ID로_폴백한다()
    {
        var empty = new IapProductId(null, null);
        Assert.AreEqual("remove_ads", empty.Resolve("remove_ads"));
    }

    [Test]
    public void 플랫폼_오버라이드가_있으면_그것을_쓴다()
    {
#if UNITY_ANDROID
        var id = new IapProductId("com.game.remove_ads.android", "com.game.remove_ads.ios");
        Assert.AreEqual("com.game.remove_ads.android", id.Resolve("remove_ads"));
#elif UNITY_IOS
        var id = new IapProductId("com.game.remove_ads.android", "com.game.remove_ads.ios");
        Assert.AreEqual("com.game.remove_ads.ios", id.Resolve("remove_ads"));
#else
        Assert.Pass("모바일 타깃이 아니면 오버라이드가 없다");
#endif
    }

    [Test]
    public void 구매결과의_IsSuccess가_성공_결과에서만_참이다()
    {
        var purchase = new IapPurchase("gems", IapProductType.Consumable, "tx", "receipt", 4.99, "USD", false);

        Assert.IsTrue(IapPurchaseResult.Purchased(purchase).IsSuccess);
        Assert.IsTrue(IapPurchaseResult.Restored(purchase).IsSuccess);
        Assert.IsTrue(IapPurchaseResult.AlreadyOwned(purchase).IsSuccess);
        Assert.IsFalse(IapPurchaseResult.UserCancelled().IsSuccess);
        Assert.IsFalse(IapPurchaseResult.Deferred().IsSuccess);
        Assert.IsFalse(IapPurchaseResult.NotReady().IsSuccess);
        Assert.IsFalse(IapPurchaseResult.InvalidReceipt(new IapError(1, "bad")).IsSuccess);
        Assert.IsFalse(IapPurchaseResult.Failed(new IapError(2, "boom")).IsSuccess);
    }
}
```

- [ ] **Step 2: 컴파일 에러로 실패 확인** — `read_console`로 "IapProductId를 찾을 수 없음" 계열 에러 확인
- [ ] **Step 3: `IapTypes.cs` + `IapProductId.cs` 작성** — 위 확정 시그니처 그대로
- [ ] **Step 4: `run_tests(EditMode)` 전체 통과 확인**
- [ ] **Step 5: 커밋** — `[BEHAVIORAL] IAPService 값 타입 추가`

---

## Task 2: seam 인터페이스 + 테스트 대역 + 초기화

**Files:**
- Create: `IAPService/IIapService.cs`, `IAPService/Providers/IIapProvider.cs`, `IAPService/Fulfillment/IIapFulfillment.cs`, `IAPService/Fulfillment/AutoConfirmFulfillment.cs`, `IAPService/Validation/IReceiptValidator.cs`, `IAPService/Validation/NoopReceiptValidator.cs`, `IAPService/Entitlements/IEntitlementStorage.cs`, `IAPService/IapService.cs`
- Create: `Tests/IapTestDoubles.cs`, `Tests/IapServiceTest.cs`

**Interfaces:**
- Consumes: Task 1의 값 타입.
- Produces: `IapService` 생성자, `InitializeAsync`, `Products`, `TryGetProduct`, `IsOwned`.

테스트 대역(`IapTestDoubles.cs`)의 확정 형태:

```csharp
public class FakeIapProvider : IIapProvider
{
    public string Name => "Fake";
    public bool InitializeResult = true;
    public int InitializeCount;
    public readonly List<string> PurchaseCalls = new();
    public readonly List<string> ConfirmCalls = new();
    public bool PurchaseResult = true;
    public bool RestoreResult = true;
    public int RestoreCount;
    public bool IsDisposed;

    // InitializeAsync 도중(구독 이후) 발행할 미확정 구매. Unity IAP의 FetchPurchases 재전달을 흉내낸다.
    public readonly List<IapPendingPurchase> PendingOnInitialize = new();
    // RestoreAsync 도중 발행할 복원 구매.
    public readonly List<IapPendingPurchase> PendingOnRestore = new();

    public IReadOnlyList<IapProduct> Products => _products;
    private List<IapProduct> _products = new();

    public async Awaitable<bool> InitializeAsync(IapProviderContext context) { ... }
    public bool Purchase(string storeId) { PurchaseCalls.Add(storeId); return PurchaseResult; }
    public void Confirm(string transactionId) => ConfirmCalls.Add(transactionId);
    public async Awaitable<bool> RestoreAsync() { ... }
    public void Dispose() => IsDisposed = true;

    // 테스트가 직접 이벤트를 쏜다.
    public void RaisePending(IapPendingPurchase p) => PurchasePending?.Invoke(p);
    public void RaiseFailed(IapPurchaseFailure f) => PurchaseFailed?.Invoke(f);
    public void RaiseDeferred(string storeId) => PurchaseDeferred?.Invoke(storeId);

    public event Action<IapPendingPurchase> PurchasePending;
    public event Action<IapPurchaseFailure> PurchaseFailed;
    public event Action<string> PurchaseDeferred;
}

public class FakeFulfillment : IIapFulfillment
{
    public readonly List<IapPurchase> Calls = new();
    public bool Result = true;
    public bool Throw;
    public Awaitable<bool> FulfillAsync(IapPurchase purchase) { ... }
}

public class FakeReceiptValidator : IReceiptValidator
{
    public bool Result = true;
    public int CallCount;
    public bool Validate(IapPurchase purchase, out IapError error) { ... }
}

public class FakeEntitlementStorage : IEntitlementStorage
{
    public readonly HashSet<string> Owned = new();
    public bool IsOwned(string productId) => Owned.Contains(productId);
    public void SetOwned(string productId, bool owned) { ... }
}
```

`FakeIapProvider.InitializeAsync`는 context의 정의로 `_products`를 채운 뒤(가격은 `$0.99`/`0.99`/`USD` 고정),
`PendingOnInitialize`를 순서대로 발행하고 `InitializeResult`를 반환한다.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[UnityTest, Timeout(5000)]
public IEnumerator 초기화하면_provider_상품이_노출된다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider();
    var sut = NewService(provider);

    var ok = await sut.InitializeAsync();

    Assert.IsTrue(ok);
    Assert.IsTrue(sut.IsInitialized);
    Assert.AreEqual(2, sut.Products.Count);
    Assert.IsTrue(sut.TryGetProduct("gems", out var gems));
    Assert.AreEqual("gems_store", gems.StoreId);
});

[UnityTest, Timeout(5000)]
public IEnumerator 초기화_전_구매는_NotReady다() => UniTask.ToCoroutine(async () =>
{
    var sut = NewService(new FakeIapProvider());
    var result = await sut.PurchaseAsync("gems");
    Assert.AreEqual(IapPurchaseOutcome.NotReady, result.Outcome);
});

[UnityTest, Timeout(5000)]
public IEnumerator InitializeAsync는_재진입해도_provider를_한_번만_초기화한다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider();
    var sut = NewService(provider);

    var first = sut.InitializeAsync();
    var second = sut.InitializeAsync();

    Assert.IsTrue(await first);
    Assert.IsTrue(await second);
    Assert.AreEqual(1, provider.InitializeCount);
});
```

테스트 헬퍼:

```csharp
private static readonly IapProductDefinition[] Catalog =
{
    new("gems", "gems_store", IapProductType.Consumable),
    new("remove_ads", "remove_ads_store", IapProductType.NonConsumable),
};

private static IapService NewService(FakeIapProvider provider,
                                     FakeFulfillment fulfillment = null,
                                     FakeReceiptValidator validator = null,
                                     FakeEntitlementStorage entitlements = null)
    => new(provider, new IapServiceOptions(Catalog, false),
           fulfillment ?? new FakeFulfillment(),
           validator ?? new FakeReceiptValidator(),
           entitlements ?? new FakeEntitlementStorage());
```

- [ ] **Step 2: 실패 확인** (컴파일 에러)
- [ ] **Step 3: 구현** — 이벤트 구독은 **`_provider.InitializeAsync` 호출 전**에 끝낸다(Unity IAP가 Connect 전 구독을 요구하고, 재전달 구매가 초기화 중에 올라오기 때문). 재진입은 `AwaitableCompletionSource<bool>` 하나를 공유해 처리한다.
- [ ] **Step 4: 전체 테스트 통과 확인**
- [ ] **Step 5: 커밋** — `[BEHAVIORAL] IapService 초기화와 상품 노출`

---

## Task 3: 구매 파이프라인 (검증 → 지급 → 확정 → 이벤트)

**Files:**
- Modify: `IAPService/IapService.cs`
- Modify: `Tests/IapServiceTest.cs`

**Interfaces:**
- Produces: `PurchaseAsync`, `Purchased`, `OwnedChanged`.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[UnityTest, Timeout(5000)]
public IEnumerator 구매가_검증_지급_확정_순서로_진행된다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider();
    var fulfillment = new FakeFulfillment();
    var validator = new FakeReceiptValidator();
    var sut = NewService(provider, fulfillment, validator);
    await sut.InitializeAsync();

    IapPurchase? published = null;
    sut.Purchased += p => published = p;

    var task = sut.PurchaseAsync("gems");
    provider.RaisePending(new IapPendingPurchase("gems_store", "tx-1", "receipt-1", false));
    var result = await task;

    Assert.AreEqual(IapPurchaseOutcome.Purchased, result.Outcome);
    Assert.AreEqual(1, validator.CallCount);
    Assert.AreEqual(1, fulfillment.Calls.Count);
    Assert.AreEqual("gems", fulfillment.Calls[0].ProductId);
    CollectionAssert.AreEqual(new[] { "tx-1" }, provider.ConfirmCalls);
    Assert.IsTrue(published.HasValue, "확정 후 Purchased 이벤트가 발행되지 않았다");
});

[UnityTest, Timeout(5000)]
public IEnumerator 지급이_실패하면_확정하지_않는다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider();
    var fulfillment = new FakeFulfillment { Result = false };
    var entitlements = new FakeEntitlementStorage();
    var sut = NewService(provider, fulfillment, entitlements: entitlements);
    await sut.InitializeAsync();

    var task = sut.PurchaseAsync("remove_ads");
    provider.RaisePending(new IapPendingPurchase("remove_ads_store", "tx-2", "r", false));
    var result = await task;

    Assert.AreEqual(IapPurchaseOutcome.Failed, result.Outcome);
    Assert.IsEmpty(provider.ConfirmCalls, "지급 실패인데 확정했다");
    Assert.IsFalse(sut.IsOwned("remove_ads"));
});

[UnityTest, Timeout(5000)]
public IEnumerator 지급이_예외를_던져도_확정하지_않고_서비스가_살아있다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider();
    var sut = NewService(provider, new FakeFulfillment { Throw = true });
    await sut.InitializeAsync();

    LogAssert.Expect(LogType.Error, new Regex("IAPService"));

    var task = sut.PurchaseAsync("gems");
    provider.RaisePending(new IapPendingPurchase("gems_store", "tx-3", "r", false));
    var result = await task;

    Assert.AreEqual(IapPurchaseOutcome.Failed, result.Outcome);
    Assert.IsEmpty(provider.ConfirmCalls);
});

[UnityTest, Timeout(5000)]
public IEnumerator 영수증_검증에_실패하면_지급도_확정도_하지_않는다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider();
    var fulfillment = new FakeFulfillment();
    var sut = NewService(provider, fulfillment, new FakeReceiptValidator { Result = false });
    await sut.InitializeAsync();

    var task = sut.PurchaseAsync("gems");
    provider.RaisePending(new IapPendingPurchase("gems_store", "tx-4", "bad", false));
    var result = await task;

    Assert.AreEqual(IapPurchaseOutcome.InvalidReceipt, result.Outcome);
    Assert.IsEmpty(fulfillment.Calls, "검증 실패인데 지급했다");
    Assert.IsEmpty(provider.ConfirmCalls);
});

[UnityTest, Timeout(5000)]
public IEnumerator 비소모성은_확정_후_소유로_기록되고_소모성은_아니다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider();
    var sut = NewService(provider);
    await sut.InitializeAsync();

    string ownedChanged = null;
    sut.OwnedChanged += id => ownedChanged = id;

    var task = sut.PurchaseAsync("remove_ads");
    provider.RaisePending(new IapPendingPurchase("remove_ads_store", "tx-5", "r", false));
    await task;

    Assert.IsTrue(sut.IsOwned("remove_ads"));
    Assert.AreEqual("remove_ads", ownedChanged);

    var consumable = sut.PurchaseAsync("gems");
    provider.RaisePending(new IapPendingPurchase("gems_store", "tx-6", "r", false));
    await consumable;

    Assert.IsFalse(sut.IsOwned("gems"), "소모성이 소유로 기록됐다");
});
```

- [ ] **Step 2: 실패 확인**
- [ ] **Step 3: 구현**

핵심 골격:

```csharp
public async Awaitable<IapPurchaseResult> PurchaseAsync(string productId)
{
    if (_disposed || !_initialized) return IapPurchaseResult.NotReady();
    if (!TryGetDefinition(productId, out var definition)) return IapPurchaseResult.NotReady();
    if (definition.Type == IapProductType.NonConsumable && IsOwned(productId))
        return IapPurchaseResult.AlreadyOwned(OwnedPurchaseOf(definition));
    if (_pendingSource != null) return IapPurchaseResult.NotReady();   // 중복 호출

    _pendingSource = new AwaitableCompletionSource<IapPurchaseResult>();
    _pendingProductId = productId;

    if (!_provider.Purchase(definition.StoreId))
    {
        ClearPending();
        return IapPurchaseResult.Failed(new IapError(PurchaseStartFailed, "구매를 시작하지 못했다"));
    }

    return await _pendingSource.Awaitable;
}

private async void HandlePending(IapPendingPurchase pending)
{
    if (!TryGetDefinitionByStoreId(pending.StoreId, out var definition))
    {
        // 카탈로그에 없는 상품. 확정하면 영영 지급할 수 없으므로 확정하지 않는다.
        Debug.LogWarning($"[IAPService] 카탈로그에 없는 상품의 구매가 도착했다: {pending.StoreId}. 확정하지 않는다.");
        return;
    }

    var purchase = BuildPurchase(definition, pending);

    if (!_validator.Validate(purchase, out var validationError))
    {
        Debug.LogError($"[IAPService] 영수증 검증 실패: {purchase.ProductId} {validationError}");
        Complete(definition.Id, IapPurchaseResult.InvalidReceipt(validationError));
        return;
    }

    bool fulfilled;
    try { fulfilled = await _fulfillment.FulfillAsync(purchase); }
    catch (Exception e)
    {
        Debug.LogError($"[IAPService] 지급 핸들러가 예외를 던졌다: {e}");
        fulfilled = false;
    }

    if (!fulfilled)
    {
        // 확정하지 않는다 — 스토어가 다음 실행에 다시 내려준다.
        Complete(definition.Id, IapPurchaseResult.Failed(new IapError(FulfillmentFailed, "지급에 실패했다")));
        return;
    }

    _provider.Confirm(pending.TransactionId);

    if (definition.Type == IapProductType.NonConsumable && !_entitlements.IsOwned(definition.Id))
    {
        _entitlements.SetOwned(definition.Id, true);
        OwnedChanged?.Invoke(definition.Id);
    }

    if (pending.IsRestored) _restoredCount++;

    Purchased?.Invoke(purchase);
    Complete(definition.Id, pending.IsRestored
        ? IapPurchaseResult.Restored(purchase)
        : IapPurchaseResult.Purchased(purchase));
}
```

`Complete(productId, result)`는 대기 중인 구매의 상품과 일치할 때만 `_pendingSource`를 완료시키고 초기화한다.
일치하지 않으면(재전달/복원) 조용히 무시한다.

`HandlePending`이 `async void`인 이유: provider 이벤트 핸들러라 반환값을 기다릴 주체가 없다. 예외는 전부 내부에서 잡는다.

- [ ] **Step 4: 전체 테스트 통과 확인**
- [ ] **Step 5: 커밋** — `[BEHAVIORAL] IapService 구매 파이프라인`

---

## Task 4: 실패·취소·중복·이미 소유

**Files:**
- Modify: `IAPService/IapService.cs`
- Modify: `Tests/IapServiceTest.cs`

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[UnityTest, Timeout(5000)]
public IEnumerator 사용자_취소와_그_외_실패를_구분한다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider();
    var sut = NewService(provider);
    await sut.InitializeAsync();

    var cancelled = sut.PurchaseAsync("gems");
    provider.RaiseFailed(new IapPurchaseFailure("gems_store", true, new IapError(0, "cancelled")));
    Assert.AreEqual(IapPurchaseOutcome.UserCancelled, (await cancelled).Outcome);

    var failed = sut.PurchaseAsync("gems");
    provider.RaiseFailed(new IapPurchaseFailure("gems_store", false, new IapError(7, "network")));
    var result = await failed;
    Assert.AreEqual(IapPurchaseOutcome.Failed, result.Outcome);
    Assert.AreEqual(7, result.Error.Code);
});

[UnityTest, Timeout(5000)]
public IEnumerator 이미_소유한_비소모성은_스토어를_거치지_않는다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider();
    var entitlements = new FakeEntitlementStorage();
    entitlements.Owned.Add("remove_ads");
    var sut = NewService(provider, entitlements: entitlements);
    await sut.InitializeAsync();

    var result = await sut.PurchaseAsync("remove_ads");

    Assert.AreEqual(IapPurchaseOutcome.AlreadyOwned, result.Outcome);
    Assert.AreEqual("remove_ads", result.Purchase.ProductId);
    Assert.IsEmpty(provider.PurchaseCalls, "이미 소유인데 스토어를 호출했다");
});

[UnityTest, Timeout(5000)]
public IEnumerator 구매가_진행_중이면_두_번째_호출은_즉시_NotReady다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider();
    var sut = NewService(provider);
    await sut.InitializeAsync();

    var first = sut.PurchaseAsync("gems");
    var second = await sut.PurchaseAsync("gems");

    Assert.AreEqual(IapPurchaseOutcome.NotReady, second.Outcome);
    Assert.AreEqual(1, provider.PurchaseCalls.Count);

    provider.RaisePending(new IapPendingPurchase("gems_store", "tx-7", "r", false));
    await first;
});

[UnityTest, Timeout(5000)]
public IEnumerator provider가_구매_시작을_거부하면_Failed다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider { PurchaseResult = false };
    var sut = NewService(provider);
    await sut.InitializeAsync();

    var result = await sut.PurchaseAsync("gems");

    Assert.AreEqual(IapPurchaseOutcome.Failed, result.Outcome);
});

[UnityTest, Timeout(5000)]
public IEnumerator 카탈로그에_없는_상품_구매는_NotReady다() => UniTask.ToCoroutine(async () =>
{
    var sut = NewService(new FakeIapProvider());
    await sut.InitializeAsync();
    Assert.AreEqual(IapPurchaseOutcome.NotReady, (await sut.PurchaseAsync("unknown")).Outcome);
});
```

- [ ] **Step 2: 실패 확인**
- [ ] **Step 3: 구현** — `HandleFailed`는 대기 중 상품과 store ID가 일치할 때만 완료시킨다.
- [ ] **Step 4: 전체 테스트 통과 확인**
- [ ] **Step 5: 커밋** — `[BEHAVIORAL] IapService 실패·중복·소유 분기`

---

## Task 5: 재전달·복원·보류·Dispose

**Files:**
- Modify: `IAPService/IapService.cs`
- Modify: `Tests/IapServiceTest.cs`

**Interfaces:**
- Produces: `RestoreAsync`, `Dispose`.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[UnityTest, Timeout(5000)]
public IEnumerator 미확정_구매는_초기화_때_지급되고_확정된다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider();
    provider.PendingOnInitialize.Add(new IapPendingPurchase("gems_store", "tx-old", "r", false));
    var fulfillment = new FakeFulfillment();
    var sut = NewService(provider, fulfillment);

    await sut.InitializeAsync();
    await Awaitable.NextFrameAsync();   // async void 핸들러가 끝날 틈을 준다

    Assert.AreEqual(1, fulfillment.Calls.Count, "재전달된 구매가 지급되지 않았다");
    CollectionAssert.AreEqual(new[] { "tx-old" }, provider.ConfirmCalls);
});

[UnityTest, Timeout(5000)]
public IEnumerator 복원은_비소모성_소유를_되살리고_개수를_보고한다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider();
    provider.PendingOnRestore.Add(new IapPendingPurchase("remove_ads_store", "tx-r", "r", true));
    var sut = NewService(provider);
    await sut.InitializeAsync();

    string ownedChanged = null;
    sut.OwnedChanged += id => ownedChanged = id;

    var result = await sut.RestoreAsync();

    Assert.IsTrue(result.Success);
    Assert.AreEqual(1, result.RestoredCount);
    Assert.IsTrue(sut.IsOwned("remove_ads"));
    Assert.AreEqual("remove_ads", ownedChanged);
});

[UnityTest, Timeout(5000)]
public IEnumerator 복원이_실패하면_Success가_거짓이다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider { RestoreResult = false };
    var sut = NewService(provider);
    await sut.InitializeAsync();

    var result = await sut.RestoreAsync();

    Assert.IsFalse(result.Success);
    Assert.AreEqual(0, result.RestoredCount);
});

[UnityTest, Timeout(5000)]
public IEnumerator 보류된_구매는_지급하지_않고_Deferred를_반환한다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider();
    var fulfillment = new FakeFulfillment();
    var sut = NewService(provider, fulfillment);
    await sut.InitializeAsync();

    var task = sut.PurchaseAsync("gems");
    provider.RaiseDeferred("gems_store");
    var result = await task;

    Assert.AreEqual(IapPurchaseOutcome.Deferred, result.Outcome);
    Assert.IsEmpty(fulfillment.Calls);
});

[UnityTest, Timeout(5000)]
public IEnumerator Dispose하면_provider가_해제되고_이후_구매는_NotReady다() => UniTask.ToCoroutine(async () =>
{
    var provider = new FakeIapProvider();
    var sut = NewService(provider);
    await sut.InitializeAsync();

    sut.Dispose();
    sut.Dispose();   // 중복 Dispose 안전

    Assert.IsTrue(provider.IsDisposed);
    Assert.AreEqual(IapPurchaseOutcome.NotReady, (await sut.PurchaseAsync("gems")).Outcome);
});
```

- [ ] **Step 2: 실패 확인**
- [ ] **Step 3: 구현**
  - `RestoreAsync`: `_restoredCount = 0` → `await _provider.RestoreAsync()` → 성공이면 `IapRestoreResult.Ok(_restoredCount)`. 복원 구매는 `PurchasePending(IsRestored: true)`로 들어와 같은 파이프라인을 탄다.
  - `PurchaseDeferred`: 대기 중 상품과 일치하면 `Deferred`로 완료하고 대기를 비운다. 나중에 실제 구매가 오면 `PurchasePending`으로 들어와 지급된다.
  - `Dispose`: 이벤트 해제 → `_provider.Dispose()` → `_disposed = true`. 대기 중인 `PurchaseAsync`가 있으면 `NotReady`로 완료시켜 영원히 매달리지 않게 한다.
- [ ] **Step 4: 전체 테스트 통과 확인**
- [ ] **Step 5: 커밋** — `[BEHAVIORAL] IapService 재전달·복원·보류·Dispose`

---

## Task 6: 소유 저장소 (PlayerPrefs)

**Files:**
- Create: `IAPService/Entitlements/PlayerPrefsEntitlementStorage.cs`
- Modify: `Tests/IapServiceTest.cs` (또는 `Tests/IapEntitlementStorageTest.cs` 신규)

키는 `FoundationDI.IAP.Owned.<productId>`, 값은 0/1. `PlayerPrefsAdRemovalStorage`와 같은 형태.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
public class IapEntitlementStorageTest
{
    private const string ProductId = "test_remove_ads";

    [TearDown]
    public void TearDown() => PlayerPrefs.DeleteKey($"FoundationDI.IAP.Owned.{ProductId}");

    [Test]
    public void 저장한_소유_상태가_다시_읽힌다()
    {
        var storage = new PlayerPrefsEntitlementStorage();

        Assert.IsFalse(storage.IsOwned(ProductId));
        storage.SetOwned(ProductId, true);
        Assert.IsTrue(new PlayerPrefsEntitlementStorage().IsOwned(ProductId));
        storage.SetOwned(ProductId, false);
        Assert.IsFalse(new PlayerPrefsEntitlementStorage().IsOwned(ProductId));
    }
}
```

- [ ] **Step 2: 실패 확인**
- [ ] **Step 3: 구현**
- [ ] **Step 4: 전체 테스트 통과 확인**
- [ ] **Step 5: 커밋** — `[BEHAVIORAL] 비소모성 소유 PlayerPrefs 저장소`

---

## Task 7: Provider 선택 + 설정 SO + DI 등록

**Files:**
- Create: `IAPService/Settings/IapProviderType.cs`, `IapProductEntry.cs`, `IapServiceSettings.cs`
- Create: `IAPService/Providers/IIapProviderFactory.cs`, `IapProviderFactory.cs`, `IapProviderRegistry.cs`
- Create: `IAPService/Providers/Dummy/DummyIapOptions.cs` (빈 껍데기 아님 — 아래 값 포함)
- Create: `IAPService/IapServiceRegistration.cs`
- Create: `Tests/IapProviderFactoryTest.cs`, `Tests/IapServiceRegistrationTest.cs`

**Interfaces:**

```csharp
public static class IapProviderFactory   // 정적 부분
{
    public static bool IsAvailable(IapProviderType type);
    public static IapProviderType Resolve(IapProviderType requested, bool forceDummy, out string warning);
}

public interface IIapProviderFactory
{
    IIapProvider Create(IapProviderType type, DummyIapOptions dummyOptions, bool forceDummy);
}

public readonly struct IapProviderCreationContext { }   // 오늘은 비어 있다 — 나중 의존성 추가 지점

public static class IapProviderRegistry
{
    public static void Register(IapProviderType type, Func<IapProviderCreationContext, IIapProvider> creator);
    internal static bool TryResolve(IapProviderType type, out Func<IapProviderCreationContext, IIapProvider> creator);
    internal static void Reset();
}

[Serializable]
public struct DummyIapOptions
{
    public float DelaySeconds { get; }      // 기본 0.5
    public bool AlwaysFail { get; }
    public bool AlwaysCancel { get; }
    public string PriceFormat { get; }      // 기본 "$0.99"
    public static DummyIapOptions Default { get; }
}

[Serializable]
public class IapProductEntry
{
    public string Id { get; }
    public IapProductType Type { get; }
    public IapProductId StoreId { get; }
    public IapProductDefinition ToDefinition();   // StoreId.Resolve(Id)
}
```

`IapServiceSettings.ToOptions()`는 `Id`가 비었거나 중복인 항목을 걸러내고(각각 경고 로그) `IapServiceOptions`를 만든다.

`RegisterIapService`:

```csharp
public static IContainerBuilder RegisterIapService(this IContainerBuilder builder, IapServiceSettings settings)
{
    if (settings == null) { Debug.LogError("[IAPService] IapServiceSettings가 null이다. 서비스를 등록하지 않는다."); return builder; }

    builder.RegisterInstance(settings);
    builder.Register<IIapProviderFactory, IapProviderFactory>(Lifetime.Singleton);

    builder.Register<IIapService>(container =>
    {
        var factory = container.Resolve<IIapProviderFactory>();
        var forceDummy = settings.ForceDummyInEditor && Application.isEditor;
        var provider = factory.Create(settings.Provider, settings.DummyOptions, forceDummy);

        // 게임이 등록하지 않았으면 기본 구현으로 폴백한다 — 등록 순서에 의존하지 않는다.
        var fulfillment = ResolveOrDefault<IIapFulfillment>(container, () => new AutoConfirmFulfillment());
        var validator = ResolveOrDefault<IReceiptValidator>(container, CreateDefaultValidator);
        var entitlements = ResolveOrDefault<IEntitlementStorage>(container, () => new PlayerPrefsEntitlementStorage());

        return new IapService(provider, settings.ToOptions(), fulfillment, validator, entitlements);
    }, Lifetime.Singleton);

    return builder;
}
```

`ResolveOrDefault<T>`는 `container.TryResolve<T>(out var value)`를 쓰고 실패 시 팩토리를 호출한다.
`CreateDefaultValidator`는 `IapReceiptValidatorRegistry.Current ?? new NoopReceiptValidator()`를 돌려준다
(옵셔널 어셈블리가 심볼이 있을 때 자신의 검증기를 등록해 두는 정적 슬롯 — Task 9에서 채운다).

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
public class IapProviderFactoryTest
{
    [TearDown] public void TearDown() => IapProviderRegistry.Reset();

    [Test]
    public void 강제_더미면_요청과_무관하게_Dummy다()
    {
        var resolved = IapProviderFactory.Resolve(IapProviderType.UnityIAP, forceDummy: true, out var warning);
        Assert.AreEqual(IapProviderType.Dummy, resolved);
        Assert.IsNull(warning, "의도된 강제 더미인데 경고를 냈다");
    }

    [Test]
    public void 심볼이_없으면_경고와_함께_Dummy로_대체한다()
    {
        var resolved = IapProviderFactory.Resolve(IapProviderType.UnityIAP, forceDummy: false, out var warning);
#if FOUNDATIONDI_UNITYIAP
        Assert.AreEqual(IapProviderType.UnityIAP, resolved);
#else
        Assert.AreEqual(IapProviderType.Dummy, resolved);
        StringAssert.Contains("FOUNDATIONDI_UNITYIAP", warning);
#endif
    }

    [Test]
    public void 등록된_creator가_있으면_그것으로_만든다()
    {
        var stub = new FakeIapProvider();
        IapProviderRegistry.Register(IapProviderType.UnityIAP, _ => stub);

        var factory = new IapProviderFactory();
        Assert.AreSame(stub, factory.Build(IapProviderType.UnityIAP, DummyIapOptions.Default));
    }

    [Test]
    public void 사용_가능하다고_판단됐는데_creator가_없으면_에러_후_Dummy다()
    {
        LogAssert.Expect(LogType.Error, new Regex("IAPService"));
        var factory = new IapProviderFactory();
        Assert.IsInstanceOf<DummyIapProvider>(factory.Build(IapProviderType.UnityIAP, DummyIapOptions.Default));
    }
}
```

```csharp
public class IapServiceRegistrationTest
{
    [Test]
    public void RegisterIapService로_IIapService가_싱글턴_등록된다()
    {
        var settings = ScriptableObject.CreateInstance<IapServiceSettings>();
        var builder = new ContainerBuilder();
        builder.RegisterIapService(settings);

        using var container = builder.Build();
        var a = container.Resolve<IIapService>();
        var b = container.Resolve<IIapService>();

        Assert.IsNotNull(a);
        Assert.AreSame(a, b);
        Object.DestroyImmediate(settings);
    }

    [Test]
    public void settings가_null이면_등록하지_않고_에러를_남긴다()
    {
        LogAssert.Expect(LogType.Error, new Regex("IapServiceSettings"));
        var builder = new ContainerBuilder();
        builder.RegisterIapService(null);

        using var container = builder.Build();
        Assert.IsFalse(container.TryResolve<IIapService>(out _));
    }
}
```

`IapProviderFactory.Build`는 AdService와 같은 이유로 `internal`이고 `FoundationDI` asmdef에
`InternalsVisibleTo("FoundationDI.Tests")`가 이미 걸려 있다(없으면 Task 7에서 추가한다).

- [ ] **Step 2: 실패 확인**
- [ ] **Step 3: 구현** (Dummy provider는 Task 8에서 채우되, 컴파일을 위해 이 태스크에서 최소 골격을 만든다)
- [ ] **Step 4: 전체 테스트 통과 확인**
- [ ] **Step 5: 커밋** — `[BEHAVIORAL] IAP provider 선택과 DI 등록`

---

## Task 8: Dummy provider

**Files:**
- Modify: `IAPService/Providers/Dummy/DummyIapProvider.cs`
- Create: `Tests/DummyIapProviderTest.cs`

동작:
- `InitializeAsync`: context 정의로 `IapProduct` 목록을 만든다(`Title = "<Id> (Dummy)"`, `LocalizedPrice = options.PriceFormat`, `Price = 0.99`, `CurrencyCode = "USD"`, `IsAvailable = true`). 이전에 확정된 비소모성을 `PlayerPrefs`(`FoundationDI.IAP.Dummy.Owned.<storeId>`)에서 읽어 `PurchasePending(IsRestored: true)`로 재발행한다.
- `Purchase(storeId)`: 모르는 storeId면 false. `AlwaysCancel`이면 `DelaySeconds` 뒤 `PurchaseFailed(IsUserCancelled: true)`, `AlwaysFail`이면 `PurchaseFailed(IsUserCancelled: false)`, 아니면 `PurchasePending`.
- `Confirm(transactionId)`: 해당 거래가 비소모성이면 PlayerPrefs에 기록.
- `RestoreAsync`: 기록된 비소모성을 `IsRestored: true`로 발행하고 true 반환.
- 트랜잭션 ID는 `$"dummy-{storeId}-{_sequence++}"`.

- [ ] **Step 1: 실패하는 테스트 작성** — 지연을 0으로 준 옵션으로 구매→pending 발행, `AlwaysCancel`→취소 실패, 확정한 비소모성이 `RestoreAsync`에서 되돌아옴, 모르는 storeId는 false. `[TearDown]`에서 PlayerPrefs 키를 지운다.
- [ ] **Step 2: 실패 확인**
- [ ] **Step 3: 구현**
- [ ] **Step 4: 전체 테스트 통과 확인**
- [ ] **Step 5: 커밋** — `[BEHAVIORAL] Dummy IAP provider`

---

## Task 9: Unity IAP 어댑터 (옵셔널 어셈블리)

**Files:**
- Modify: `Packages/manifest.json` (`"com.unity.purchasing": "5.4.2"`)
- Create: `IAPService/Providers/UnityIAP/FoundationDI.UnityIAP.asmdef` (references: `FoundationDI`, `Unity.Purchasing`; defineConstraints: `FOUNDATIONDI_UNITYIAP`)
- Create: `UnityIapProvider.cs`, `UnityIapInstaller.cs`, `CrossPlatformReceiptValidator.cs`
- Create: `IAPService/Validation/IapReceiptValidatorRegistry.cs` (코어 쪽 정적 슬롯)
- Modify: `ProjectSettings/ProjectSettings.asset` — Android/iOS 스크립팅 심볼에 `FOUNDATIONDI_UNITYIAP` 추가

`UnityIapProvider` 매핑:

| IIapProvider | Unity IAP v5 |
| --- | --- |
| `InitializeAsync` | 이벤트 전부 구독 → `await controller.Connect()` → `FetchProducts` → `FetchPurchases` (각각 완료 이벤트를 `AwaitableCompletionSource`로 기다린다) |
| `Purchase(storeId)` | `controller.PurchaseProduct(storeId)` |
| `Confirm(transactionId)` | 보관 중인 `PendingOrder`를 찾아 `controller.ConfirmPurchase(order)` |
| `RestoreAsync` | `controller.RestoreTransactions(callback)` (iOS/Android 공통 호출) |
| `PurchasePending` | `OnPurchasePending` + `OnPurchasesFetched`의 `PendingOrders`(후자는 `IsRestored: true`) |
| `PurchaseFailed` | `OnPurchaseFailed` — `FailureReason.UserCancelled`면 `IsUserCancelled: true` |
| `PurchaseDeferred` | `OnPurchaseDeferred` |

`ProcessPendingOrdersOnPurchasesFetched(false)`를 호출해 SDK가 임의로 확정하지 않게 하고,
확정 시점은 전적으로 정책 계층이 정한다. `TransactionId`는 `PendingOrder.Info.TransactionID`,
영수증은 `order.Info.Receipt`, 가격/통화는 `Product.metadata`에서 가져온다.

`UnityIapInstaller`는 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`에서
`IapProviderRegistry.Register(IapProviderType.UnityIAP, _ => new UnityIapProvider())`와
`IapReceiptValidatorRegistry.Current = new CrossPlatformReceiptValidator()`를 등록한다.

`CrossPlatformReceiptValidator`: `UNITY_ANDROID && !UNITY_EDITOR`에서만 `CrossPlatformValidator`로 검증하고,
그 외에는 통과. `GooglePlayTangle` 타입이 없으면(Obfuscator 미실행) `#if !FOUNDATIONDI_IAP_TANGLE`로 컴파일에서 제외하고
최초 1회 경고를 남긴 뒤 통과시킨다.

- [ ] **Step 1: 패키지 설치** — `manage_packages(add_package, "com.unity.purchasing@5.4.2")`, `read_console`로 컴파일 확인
- [ ] **Step 2: asmdef + 어댑터 작성** (심볼 미정의 상태라 컴파일에서 제외됨을 확인)
- [ ] **Step 3: `FOUNDATIONDI_UNITYIAP` 심볼 정의 후 컴파일 확인** — `read_console`에 에러 0
- [ ] **Step 4: 전체 테스트 통과 확인** (심볼이 켜지면 `IapProviderFactoryTest`의 `#if` 분기가 바뀌는 것까지 확인)
- [ ] **Step 5: 커밋** — `[BEHAVIORAL] Unity IAP 어댑터 추가`

---

## Task 10: 상품 상수 생성기 (에디터)

**Files:**
- Create: `Assets/FoundationDI/Editor/IAPService/IapProductConstantsGenerator.cs`

`Tools/FoundationDI/IAP/Generate Product Constants` 메뉴. 선택되었거나 프로젝트에서 찾은 `IapServiceSettings`의
상품 목록으로 다음을 `<설정 SO 폴더>/Generated/IapProducts.cs`에 쓴다:

```csharp
// <auto-generated> IapProductConstantsGenerator가 생성했다. 직접 편집하지 말 것.
namespace DarkNaku.FoundationDI
{
    public static class IapProducts
    {
        public const string RemoveAds = "remove_ads";
    }
}
```

식별자는 `Id`를 PascalCase로 변환하고(`remove_ads` → `RemoveAds`), 숫자로 시작하면 `_`를 붙인다.
같은 폴더에 `FoundationDI.asmref`를 생성해 `FoundationDI` 어셈블리에 합류시킨다(SoundService 생성기와 같은 방식 —
그 구현을 참고해 중복 코드를 만들지 말고 필요한 부분만 가져온다).

- [ ] **Step 1: SoundService 상수 생성기 구현 확인** — `Assets/FoundationDI/Editor/SoundService/` 아래
- [ ] **Step 2: 생성기 작성**
- [ ] **Step 3: 메뉴 실행 후 생성 결과 확인** — `read_console` 에러 0, 생성 파일 존재
- [ ] **Step 4: 전체 테스트 통과 확인**
- [ ] **Step 5: 커밋** — `[BEHAVIORAL] IAP 상품 상수 생성기`

---

## Task 11: 문서 + 호스트 배선 + 스모크

**Files:**
- Create: `IAPService/README.md`
- Modify: `CLAUDE.md` (서비스 목록에 IAPService 추가)
- Modify: `plan.md` (완료 이동)
- Create: `Assets/Scripts/IapServiceSmokeTest.cs`
- Modify: `Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs`

README에는 API 표, 지급 파이프라인 다이어그램, Google Play Tangle 생성 절차
(`Services > In-App Purchasing > Receipt Validation Obfuscator`), 스토어 콘솔 설정 체크리스트,
`IIapFulfillment` 구현 예제를 담는다.

`IapServiceSmokeTest`는 `AdServiceSmokeTest`와 같은 형태로 화면 버튼을 만들어
초기화 → 상품 목록 → 구매 → 복원을 에디터에서 눌러볼 수 있게 한다.

- [ ] **Step 1: README 작성**
- [ ] **Step 2: 호스트 배선 + 스모크 스크립트 작성**
- [ ] **Step 3: 에디터에서 Dummy provider로 구매·복원 플로우 수동 확인** — `read_console` 에러 0
- [ ] **Step 4: 전체 테스트 통과 확인**
- [ ] **Step 5: 커밋** — 문서/계획은 `[STRUCTURAL]`, 배선/스모크는 `[BEHAVIORAL]`로 **분리 커밋**

---

## Self-Review

- **스펙 커버리지:** 값 타입(T1) · 공개 계약과 초기화(T2) · 지급 파이프라인(T3) · 실패 분기(T4) · 재전달/복원/보류(T5) · 소유 저장소(T6) · provider 선택·설정·DI(T7) · Dummy(T8) · Unity IAP 어댑터와 검증기(T9) · 상수 생성기(T10) · 문서와 배선(T11). 스펙의 17개 테스트 항목이 T1~T7에 모두 배치됐다.
- **플레이스홀더:** 없음. 모든 코드 스텝에 실제 시그니처와 테스트 코드가 있다.
- **타입 일관성:** `IapPendingPurchase.StoreId` ↔ `IapProductDefinition.StoreId`, `IEntitlementStorage.IsOwned/SetOwned`, `IIapFulfillment.FulfillAsync`, `IReceiptValidator.Validate(out IapError)` — 모든 태스크에서 같은 이름을 쓴다.
