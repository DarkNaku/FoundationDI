# IAPService 재설계 — Unity IAP 직접 사용 헬퍼

- 작성일: 2026-10-05
- 상태: 승인됨 (미결 사항은 제안대로 결정, 2026-10-05)
- 대체 대상: `2026-08-23-iapservice-design.md`의 "Unity IAP는 옵셔널 어셈블리", "카탈로그는 `IAPServiceSettings`" 결정
- SDK: Unity In-App Purchasing `com.unity.purchasing` 5.4.2
- 버전: 0.10.11 → **0.11.0** (하위 호환 깨짐)

## 배경

IAPService는 AdService를 본떠 provider 추상화(`IIAPProvider`)를 두고, Unity IAP를 그 뒤의 옵셔널
어댑터로 붙였다. 그런데 AdService와 달리 바꿔 끼울 대상이 없다. Unity 모바일 결제는 Unity IAP가
사실상 표준이고, Google Play/App Store 차이도 Unity IAP가 이미 흡수한다. 결과적으로 추상화는
단 하나의 SDK를 위한 번역층이 됐고 다음 비용을 치르고 있다.

- **SDK가 이미 가진 것을 다시 만들었다.** 카탈로그(`IAPServiceSettings` 상품 목록 + `IAPProductId`)는
  Unity IAP Catalog와 겹치고, Dummy provider는 Unity IAP가 에디터에서 쓰는 FakeStore와 겹친다.
- **번역층이 테스트되지 않는다.** 정책 계층은 `FakeIAPProvider`로 촘촘히 테스트되지만, 실제 SDK와
  맞닿은 `UnityIAPProvider`는 "스모크로 검증"으로 남았다. 2026-10-05에 고친 버그 셋(확정 주문을
  버림, 복원이 조회 전에 끝남, 미확정 주문의 이중 발행)이 **전부 이 테스트되지 않은 층에 있었다.**
- **SDK 없이도 동작한다는 약속이 실제로는 쓸모가 적다.** Unity IAP 없이 할 수 있는 일은 Dummy로
  UI를 미리 개발하는 것뿐이고, 실제 결제를 하려면 결국 Unity IAP가 필요하다. 다른 SDK를 꽂을
  자리도 막혀 있다(`IAPProviderType`이 닫힌 enum이다).

## 목표

1. IAPService를 **Unity IAP를 직접 호출하는 헬퍼**로 바꾼다. provider 추상화와 Dummy를 없앤다.
2. 다른 서드파티 어댑터처럼 **`FOUNDATIONDI_UNITYIAP` 심볼로만 활성화**한다. 심볼은 지금처럼
   `SdkDefineSynchronizer`가 Unity IAP 설치 여부를 보고 자동 관리한다.
3. **Unity IAP를 그대로 넘겨주는 얇은 래퍼가 되지 않는다.** 아래 "남기는 가치"는 그대로 유지한다.
4. **SDK와 맞닿은 코드까지 단위 테스트한다.** 정책과 번역을 한 클래스에 두고 Unity IAP 인터페이스를
   NSubstitute로 대체한다.
5. 게임 코드가 보는 `IIAPService` 표면은 최대한 유지한다.

### 범위 밖

- 구독 상품. 이전과 같다.
- 서버 영수증 검증. `IReceiptValidator` seam만 연다(이전과 같다).
- 다른 결제 SDK 지원. 필요해지면 그때 seam을 다시 연다.

## 남기는 가치 (헬퍼가 존재하는 이유)

Unity IAP만으로는 매번 직접 맞춰야 하고, 틀리면 조용히 돈이나 소유가 새는 부분이다.

| 규칙 | 이유 |
| --- | --- |
| 검증 → 지급 → 확정 순서 (`IIAPFulfillment`) | 지급이 저장된 뒤에만 확정해야 재화가 유실되지 않는다. 실패하면 확정하지 않아 스토어가 다시 준다 |
| 소유 캐시 + 스토어 확정 목록 동기화 (`IEntitlementStorage`) | Unity IAP의 구매 캐시는 조회가 성공해야 채워진다. 오프라인으로 시작하면 비어 있다. 환불 회수·재설치 복구는 확정 목록으로 맞춘다 |
| 복원 완료 시점 | `RestoreTransactions` 콜백은 뒤따르는 `FetchPurchases` 결과보다 먼저 불린다. 둘 다 도착해야 끝난다 |
| 같은 주문의 이중 처리 방지 | `ProcessPendingOrdersOnPurchasesFetched(false)`로 SDK의 세션 중복 제거를 끄기 때문이다(재전달 구매에 "복원됨"을 붙이려고 끈다) |
| `Awaitable` API와 결과 enum 8종 | Unity IAP는 이벤트 모델이다. 게임 코드는 `await _iap.PurchaseAsync(...)` 한 줄로 끝나야 한다 |
| 영수증 검증 래퍼 (`CrossPlatformReceiptValidator`) | Tangle 리플렉션 탐색, Tangle 없을 때 경고 후 통과, iOS 통과 |
| 동시 구매 1건 제한 | 스토어 UI가 모달이고 이벤트에 "어느 호출의 결과인가"가 실려 오지 않는다 |

