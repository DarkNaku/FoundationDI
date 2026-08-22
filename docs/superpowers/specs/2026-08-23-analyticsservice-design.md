# AnalyticsService 설계 — 다중 분석/MMP 팬아웃 서비스

- **일자**: 2026-08-23
- **대상**: `Assets/FoundationDI/Runtime/Services/AnalyticsService/`
- **성격**: Firebase Analytics를 기본으로 하되, MMP(AppsFlyer / Adjust / Singular / Airbridge)를 추가해도 **게임 코드는 API를 한 번만 호출**하는 서비스 신규 작성.

## 배경 / 목표

게임에 Firebase Analytics 하나만 붙는 경우는 드물다. 어트리뷰션을 위해 MMP가 최소 하나 더 붙고,
퍼블리셔 요구로 두세 개가 동시에 붙기도 한다. 그대로 두면 이벤트 하나를 찍을 때마다 게임 코드가
SDK 수만큼 호출을 늘어놓게 되고, SDK를 추가·교체할 때 모든 호출 지점을 고쳐야 한다.

목표:

- **한 개의 공개 계약** — 게임 코드는 `IAnalyticsService`만 알고, 어떤 SDK가 몇 개 붙어 있는지 모른다.
- **한 번의 호출 → 전 provider 브로드캐스트** — 라우팅 규칙 없음. 무엇을 무시할지는 각 어댑터가 결정한다.
- **SDK마다 다른 예약 이벤트 이름·전용 API를 어댑터 경계 안쪽에 가둔다** — 특히 구매와 광고 수익.
- **SDK 없이도 컴파일되고, SDK 없이도 전체 흐름을 EditMode 테스트로 검증할 수 있다.**

기존 `AdService`가 같은 문제(3사 SDK 중립)를 이미 풀어 뒀으므로 seam 구조·옵셔널 asmdef·Registry
패턴은 그대로 따른다. 갈리는 지점은 하나뿐이다 — **AdService는 provider가 하나, 여기는 동시에 여럿.**

## 확정된 설계 결정

브레인스토밍에서 사용자와 확정한 것들. 각 항목은 "왜 그렇게 정했는가"까지 포함한다.

1. **팬아웃은 무조건 전체 브로드캐스트.** 이벤트 정의 시점에도, 호출 시점에도 대상을 좁히지 않는다.
   무엇을 무시할지는 각 어댑터(또는 MMP 대시보드)가 결정한다. 라우팅 규칙을 도입하면 "이 이벤트가
   어디로 갔는지"가 코드 두 곳에 나뉘어 추적이 어려워지고, 실제로 필요해지면 그때 어댑터 설정으로
   해결할 수 있다.

2. **시맨틱 메서드는 최소 세트.** `LogEvent`(자유형) + `SetUserId` + `SetUserProperty` +
   `LogPurchase` + `LogAdImpression` + `CollectionEnabled`. 즉 **5사 전부가 예약 이름이나 전용
   API를 가진 것만** 시맨틱으로 두고 나머지는 전부 자유형이다. 시맨틱 메서드를 나중에 추가하는 것은
   기존 호출부를 깨지 않으므로, 지금 없어서 생기는 손해가 지금 있어서 생기는 손해보다 작다.

   > 자유형 문자열 하나만으로는 예약 이벤트에 대응할 수 없다는 것이 이 결정의 근거다. **Adjust는
   > 이벤트 "이름"이 아니라 대시보드에서 발급한 6자 토큰**을 요구하고, Adjust·Singular는 매출을
   > 파라미터가 아닌 전용 API로 받는다. 문자열 이름만 넘기는 API로는 원리적으로 매핑이 불가능하다.
   > 이름→토큰 매핑이 필요한 SDK는 **어댑터 자기 설정(SO)**이 매핑 테이블을 들고, 정책 계층은
   > 토큰의 존재조차 모른다.

3. **버퍼는 `InitializeAsync` 완료까지만. 상한 없음.** 초기화는 보통 수 초 안에 끝나고 그 사이
   이벤트는 기껏해야 수십 개다. 상한을 두면 "무엇을 버릴 것인가"라는 답 없는 질문이 따라오는데,
   그 질문이 값어치하는 만큼의 메모리 위험이 실재하지 않는다.

