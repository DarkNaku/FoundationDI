# IAPService 설계 — 모바일 인앱 구매 서비스

- 작성일: 2026-08-23
- 상태: 승인됨
- 대상 스토어: Google Play, Apple App Store
- SDK: Unity In-App Purchasing `com.unity.purchasing` 5.4.2

## 배경 / 목표

게임 코드가 `IIapService` 하나만 알면 소모성/비소모성 상품을 구매·복원할 수 있게 한다.
스토어별 차이(구글은 로컬 영수증 검증, 애플은 StoreKit 2), Unity IAP v5의 Order 모델,
미확정 구매의 재전달 같은 세부는 전부 서비스 안에 가둔다.

호출부의 이상적인 모습:

```csharp
var result = await _iap.PurchaseAsync(IapProducts.RemoveAds);
if (result.Outcome == IapPurchaseOutcome.Purchased) _ads.AdsRemoved = true;
```

## 확정된 설계 결정

1. **로컬 검증만.** 서버 검증은 범위 밖이되 `IReceiptValidator` seam은 열어둔다.
2. **소모성 + 비소모성만.** 구독은 별도 계획.
3. **Unity IAP는 옵셔널 어셈블리.** 코어 asmdef는 `com.unity.purchasing`를 참조하지 않는다.
   어댑터는 `FOUNDATIONDI_UNITYIAP` 심볼이 걸린 `FoundationDI.UnityIAP` asmdef에 둔다. AdService의 AppLovin 어댑터와 같은 구조.
4. **카탈로그는 `IapServiceSettings`(SO).** Unity IAP Catalog 에셋을 쓰지 않는다 — 코어가 SDK에 묶이면 안 되기 때문.
5. **AdService/AnalyticsService 연동은 수동 배선.** 서비스끼리 서로 모른다.
6. **지급은 `IIapFulfillment` seam.** 아래 "지급 파이프라인" 참고.

## 조사 결과: Unity IAP v5에서 달라진 것

v4의 `IStoreListener`/`ProcessPurchase`/`PurchaseProcessingResult`는 사라졌다. v5는 Order 모델이다.

| 항목 | v5 API |
| --- | --- |
| 진입점 | `UnityIAPServices.StoreController()` |
| 연결 | `await controller.Connect()` — **모든 이벤트 구독을 Connect 전에** 끝내야 한다 |
| 상품 조회 | `FetchProducts(List<ProductDefinition>)` → `OnProductsFetched` / `OnProductsFetchFailed` |
| 구매 | `PurchaseProduct(productId)` → `OnPurchasePending` → `ConfirmPurchase(order)` → `OnPurchaseConfirmed` |
| 실패 | `OnPurchaseFailed(FailedOrder)` — `FailureReason`에 `UserCancelled` 포함 |
| 보류 | `OnPurchaseDeferred(DeferredOrder)` — iOS Ask-to-Buy |
| 미확정 복구 | `FetchPurchases()` → `OnPurchasesFetched(Orders)` (`PendingOrders` 포함) |
| 복원 | `RestoreTransactions(Action<bool, string>)` — iOS 필수 |
| 확정 | `ConfirmPurchase(PendingOrder)` — **지급을 저장한 뒤에만** 호출 |

`OnPurchaseConfirmed`의 인자는 `Order`이며 `ConfirmedOrder`일 수도 `FailedOrder`일 수도 있다 — 패턴 매칭 필수.

**영수증 검증**: `CrossPlatformValidator`는 이제 Google 전용이 권장 경로다
(`new CrossPlatformValidator(GooglePlayTangle.Data(), Application.identifier)`).
iOS는 StoreKit 2로 전환되어 로컬 검증이 지원되지 않는다 — OS가 검증한 JWS를 신뢰하거나 서버로 보내야 한다.
따라서 기본 검증기는 **Android에서만 실제로 검증**하고 iOS에서는 통과시킨다.

## 설계

### 위치 / 파일 구성