## 설계

### 어셈블리

`Runtime/Services/IAPService/` 폴더 **전체**를 `FoundationDI.UnityIAP` 어셈블리로 만든다.
지금은 `Providers/UnityIAP/` 아래에만 있는 asmdef를 폴더 루트로 올린다.

```
FoundationDI.UnityIAP.asmdef
  references: FoundationDI, Reflex, Unity.Purchasing, Unity.Purchasing.Security, Unity.Purchasing.SecurityCore
  defineConstraints: FOUNDATIONDI_UNITYIAP
```

- 어셈블리 이름을 바꾸지 않으므로 `SdkDefineTable`의 항목과 링커 보존(link.xml 생성)은 그대로 쓴다.
- 코어 `FoundationDI` 어셈블리에는 IAP 타입이 하나도 남지 않는다.
- **심볼이 없으면 `IIAPService` 타입 자체가 없다.** 다른 서드파티 어댑터와 같은 규칙이다. IAP를 쓰는
  게임 코드는 Unity IAP를 설치한다(그러면 심볼은 자동으로 켜진다). 설치 여부가 프로젝트마다 다를 수
  있는 공용 코드만 `#if FOUNDATIONDI_UNITYIAP`로 감싼다.

에디터 도구(상품 상수 생성기)도 Unity IAP Catalog를 읽어야 하므로 별도 에디터 어셈블리로 옮긴다.

```
Editor/IAPService/FoundationDI.UnityIAP.Editor.asmdef
  references: FoundationDI.UnityIAP, Unity.Purchasing, (Editor 전용)
  defineConstraints: FOUNDATIONDI_UNITYIAP
```

### 공개 API

`IIAPService`는 **그대로 둔다.** 게임 코드가 쓰는 표면은 바뀌지 않는다.

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
    event Action<IAPPurchase> Purchased;
    event Action<string> OwnedChanged;
}
```

값 타입 `IAPProduct`/`IAPPurchase`/`IAPPurchaseResult`/`IAPRestoreResult`/`IAPError`/
`IAPPurchaseOutcome`/`IAPProductType`도 그대로 둔다. Unity의 `Product`/`Order`를 표면에 내보내지
않는 이유는 둘이다. 첫째, `IAPPurchase`는 `AnalyticsService.LogPurchase`에 그대로 배선되는 값이라
안정적이어야 한다. 둘째, Unity 타입은 테스트에서 만들기 어렵다(아래 "테스트" 참고).

**`IAPPurchase.ProductId`의 의미가 정확해진다.** 지금은 "우리 카탈로그의 공용 ID"이고 스토어 ID와
따로 매핑했다. 바꾸면 Unity의 `ProductDefinition.id`(카탈로그 ID)가 곧 공용 ID다. Unity IAP가 이미
`id`(공용) / `storeSpecificId`(스토어별) 두 층을 갖고 있으므로 우리 쪽 매핑 테이블(`_byStoreId`)은 없어진다.

### 카탈로그

**Unity IAP Catalog(`Assets/Resources/IAPProductCatalog.json`)를 쓴다.** `Services > In-App Purchasing >
IAP Catalog` 창에서 편집하며, 스토어별 ID 오버라이드와 Google Play/App Store 내보내기를 Unity가 제공한다.

- 초기화 때 `ProductCatalog.LoadDefaultCatalog()`를 읽어 `allValidProducts`를
  `ProductDefinition(item.id, storeId, item.type)`로 바꾼다. 스토어 ID는 플랫폼에 맞는
  `item.GetStoreID(GooglePlay.Name | AppleAppStore.Name)`이고, 비어 있으면 `item.id`를 쓴다.
  (지금의 `IAPProductId.Resolve`와 같은 규칙이다.)
- **구독(`ProductType.Subscription`)은 경고 후 건너뛴다.** 범위 밖이다.
- **Codeless 자동 초기화를 꺼야 한다.** `ProductCatalog.enableCodelessAutoInitialization`의 기본값이
  `true`이고, 켜져 있으면 `CodelessIAPStoreListener`가 별도로 스토어에 연결해 구매 이벤트를 처리한다.
  우리가 확정을 미루는 동안 다른 경로가 처리하면 확정 규율이 깨진다. 그래서 `InitializeAsync`가
  이 값을 보고 **에러 로그를 남긴다**(초기화 자체는 막지 않는다). 상수 생성기도 같은 경고를 띄운다.
- 테스트와 고급 사용을 위해 카탈로그를 직접 넘기는 생성자 경로를 남긴다(아래 "구성").

### 구성

```csharp
public sealed class IAPService : IIAPService
{
    // 기본: UnityIAPServices.StoreController()와 기본 카탈로그를 쓴다.
    public IAPService(IIAPFulfillment fulfillment = null,
                      IReceiptValidator validator = null,
                      IEntitlementStorage entitlements = null,
                      bool verboseLogging = false);