4. **유저 상태와 이벤트는 버퍼에서 분리한다.** `SetUserId`/`SetUserProperty`는 이벤트가 아니라
   **상태**다. 같은 큐에 넣으면 순서 규칙·상한 규칙이 상태에도 적용되어, 초기화 전에 같은 프로퍼티를
   5번 세팅하면 5번 전달되는 낭비가 생긴다. 상태는 latest-wins 슬롯에, 이벤트는 순서 보존 큐에 둔다.
   flush는 **상태 먼저, 이벤트 나중** — 유저 귀속이 붙은 상태로 이벤트가 나가야 하기 때문이다.

5. **동의 판단은 범위 밖.** `CollectionEnabled` bool 하나만 제공하고 초기값은 Settings SO에서
   온다. 영속화하지 않는다.

   > 이유를 명확히 해 둔다. **ATT(iOS)와 GDPR형 동의는 다르다.** ATT는 거부 시 OS가 IDFA를
   > 막으므로 앱이 아무것도 하지 않아도 그 정보는 얻어지지 않는다. 반면 **GDPR형 수집 동의는 OS가
   > 강제하지 않는다** — `FirebaseAnalytics.LogEvent()`를 부르면 그냥 나간다. 그래서 게이트 자체는
   > 필요하지만, 무엇이 동의이고 언제 물을지는 법률·지역·퍼블리셔 요구에 따라 달라지는 게임의
   > 판단이다. `AdService`가 `AdsRemoved` 세터만 두고 IAP를 범위 밖으로 뺀 것과 같은 경계다.
   > 영속화하지 않는 것도 같은 이유 — 동의 기록의 보관은 판단의 일부다.

6. **`AnalyticsParams`는 컬렉션 초기화 구문을 쓰는 class.** `Of`/`And` 정적+인스턴스 체이닝은
   C#이 static과 instance에 같은 이름을 허용하지 않는다는 **언어 제약이 API로 새어 나온** 모양이라
   폐기했다. `Add(string, string/long/double)` 세 오버로드만 두면 컴파일러가 중괄호를 풀어 주고,
   타입 제약은 그대로 컴파일 타임에 강제되며, 외울 이름이 없다. class인 이유는 컬렉션 초기화가
   대상을 변형해야 해서 struct로 하면 mutable struct라는 더 나쁜 함정이 되기 때문이다.

7. **`LogAdImpression`은 AdService의 `AdImpression`을 그대로 재사용한다.** 같은 `FoundationDI`
   어셈블리 안이고, `AdImpression`은 동작 없는 순수 `readonly struct`이며, 그 필드가 이미 Firebase
   `ad_impression` 파라미터와 1:1이다(AdService README 4.1절). 별도 타입 + 변환 코드는 순수한 중복이다.
   **단 자동 배선은 하지 않는다** — 게임 코드가 `_ads.Paid += _analytics.LogAdImpression;` 한 줄을
   쓴다. 두 서비스가 서로를 모르는 상태로 남고 DI 해석 순서 의존이 생기지 않는다.

8. **이번 구현 범위는 정책 계층 + Debug provider + Firebase 어댑터.** AppsFlyer/Adjust/Singular/
   Airbridge 어댑터는 각각 별도 계획.

## 조사 결과: 5사의 결정적 차이 (구매 이벤트 기준)

시맨틱 메서드가 왜 필요한지를 보여주는 표. 실제 어댑터 작성 시 대조할 체크포인트이기도 하다.

| SDK | 이벤트 식별자 | 매출 전달 방식 |
| --- | --- | --- |
| Firebase | `"purchase"` (`FirebaseAnalytics.EventPurchase`) | `value`(double) + `currency`(ISO 4217) + `transaction_id` 파라미터 |
| AppsFlyer | `"af_purchase"` | `af_revenue` + `af_currency` + `af_content_id` 파라미터 |
| **Adjust** | **대시보드 발급 6자 토큰** (이름 아님) | `AdjustEvent.setRevenue(amount, currency)` — 전용 API |
| Singular | `sng_ecommerce_purchase` | `Singular.InAppPurchase(...)` — 전용 API |
| Airbridge | `airbridge.ecommerce.order.completed` | semantic attribute `value` / `currency` / `transactionID` |