```
Assets/FoundationDI/Runtime/Services/IAPService/
  IIapService.cs                     공개 계약
  IapService.cs                      정책 계층
  IapTypes.cs                        값 타입
  IapServiceRegistration.cs          builder.RegisterIapService(settings)
  README.md
  Fulfillment/
    IIapFulfillment.cs
    AutoConfirmFulfillment.cs        기본 구현 (항상 확정)
  Validation/
    IReceiptValidator.cs
    NoopReceiptValidator.cs          기본 구현 (항상 통과)
  Entitlements/
    IEntitlementStorage.cs
    PlayerPrefsEntitlementStorage.cs
  Providers/
    IIapProvider.cs                  SDK seam
    IIapProviderFactory.cs
    IapProviderFactory.cs
    IapProviderRegistry.cs           옵셔널 어셈블리의 자기 등록 창구
    Dummy/
      DummyIapProvider.cs
      DummyIapOptions.cs
    UnityIAP/                        FoundationDI.UnityIAP.asmdef (FOUNDATIONDI_UNITYIAP)
      UnityIapProvider.cs
      UnityIapInstaller.cs
      CrossPlatformReceiptValidator.cs
  Settings/
    IapProviderType.cs
    IapProductId.cs                  플랫폼별 ID 오버라이드
    IapProductEntry.cs
    IapServiceSettings.cs
```

에디터 도구는 `Assets/FoundationDI/Editor/IAPService/`에 상수 생성기를 둔다.

### 1. 값 타입 (`IapTypes.cs`)

```csharp
public enum IapProductType { Consumable, NonConsumable }

public enum IapPurchaseOutcome
{
    Purchased,       // 신규 구매 — 검증·지급·확정 완료
    Restored,        // 복원 또는 재전달된 미확정 구매
    AlreadyOwned,    // 비소모성인데 이미 소유
    UserCancelled,   // 사용자가 스토어 시트를 닫음
    Deferred,        // iOS Ask-to-Buy 등 — 나중에 Purchased 이벤트로 온다
    NotReady,        // 초기화 안 됨 / 상품 없음 / 중복 호출
    InvalidReceipt,  // 검증 실패 — 지급도 확정도 하지 않았다
    Failed,          // 그 외 스토어 실패
}

public readonly struct IapError { int Code; string Message; }

public readonly struct IapProduct
{
    string Id;               // 게임이 쓰는 공용 ID
    string StoreId;          // 실제 스토어에 올라간 ID
    IapProductType Type;
    string Title;            // 스토어 현지화 제목
    string Description;
    string LocalizedPrice;   // "₩5,500" — 그대로 UI에 찍는다
    double Price;            // 분석 전송용 숫자
    string CurrencyCode;     // "KRW"
    bool IsAvailable;        // 스토어에서 조회 성공
}

public readonly struct IapPurchase
{
    string ProductId;
    IapProductType Type;
    string TransactionId;
    string Receipt;
    double Price;
    string CurrencyCode;
    bool IsRestored;
}

public readonly struct IapPurchaseResult
{
    IapPurchaseOutcome Outcome;
    IapPurchase Purchase;    // Purchased/Restored/AlreadyOwned 일 때 유효
    IapError Error;          // Failed/InvalidReceipt 일 때 유효
    bool IsSuccess => Outcome is Purchased or Restored or AlreadyOwned;
}

public readonly struct IapRestoreResult
{
    bool Success;
    int RestoredCount;
    IapError Error;
}
```

`IapPurchase`에 `in` 한정자를 쓰지 않는다 — AnalyticsService의 `LogPurchase`에 이벤트 핸들러로 직접
대입할 수 있어야 하기 때문이다(AnalyticsService 설계와 같은 이유).

### 2. 공개 계약 (`IIapService.cs`)

```csharp
public interface IIapService : IDisposable
{
    bool IsInitialized { get; }
    Awaitable<bool> InitializeAsync();

    IReadOnlyList<IapProduct> Products { get; }
    bool TryGetProduct(string productId, out IapProduct product);
    bool IsOwned(string productId);

    Awaitable<IapPurchaseResult> PurchaseAsync(string productId);
    Awaitable<IapRestoreResult> RestoreAsync();

    event Action<IapPurchase> Purchased;   // 확정된 구매만 (분석 연동 지점)
    event Action<string> OwnedChanged;     // 비소모성 소유 상태 변화
}
```

### 3. 지급 파이프라인 (`IIapFulfillment`)

Unity IAP v5의 정석은 `OnPurchasePending` → 지급 → 저장 → **저장 성공 시에만** `ConfirmPurchase`다.
확정 전에 앱이 죽으면 스토어가 다음 실행에 같은 구매를 다시 내려주므로 재화가 유실되지 않는다.
이 규율을 게임 코드가 매번 지키게 하면 편의성이 무너지므로 seam 하나로 접는다.

```csharp
public interface IIapFulfillment
{
    // true를 반환해야 ConfirmPurchase가 호출된다. 저장 실패면 false → 다음 실행에 재전달된다.
    Awaitable<bool> FulfillAsync(IapPurchase purchase);
}
```

