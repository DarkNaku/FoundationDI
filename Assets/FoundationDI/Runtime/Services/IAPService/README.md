# IAPService

모바일 인앱 구매(Google Play / App Store) 서비스. 게임 코드는 `IIAPService` 하나만 알면 된다.

```csharp
var result = await _iap.PurchaseAsync(IAPProducts.RemoveAds);

if (result.IsSuccess) _ads.AdsRemoved = true;
```

- **SDK**: Unity In-App Purchasing 5.4.2 (`com.unity.purchasing`) — **필수.** Unity IAP를 직접 호출하는 헬퍼다.
- **상품 타입**: 소모성 / 비소모성 (구독은 범위 밖)
- **활성화**: 다른 서드파티 어댑터처럼 `FOUNDATIONDI_UNITYIAP` 심볼로만 켜진다. 심볼은 Unity IAP를 설치하면
  `SdkDefineSynchronizer`가 자동으로 켠다. 서비스 전체가 `FoundationDI.UnityIAP` 어셈블리에 있으므로
  **Unity IAP가 없으면 `IIAPService` 타입 자체가 없다.** 설치 여부가 프로젝트마다 다른 공용 코드만
  `#if FOUNDATIONDI_UNITYIAP`로 감싼다.
- **상품 카탈로그**: Unity IAP Catalog(`Assets/Resources/IAPProductCatalog.json`).

Unity IAP를 그대로 넘겨주는 래퍼가 아니다. Unity IAP만으로는 매번 직접 맞춰야 하고 틀리면 조용히
돈이나 소유가 새는 규율 — 지급 후 확정, 소유 동기화, 복원 완료 시점, 같은 주문의 이중 처리 방지 — 을
이 서비스가 맡는다.

---

## 1. API

```csharp
public interface IIAPService : IDisposable
{
    bool IsInitialized { get; }
    Awaitable<bool> InitializeAsync();

    IReadOnlyList<IAPProduct> Products { get; }
    bool TryGetProduct(string productId, out IAPProduct product);
    bool IsOwned(string productId);

    Awaitable<IAPPurchaseResult> PurchaseAsync(string productId);
    Awaitable<IAPRestoreResult> RestoreAsync();
    Awaitable ProcessUnconfirmedAsync();   // 0.17 — 2절 "미확정 구매" 참고

    event Action<IAPPurchase> Purchased;
    event Action<string> OwnedChanged;
}
```

### `IAPPurchaseResult.Outcome`

| 값 | 의미 | 호출부가 할 일 |
| --- | --- | --- |
| `Purchased` | 신규 구매 — 검증·지급·확정 완료 | 성공 UI |
| `Restored` | 복원 또는 재전달된 미확정 구매 | 성공 UI(조용히 처리해도 됨) |
| `AlreadyOwned` | 비소모성인데 이미 소유 | 이미 보유 안내 |
| `UserCancelled` | 사용자가 스토어 시트를 닫음 | **아무것도 하지 않는다** — 에러가 아니다 |
| `Deferred` | iOS Ask-to-Buy 등 승인 대기 | "승인 후 지급됩니다" 안내 |
| `NotReady` | 초기화 안 됨 / 카탈로그에 없음 / 이미 구매 진행 중 | 버튼 비활성 점검 |
| `InvalidReceipt` | 영수증 검증 실패 | 지급되지 않았다. 실패 UI |
| `Failed` | 그 외 스토어 실패, 지급 실패 | `result.Error`를 보여준다(코드는 아래 표) |

`Failed`·`InvalidReceipt`의 `Error.Code`는 0 이상이면 Unity IAP `PurchaseFailureReason` 값이고, 음수는 이 서비스가
정한 `IAPErrorCodes`다.

| `IAPErrorCodes` | 뜻 | 지급 |
| --- | --- | --- |
| `FulfillmentFailed` | 지급 처리기가 false·예외 | 확정 안 함 — 재처리·다음 실행에 다시 온다 |
| `FulfillmentNotReady` | 지급 처리기가 준비 전(`IIAPFulfillmentGate`) — 보류 | 준비 뒤 `ProcessUnconfirmedAsync()`가 지급, `Purchased` |
| `Timeout` | `PurchaseTimeoutSeconds` 안에 결과가 없음 | 결제가 끝났다면 늦게라도 지급, `Purchased` |
| `DuplicateTransaction` | 같은 소모성의 미확정 구매가 새 구매를 막음(구글 ItemAlreadyOwned) | 서비스가 곧바로 미확정 구매를 재처리 |
| `ValidationRetryLater` | 서버 검증이 "나중에" | 확정 안 함 — 재처리 때 다시 검증 |
| `ReceiptRejected` | 서버 검증이 거절(`InvalidReceipt`와 함께) | 지급 안 함 |
| `Disposed` / `NotInitialized` / `RestoreFailed` / `RestoreInProgress` | 서비스 상태 | — |