> 이 표는 어댑터를 실제로 붙일 때 각 SDK 문서와 재대조하고, 어긋나면 표를 갱신한다.
> Firebase 항목은 작성 시점에 공식 문서로 확인했다.

## 설계

### 위치 / 파일 구성

```
Assets/FoundationDI/Runtime/Services/AnalyticsService/
├── IAnalyticsService.cs                 공개 계약
├── AnalyticsService.cs                  팬아웃 + 버퍼 + 예외 격리 + 수집 게이트
├── AnalyticsTypes.cs                    AnalyticsParams / AnalyticsParamValue / PurchaseInfo
├── AnalyticsServiceRegistration.cs      builder.RegisterAnalyticsService(settings)
├── Providers/
│   ├── IAnalyticsProvider.cs            SDK seam
│   ├── IAnalyticsProviderFactory.cs
│   ├── AnalyticsProviderFactory.cs      플래그 → creator 조회 → 생성. 없으면 그 provider만 스킵
│   ├── AnalyticsProviderRegistry.cs     옵셔널 어셈블리가 자신을 등록하는 진입점
│   ├── Debug/
│   │   └── DebugAnalyticsProvider.cs    콘솔에 찍는 provider
│   └── Firebase/                        ← FoundationDI.Firebase asmdef 가 이 폴더를 도려낸다
│       ├── FoundationDI.Firebase.asmdef   defineConstraints: FOUNDATIONDI_FIREBASE
│       ├── FirebaseAnalyticsProvider.cs
│       ├── FirebaseParamConverter.cs      AnalyticsParams → Firebase.Analytics.Parameter[]
│       └── FirebaseInstaller.cs           [RuntimeInitializeOnLoadMethod] 자기 등록
└── Settings/
    ├── AnalyticsServiceSettings.cs      ScriptableObject
    └── AnalyticsProviderType.cs         [Flags] enum
```

### 1. 값 타입 (`AnalyticsTypes.cs`)

**`AnalyticsParamValue`** — `string`/`long`/`double` 3-way union의 `readonly struct`.
`Kind` + 세 필드. 박싱이 없고, Firebase `Parameter` 생성자가 정확히 이 세 타입을 받으므로 1:1이다.

**`AnalyticsParams`** — `IEnumerable<KeyValuePair<string, AnalyticsParamValue>>`를 구현하는 class.
내부는 `List<...>` 하나.

```csharp
public sealed class AnalyticsParams : IEnumerable<KeyValuePair<string, AnalyticsParamValue>>
{
    public int Count { get; }
    public void Add(string key, string value);
    public void Add(string key, long value);
    public void Add(string key, double value);
    // GetEnumerator (컬렉션 초기화 구문 성립 조건)
}
```

사용:

```csharp
_analytics.LogEvent("level_complete", new AnalyticsParams
{
    { "level", 12L },
    { "clear_time", 34.5 },
    { "difficulty", "hard" },
});
```

`bool`/`enum`/`DateTime` 등은 오버로드가 없어 **컴파일 에러**가 된다. 호출부가 명시적으로
`(long)`/`.ToString()`으로 변환해야 하며, 이는 의도된 마찰이다 — Firebase는 지원하지 않는 타입을
런타임에 조용히 버린다.

`null`은 "파라미터 없음"으로 취급한다.

**`PurchaseInfo`** — `readonly struct`.

```csharp
public readonly struct PurchaseInfo
{
    public string ProductId { get; }
    public double Price { get; }            // 단가
    public string Currency { get; }         // ISO 4217 ("USD", "KRW")
    public int Quantity { get; }
    public string TransactionId { get; }
    public AnalyticsParams Extra { get; }   // 게임 고유 컨텍스트. null 허용
    public double Revenue => Price * Quantity;
}
```

**`AdImpression`** — 새로 만들지 않는다. `AdService/AdTypes.cs`의 것을 그대로 쓴다.

