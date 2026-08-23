# AnalyticsService

**다중 분석/MMP 팬아웃 서비스**입니다. Firebase Analytics를 기본으로 하되 AppsFlyer / Adjust /
Singular / Airbridge 같은 MMP를 몇 개 붙이든, 게임 코드는 `IAnalyticsService` API를 **한 번만**
호출하면 등록된 모든 provider로 브로드캐스트됩니다.

버퍼링·예외 격리·수집 게이트 같은 정책은 provider(SDK 어댑터)가 아니라 서비스 자신이 갖고 있어서,
SDK 어댑터마다 같은 로직을 복붙하지 않습니다.

현재 실제로 구현된 provider는 **Debug**와 **Firebase** 둘입니다. MMP 4사 어댑터는 각각 별도
계획으로 붙습니다.

---

## 1. 빠른 시작

### 1.1 설정 에셋 만들기

`Assets/Settings/`(또는 원하는 위치)에 우클릭 → `Create > FoundationDI > Analytics Service Settings`.

| 필드 | 의미 |
| --- | --- |
| `Providers` | 동시에 사용할 provider(다중 선택). 켜진 것 전부로 브로드캐스트된다 |
| `Force Debug Only In Editor` | 켜면 에디터에서는 `Debug` provider만 생성한다. 개발 중 이벤트가 실제 대시보드를 오염시키는 것을 막는다 |
| `Collection Enabled By Default` | `CollectionEnabled`의 초기값. 동의를 먼저 받아야 하는 앱은 꺼진 채로 출시한다 |

### 1.2 DI 등록

```csharp
using DarkNaku.FoundationDI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public class RootLifetimeScope : LifetimeScope
{
    [SerializeField] private AnalyticsServiceSettings _analyticsServiceSettings;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterAnalyticsService(_analyticsServiceSettings);
    }
}
```

`settings`가 `null`이면 에러 로그만 남기고 서비스를 등록하지 않습니다(등록 자체를 건너뛰므로,
주입받는 쪽에서 VContainer 해석 에러로 드러납니다).

### 1.3 사용

```csharp
public class GameFlow
{
    private readonly IAnalyticsService _analytics;
    private readonly IAdService _ads;

    public GameFlow(IAnalyticsService analytics, IAdService ads)
    {
        _analytics = analytics;
        _ads = ads;

        // 광고 수익을 분석으로 흘려보내는 배선. 자동으로 해주지 않는 이유는 3.3절 참고.
        _ads.Paid += _analytics.LogAdImpression;
    }

    public async Awaitable BootAsync()
    {
        // 이 호출 전에 발행한 이벤트도 버려지지 않는다 — 버퍼링됐다가 여기서 flush된다.
        await _analytics.InitializeAsync();

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
}
```

---

## 2. 공개 API

```csharp
public interface IAnalyticsService : IDisposable
{
    bool IsInitialized { get; }
    Awaitable<bool> InitializeAsync();

    bool CollectionEnabled { get; set; }

    void LogEvent(string name);
    void LogEvent(string name, AnalyticsParams parameters);
    void LogPurchase(PurchaseInfo purchase);
    void LogAdImpression(AdImpression impression);

    void SetUserId(string userId);
    void SetUserProperty(string name, string value);
}
```

로깅 API는 전부 **동기 `void`**입니다. 5사 SDK 모두 로깅이 fire-and-forget이라 게임 코드가 전송
완료를 기다릴 이유가 없습니다. `InitializeAsync`만 비동기입니다 — Firebase가 Play 서비스 의존성
확인에서 실패할 수 있기 때문입니다.

`InitializeAsync`는 **재진입 안전**합니다. 진행 중에 다시 호출하면 새 초기화를 시작하지 않고 같은
결과에 편승하고, 이미 초기화됐으면 즉시 `true`를 반환합니다.

### 2.1 이벤트와 유저 프로퍼티는 다른 것이다

혼동하기 쉬운데, 이 둘은 개념도 SDK API도 처음부터 별개입니다.