    // 테스트 seam. StoreController가 구현하는 Unity의 public 인터페이스 셋을 받는다.
    internal IAPService(IStoreService store, IProductService products, IPurchaseService purchases,
                        IReadOnlyList<ProductDefinition> catalog,
                        IIAPFulfillment fulfillment, IReceiptValidator validator,
                        IEntitlementStorage entitlements, bool verboseLogging);
}
```

`StoreController`는 `IStoreService`/`IProductService`/`IPurchaseService`를 구현하고, 필요한 메서드와
이벤트(`Connect`, `FetchProducts`, `PurchaseProduct`, `ConfirmPurchase`, `FetchPurchases`,
`RestoreTransactions`, `ProcessPendingOrdersOnPurchasesFetched`, `OnPurchasePending` 등)는 전부 이
세 인터페이스에 있다. 기본 생성자는 같은 `StoreController` 인스턴스를 세 자리에 넘긴다.

### DI 등록

```csharp
builder.RegisterIAPService();                      // 설정 SO 없음
builder.RegisterIAPService(verboseLogging: true);
```

- `IAPServiceSettings` SO는 **없앤다.** 상품 목록은 IAP Catalog로, provider와 Dummy 설정은 없어지고,
  남는 것은 로그 플래그 하나라 SO를 둘 이유가 없다.
- `IIAPFulfillment`/`IReceiptValidator`/`IEntitlementStorage` 선택 등록과 폴백은 그대로다.
  `IReceiptValidator` 기본값은 `IAPReceiptValidatorRegistry` 대신 `CrossPlatformReceiptValidator`를
  직접 만든다(같은 어셈블리가 됐으므로 레지스트리가 필요 없다).

### 에디터 개발

Dummy provider 대신 **Unity IAP의 FakeStore**를 쓴다. Unity IAP는 에디터에서 자동으로 FakeStore로
연결된다.

알려진 차이가 하나 있다. FakeStore는 실행 사이에 구매 기록을 유지하지 않는다고 알려져 있다.
그렇다면 에디터에서는 매 실행마다 소유가 스토어 목록 기준으로 회수된다(소유 동기화가 의도대로
동작하는 것이다). 실행을 넘어 소유를 유지하며 테스트해야 한다면 `IEntitlementStorage`를 교체한다.
→ 구현 첫 단계에서 실제 동작을 확인하고 README에 적는다.

### 지우는 것

| 대상 | 대체 |
| --- | --- |
| `IIAPProvider`, `IAPPendingPurchase`, `IAPPurchaseFailure`, `IAPProviderContext` | Unity 인터페이스와 `Order` 타입을 직접 쓴다 |
| `UnityIAPProvider` | 번역 로직이 `IAPService` 안으로 들어간다 |
| `DummyIAPProvider`, `DummyIAPOptions` | FakeStore |
| `IAPProviderFactory`, `IIAPProviderFactory`, `IAPProviderRegistry`, `IAPProviderType` | 필요 없음 (provider가 하나) |
| `UnityIAPInstaller`, `IAPReceiptValidatorRegistry`, `NoopReceiptValidator` | 같은 어셈블리라 직접 생성 |
| `IAPServiceSettings`, `IAPProductEntry`, `IAPProductId`, `IAPProductDefinition`, `IAPServiceOptions` | IAP Catalog + `RegisterIAPService(verboseLogging)` |

지우는 런타임 코드는 약 1,000줄이다. 이 중 `UnityIAPProvider`의 번역 로직(약 350줄)은 `IAPService`로 옮겨 오므로 순감소는 런타임 약 600줄, 테스트 약 250줄(Dummy·팩토리 테스트)이다.

### 상품 상수 생성기

`Tools/FoundationDI/IAP/Generate Product Constants`는 유지한다. 입력만 `IAPServiceSettings`에서
IAP Catalog로 바꾼다. 출력(`IAPProducts` 클래스, `Generated/` + `.asmref`)은 그대로라 게임 코드의
`IAPProducts.RemoveAds`는 바뀌지 않는다.

출력 위치는 지금 "설정 SO 옆"이다. SO가 없어지므로 다음 순서로 정한다(결정 1).

1. EditorPrefs(프로젝트별 키)에 기억한 폴더가 있으면 그 폴더.
2. 없으면 프로젝트에 이미 있는 `IAPProducts.cs`의 폴더(0.10.x에서 생성한 파일을 그대로 덮어 쓴다).
3. 그것도 없으면 폴더 선택 창을 띄우고, 고른 폴더(반드시 `Assets/` 아래)를 기억한다.

생성 파일이 어느 어셈블리에 들어갈지는 지금처럼 `.asmref`로 정한다. 대상은 코어 `FoundationDI`다 —
상수는 문자열뿐이라 Unity IAP 없이도 컴파일되고, 게임 코드는 어느 쪽 어셈블리든 참조할 수 있다.

## 테스트

### 어셈블리

`Tests/UnityIAP/FoundationDI.UnityIAP.Tests.asmdef`를 새로 만든다.

```
references: FoundationDI, FoundationDI.UnityIAP, FoundationDI.TestSupport, Unity.Purchasing, Reflex,
            UnityEngine.TestRunner, UnityEditor.TestRunner