### 2. 공개 계약 (`IAnalyticsService.cs`)

```csharp
public interface IAnalyticsService : IDisposable
{
    bool IsInitialized { get; }
    Awaitable<bool> InitializeAsync();

    bool CollectionEnabled { get; set; }

    void LogEvent(string name);
    void LogEvent(string name, AnalyticsParams parameters);
    void LogPurchase(in PurchaseInfo purchase);
    void LogAdImpression(in AdImpression impression);

    void SetUserId(string userId);              // null이면 해제
    void SetUserProperty(string name, string value);
}
```

로깅 API가 전부 **동기 void**인 이유: 5사 SDK 모두 로깅이 fire-and-forget이고, 게임 코드가
전송 완료를 기다릴 이유가 없다. `InitializeAsync`만 비동기다 — Firebase가 Play 서비스 의존성
확인을 하고 실패할 수 있기 때문이다.

`InitializeAsync`는 **재진입 안전**하다(AdService와 동일). 진행 중에 다시 부르면 새 초기화를
시작하지 않고 같은 결과에 편승하고, 이미 초기화됐으면 즉시 `true`를 반환한다.

### 3. Provider seam (`Providers/IAnalyticsProvider.cs`)

공개 계약과 거의 동형이다. 팬아웃·버퍼·게이트는 전부 위 계층이 처리하므로, 어댑터는
**"내가 아는 SDK로 번역해서 넘긴다"**만 한다.

```csharp
public interface IAnalyticsProvider : IDisposable
{
    string Name { get; }                        // 로그·진단용 ("Firebase", "Debug")
    Awaitable<bool> InitializeAsync();
    void SetCollectionEnabled(bool enabled);
    void LogEvent(string name, AnalyticsParams parameters);
    void LogPurchase(in PurchaseInfo purchase);
    void LogAdImpression(in AdImpression impression);
    void SetUserId(string userId);
    void SetUserProperty(string name, string value);
}
```

**어댑터 계약**: 모든 메서드는 메인 스레드에서 호출된다. SDK 콜백을 메인 스레드로 마샬링하는
책임은 **어댑터에 있다** — `AdService`와 동일한 계약이며 같은 이유다(README 5절 참조).

**`AnalyticsProviderRegistry`** — `AdProviderRegistry`와 같은 모양. `FoundationDI`는 옵셔널
어셈블리를 참조할 수 없으므로(순환 참조), 반대로 옵셔널 어셈블리가
`[RuntimeInitializeOnLoadMethod]`에서 자신을 밀어 넣는다. 같은 타입 재등록은 예외 없이 교체한다
(도메인 리로드가 이 경로를 여러 번 태운다).

```csharp
public readonly struct AnalyticsProviderCreationContext
{
    public AnalyticsServiceOptions Options { get; }
}

public static void Register(AnalyticsProviderType type,
                            Func<AnalyticsProviderCreationContext, IAnalyticsProvider> creator);
```

`AdProviderCreationContext`와 같은 이유로 파라미터 목록이 아니라 struct다 — 나중에 두 번째
의존성이 생겨도 이미 등록된 creator 델리게이트의 시그니처가 깨지지 않는다.

### 4. Provider 선택 — AdService와 갈리는 유일한 지점

AdService는 provider가 **하나**라 enum 단일값 + Dummy 폴백이었다. 여기는 **동시에 여럿**이므로
세 곳이 달라진다.

```csharp
[Flags]
public enum AnalyticsProviderType
{
    None = 0, Debug = 1, Firebase = 2,
    AppsFlyer = 4, Adjust = 8, Singular = 16, Airbridge = 32,
}
```

- `AnalyticsServiceSettings.Providers`는 **다중 선택 플래그**다.
- `AnalyticsProviderFactory.CreateAll(flags)`는 켜진 비트마다 Registry에 creator를 묻고,
  **없으면 에러 로그 후 그 provider만 건너뛴다.** AdService처럼 Dummy로 폴백하지 않는다 —
  provider가 여럿인 이상 나머지가 계속 도는 것이 옳은 동작이다.