| | 이벤트 | 유저 프로퍼티 |
| --- | --- | --- |
| 의미 | "지금 이 일이 일어났다" (시간축의 점) | "이 유저는 지금 이런 상태다" (유저에 붙어 지속) |
| 예 | `level_complete`, `purchase` | `player_level=37`, `is_paying=true` |
| API | `LogEvent(name, params)` | `SetUserProperty(k, v)` / `SetUserId(id)` |

### 2.2 `AnalyticsParams`

```csharp
_analytics.LogEvent("level_complete", new AnalyticsParams
{
    { "level", 12L },
    { "clear_time", 34.5 },
    { "difficulty", "hard" },
});
```

`Add` 오버로드가 **`string` / `long` / `double` 셋뿐**입니다. `bool`·`enum`·`DateTime`을 넣으면
**컴파일 에러**가 나므로 호출부가 명시적으로 변환해야 합니다. 의도된 마찰입니다 — Firebase는
지원하지 않는 타입의 파라미터를 런타임에 **조용히 버립니다.** 내부는 3-way union이라 박싱도
없습니다.

### 2.3 `LogPurchase`가 시맨틱 메서드인 이유

자유형 `LogEvent` 하나로는 구매를 처리할 수 없습니다. 5사가 전부 다릅니다.

| SDK | 이벤트 식별자 | 매출 전달 방식 |
| --- | --- | --- |
| Firebase | `"purchase"` | `value` + `currency` + `transaction_id` 파라미터 |
| AppsFlyer | `"af_purchase"` | `af_revenue` + `af_currency` 파라미터 |
| **Adjust** | **대시보드 발급 6자 토큰** (이름 아님) | `setRevenue(amount, currency)` — 전용 API |
| Singular | `sng_ecommerce_purchase` | `InAppPurchase(...)` — 전용 API |
| Airbridge | `airbridge.ecommerce.order.completed` | semantic `value` / `currency` / `transactionID` |

**Adjust가 결정적입니다.** 이벤트 "이름"이 아니라 토큰을 요구하므로 문자열 이름만 넘기는 API로는
원리적으로 매핑이 불가능합니다. 그래서 게임은 `PurchaseInfo` 하나만 넘기고 **번역은 각 어댑터가**
합니다. 이름→토큰 매핑이 필요한 SDK는 어댑터 자기 설정이 매핑 테이블을 들고, 정책 계층은 토큰의
존재조차 모릅니다.

> `PurchaseInfo.Price`는 **단가**이고 `Revenue`는 `Price * Quantity`입니다. Firebase의 `value`는
> 거래 총액이므로 어댑터가 `Revenue`를 넘깁니다 — 단가를 넘기면 수량이 2 이상일 때 매출이 샙니다.

---

## 3. 팬아웃 정책

### 3.1 라우팅은 없다

`LogEvent` 한 번은 **등록된 모든 provider**로 갑니다. "이 이벤트는 Firebase만"같은 규칙은 없습니다.
무엇을 무시할지는 각 어댑터(또는 MMP 대시보드)가 결정합니다. 규칙을 도입하면 "이 이벤트가 어디로
갔는가"가 코드 두 곳에 나뉘어 추적이 어려워집니다.

### 3.2 provider 하나가 죽어도 나머지는 산다

| 상황 | 동작 |
| --- | --- |
| provider가 로깅 중 예외를 던짐 | 그 provider만 에러 로그. 나머지는 정상 호출된다 |
| provider 하나가 초기화 실패 | 팬아웃 목록에서 제외. **`InitializeAsync`는 `true`** (나머지가 살아 있으므로) |
| provider 전부 초기화 실패 | `false` 반환, `IsInitialized`는 `false`. **버퍼는 유지되고 재호출 시 그대로 재시도된다** |
| provider가 요청됐는데 creator 미등록 | 그 provider만 에러 로그 후 건너뜀. Dummy로 폴백하지 않는다 |

전부 실패해도 재시도가 되는 이유: **네트워크 없이 앱을 켠 경우가 실제로 이 경로**이기 때문입니다.
한 번 실패했다고 그 세션 전체의 분석을 포기하지 않습니다.

### 3.3 광고 수익 연동은 수동 배선이다

```csharp
_ads.Paid += _analytics.LogAdImpression;
```

`AdService`의 `AdImpression`을 **그대로** 받습니다(새 타입을 만들어 변환하지 않습니다 — 같은
어셈블리이고 필드가 이미 `ad_impression` 파라미터와 1:1입니다).