결과가 실패여도 **지급은 `Purchased` 이벤트로 나중에 올 수 있다**(위 표의 보류·시간 초과·중복). 재화 UI는
`PurchaseAsync` 결과가 아니라 `Purchased` 이벤트나 지급 처리기에서 갱신하면 어긋나지 않는다.

`IsSuccess`는 `Purchased | Restored | AlreadyOwned`일 때 참이다 — "줘도 되는가"만 알면 되는 대부분의 호출부는 이것만 보면 된다.

`IAPProduct`는 스토어가 현지화한 `Title` / `Description` / `LocalizedPrice`를 담는다.
가격은 반드시 `LocalizedPrice`(예: `"₩5,500"`)를 그대로 찍는다 — 직접 포맷하면 스토어 정책 위반이 될 수 있다.
`Price`(double)와 `CurrencyCode`는 분석 전송용이다.

---

## 2. 지급 파이프라인 — 이 서비스에서 가장 중요한 부분

Unity IAP 5의 정석은 **지급을 저장한 뒤에만 확정(`ConfirmPurchase`)** 하는 것이다.
확정 전에 앱이 죽으면 스토어가 다음 실행에 같은 구매를 다시 내려주므로 재화가 유실되지 않는다.
이 규율을 게임이 매번 지키지 않아도 되도록 seam 하나로 접었다.

```csharp
public interface IIAPFulfillment
{
    // true를 반환해야 확정된다. 저장에 실패했으면 false를 반환할 것.
    Awaitable<bool> FulfillAsync(IAPPurchase purchase);
}
```

```csharp
public class MyFulfillment : IIAPFulfillment
{
    private readonly IInventory _inventory;

    public MyFulfillment(IInventory inventory) => _inventory = inventory;

    public async Awaitable<bool> FulfillAsync(IAPPurchase purchase)
    {
        switch (purchase.ProductId)
        {
            case IAPProducts.Gems100: _inventory.AddGems(100); break;
            case IAPProducts.RemoveAds: break;   // 소유 기록만으로 충분한 상품
            default:
                Debug.LogWarning($"모르는 상품: {purchase.ProductId}");
                return false;   // 확정하지 않는다 — 다음 버전이 지급할 수 있게 남긴다
        }

        return await _inventory.SaveAsync();   // 저장이 성공해야 true
    }
}
```

**신규 구매와 아직 확정되지 않은 구매(앱 재시작·복원 때 발견된 것)가 전부 이 한 메서드로 들어온다.**
그래서 지급 로직을 한 곳에만 쓰면 된다. 재전달을 대비해 `purchase.TransactionId`로 중복 지급을 막을 것.

**이미 확정된 구매는 여기로 다시 들어오지 않는다** — 재설치나 복원으로 돌아온 비소모성은 지급이 아니라
소유(`IsOwned`/`OwnedChanged`)로 전달된다. 그래서 비소모성의 효과(광고 제거 등)는 지급 핸들러가 아니라
`IsOwned`를 보고 켜야 한다. 아래 "소유 상태" 절 참고.

등록하지 않으면 `AutoConfirmFulfillment`(지급 없이 즉시 확정)로 폴백한다.
소모성 재화를 파는 순간부터는 반드시 자기 구현으로 교체해야 한다.

전체 순서: **로컬 검증 → (서버 검증) → 지급 → 확정 → 소유 기록 → `Purchased` 이벤트 → `PurchaseAsync` 완료**.
검증에 실패하면 지급하지 않는다(확정 여부는 6절 `InvalidReceiptPolicy`).

### 미확정 구매 — 보류와 재처리 (0.17)

**지급 처리기가 진행 데이터를 늦게 받는 게임**(씬에서 세이브를 불러오는 경우)은 앱 시작 직후 재전달된 구매를
지급할 수 없다. 지급 처리기에 `IIAPFulfillmentGate`를 함께 구현한다.

```csharp
public class MyFulfillment : IIAPFulfillment, IIAPFulfillmentGate
{
    public bool IsReady => _progress.IsLoaded;   // 준비 전에 온 주문은 서비스가 확정하지 않고 붙잡아 둔다
    ...
}

// 진행 데이터를 불러온 뒤
await _iap.ProcessUnconfirmedAsync();
```

