# IAPService

모바일 인앱 구매(Google Play / App Store) 서비스. 게임 코드는 `IIapService` 하나만 알면 된다.

```csharp
var result = await _iap.PurchaseAsync(IapProducts.RemoveAds);

if (result.IsSuccess) _ads.AdsRemoved = true;
```

- **SDK**: Unity In-App Purchasing 5.4.2 (`com.unity.purchasing`)
- **상품 타입**: 소모성 / 비소모성 (구독은 범위 밖)
- **어댑터 격리**: Unity IAP는 `FOUNDATIONDI_UNITYIAP` 심볼이 걸린 `FoundationDI.UnityIAP` 어셈블리에만 있다.
  코어는 SDK를 참조하지 않으므로 IAP 패키지가 없는 프로젝트에서도 컴파일된다.

---

## 1. API

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

    event Action<IapPurchase> Purchased;
    event Action<string> OwnedChanged;
}
```

### `IapPurchaseResult.Outcome`

| 값 | 의미 | 호출부가 할 일 |
| --- | --- | --- |
| `Purchased` | 신규 구매 — 검증·지급·확정 완료 | 성공 UI |
| `Restored` | 복원 또는 재전달된 미확정 구매 | 성공 UI(조용히 처리해도 됨) |
| `AlreadyOwned` | 비소모성인데 이미 소유 | 이미 보유 안내 |
| `UserCancelled` | 사용자가 스토어 시트를 닫음 | **아무것도 하지 않는다** — 에러가 아니다 |
| `Deferred` | iOS Ask-to-Buy 등 승인 대기 | "승인 후 지급됩니다" 안내 |
| `NotReady` | 초기화 안 됨 / 카탈로그에 없음 / 이미 구매 진행 중 | 버튼 비활성 점검 |
| `InvalidReceipt` | 영수증 검증 실패 | 지급되지 않았다. 실패 UI |
| `Failed` | 그 외 스토어 실패, 지급 실패 | `result.Error`를 보여준다 |

`IsSuccess`는 `Purchased | Restored | AlreadyOwned`일 때 참이다 — "줘도 되는가"만 알면 되는 대부분의 호출부는 이것만 보면 된다.

`IapProduct`는 스토어가 현지화한 `Title` / `Description` / `LocalizedPrice`를 담는다.
가격은 반드시 `LocalizedPrice`(예: `"₩5,500"`)를 그대로 찍는다 — 직접 포맷하면 스토어 정책 위반이 될 수 있다.
`Price`(double)와 `CurrencyCode`는 분석 전송용이다.

---

## 2. 지급 파이프라인 — 이 서비스에서 가장 중요한 부분

Unity IAP 5의 정석은 **지급을 저장한 뒤에만 확정(`ConfirmPurchase`)** 하는 것이다.
확정 전에 앱이 죽으면 스토어가 다음 실행에 같은 구매를 다시 내려주므로 재화가 유실되지 않는다.
이 규율을 게임이 매번 지키지 않아도 되도록 seam 하나로 접었다.

```csharp
public interface IIapFulfillment
{
    // true를 반환해야 확정된다. 저장에 실패했으면 false를 반환할 것.
    Awaitable<bool> FulfillAsync(IapPurchase purchase);
}
```

```csharp
public class MyFulfillment : IIapFulfillment
{
    private readonly IInventory _inventory;

    public MyFulfillment(IInventory inventory) => _inventory = inventory;