- 신규 구매, 앱 재시작 시 발견된 미확정 구매, 복원 — **셋 다 이 한 메서드로 들어온다.**
- 미등록이면 `AutoConfirmFulfillment`(즉시 true)로 폴백한다. 간단한 게임은 신경 쓰지 않아도 동작한다.
- 예외를 던지면 false로 취급하고 에러 로그를 남긴다. 확정하지 않으므로 다음 실행에 다시 온다.

순서는 **검증 → 지급 → 확정 → 소유 기록 → `Purchased` 이벤트**다.
검증에 실패하면 지급도 확정도 하지 않는다.

### 4. Provider seam (`IIapProvider.cs`)

```csharp
public readonly struct IapProviderContext
{
    IReadOnlyList<IapProductDefinition> Products;   // 공용 ID → 스토어 ID + 타입
    bool VerboseLogging;
}

public interface IIapProvider : IDisposable
{
    string Name { get; }
    Awaitable<bool> InitializeAsync(IapProviderContext context);

    IReadOnlyList<IapProduct> Products { get; }
    bool Purchase(string storeId);              // 시작만 — 결과는 이벤트로 온다
    void Confirm(string transactionId);
    Awaitable<bool> RestoreAsync();

    event Action<IapPendingPurchase> PurchasePending;  // 지급 대상
    event Action<IapPurchaseFailure> PurchaseFailed;
    event Action<string> PurchaseDeferred;             // storeId
}
```

`IapPendingPurchase`는 `IapPurchase` + `TransactionId` + `IsRestored`를 담는다.
provider는 확정 시점을 스스로 정하지 않는다 — 정책 계층이 `Confirm`을 호출할 때까지 기다린다.

**Provider 선택**은 AdService와 동일한 3단 구조다.
`IapProviderFactory.Resolve(requested, forceDummy, out warning)`가 순수 함수로 무엇을 쓸지 결정하고,
`Build`가 실제 인스턴스를 만든다. 옵셔널 어셈블리는 `IapProviderRegistry.Register`로 자신을 등록한다.
심볼은 있는데 등록이 없으면 에러 로그 후 Dummy로 폴백한다(조용한 폴백 금지).

### 5. Dummy provider

에디터에서 스토어 없이 전체 구매 플로우를 돌리기 위한 구현이다.

- 설정 SO의 상품 목록을 그대로 가짜 가격(`DummyIapOptions.PriceFormat`, 기본 `"$0.99"`)으로 노출한다.
- `Purchase`는 `DummyIapOptions.DelaySeconds` 뒤에 `PurchasePending`을 발행한다.
- `AlwaysFail` / `AlwaysCancel` 토글로 실패 경로를 재현한다.
- `RestoreAsync`는 이전에 확정된 비소모성만 되돌려준다(`PlayerPrefs` 기반).

### 6. 영수증 검증

```csharp
public interface IReceiptValidator
{
    bool Validate(IapPurchase purchase, out IapError error);
}
```

- 기본 `NoopReceiptValidator` — 항상 통과. 코어 asmdef가 `UnityEngine.Purchasing.Security`를 모르기 때문.
- `FoundationDI.UnityIAP`의 `CrossPlatformReceiptValidator` —
  Android에서 `GooglePlayTangle.Data()`로 검증하고, iOS/에디터는 통과시킨다.
  Tangle 클래스가 없으면(Obfuscator 미실행) 경고 한 번 남기고 통과 — 개발이 막히지 않게 한다.
  `builder.RegisterIapService` 안에서 심볼이 있으면 자동으로 이쪽이 등록된다.

### 7. 소유 상태 (`IEntitlementStorage`)

비소모성의 진실의 원천은 스토어(`FetchPurchases`/`Restore`)지만 오프라인에서도 `IsOwned`가 답해야 한다.
`PlayerPrefsEntitlementStorage`가 확정된 비소모성 ID를 캐시한다. 키는 `FoundationDI.IAP.Owned.<productId>`.
스토어 조회 결과가 오면 캐시를 덮어쓴다(스토어가 우선).

### 8. 설정 & DI

```csharp
[CreateAssetMenu(menuName = "FoundationDI/IAP Service Settings")]
public class IapServiceSettings : ScriptableObject
{
    IapProviderType Provider;          // Dummy / UnityIAP
    bool ForceDummyInEditor = true;
    List<IapProductEntry> Products;    // Id, Type, IapProductId(Android/iOS 오버라이드)
    bool VerboseLogging;
    DummyIapOptions DummyOptions;
    IapServiceOptions ToOptions();
}
```

`IapProductId`는 `AdUnitId`와 같은 패턴이다 — 기본값 + Android/iOS 오버라이드, `Current`가 플랫폼에 맞는 값을 준다.
비어 있으면 공용 `Id`를 그대로 쓴다(대부분의 게임은 양 스토어에 같은 ID를 올린다).