precompiledReferences: nunit.framework.dll, NSubstitute.dll, Castle.Core.dll
defineConstraints: UNITY_INCLUDE_TESTS, FOUNDATIONDI_UNITYIAP
```

기존 `FoundationDI.Tests`에 있는 IAP 테스트는 전부 여기로 옮긴다. 호스트 프로젝트는 Unity IAP가
설치돼 있어 심볼이 켜져 있으므로 계속 실행된다.

### Unity 객체 만들기

`IStoreService`/`IProductService`/`IPurchaseService`는 NSubstitute로 대체하고 이벤트는
`Raise.Event<Action<T>>(...)`로 쏜다. 주문 객체는 전부 public 생성자가 있다 —
`PendingOrder(ICart, IOrderInfo)`, `ConfirmedOrder(...)`, `FailedOrder(ICart, PurchaseFailureReason, string)`,
`DeferredOrder(...)`, `Cart(CartItem)`, `CartItem(Product)`, `OrderInfo(receipt, transactionId, storeName)`,
`Orders(confirmed, pending, deferred)`, `ProductDefinition(...)`, `ProductMetadata(...)`.

**`Product`만 생성자가 `internal`이다**(`Product(ProductDefinition, ProductMetadata, bool)`).
테스트 헬퍼 한 곳(`UnityIAPTestObjects.Product(...)`)에서 리플렉션으로 만든다. Unity IAP가 이 생성자를
바꾸면 이 헬퍼가 테스트에서 시끄럽게 깨진다 — 프로덕션 코드는 영향이 없다.
`InternalsVisibleTo`는 Unity IAP 쪽 선언이라 우리가 쓸 수 없다.

### 테스트 목록 (TDD 순서)

기존 `IAPServiceTest` 28개의 의도를 그대로 옮기되, 가짜 provider 대신 Unity 인터페이스를 쓴다.
새로 추가되는 것(★)은 지금까지 테스트되지 않던 번역 계층이다.

초기화
- [ ] ★ 카탈로그를 `FetchProducts`에 공용 ID와 플랫폼 스토어 ID로 넘긴다
- [ ] ★ 구독 상품은 경고 후 카탈로그에서 뺀다
- [ ] ★ 연결 → 상품 조회 → 구매 조회 순서로 진행하고, 이벤트 구독은 `Connect` 전에 끝난다
- [ ] ★ `ProcessPendingOrdersOnPurchasesFetched(false)`를 연결 전에 부른다
- [ ] 초기화하면 조회된 상품이 노출된다 (가격·통화·`availableToPurchase` 번역 포함)
- [ ] ★ 연결이 끊기면 초기화가 실패한다
- [ ] ★ 상품 조회가 실패하면 초기화가 실패한다
- [ ] ★ 구매 조회가 실패해도 초기화는 성공한다
- [ ] 초기화 전 구매는 NotReady다
- [ ] InitializeAsync는 재진입해도 한 번만 연결한다

구매
- [ ] 구매가 검증·지급·확정 순서로 진행된다 (`ConfirmPurchase`에 같은 `PendingOrder`가 넘어간다)
- [ ] 지급이 실패하면 확정하지 않는다
- [ ] 지급이 예외를 던져도 확정하지 않고 서비스가 살아있다
- [ ] 영수증 검증에 실패하면 지급도 확정도 하지 않는다
- [ ] 비소모성은 확정 후 소유로 기록되고 소모성은 아니다
- [ ] 사용자 취소와 그 외 실패를 구분한다 (`PurchaseFailureReason.UserCancelled`)
- [ ] 다른 상품의 실패는 대기 중인 구매를 끝내지 않는다
- [ ] 이미 소유한 비소모성은 스토어를 거치지 않는다
- [ ] 구매가 진행 중이면 두 번째 호출은 즉시 NotReady다
- [ ] 카탈로그에 없는 상품 구매는 NotReady다
- [ ] ★ 트랜잭션 ID가 없는 주문은 건너뛴다
- [ ] 보류된 구매는 지급하지 않고 Deferred를 반환한다

미확정 구매와 소유
- [ ] 미확정 구매는 초기화 때 지급되고 확정된다
- [ ] 카탈로그에 없는 상품의 구매가 도착하면 확정하지 않고 경고한다
- [ ] ★ `ConfirmedOrders`의 비소모성이 소유로 기록되고 OwnedChanged가 발행된다
- [ ] ★ 조회 결과에 없는 비소모성 소유는 회수된다
- [ ] 소유 상태가 그대로면 OwnedChanged를 발행하지 않는다
- [ ] 소모성과 카탈로그에 없는 상품은 소유 목록에서 무시한다
- [ ] ★ 지급 중인 주문이 다음 조회에 또 와도 두 번 지급하지 않는다
- [ ] ★ 이번 세션에 확정한 주문이 조회에 미확정으로 와도 다시 지급하지 않고 소유로 센다

복원
- [ ] ★ 복원은 콜백과 뒤따르는 조회가 모두 도착해야 끝난다 (두 순서 모두)
- [ ] 복원은 스토어가 확인한 비소모성 개수를 보고한다
- [ ] 복원은 미확정 구매를 지급하고 개수에 포함한다
- [ ] 복원이 실패하면 Success가 거짓이다
- [ ] ★ 복원 뒤의 조회가 실패하면 Success가 거짓이다
- [ ] ★ 복원이 진행 중이면 두 번째 복원은 즉시 실패한다
- [ ] 초기화 전 복원은 실패한다

수명
- [ ] Dispose하면 이벤트 구독이 풀리고 이후 구매는 NotReady다
- [ ] Dispose하면 대기 중인 구매·복원이 매달리지 않는다
- [ ] ★ Codeless 자동 초기화가 켜진 카탈로그면 에러를 남긴다

등록과 기타
- [ ] RegisterIAPService로 IIAPService가 싱글턴 등록되고 선택 seam이 폴백된다
- [ ] 저장한 소유 상태가 다시 읽힌다 (`PlayerPrefsEntitlementStorage`, 유지)
- [ ] 구매결과의 IsSuccess가 성공 결과에서만 참이다 (유지)
- [ ] 상품 상수 생성기가 IAP Catalog에서 상수를 만든다 (순수 함수 부분)
- [ ] `FoundationDILinkXmlTest`/`SdkDefineTable` 대조가 계속 통과한다

## 진행 순서

구조 변경과 동작 변경을 섞지 않기 위해 다음 순서로 간다. 세부는 계획서에서 쪼갠다.

1. **[STRUCTURAL] 어셈블리 이동.** asmdef를 `IAPService/` 루트로 올리고 IAP 테스트를 새 테스트
   어셈블리로 옮긴다. 코드는 그대로다. 호스트(`RootInstaller`, `IAPServiceSmokeTest`)는 심볼로 감싼다.
   모든 테스트가 그대로 통과해야 한다.
2. **[BEHAVIORAL] 새 `IAPService`를 TDD로 만든다.** Unity 인터페이스를 받는 내부 생성자부터
   위 목록 순서대로 한 테스트씩. 이 동안 기존 provider 기반 코드는 별도 이름으로 남겨 둔다
   (같은 어셈블리 안이라 이름만 겹치지 않으면 공존한다).
3. **[BEHAVIORAL] 등록을 새 클래스로 바꾸고 카탈로그를 IAP Catalog로 옮긴다.**
4. **[STRUCTURAL] 옛 코드를 지운다.** provider 계층, Dummy, 팩토리, 레지스트리, 설정 SO와 그 테스트.
5. **[BEHAVIORAL] 상수 생성기 입력을 IAP Catalog로 바꾼다.**
6. **[STRUCTURAL] 문서와 버전.** IAPService README, `CLAUDE.md`, 호스트 프로젝트, 0.11.0, 이전 안내.

## 이전 안내 (0.10.x → 0.11.0)

README에 둘 내용이다.

1. Unity IAP가 없으면 `IIAPService`가 없다. IAP를 쓰는 프로젝트는 `com.unity.purchasing`을 설치한다.
2. `IAPServiceSettings`의 상품을 IAP Catalog로 옮긴다. 공용 ID는 카탈로그의 ID로, Android/iOS
   오버라이드는 카탈로그의 Google Play/Apple 스토어 ID로 옮긴다. **공용 ID를 바꾸지 않으면 기존
   소유 캐시(`FoundationDI.IAP.Owned.<id>`)가 그대로 읽힌다.**
3. Codeless 자동 초기화를 끈다.
4. `RegisterIAPService(_settings)` → `RegisterIAPService()`.
5. `Force Dummy In Editor`/Dummy 옵션으로 하던 에디터 테스트는 FakeStore로 한다.

## 위험

| 위험 | 대응 |
| --- | --- |
| Unity IAP가 `Product`의 internal 생성자를 바꾼다 | 리플렉션 헬퍼 한 곳에서만 쓴다. 테스트가 깨질 뿐 프로덕션은 영향 없음 |
| FakeStore가 에디터에서 예상과 다르게 동작한다 | 1단계 직후 에디터에서 초기화·구매·복원을 한 번 돌려 확인하고 README에 적는다 |
| 심볼이 없는 프로젝트에서 IAP 참조 코드가 컴파일 실패 | 다른 서드파티와 같은 규칙. README에 명시 |
| 테스트가 Unity 인터페이스의 이벤트 시그니처에 묶인다 | 의도된 것이다. SDK가 바뀌면 테스트가 먼저 알려 준다 |

## 결정된 사항

1. **상수 생성기의 출력 위치**: 기억한 폴더 → 기존 `IAPProducts.cs` 폴더 → 폴더 선택 창 순서. 위 "상품 상수 생성기" 참고.
2. **기존 `IAPServiceSettings` 자동 이전 도구**: 만들지 않는다. README의 수동 이전 안내로 끝낸다.
   이 패키지의 IAP 사용처가 적고 상품 수도 보통 한 자릿수다.

## 구현 중 발견해 반영한 제약

- 코어의 `ProviderDiagnostics`는 `internal`이고 옛 `IAPProviderFactory`가 쓴다. 1단계(어셈블리 이동)에서는
  코어 `AssemblyInfo`에 `InternalsVisibleTo("FoundationDI.UnityIAP")`를 임시로 두고, 옛 코드를 지우는
  4단계에서 함께 지운다.
- 옮겨 간 IAP 어셈블리는 테스트 어셈블리에 internal을 열어 줘야 한다(`FoundationDI.UnityIAP.Tests`,
  NSubstitute용 `DynamicProxyGenAssembly2`). 에디터 어셈블리도 같은 테스트 어셈블리에 연다.
  테스트 어셈블리는 에디터 전용(`includePlatforms: Editor`) 하나로 런타임·에디터 IAP 코드를 함께 시험한다.
- 새 클래스는 옛 `IAPService`와 공존해야 하므로 `UnityIAPService`라는 이름으로 만들고, 4단계에서 옛 클래스를
  지운 뒤 `IAPService`로 이름을 바꾼다(구조 변경 커밋).