자동으로 배선하지 않는 이유는 두 서비스가 서로를 모르는 상태로 남기기 위해서입니다. 자동 배선을
넣으면 "AnalyticsService를 쓰려면 AdService도 등록돼 있어야 한다"는 전제가 생기고 DI 해석 순서
의존이 따라옵니다. 한 줄 쓰는 편이 낫습니다.

> `LogAdImpression`의 파라미터에 `in`을 붙이지 않은 것은 의도적입니다. `in`이 붙은 메서드는
> `Action<T>`에 대입할 수 없어 위 한 줄이 컴파일되지 않습니다.

---

## 4. 초기화 전 호출은 버려지지 않는다

`InitializeAsync`가 끝나기 전의 호출은 버퍼에 담겼다가 초기화 완료 시 전달됩니다. **앱 첫 실행
초반(튜토리얼 시작, 첫 진입)은 지표상 가장 중요한 구간인데 정확히 거기서 유실이 나기 때문**입니다.

- **이벤트**(`LogEvent` / `LogPurchase` / `LogAdImpression`)는 **순서 보존 큐**에 담깁니다.
  **상한은 없습니다** — 초기화는 보통 수 초 안에 끝나고 그 사이 이벤트는 기껏해야 수십 개라,
  "무엇을 버릴 것인가"라는 답 없는 질문이 값어치할 만큼의 메모리 위험이 실재하지 않습니다.
- **유저 상태**(`SetUserId` / `SetUserProperty`)는 큐가 아니라 **latest-wins 슬롯**에 담깁니다.
  초기화 전에 같은 프로퍼티를 다섯 번 세팅해도 마지막 값 하나만 전달됩니다.
- **flush 순서는 수집 상태 → 유저 상태 → 이벤트**입니다. 유저 귀속이 붙은 상태로 이벤트가 나가야
  하기 때문입니다 — 순서가 뒤집히면 첫 이벤트들이 익명으로 집계됩니다.

---

## 5. `CollectionEnabled`와 동의의 경계

```csharp
_analytics.CollectionEnabled = false;   // 게임이 판단해서 밀어 넣는다
```

`false`면 `LogEvent` / `LogPurchase` / `LogAdImpression` / `SetUserId` / `SetUserProperty`가
**호출 즉시 드롭**됩니다. **버퍼에도 들어가지 않습니다** — 동의 전에 쌓아 뒀다가 동의 시점에 소급
전송하는 것은 게이트를 두는 의미 자체를 없앱니다.

동시에 모든 provider에 `SetCollectionEnabled(false)`가 전파됩니다. **이게 없으면 게이트가 사실상
무력합니다** — Firebase 같은 SDK는 우리가 `LogEvent`를 부르지 않아도 세션·화면 이벤트를 자동
수집하기 때문입니다.

초기값은 `AnalyticsServiceSettings.CollectionEnabledByDefault`에서 오고, **영속화하지 않습니다.**

### ATT와 GDPR형 동의는 다르다

혼동이 잦은 지점이라 명시해 둡니다.

- **ATT(iOS 추적 동의)** — 거부하면 **OS가 IDFA를 막습니다.** 앱이 아무것도 하지 않아도 그 정보는
  얻어지지 않습니다. SDK는 그대로 돌고 이벤트도 그대로 나가며, 광고 식별자만 비어 옵니다.
  이 서비스가 할 일이 없습니다.
- **GDPR형 수집 동의** — **OS가 강제하지 않습니다.** `FirebaseAnalytics.LogEvent()`를 부르면 그냥
  나갑니다. 앱이 안 부르거나 SDK에 명시적으로 꺼달라고 해야만 안 나갑니다. 그래서 게이트가
  필요합니다.

**동의 UI·법적 판단·ATT 팝업·동의 기록 보관은 전부 이 서비스의 범위 밖입니다.** 게임(또는 별도
동의 서비스)이 판단하고 결과 `bool`만 밀어 넣습니다 — `AdService`가 `AdsRemoved` 세터만 두고 IAP를
범위 밖으로 뺀 것과 같은 경계입니다.

---

## 6. provider를 추가하는 방법