```csharp
builder.RegisterIapService(_iapServiceSettings);
builder.Register<IIapFulfillment, MyFulfillment>(Lifetime.Singleton);   // 선택
```

등록 순서에 관계없이 동작하도록 `IIapFulfillment`는 컨테이너에서 `TryResolve`하고 없으면 기본 구현을 쓴다.

### 9. 에디터 도구

`Tools/FoundationDI/IAP/Generate Product Constants` — 설정 SO의 상품 목록으로
`IapProducts` 상수 클래스를 `<SettingsFolder>/Generated/`에 생성하고 `.asmref`로 `FoundationDI`에 합류시킨다
(SoundService의 `SFX`/`Track` 생성기와 같은 방식).
그러면 `_iap.PurchaseAsync(IapProducts.RemoveAds)`처럼 오타가 컴파일 타임에 잡힌다.

### 10. 게임 코드 사용 예

```csharp
public class ShopPresenter : UIPagePresenter<ShopView>
{
    [Inject] private IIapService _iap;
    [Inject] private IAdService _ads;

    protected override void OnInitialize()
    {
        _ads.AdsRemoved = _iap.IsOwned(IapProducts.RemoveAds);
        foreach (var p in _iap.Products) View.AddRow(p.Title, p.LocalizedPrice, () => Buy(p.Id));
    }

    private async void Buy(string productId)
    {
        var result = await _iap.PurchaseAsync(productId);

        switch (result.Outcome)
        {
            case IapPurchaseOutcome.Purchased:
            case IapPurchaseOutcome.Restored:
                if (productId == IapProducts.RemoveAds) _ads.AdsRemoved = true;
                break;
            case IapPurchaseOutcome.UserCancelled:
                break;   // 조용히 무시
            default:
                View.ShowError(result.Error.Message);
                break;
        }
    }
}
```

분석 연동(수동 배선):

```csharp
_iap.Purchased += p => _analytics.LogPurchase(new PurchaseInfo(p.ProductId, p.Price, p.CurrencyCode));
```

## 테스트

EditMode(`FoundationDI.Tests`) + 손으로 쓴 fake(`IapTestDoubles.cs`)로 정책 계층만 검증한다.

1. `IapProductId`가 플랫폼 오버라이드를 고르고 비면 공용 ID로 폴백한다
2. 초기화하면 provider 상품이 노출되고 초기화 전 구매는 `NotReady`다
3. `InitializeAsync`는 재진입해도 provider를 두 번 초기화하지 않는다
4. 구매가 검증 → 지급 → 확정 순서로 진행되고 `Purchased` 이벤트가 확정 후에 발행된다
5. 지급이 false를 반환하면 확정하지 않고 소유로 기록하지 않는다
6. 지급이 예외를 던져도 서비스는 살아 있고 확정하지 않는다
7. 영수증 검증에 실패하면 지급도 확정도 하지 않고 `InvalidReceipt`를 반환한다
8. 사용자가 취소하면 `UserCancelled`, 그 외 실패는 `Failed`로 구분된다
9. 이미 소유한 비소모성은 스토어를 거치지 않고 `AlreadyOwned`를 반환한다
10. 같은 상품 구매가 진행 중이면 두 번째 호출은 `NotReady`로 즉시 반환된다
11. 소모성은 확정 후에도 소유로 기록되지 않는다
12. 미확정 구매가 초기화 시 재전달되면 지급 핸들러로 들어오고 확정된다
13. 복원은 비소모성 소유를 되살리고 `OwnedChanged`를 발행한다
14. `Deferred`는 지급하지 않고 `Deferred`를 반환한다
15. Dispose하면 provider가 Dispose되고 이후 호출은 `NotReady`다
16. `IapProviderFactory.Resolve`가 강제 더미·미가용 심볼을 처리한다
17. `RegisterIapService`로 `IIapService`가 싱글턴 등록되고 fulfillment 미등록 시 기본 구현이 쓰인다

Unity IAP 어댑터와 Dummy provider의 실기 동작은 스모크 테스트로 검증한다(AdService 선례와 동일).

## 범위 밖

- 구독 상품 (별도 계획)
- 서버 영수증 검증 (seam만 열어둠)
- 프로모션 코드 / 가격 실험 / 스토어별 확장 API(`GooglePlayStoreExtendedService` 등)
- 상점 UI (게임 프로젝트 소관)
- iOS 로컬 검증 — StoreKit 2에서 지원되지 않는다