- 준비 전에 온 주문은 지급도 확정도 하지 않고 **보류**한다(트랜잭션마다 하나). 사용자가 그때 산 구매는
  `Failed(FulfillmentNotReady)`로 끝나고, 준비 뒤 지급되면 `Purchased`가 온다.
- `ProcessUnconfirmedAsync()`는 보류분을 지급하고 **스토어에 미확정 구매를 다시 묻는다** — 지급·저장이 일시적으로
  실패한 구매를 앱 재시작 없이 재시도한다(구글은 3일 안에 확정하지 않으면 환불한다). 동시에 여러 번 불러도 조회는
  하나, 초기화 전에는 아무것도 하지 않는다. 조회 결과가 오면 끝나며 지급 결과는 `Purchased`로 받는다.
- 권장 호출 시점: 진행 데이터 준비 완료, 상점 열기, 포그라운드 복귀(`OnApplicationPause(false)`).
- 재전달된 예전 주문의 결과로 **진행 중인 같은 상품 구매를 끝내지 않는다** — 그 구매는 자기 주문을 기다린다.
- 소모성 상품이 카탈로그에 있는데 지급 처리기를 등록하지 않았으면 초기화 때 경고한다(지급 없이 확정된다).

---

### 소유 상태

`IsOwned`는 로컬 캐시(`IEntitlementStorage`)를 본다. 그래서 오프라인에서도 답한다.
캐시를 채우고 고치는 것은 **스토어의 확정 목록**(`FetchPurchases`의 `Orders.ConfirmedOrders`)이다.
초기화와 복원 때 조회가 성공하면 서비스가 캐시를 그 목록에 맞춘다. iOS는 `Transaction.currentEntitlements`라
환불분이 빠지고, Google Play는 소비되지 않은 구매만 온다.

| 상황 | 결과 |
| --- | --- |
| 재설치·기기 변경 후 초기화 | 스토어 목록에 있으므로 소유로 돌아온다 + `OwnedChanged` |
| 환불 | 목록에서 빠지므로 **회수된다** + `OwnedChanged` |
| 오프라인(조회 실패) | 목록이 오지 않는다. 캐시가 그대로 답한다 |
| 상태가 그대로 | 이벤트 없음 |

확정된 구매는 지급을 다시 거치지 않고 `Purchased` 이벤트도 발행하지 않는다(매출이 아니다).
같은 조회에 미확정으로 실려 온 비소모성은 지금 지급·확정 중이므로 회수하지 않는다.
`RestoreResult.RestoredCount`는 스토어가 확인한 비소모성 수와 복원 중에 지급된 미확정 구매 수의 합이다.

---

## 3. 상품 카탈로그

**Services > In-App Purchasing > IAP Catalog**에서 상품을 등록한다. 별도 설정 에셋은 없다.

- **ID**가 게임 코드가 쓰는 공용 ID다(`PurchaseAsync`/`IsOwned`/`IAPPurchase.ProductId`).
- 스토어에 올린 ID가 다르면 **Store ID Overrides**의 Google Play / Apple App Store 칸에 넣는다.
  **비워두면 공용 ID를 그대로 쓴다.** 에디터에서는 현재 빌드 타깃의 스토어 ID가 쓰인다.
- **구독 상품은 경고 후 건너뛴다**(범위 밖).
- 상품을 게임 스펙 표로 관리하면 Catalog 대신 코드로 넘길 수 있다(0.17). 지정하면 IAP Catalog를 읽지 않는다
  (Codeless 경고도 없다). 중복·빈 ID는 경고 후 뺀다. 상수 생성기는 IAP Catalog를 쓸 때만 쓸모가 있다.

```csharp
builder.RegisterIAPService(new IAPServiceOptions
{
    CatalogSource = () => _shopTable.PaidRows.Select(row =>
        new IAPProductDefinition(row.Pid, IAPProductType.Consumable, googlePlayId: row.GoogleId, appleId: row.AppleId)),
});
```
- **`Automatically initialize UnityPurchasing`(Codeless 자동 초기화)를 반드시 끈다.** 기본값이 켜짐이고,
  켜져 있으면 `CodelessIAPStoreListener`가 따로 스토어에 연결해 구매를 처리한다 — 지급을 저장할 때까지
  확정을 미루는 이 서비스의 규율이 깨진다. 켜져 있으면 초기화와 상수 생성 때 에러 로그가 남는다.