    public async Awaitable<bool> FulfillAsync(IapPurchase purchase)
    {
        switch (purchase.ProductId)
        {
            case IapProducts.Gems100: _inventory.AddGems(100); break;
            case IapProducts.RemoveAds: break;   // 소유 기록만으로 충분한 상품
            default:
                Debug.LogWarning($"모르는 상품: {purchase.ProductId}");
                return false;   // 확정하지 않는다 — 다음 버전이 지급할 수 있게 남긴다
        }

        return await _inventory.SaveAsync();   // 저장이 성공해야 true
    }
}
```

**신규 구매·앱 재시작 때 발견된 미확정 구매·복원이 전부 이 한 메서드로 들어온다.**
그래서 지급 로직을 한 곳에만 쓰면 된다. 재전달을 대비해 `purchase.TransactionId`로 중복 지급을 막을 것.

등록하지 않으면 `AutoConfirmFulfillment`(지급 없이 즉시 확정)로 폴백한다.
소모성 재화를 파는 순간부터는 반드시 자기 구현으로 교체해야 한다.

전체 순서: **검증 → 지급 → 확정 → 소유 기록 → `Purchased` 이벤트 → `PurchaseAsync` 완료**.
검증에 실패하면 지급도 확정도 하지 않는다.

---

## 3. 설정

`Create > FoundationDI > IAP Service Settings`로 `IapServiceSettings.asset`을 만든다.

| 항목 | 설명 |
| --- | --- |
| Provider | `UnityIAP` / `Dummy` |
| Force Dummy In Editor | 에디터에서 항상 Dummy를 쓴다(기본 켬) |
| Products | 상품 목록: 공용 `Id`, `Type`, 스토어별 ID 오버라이드 |
| Verbose Logging | 초기화·조회 로그 |
| Dummy Options | 지연 시간, 항상 실패/취소, 표시 가격 |

스토어별 ID 오버라이드는 **비워두면 공용 `Id`를 그대로 쓴다.** 양 스토어에 같은 ID를 올렸다면 건드릴 필요가 없다.

### 상품 상수 생성

`Tools > FoundationDI > IAP > Generate Product Constants`

설정 SO 옆 `Generated/IapProducts.cs`에 상수 클래스를 만들고 `.asmref`로 `FoundationDI` 어셈블리에 합류시킨다.

```csharp
_iap.PurchaseAsync(IapProducts.RemoveAds);   // 오타가 컴파일 타임에 잡힌다
```

---

## 4. DI 등록

```csharp
protected override void Configure(IContainerBuilder builder)
{
    builder.RegisterIapService(_iapServiceSettings);

    // 선택 — 등록 순서는 상관없다.
    builder.Register<IIapFulfillment, MyFulfillment>(Lifetime.Singleton);
    builder.Register<IEntitlementStorage, CloudEntitlementStorage>(Lifetime.Singleton);
}
```

`IIapFulfillment` / `IReceiptValidator` / `IEntitlementStorage`는 모두 선택 등록이다.
등록하지 않으면 각각 `AutoConfirmFulfillment` / 심볼에 맞는 기본 검증기 / `PlayerPrefsEntitlementStorage`가 쓰인다.

초기화는 게임이 원하는 시점에 한 번 부른다(재진입해도 SDK를 두 번 초기화하지 않는다):

```csharp
await _iap.InitializeAsync();
```

---

## 5. 다른 서비스와의 연동 (수동 배선)

IAPService는 AdService/AnalyticsService를 모른다. 호스트가 붙인다.

```csharp
// 광고 제거
_ads.AdsRemoved = _iap.IsOwned(IapProducts.RemoveAds);
_iap.OwnedChanged += id => { if (id == IapProducts.RemoveAds) _ads.AdsRemoved = true; };

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
`Assets/Plugins/UnityPurchasing/generated/GooglePlayTangle.cs`를 만들어야 한다.
생성 폴더는 `Assembly-CSharp`에 속해 패키지 어셈블리가 직접 참조할 수 없으므로 리플렉션으로 찾는다.
**Tangle이 없으면 경고 한 번만 남기고 통과시킨다** — 개발 빌드가 막히지 않게 하기 위해서다.

더 강한 보증이 필요하면 `IReceiptValidator`를 직접 구현해 서버 검증을 붙인다.

---

## 7. Dummy provider

에디터에서 스토어 없이 구매 플로우 전체를 돌린다. 실제 스토어와 두 가지 규율을 맞췄다.

1. 확정되기 전에는 소유로 기록하지 않는다.
2. 이미 확정된 구매는 다음 실행에 재전달하지 않는다 — `RestoreAsync`로만 되돌아온다.

`AlwaysFail` / `AlwaysCancel`로 실패·취소 경로를 재현할 수 있다.
Dummy가 기록한 소유는 `FoundationDI.IAP.Dummy.Owned.<storeId>` PlayerPrefs 키에 남는다(테스트 초기화 시 삭제).

---

## 8. 스토어 콘솔 체크리스트

- **Google Play**: 상품 ID 등록 → 활성화 → 라이선스 테스터 계정 추가 → 서명된 AAB를 내부 테스트 트랙에 업로드.
  로컬 검증을 쓰려면 Play Console의 라이선싱 공개 키를 Obfuscator에 넣는다.
- **App Store**: App Store Connect에 상품 등록 → "제출 준비 완료" 상태 → Sandbox 테스터 계정 →
  기기의 App Store 계정에서 로그아웃한 상태로 테스트.