- `ForceDebugOnlyInEditor` — 켜면 에디터에서 `Debug` provider만 생성한다. 개발 중 이벤트가
  실제 대시보드를 오염시키는 것을 막는다(AdService의 `ForceDummyInEditor` 대응).

### 5. 정책 계층 (`AnalyticsService.cs`)

`AnalyticsService`가 혼자 갖는 것 — 어댑터는 이 중 무엇도 다시 구현하지 않는다.

| 정책 | 동작 |
| --- | --- |
| **팬아웃** | 살아 있는 모든 provider에 같은 호출을 순서대로 전달 |
| **예외 격리** | provider별 `try/catch`. 하나가 던져도 나머지는 호출된다. 로그에 `provider.Name` 포함 (`MessageService`의 핸들러 격리와 같은 모양) |
| **이벤트 버퍼** | `InitializeAsync` 완료 전 `LogEvent`/`LogPurchase`/`LogAdImpression`을 순서 보존 큐에 담았다가 완료 시 순서대로 flush. **상한 없음** |
| **상태 슬롯** | `SetUserId`는 단일 슬롯, `SetUserProperty`는 `Dictionary<string,string>`. latest-wins. flush 시 **이벤트보다 먼저** 적용 |
| **부분 초기화 성공** | provider 중 **하나라도** 성공하면 `InitializeAsync`는 `true`. 실패한 provider는 팬아웃 목록에서 제외 + 에러 로그. 전부 실패하면 `false`이고 `IsInitialized`는 `false`로 남는다. **버퍼는 유지되고 `InitializeAsync`를 다시 부르면 재시도한다** — 네트워크 없이 앱을 켠 경우가 실제로 이 경로이므로 한 번 실패했다고 그 세션 전체를 포기하지 않는다 |
| **`CollectionEnabled`** | `false`면 `LogEvent`/`LogPurchase`/`LogAdImpression`/`SetUserId`/`SetUserProperty` 전부 **호출 즉시 드롭**(버퍼에도 안 들어감). 값이 바뀌면 전 provider에 `SetCollectionEnabled` 전파. 같은 값 재설정은 no-op |
| **`Dispose`** | 전 provider `Dispose`, 버퍼·슬롯 clear, 이후 호출은 무시 + 경고 (`MessageService`와 동일) |
| **스레드** | 메인 스레드 전제. 잠금 없음. 서비스는 어떤 콜백도 마샬링하지 않는다 |

### 6. Firebase 어댑터 (`Providers/Firebase/`)

`FoundationDI.Firebase` asmdef. `references`에 `FoundationDI` + `Firebase.App` + `Firebase.Analytics` +
`Firebase.TaskExtension`, `defineConstraints`에 `FOUNDATIONDI_FIREBASE`.
심볼이 꺼진 프로젝트에서는 Unity가 이 asmdef를 통째로 건너뛰므로 SDK 없이도 컴파일된다.

| 서비스 호출 | Firebase |
| --- | --- |
| `InitializeAsync` | `FirebaseApp.CheckAndFixDependenciesAsync()` → `DependencyStatus.Available` 확인. `Firebase.Extensions`의 `ContinueWithOnMainThread`로 메인 스레드 복귀 |
| `LogEvent(name, params)` | `FirebaseAnalytics.LogEvent(name, Parameter[])` |
| `LogPurchase` | `FirebaseAnalytics.EventPurchase` + `ParameterValue`(=`Revenue`) / `ParameterCurrency` / `ParameterTransactionID` / `ParameterQuantity` / `ParameterItemID` + `Extra` 병합 |
| `LogAdImpression` | `FirebaseAnalytics.EventAdImpression` + `ad_platform` / `ad_source` / `ad_unit_name` / `ad_format` / `value` / `currency` |
| `SetUserId` / `SetUserProperty` | `FirebaseAnalytics.SetUserId` / `SetUserProperty` |
| `SetCollectionEnabled` | `FirebaseAnalytics.SetAnalyticsCollectionEnabled` |

**`FirebaseParamConverter`** — `AnalyticsParams`의 3-way union을
`new Firebase.Analytics.Parameter(key, string|long|double)`로 그대로 변환한다. 분기 3개가 전부다.