### 상품 상수 생성

`Tools > FoundationDI > IAP > Generate Product Constants`

IAP Catalog의 ID로 `IAPProducts.cs` 상수 클래스를 만들고, 같은 폴더의 `.asmref`로 `FoundationDI` 어셈블리에
합류시킨다(상수는 문자열뿐이라 Unity IAP가 없어도 컴파일된다). 출력 폴더는 이 순서로 정한다.

1. 이 프로젝트에서 전에 고른 폴더(EditorPrefs에 프로젝트별로 기억)
2. 이 도구가 만든 기존 `IAPProducts.cs`가 있는 폴더 — 0.10.x에서 만든 파일을 그대로 덮어 쓴다
3. 둘 다 없으면 폴더 선택 창(`Assets/` 아래만)

```csharp
_iap.PurchaseAsync(IAPProducts.RemoveAds);   // 오타가 컴파일 타임에 잡힌다
```

---

## 4. DI 등록

```csharp
public void InstallBindings(ContainerBuilder builder)
{
#if FOUNDATIONDI_UNITYIAP
    builder.RegisterIAPService();                      // 또는 RegisterIAPService(verboseLogging: true)

    // 선택 — 등록 순서는 상관없다.
    builder.RegisterType(typeof(MyFulfillment), new[] { typeof(IIAPFulfillment) }, Lifetime.Singleton, Resolution.Lazy);
    builder.RegisterType(typeof(CloudEntitlementStorage), new[] { typeof(IEntitlementStorage) }, Lifetime.Singleton, Resolution.Lazy);
#endif
}
```

`IIAPFulfillment` / `IReceiptValidator` / `IAsyncReceiptValidator` / `IEntitlementStorage`는 모두 선택 등록이다.
등록하지 않으면 각각 `AutoConfirmFulfillment` / `CrossPlatformReceiptValidator` / (서버 검증 없음) /
`PlayerPrefsEntitlementStorage`가 쓰인다.

### 옵션 (0.17)

```csharp
builder.RegisterIAPService(new IAPServiceOptions
{
    PurchaseTimeoutSeconds = 10f,                                   // 기본 0(끔)
    InvalidReceiptPolicy = InvalidReceiptPolicy.ConfirmWithoutGrant, // 기본 KeepPending
});
```

| 옵션 | 기본 | 뜻 |
| --- | --- | --- |
| `VerboseLogging` | false | `RegisterIAPService(verboseLogging: true)`와 같다 |
| `ConnectTimeoutSeconds` | 30 | 스토어 연결 대기. 넘으면 `InitializeAsync`가 false, 다시 부르면 처음부터 재시도 |
| `FetchTimeoutSeconds` | 30 | 상품 조회(넘으면 초기화 실패)·구매 조회(넘어도 초기화 성공) 각각 |
| `PurchaseTimeoutSeconds` | 0 | 구매 결과 대기. 넘으면 `Failed(Timeout)`, 늦게 온 주문도 지급·확정 |
| `InvalidReceiptPolicy` | `KeepPending` | 6절 |
| `CatalogSource` | null | 3절 — 코드로 상품 목록 |

시간 제한은 `IMainThreadDispatcher`(등록이 함께 넣는다)로 잰다. `new IAPService(...)`로 직접 만들면 시간 제한이 없다.
0 이하는 끝없이 기다린다. 구매 시간 제한은 스토어 시트에서 결제 수단을 고르는 시간까지 들어가 기본으로 끈다.

**상품 일부 누락**: 받은 상품이 하나라도 있으면 초기화는 성공하고, 스토어 콘솔에 없는(심사 전·삭제된) 상품은
`Products`에서 빠진 채 경고 로그에 ID가 남는다. 하나도 받지 못했을 때만 실패한다.

해석은 지연(Lazy)이고, 해석만으로는 스토어에 연결하지 않는다. 초기화는 게임이 원하는 시점에 한 번 부른다
(재진입해도 SDK를 두 번 연결하지 않는다):

```csharp
await _iap.InitializeAsync();
```

---

## 5. 다른 서비스와의 연동 (수동 배선)

IAPService는 AdService/AnalyticsService를 모른다. 호스트가 붙인다.