정책 계층(`AnalyticsService`)은 건드리지 않습니다. `AdService` README 5절과 같은 절차입니다.

1. SDK를 설치한다.
2. `SdkDefineTable.Entries`(`Editor/SdkDefines/`)에 한 줄 추가한다 — 심볼, SDK 대표 타입,
   표시 이름. SDK를 임포트하면 `FOUNDATIONDI_APPSFLYER` 같은 심볼이 자동으로 켜지고
   SDK를 지우면 꺼진다. 자동 관리를 쓰지 않는다면 심볼을 직접 정의해도 된다.
3. `FoundationDI.AppsFlyer` asmdef를 만든다. `references`에 `FoundationDI`, `defineConstraints`에
   2번 심볼을 넣는다. **`defineConstraints`가 충족되지 않으면 Unity는 이 asmdef를 통째로
   건너뛰므로** SDK 없는 프로젝트에서도 컴파일이 깨지지 않는다.
   - SDK가 **asmdef**로 제공되면 `references`에 그 asmdef 이름을 넣는다(AppLovin 어댑터가 그렇다).
   - SDK가 **precompiled DLL**로 제공되면 `overrideReferences: true` + `precompiledReferences`에
     DLL 이름을 넣는다(**Firebase 어댑터가 그렇다** — `Assets/Firebase/Plugins/*.dll`).
4. `IAnalyticsProvider`를 구현한다.
5. `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`에서 자신을 등록한다.
   **`BeforeSceneLoad`여야 합니다** — `LifetimeScope.Configure`보다 먼저 돌아야 팩토리가 찾을 수 있습니다.

   ```csharp
   internal static class AppsFlyerInstaller
   {
       [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
       private static void Register()
       {
           AnalyticsProviderRegistry.Register(AnalyticsProviderType.AppsFlyer,
                                              _ => new AppsFlyerAnalyticsProvider());
       }
   }
   ```
6. **SDK 콜백이 메인 스레드에서 오는지 직접 확인한다.** `IAnalyticsProvider`는 모든 메서드가 메인
   스레드에서 호출된다고 전제하는 계약이고, 서비스는 어떤 마샬링도 대신 해주지 않습니다.
   SDK가 마샬링하지 않는다면 어댑터가 직접 메인 스레드로 옮겨야 합니다 — `AdService` README 5절의
   AppLovin 사례(같은 SDK, 같은 이벤트 이름인데 포맷에 따라 스레드가 다름)를 반드시 읽어 보세요.
7. **SDK 고유의 이름 제약 검사는 어댑터 안에 둔다.** 공통 계층에 두면 안 됩니다 — Firebase는
   `firebase_` 접두어를 금지하지만 AppsFlyer는 `af_` 접두어를 오히려 요구합니다. 공통 계층이 가장
   빡빡한 규칙을 강요하면 다른 SDK에서 멀쩡한 이벤트가 막힙니다.

`AnalyticsService`를 수정해야만 어댑터가 붙는다면 seam 설계가 잘못됐다는 신호입니다 — 멈추고 재검토합니다.

---

## 7. Firebase 어댑터

| 서비스 호출 | Firebase |
| --- | --- |
| `InitializeAsync` | `FirebaseApp.CheckAndFixDependenciesAsync()` → `ContinueWithOnMainThread` → `DependencyStatus.Available` 확인 |
| `LogEvent(name, params)` | `FirebaseAnalytics.LogEvent(name, Parameter[])` |
| `LogPurchase` | `EventPurchase` + `ParameterValue`(=`Revenue`) / `ParameterCurrency` / `ParameterQuantity` / `ParameterItemID` / `ParameterTransactionID` + `Extra` 병합 |
| `LogAdImpression` | `EventAdImpression` + `ad_platform` / `ad_source` / `ad_unit_name` / `ad_format` / `value` / `currency` |
| `SetUserId` / `SetUserProperty` | 동명 API |
| `SetCollectionEnabled` | `SetAnalyticsCollectionEnabled` |