**이름 검증도 어댑터가 한다.** Firebase 제약 — 이벤트명 40자 이내, 영숫자와 `_`만, 문자로 시작,
`firebase_`/`google_`/`ga_` 예약 접두어 금지, 이벤트당 파라미터 25개 이내. 어긋나면 **SDK가 조용히
버리므로** 어댑터가 경고를 남긴다. 정책 계층에 두지 않는 이유는 이 제약이 Firebase 고유이고
AppsFlyer/Adjust에는 다른 규칙이 적용되기 때문이다.

### 7. Debug provider (`Providers/Debug/`)

SDK 없이 전체 흐름(버퍼 flush 순서, 팬아웃, 게이트)을 실기에서 눈으로 확인하기 위한 provider.
`Debug.Log`로 `[Analytics/Debug] level_complete { level=12, clear_time=34.5 }` 형태로 찍는다.
`InitializeAsync`는 항상 즉시 `true`. `AdService`의 Dummy provider와 같은 역할이다.

### 8. 설정 & DI

**`AnalyticsServiceSettings`** (ScriptableObject, `Create > FoundationDI > Analytics Service Settings`)

| 필드 | 의미 |
| --- | --- |
| `Providers` | `[Flags] AnalyticsProviderType` 다중 선택 |
| `ForceDebugOnlyInEditor` | 에디터에서 `Debug` provider만 생성 |
| `CollectionEnabledByDefault` | `CollectionEnabled` 초기값. 동의 선행이 필요한 앱은 `false`로 출시 |

`AnalyticsService`는 SO를 직접 참조하지 않고 `AnalyticsServiceOptions`(`readonly struct`)를 받는다 —
EditMode 테스트가 SO 없이 서비스를 조립할 수 있게 하기 위함이다(AdService와 동일).

**등록**

```csharp
builder.RegisterAnalyticsService(_analyticsServiceSettings);
```

`IAnalyticsProviderFactory`(`AnalyticsProviderFactory`)와 `IAnalyticsService`(`AnalyticsService`)를
싱글턴 등록한다. `settings`가 `null`이면 에러 로그만 남기고 등록을 건너뛴다(AdService와 동일 —
주입받는 쪽에서 VContainer 해석 에러로 드러난다).

### 9. 게임 코드 사용 예

```csharp
public class GameFlow
{
    private readonly IAnalyticsService _analytics;
    private readonly IAdService _ads;

    public GameFlow(IAnalyticsService analytics, IAdService ads)
    {
        _analytics = analytics;
        _ads = ads;
        _ads.Paid += _analytics.LogAdImpression;   // 수동 배선, 한 줄
    }

    public async Awaitable BootAsync()
    {
        await _analytics.InitializeAsync();        // 이 전 이벤트는 버퍼링된다
        _analytics.SetUserId(SaveData.PlayerGuid);
        _analytics.SetUserProperty("is_paying", "false");
    }

    public void OnLevelCleared(int level, float seconds)
    {
        _analytics.LogEvent("level_complete", new AnalyticsParams
        {
            { "level", (long)level },
            { "clear_time", (double)seconds },
        });
    }

    public void OnPurchased(Product p, string transactionId)
    {
        _analytics.LogPurchase(new PurchaseInfo(
            productId: p.definition.id,
            price: (double)p.metadata.localizedPrice,
            currency: p.metadata.isoCurrencyCode,
            quantity: 1,
            transactionId: transactionId));
    }
}
```

## 테스트

EditMode(`FoundationDI.Tests`). NSubstitute로 `IAnalyticsProvider`를 대체해 SDK 없이 정책 계층
전체를 검증한다. 테스트 함수 이름은 한국어 의도로 작성한다.