- 양쪽 모두 **번들 ID / 패키지명이 콘솔과 정확히 일치**해야 상품이 조회된다.

---

## 9. IL2CPP 빌드에서의 어댑터 보존

**어댑터 어셈블리(`FoundationDI.UnityIAP`)는 IL2CPP 빌드에서 보존되어야 한다.**

코어(`FoundationDI`)는 어댑터를 참조하지 않는다 — 참조하면 순환이 된다. 대신 어댑터가
`[RuntimeInitializeOnLoadMethod]`로 스스로를 `IapProviderRegistry`에 등록하고 코어는 조회만 한다.
그 결과 어댑터는 참조 그래프상 어디에서도 닿지 않는 섬이 되고, IL2CPP 링커는 닿지 않는
어셈블리를 통째로 걷어낸다. 등록이 일어나지 않으면 조회가 비어 서비스가 조용히 Dummy provider로 떨어진다. 즉 **실기에서 결제가
가짜로 성공한다** — 스토어에 아무것도 청구되지 않은 채 지급만 일어난다.

**에디터에서는 링커가 돌지 않아 재현되지 않는다 — 빌드해 봐야만 드러난다.** 문서가 없으면
매번 같은 시간을 쓰게 되는 종류의 실패다.

### 9.1 패키지가 스스로 막는다 (소비 프로젝트가 할 일 없음)

- `FoundationDILinkXmlGenerator`(`Editor/Linker/`)가 `IUnityLinkerProcessor`로 빌드마다
  link.xml을 생성해 링커에 넘긴다. 어댑터 어셈블리와 **그 뒤의 SDK 어셈블리**(`Unity.Purchasing`, `Unity.Purchasing.Security`, `Unity.Purchasing.SecurityCore`)를
  함께 보존한다.
- 각 어댑터 폴더의 `AssemblyInfo.cs`에 `[assembly: AlwaysLinkAssembly]`가 붙어 있다.
  생성 link.xml이 닿지 않는 빌드 경로에서도 어댑터 자신은 살아남게 하는 2차 방어선이다.

> **link.xml 파일을 패키지에 그냥 넣어 두는 방법은 통하지 않는다.** 에디터가 사용자 link.xml을
> 수집하는 곳은 `UnityEditorInternal.AssemblyStripper.GetUserBlacklistFiles` 하나뿐이고, 그
> 구현은 `Directory.GetFiles("Assets", "link.xml", SearchOption.AllDirectories)`다. 즉 `Assets/`
> 아래만 본다. UPM(git URL)으로 설치하면 패키지는 `Library/PackageCache/` 아래에 놓이므로
> 거기 넣어 둔 link.xml은 영원히 읽히지 않는다.

### 9.2 빌드에서 확인하는 방법

빌드 산출물의 global-metadata에 타입 이름이 남아 있는지 본다.

```bash
# Android APK
unzip -p app.apk assets/bin/Data/Managed/Metadata/global-metadata.dat \
  | strings | grep -E 'UnityIapInstaller|UnityIapProvider|CrossPlatformReceiptValidator'
```

하나도 안 나오면 어셈블리가 통째로 스트리핑된 것이다. 런타임 증상은 이 에러 로그다.

```
[IAPService] UnityIAP provider가 요청됐지만 등록된 creator가 없다. FOUNDATIONDI_UNITYIAP 심볼이
없어 어댑터가 컴파일되지 않았거나, IL2CPP 빌드에서 FoundationDI.UnityIAP 어셈블리가 통째로
스트리핑된 것이다(에디터에서는 재현되지 않는다). Dummy provider로 대체한다.
```

### 9.3 어댑터를 추가할 때

`SdkDefineTable.Entries`(`Editor/SdkDefines/`)에 한 줄 넣는 것이 전부다 — 심볼, 판정용
어셈블리, 어댑터 어셈블리, 보존할 SDK 어셈블리가 한 곳에 있다. 빠뜨리면
`FoundationDILinkXmlTest`가 asmdef와 대조해 EditMode에서 잡는다(스트리핑 자체는 EditMode에서
재현할 수 없지만, 표 누락은 잡을 수 있다).

## 10. 범위 밖

- 구독 상품 (별도 계획)
- 서버 영수증 검증 (`IReceiptValidator` seam만 열려 있다)
- 프로모션 코드 / 가격 실험 / 스토어별 확장 API
- 상점 UI

설계 배경: `docs/superpowers/specs/2026-08-23-iapservice-design.md`