**이름 검증**: 이벤트명 40자 이내, 영문자로 시작하고 영숫자와 `_`만, `firebase_`/`google_`/`ga_`
예약 접두어 금지, 파라미터 25개 이내. 어긋나면 **경고만 남기고 버리지는 않습니다** — 판단은
SDK에게 맡기고 개발자에게만 알립니다. Firebase가 규칙 위반 이벤트를 조용히 버리기 때문에, 경고가
없으면 "왜 대시보드에 안 보이지"를 며칠 뒤에야 알게 됩니다.

> **⚠️ 현재 이 리포지토리에는 `google-services.json` / `GoogleService-Info.plist`가 없습니다.**
> 그래서 `CheckAndFixDependenciesAsync`가 `Available`을 반환하지 않고, **Firebase provider는
> 초기화에 실패합니다**(에러 로그를 남기고 팬아웃에서 제외됩니다 — 서비스 자체는 계속 동작합니다).
> 실제 전송을 검증하려면 Firebase 콘솔에서 앱을 등록하고 설정 파일을 프로젝트에 넣어야 합니다.

---

## 8. 구조

```
AnalyticsService/
├── IAnalyticsService.cs               공개 계약
├── AnalyticsService.cs                팬아웃 + 버퍼 + 상태 슬롯 + 예외 격리 + 수집 게이트
├── AnalyticsTypes.cs                  AnalyticsParamValue / AnalyticsParams / PurchaseInfo / AnalyticsServiceOptions
├── AnalyticsServiceRegistration.cs    builder.RegisterAnalyticsService(settings)
├── Providers/
│   ├── IAnalyticsProvider.cs          SDK seam
│   ├── IAnalyticsProviderFactory.cs / AnalyticsProviderFactory.cs
│   ├── AnalyticsProviderRegistry.cs   옵셔널 어셈블리가 자신을 등록하는 진입점
│   ├── Debug/                         콘솔에 찍는 provider (SDK 없이 흐름 확인용)
│   └── Firebase/                      ← FoundationDI.Firebase asmdef 가 이 폴더를 도려낸다
└── Settings/
    ├── AnalyticsProviderType.cs       [Flags] enum
    └── AnalyticsServiceSettings.cs    ScriptableObject + ToOptions()
```

- `AnalyticsService`는 `ScriptableObject`를 직접 참조하지 않고 `AnalyticsServiceOptions`
  (`readonly struct`)를 받습니다. EditMode 테스트가 SO 없이 서비스를 조립할 수 있게 하기 위함입니다.
- **`AnalyticsProviderType`은 `[Flags]`입니다.** `AdService`의 `AdProviderType`과 다른 점이며,
  광고는 미디에이션 SDK 하나만 붙지만 분석은 여럿이 동시에 붙는 것이 정상이기 때문입니다.
- `Debug` provider도 팩토리가 직접 `new` 하지 않고 레지스트리를 거칩니다. 팩토리에 "Debug만
  특별대우"하는 분기가 생기는 순간 그 분기가 곧 규칙의 예외가 되기 때문입니다.

---

## 9. 알려진 범위 밖

- **AppsFlyer / Adjust / Singular / Airbridge 어댑터** — seam과 매핑표만 준비돼 있습니다.
- **동의(GDPR/ATT) UI·판단·영속화** — `CollectionEnabled` 세터만 제공합니다(5절).
- **`google-services.json` 없이의 Firebase 실전송 검증** — 설정 파일이 있어야 합니다(7절).
- **라우팅 규칙** — 이벤트별로 provider를 골라 보내는 기능은 없습니다. 전체 브로드캐스트만 합니다.
- **이벤트 이름 상수 생성** — `SoundService`의 `SFX`/`Track` 같은 에디터 생성 상수는 두지 않습니다.
  게임마다 이벤트 어휘가 달라서, 필요하면 게임이 자기 `static class GameEvents`를 두면 됩니다.
- **오프라인 큐 / 재전송** — 초기화 전 버퍼링만 합니다. 초기화 이후 네트워크 실패 시 재전송은
  각 SDK가 자체적으로 처리하는 영역입니다.
- **BigQuery·대시보드 설정, 이벤트 스키마 정의** — 서비스가 아니라 운영의 영역입니다.
- **스레드 안전성** — 메인 스레드 단독 접근 전제이며 잠금이 없습니다. SDK 콜백을 메인 스레드로
  마샬링하는 책임은 어댑터에 있습니다.