1. 이벤트를 발행하면 등록된 모든 provider가 각각 한 번씩 받는다
2. 한 provider가 예외를 던져도 나머지 provider는 호출된다
3. 초기화 전 이벤트는 버퍼링됐다가 초기화 후 순서대로 전달된다
4. 초기화 전 SetUserProperty는 같은 키의 마지막 값만 전달된다
5. 초기화 시 유저 상태가 버퍼된 이벤트보다 먼저 전달된다
6. provider 하나가 초기화에 실패해도 초기화는 성공하고 실패한 provider에는 전달되지 않는다
7. 모든 provider가 초기화에 실패하면 InitializeAsync가 false를 반환한다
8. InitializeAsync는 재진입해도 초기화를 두 번 시작하지 않는다
9. CollectionEnabled가 false면 어떤 provider에도 전달되지 않는다
10. CollectionEnabled를 바꾸면 모든 provider에 전파되고 같은 값 재설정은 전파되지 않는다
11. AnalyticsParams 컬렉션 초기화가 순서와 타입을 보존한다
12. Dispose하면 모든 provider가 Dispose되고 이후 호출은 무시된다
13. AnalyticsProviderFactory는 creator가 없는 provider만 건너뛰고 나머지를 생성한다
14. RegisterAnalyticsService로 IAnalyticsService가 싱글턴 등록된다

**Firebase 어댑터는 통째로 EditMode 단위 테스트 대상이 아니다.** `FirebaseAnalytics`가 정적
API라 seam이 없고, `AdService`의 AppLovin 어댑터와 같은 이유로 실기 스모크 테스트
(`Assets/Scripts/`의 호스트 프로젝트 코드)로 검증한다.

`FirebaseParamConverter`와 이름 검증 로직은 정적 API에 닿지 않아 이론상 단위 테스트가 가능하지만,
**하지 않는다.** `FoundationDI.Firebase`는 `FOUNDATIONDI_FIREBASE` 심볼이 꺼지면 어셈블리째
사라지므로, `FoundationDI.Tests`가 이를 참조하면 **심볼 유무에 따라 테스트 스위트의 구성이
달라진다** — "전체 테스트가 통과한다"가 환경마다 다른 것을 뜻하게 되고, 이 리포지토리의 TDD
사이클이 기대는 바로 그 신호가 무너진다. 두 로직은 각각 분기 3개와 정규식 한 줄이라 스모크
테스트로 덮이는 범위 안에 있다.

### 구현 시 조기 검증할 리스크

- **Firebase Unity SDK 설치 방식** — `.unitypackage`인지 UPM tgz인지, External Dependency Manager
  버전 충돌은 없는지. `google-services.json` / `GoogleService-Info.plist`가 있어야 실기 검증이 된다.
  **설계 확정 이후 첫 번째로 확인할 항목이며, 여기서 막히면 Firebase 어댑터를 별도 계획으로 분리한다.**
- **`Awaitable`과 Firebase `Task`의 접합** — `ContinueWithOnMainThread`가 `Task`를 반환하므로
  `Awaitable`로 감싸는 지점에서 예외·취소 처리를 확인한다. Unity6 Awaitable은 단일 사용이므로
  `await` 이후 `.IsCompleted` 접근을 피한다.
- **`AnalyticsParams`의 컬렉션 초기화가 실제로 컴파일되는지** — `IEnumerable` 구현 + `Add`
  오버로드 조건. 첫 테스트에서 바로 드러난다.

## 범위 밖

- **AppsFlyer / Adjust / Singular / Airbridge 어댑터** — seam과 매핑표만 준비하고 각각 별도 계획.
- **동의(GDPR/ATT) UI·판단·영속화** — `CollectionEnabled` 세터만 제공한다.
- **라우팅 규칙** — 이벤트별로 provider를 골라 보내는 기능. 전체 브로드캐스트만 지원한다.
- **이벤트 이름 상수 생성** — SoundService의 `SFX`/`Track` 같은 에디터 생성 상수는 두지 않는다.
  게임마다 이벤트 어휘가 다르고, 필요하면 게임이 자기 `static class GameEvents`를 두면 된다.
- **오프라인 큐 / 재전송** — 초기화 전 버퍼링만 한다. 초기화 이후 네트워크 실패 시 재전송은 각 SDK가
  자체적으로 처리하는 영역이다.
- **BigQuery·대시보드 설정, 이벤트 스키마 정의** — 서비스가 아니라 운영의 영역.
- **스레드 안전성** — 메인 스레드 단독 접근 전제. 잠금 없음.