```csharp
// 광고 제거
_ads.AdsRemoved = _iap.IsOwned(IAPProducts.RemoveAds);
_iap.OwnedChanged += id =>
{
    // true를 박지 말고 다시 읽는다 — 환불되면 같은 이벤트로 회수가 온다.
    if (id == IAPProducts.RemoveAds) _ads.AdsRemoved = _iap.IsOwned(id);
};

// 매출 분석
_iap.Purchased += p => _analytics.LogPurchase(new PurchaseInfo(p.ProductId, p.Price, p.CurrencyCode));
```

---

## 6. 영수증 검증

| 플랫폼 | 동작 |
| --- | --- |
| Google Play | `CrossPlatformValidator` + `GooglePlayTangle`로 서명 검증 |
| App Store | **로컬 검증 없음.** Unity IAP 5는 StoreKit 2를 쓰고 OS가 이미 검증한 뒤 넘겨준다 |
| 에디터 / 그 외 | 통과 |

Google Play 검증을 켜려면 **Services > In-App Purchasing > Receipt Validation Obfuscator**를 한 번 실행해
`Assets/Scripts/UnityPurchasing/generated/GooglePlayTangle.cs`를 만들어야 한다(Unity IAP 5 기준).
생성 폴더는 `Assembly-CSharp`에 속해 패키지 어셈블리가 직접 참조할 수 없으므로 리플렉션으로 찾는다
(`GooglePlayTangleLocator`). **Unity IAP 5는 이 클래스를 `UnityEngine.Purchasing.Security` 네임스페이스
안에 만든다** — 이름을 먼저 그렇게 찾고, 없으면 예전 버전의 전역 `GooglePlayTangle`을 찾는다.
리플렉션으로만 닿는 클래스라 IL2CPP 링커가 걷어내므로, `GooglePlayTangleLinkXmlGenerator`가 빌드마다
실제로 있는 Tangle 타입을 `preserve="all"`로 보존한다(소비 프로젝트가 link.xml을 둘 필요 없음).
**Tangle이 없으면 경고 한 번만 남기고 통과시킨다** — 개발 빌드가 막히지 않게 하기 위해서다.

### 서버 검증 (0.17)

`IAsyncReceiptValidator`를 등록하면 로컬 검증을 통과한 구매를 지급 전에 서버에 묻는다.

```csharp
public class PaymentServerValidator : IAsyncReceiptValidator
{
    public async Awaitable<IAPValidationResult> ValidateAsync(IAPPurchase purchase)
    {
        var response = await _api.PostPaymentAsync(purchase.ProductId, purchase.TransactionId, purchase.Receipt);

        if (response.Ok || response.AlreadyProcessed) return IAPValidationResult.Valid();
        if (response.IsRejected) return IAPValidationResult.Invalid(response.Message);
        return IAPValidationResult.RetryLater(response.Message);   // 서버 장애·네트워크
    }
}
```

| 결과 | 지급 | 확정 | `PurchaseAsync` |
| --- | --- | --- | --- |
| `Valid` | O | O | `Purchased` |
| `Invalid` | X | 정책 | `InvalidReceipt(ReceiptRejected)` |
| `RetryLater` / 예외 | X | X — 재처리·다음 실행에 다시 검증 | `Failed(ValidationRetryLater)` |

"이미 처리됨"(같은 트랜잭션을 서버가 이미 지급 처리) 응답은 `Valid`로 돌려준다 — 클라이언트 지급의 중복 방지는
지금처럼 지급 처리기가 `TransactionId`로 한다.

### 검증 실패 구매 처리 (`InvalidReceiptPolicy`, 0.17)

- `KeepPending`(기본): 확정하지 않는다. 매 실행 재전달되며, 검증 규칙을 고친 버전이 지급할 수 있다.
- `ConfirmWithoutGrant`: **소모성은** 지급 없이 확정해 버린다(위조 영수증이 영원히 재전달되지 않게).
  비소모성은 확정하면 스토어 확정 목록에 올라 소유로 동기화되므로 이 정책에서도 확정하지 않는다.

어느 쪽이든 검증 실패 에러 로그는 트랜잭션마다 실행당 한 번만 남는다.

---

## 7. 에디터에서 (FakeStore)

Unity IAP는 에디터에서 자동으로 FakeStore에 연결된다. 실제로 확인한 동작:

- **구매 확인 창**: Game 뷰에 `Do you want to Purchase ...? [Environment: FakeStore]` 창이 뜨고 Buy/Cancel로
  성공·취소 경로를 고른다. 창 없이 곧바로 끝나는 경우도 있다 — 서비스는 두 경우 모두 처리한다.
- **가격**: 카탈로그의 Google 가격을 쓴다. 비어 있으면 `0`으로 보인다.
- **복원 불가**: `RestoreAsync`는 `OSXEditor is not a supported platform for the restore button`으로 실패한다.
  복원은 기기(샌드박스/테스트 트랙)에서 확인한다.
- **실행 사이에 구매가 남지 않는다**: Play 모드를 다시 시작하면 FakeStore의 확정 목록이 비어, 소유 동기화가
  이전 실행의 비소모성 소유를 **회수한다**(`OwnedChanged`가 온다). 동기화가 의도대로 동작하는 것이다.
  재설치·환불 같은 실행을 넘는 시나리오는 기기에서 확인한다.

---

## 8. 스토어 콘솔 체크리스트

- **Google Play**: 상품 ID 등록 → 활성화 → 라이선스 테스터 계정 추가 → 서명된 AAB를 내부 테스트 트랙에 업로드.
  로컬 검증을 쓰려면 Play Console의 라이선싱 공개 키를 Obfuscator에 넣는다.
- **App Store**: App Store Connect에 상품 등록 → "제출 준비 완료" 상태 → Sandbox 테스터 계정 →
  기기의 App Store 계정에서 로그아웃한 상태로 테스트.
- 양쪽 모두 **번들 ID / 패키지명이 콘솔과 정확히 일치**해야 상품이 조회된다.

---

## 9. IL2CPP 빌드에서의 보존

`FoundationDILinkXmlGenerator`(`Editor/Linker/`)가 빌드마다 생성하는 link.xml이 `FoundationDI.UnityIAP`와
**그 뒤의 SDK 어셈블리**(`Unity.Purchasing`, `Unity.Purchasing.Security`, `Unity.Purchasing.SecurityCore`)를
보존하고, 폴더의 `AssemblyInfo.cs`에 `[assembly: AlwaysLinkAssembly]`가 2차 방어선으로 있다.

게임 코드가 `IIAPService`를 직접 쓰므로 0.10.x처럼 "어댑터가 섬이 되어 조용히 Dummy로 떨어지는" 경로는
더 이상 없다(Dummy 자체가 없다). 보존이 여전히 필요한 이유는 SDK 쪽이다 — 네이티브가 이름으로 관리 코드를
되부르는 경로는 링커가 볼 수 없다.

> **link.xml 파일을 패키지에 그냥 넣어 두는 방법은 통하지 않는다.** 에디터가 사용자 link.xml을
> 수집하는 곳은 `AssemblyStripper.GetUserBlacklistFiles` 하나뿐이고 `Assets/` 아래만 본다.
> UPM 설치 시 패키지는 `Library/PackageCache/`에 있어 읽히지 않는다.

빌드 산출물에서 확인하려면 global-metadata에 타입 이름이 남아 있는지 본다.

```bash
unzip -p app.apk assets/bin/Data/Managed/Metadata/global-metadata.dat \
  | strings | grep -E 'DarkNaku.FoundationDI.IAPService|CrossPlatformReceiptValidator'
```

---

## 10. 범위 밖

- 구독 상품 (별도 계획)
- 프로모션 코드 / 가격 실험 / 스토어별 확장 API
- 상점 UI

## 11. 0.10.x에서 옮겨 오기 (0.11.0)

1. Unity IAP가 없으면 `IIAPService`가 없다. IAP를 쓰는 프로젝트는 `com.unity.purchasing`을 설치한다.
2. `IAPServiceSettings`의 상품을 **IAP Catalog로 옮긴다.** 공용 ID는 카탈로그의 ID로, Android/iOS 오버라이드는
   Google Play/Apple 스토어 ID로. **공용 ID를 바꾸지 않으면 기존 소유 캐시(`FoundationDI.IAP.Owned.<id>`)가
   그대로 읽힌다.** 자동 이전 도구는 없다.
3. IAP Catalog의 Codeless 자동 초기화를 끈다.
4. `builder.RegisterIAPService(_settings)` → `builder.RegisterIAPService()`. 인스톨러의 설정 필드와
   `IAPServiceSettings` 에셋을 지운다.
5. `Force Dummy In Editor`/Dummy 옵션으로 하던 에디터 테스트는 FakeStore로 한다(7절).
6. 상수 생성기를 한 번 다시 돌린다 — 기존 `IAPProducts.cs` 자리를 찾아 덮어 쓴다.

---

설계 배경: `docs/superpowers/specs/2026-10-05-iapservice-unity-iap-direct-design.md`
(최초 설계: `docs/superpowers/specs/2026-08-23-iapservice-design.md`)
