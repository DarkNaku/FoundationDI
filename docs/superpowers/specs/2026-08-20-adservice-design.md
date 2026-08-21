# ADService 설계 — 광고 네트워크 중립 서비스

- **일자**: 2026-08-20
- **대상**: `Assets/FoundationDI/Runtime/Services/AdService/`
- **성격**: AdMob 미디에이션 / Unity LevelPlay / AppLovin MAX 어느 것을 쓰더라도 **게임 코드가 동일한 API로 광고를 다루는** 서비스 신규 작성.

## 배경 / 목표

세 미디에이션 SDK는 기능적으로는 거의 같은 것(배너/전면/보상)을 제공하지만 API 모양이 서로 다르다. 그대로 쓰면 네트워크 교체가 게임 코드 전면 수정을 부른다. 목표는:

- **한 개의 공개 계약** — 게임 코드는 `IAdService`만 알고, provider 교체는 설정 한 줄.
- **SDK 차이를 어댑터 경계 안쪽에 완전히 가둔다** — 위 계층은 "객체 하나가 Load/Show/이벤트"라는 한 가지 모양만 본다.
- **정책은 provider 무관하게 한 곳에** — 자동 재로드·지수 백오프·보상 확정 규칙·광고제거 게이트를 provider마다 복붙하지 않는다.
- **EditMode 단위 테스트 가능** — SDK 없이 정책 전체를 검증한다.

레퍼런스로 BlockJam의 `Watermelon Core/Modules/Monetization`을 조사했다. 세 핸들러(AdMob/LevelPlay/AppLovin)가 **동일한 백오프 재시도를 각자 구현**하고 있어, 이 설계는 그 부분을 상위 계층으로 끌어올린다.

## 확정된 설계 결정

| 항목 | 결정 |
|---|---|
| 광고 포맷 | **Banner + Interstitial + Rewarded** 3종 |
| 어댑터 구현 범위 | **인터페이스 + Dummy/Editor 어댑터만**. 3사 실제 어댑터는 후속 작업 |
| 서비스 책임 | **자동 재로드 + 지수 백오프 재시도**, **AdsRemoved(광고제거) 상태 반영** |
| 전면광고 쿨다운/최초지연 게이트 | **범위 밖** — 게임 코드가 결정 |
| 동의(GDPR/ATT) | **`IAdConsent` seam만 정의**, 어댑터별 구현은 후속 |
| 구조 | 포맷 핸들 + **정책 계층 분리**(어댑터는 극도로 얇게) |
| async | **`Awaitable`** (UniTask 아님). 호출자마다 `AwaitableCompletionSource` 생성 |
| 네임스페이스 | `DarkNaku.FoundationDI` |
| 수익 추적 | `AdImpression` + `Paid` 이벤트로 노출. **분석 SDK 연동은 게임 코드 몫** |

## 조사 결과: 세 SDK의 결정적 차이

이 세 가지가 설계를 좌우했다.

1. **인스턴스 API vs 정적 API** — MAX는 정적 메서드 + **전역 정적 이벤트**(`MaxSdkCallbacks.Interstitial.OnAdHiddenEvent`), AdMob/LevelPlay는 인스턴스 객체 + 인스턴스 이벤트. → 어댑터가 "AdUnit 하나"를 나타내는 **객체**여야 MAX의 전역 이벤트를 `adUnitId`로 필터링해 흡수할 수 있다.
2. **1회용 vs 재사용** — AdMob의 `InterstitialAd`/`RewardedAd`는 Show 후 `Destroy()` 하고 새로 만들어야 한다. LevelPlay/MAX는 같은 대상에 다시 Load. → 재로드 정책을 상위 계층에 두면 **AdMob 어댑터만 내부에서 객체를 갈아끼우면 된다.**
3. **보상/닫힘 이벤트 순서 무보장** — AdMob은 보상 콜백 → 닫힘, LevelPlay·MAX는 보상 이벤트 → 닫힘이 일반적이지만 **어느 쪽도 순서를 보장하지 않는다.** 일부 미디에이션 네트워크에서 닫힘이 먼저 오는 사례가 보고된다. → "보상 래치 후 닫힘에서 확정 + 유예 프레임"이라는 공통 규칙이 반드시 한 곳에 있어야 한다.

## 설계

### 위치 / 파일 구성

```
Assets/FoundationDI/Runtime/Services/AdService/
  IAdService.cs  AdService.cs  AdTypes.cs  AdUnitId.cs
  Ads/           IFullScreenAd.cs  IBannerAd.cs  FullScreenAdUnit.cs  BannerAdUnit.cs
  Providers/     IAdProvider.cs  IFullScreenAdapter.cs  IBannerAdapter.cs
                 IAdProviderFactory.cs  AdProviderFactory.cs
                 Dummy/ DummyAdProvider.cs  DummyFullScreenAdapter.cs
                        DummyBannerAdapter.cs  DummyAdCanvas.cs
  Consent/       IAdConsent.cs  NoopAdConsent.cs
  Dispatch/      IAdDispatcher.cs  UnityAdDispatcher.cs  AdServiceRunner.cs
  Storage/       IAdRemovalStorage.cs  PlayerPrefsAdRemovalStorage.cs
  Settings/      AdServiceSettings.cs  AdProviderType.cs  BannerOptions.cs
  AdServiceRegistration.cs  README.md
```

### 1. 값 타입 (`AdTypes.cs`)

```csharp
public enum AdFormat { Banner, Interstitial, Rewarded }

public enum AdShowOutcome
{
    Shown,      // 전면: 정상 노출 후 닫힘
    Rewarded,   // 리워드: 보상 확정
    Dismissed,  // 리워드: 보상 없이 닫힘
    NotReady,   // 준비 안 됨 — 즉시 반환
    Failed,     // 표시 중 실패 / 중복 호출
    Blocked,    // AdsRemoved 등 정책 차단
}

public enum AdRevenuePrecision { Unknown, Estimated, PublisherDefined, Exact }

public readonly struct AdReward { public string Label; public double Amount; }
public readonly struct AdError  { public int Code; public string Message; }

public readonly struct AdRetryPolicy
{
    public int MaxAttempts { get; }        // 기본 5
    public float BaseSeconds { get; }      // 기본 2  → 지연 = BaseSeconds^attempt
    public float MaxDelaySeconds { get; }  // 기본 64 → 상한
    public float DelayFor(int attempt);    // min(pow(Base, attempt), MaxDelay)
}

public readonly struct AdShowResult
{
    public AdShowOutcome Outcome { get; }
    public AdReward Reward { get; }   // Outcome == Rewarded 일 때만 유효
    public AdError Error { get; }     // Outcome == Failed 일 때만 유효
    public bool IsRewarded => Outcome == AdShowOutcome.Rewarded;
    public bool WasShown => Outcome is AdShowOutcome.Shown
                                    or AdShowOutcome.Rewarded
                                    or AdShowOutcome.Dismissed;
}

public readonly struct AdImpression
{
    public AdFormat Format { get; }
    public string AdPlatform { get; }        // "AdMob"/"LevelPlay"/"AppLovin" → ad_platform
    public string NetworkName { get; }       // 실제 채운 네트워크          → ad_source
    public string AdUnitId { get; }          //                            → ad_unit_name
    public string NetworkPlacement { get; }  // instanceName / NetworkPlacement
    public string Placement { get; }         // 게임이 ShowAsync에 넘긴 배치명
    public double Revenue { get; }
    public string Currency { get; }          // AdMob은 USD가 아닐 수 있다 — 반드시 함께 사용
    public AdRevenuePrecision Precision { get; }
    public string CreativeId { get; }        // 없으면 null
}
```

`AdUnitId`는 `{ string Android; string iOS; }` 구조체이며 `Current` 프로퍼티가 `#if UNITY_ANDROID/UNITY_IOS`로 해석한다.

### 2. 공개 계약 (`IAdService.cs`)

게임 코드가 보는 것의 전부다.

```csharp
public interface IAdService : IDisposable
{
    bool IsInitialized { get; }
    Awaitable<bool> InitializeAsync();

    IInterstitialAd Interstitial { get; }
    IRewardedAd     Rewarded    { get; }
    IBannerAd       Banner      { get; }
    IAdConsent      Consent     { get; }

    bool AdsRemoved { get; set; }

    event Action<AdFormat> Loaded;
    event Action<AdFormat> Displayed;
    event Action<AdFormat> Closed;
    event Action<AdImpression> Paid;
    event Action<bool> AdsRemovedChanged;
}

public interface IFullScreenAd
{
    bool IsReady { get; }
    void Load();
    Awaitable<AdShowResult> ShowAsync(string placement = null);
}
public interface IInterstitialAd : IFullScreenAd { }
public interface IRewardedAd     : IFullScreenAd { }

public interface IBannerAd
{
    bool IsVisible { get; }
    float Height { get; }        // 화면 픽셀, 미표시면 0
    void Show(); void Hide(); void Destroy();
    event Action<float> HeightChanged;
}
```

사용 예 — provider가 무엇이든 동일하다:

```csharp
var result = await _ads.Rewarded.ShowAsync("double_coins");
if (result.IsRewarded) GrantCoins(result.Reward.Amount);
```

`IInterstitialAd`/`IRewardedAd`는 현재 `IFullScreenAd`와 동일하지만, 호출부 타입 안전성과 향후 분화 여지를 위해 별도 타입으로 둔다. `Load()`는 공개하되 자동 재로드가 기본이라 평소에는 부를 일이 없다.

### 3. 정책 계층 (`Ads/FullScreenAdUnit.cs`)

`FullScreenAdUnit`이 `IInterstitialAd`와 `IRewardedAd`를 구현한다. **provider를 전혀 모르고** `IFullScreenAdapter` + `IAdDispatcher` + 설정값만 받는다. EditMode 테스트의 주 대상이다.

생성 파라미터: `adapter`, `dispatcher`, `format`, `retryPolicy(maxAttempts, baseSeconds, maxDelaySeconds)`, `rewardGraceFrames`, `blockWhenAdsRemoved`(Interstitial=true, Rewarded=false), `adsRemovedProvider(Func<bool>)`.

내부 상태: `_retryAttempt`, `_pendingReward(AdReward?)`, `_showCompletion(AwaitableCompletionSource<AdShowResult>)`, `_scheduledRetry(IDisposable)`, `_isShowing`.

**어댑터 이벤트 → 동작**

| 이벤트 | 동작 |
|---|---|
| `Loaded` | `_retryAttempt = 0`. 서비스로 `Loaded(format)` 전파 |
| `LoadFailed(err)` | `_retryAttempt++`. 한도 내면 `min(base^attempt, maxDelay)`초 뒤 재로드 예약. 초과면 중단 + 로그 |
| `Displayed` | 서비스로 `Displayed(format)` 전파 |
| `Rewarded(reward)` | **`_pendingReward`에 래치만 하고 완료시키지 않는다** |
| `Closed` | `rewardGraceFrames` 대기 후 확정 (아래) |
| `DisplayFailed(err)` | 즉시 `Failed(err)`로 완료 + 재로드 |
| `Paid(imp)` | 서비스로 전파 |

**`Closed` 확정 규칙** — 유예 프레임 뒤에:

- `_pendingReward != null` → `Rewarded` + 보상
- else, `format == Rewarded` → `Dismissed`
- else → `Shown`

확정 후 서비스로 `Closed(format)` 전파하고 자동 재로드.

> **유예 프레임이 이 설계의 핵심 방어물이다.** 닫힘에서 곧바로 확정하면, 닫힘이 보상보다 먼저 오는 SDK/네트워크 조합에서 보상을 조용히 떨어뜨린다. 기본 1프레임, 설정으로 0 또는 N 조정 가능.

**`ShowAsync(placement)` 진입 가드** (순서대로):

1. `AdsRemoved && blockWhenAdsRemoved` → `Blocked` 즉시 반환
2. `_isShowing` → `Failed` 즉시 반환 (중복 호출)
3. `!adapter.IsReady` → `NotReady` 즉시 반환 + `Load()` 트리거
4. 통과 시 `_pendingReward = null`, `_showCompletion` 새로 생성, `adapter.Show()`, `await _showCompletion.Awaitable`

`Dispose()`는 예약된 재시도를 취소하고, 미완료 `_showCompletion`을 `Failed`로 완료시킨 뒤 어댑터를 dispose한다.

### 4. 배너 정책 (`Ads/BannerAdUnit.cs`)

`IBannerAd` 구현. `IBannerAdapter`를 감싸며:

- `AdsRemoved`가 참이면 `Show()`는 `Hide()`로 동작하고 `Height`는 0을 보고한다.
- `AdsRemoved`가 참으로 바뀌면 즉시 숨긴다.
- 어댑터의 `HeightChanged`를 그대로 중계한다 (배너 갱신·적응형 높이 변화 대응).
- `Destroy()`는 어댑터를 dispose하고 `Height`를 0으로 만든다. 이후 `Show()`는 provider로부터 어댑터를 새로 만들어 다시 붙인다 — 즉 `Destroy()`는 영구 종료가 아니라 리소스 해제다.

배너는 전면/보상과 달리 재시도·재로드를 정책 계층이 관리하지 않는다. 세 SDK 모두 배너 갱신을 SDK가 자동 처리하며, MAX 문서는 명시적으로 **배너를 화면에 유지하라**고 권고한다.

### 5. Provider seam (`Providers/`)

```csharp
// provider가 초기화에 필요로 하는 것만 담은 전달 객체.
// AdServiceSettings 전체를 넘기지 않는 이유: provider는 자기 키와 테스트 설정만 알면 되고,
// 정책값(재시도·유예 프레임)은 상위 계층 소관이라 provider가 볼 이유가 없다.
public readonly struct AdProviderContext
{
    public string AppKey { get; }           // LevelPlay appKey / MAX sdkKey. AdMob은 불필요(null)
    public bool VerboseLogging { get; }
    public bool TestMode { get; }
    public IReadOnlyList<string> TestDeviceIds { get; }
}

public interface IAdProvider : IDisposable
{
    string Name { get; }
    Awaitable<bool> InitializeAsync(AdProviderContext context);
    IAdConsent Consent { get; }
    IFullScreenAdapter CreateInterstitial(string adUnitId);
    IFullScreenAdapter CreateRewarded(string adUnitId);
    IBannerAdapter     CreateBanner(string adUnitId, BannerOptions options);
    event Action<AdImpression> ImpressionPaid;   // 전역/미매칭 임프레션 경로
}

public interface IFullScreenAdapter : IDisposable
{
    bool IsReady { get; }
    void Load(); void Show();
    event Action Loaded;    event Action<AdError> LoadFailed;
    event Action Displayed; event Action<AdError> DisplayFailed;
    event Action Closed;    event Action<AdReward> Rewarded;
    event Action<AdImpression> Paid;
}

public interface IBannerAdapter : IDisposable
{
    float Height { get; }
    void Show(); void Hide();
    event Action<float> HeightChanged;
    event Action<AdImpression> Paid;
}

public interface IAdConsent
{
    bool CanRequestAds { get; }
    bool IsPrivacyOptionsRequired { get; }
    Awaitable<bool> RequestAsync();
    Awaitable ShowPrivacyOptionsAsync();
}
```

`Rewarded` 이벤트는 리워드 어댑터에서만 발생한다. 전면 어댑터는 발생시키지 않는다.

### 6. 3사 매핑표 (실제 어댑터 작성 시 대조할 체크포인트)

| seam | AdMob (GMA Unity) | LevelPlay 8.x | AppLovin MAX |
|---|---|---|---|
| `InitializeAsync` | `MobileAds.Initialize(cb)` | `LevelPlay.Init(appKey)` + `OnInitSuccess`/`OnInitFailed` | `MaxSdk.InitializeSdk()` + `OnSdkInitializedEvent` |
| 전면 객체 | `InterstitialAd.Load(id, req, cb)` — **1회용**, Show 후 `Destroy()`+재로드 | `new LevelPlayInterstitialAd(id)` — 재사용 | 전역 정적 이벤트를 `adUnitId`로 필터, `MaxSdk.LoadInterstitial(id)` |
| `IsReady` | `CanShowAd()` | `IsAdReady()` | `MaxSdk.IsInterstitialReady(id)` |
| `Rewarded` | `Show(Action<Reward>)` 콜백 | `OnAdRewarded(info, reward)` | `OnAdReceivedRewardEvent` |
| `Closed` | `OnAdFullScreenContentClosed` | `OnAdClosed` | `OnAdHiddenEvent` |
| `DisplayFailed` | `OnAdFullScreenContentFailed` | `OnAdDisplayFailed` | `OnAdDisplayFailedEvent` |
| 배너 생성 | `new BannerView(id, AdSize, AdPosition)` | `new LevelPlayBannerAd(id, Config)` (Builder로 위치/사이즈) | `MaxSdk.CreateBanner(id, AdViewConfiguration)` |
| 배너 제어 | `Show/Hide/Destroy` | `ShowAd/HideAd/DestroyAd` | `ShowBanner/HideBanner/DestroyBanner` |
| 배너 높이 | `GetHeightInPixels()` | `LevelPlayAdSize.Height` × density | `MaxSdkUtils.GetAdaptiveBannerHeight()` × `GetScreenDensity()` |
| 동의 | UMP `ConsentInformation` / `ConsentForm` | `LevelPlay.SetConsent(bool)` | T&P Flow + `MaxSdk.SetHasUserConsent` |
| 테스트 도구 | `RequestConfiguration.TestDeviceIds` | `LevelPlay.LaunchTestSuite()` | Mediation Debugger |

**임프레션 수익 (ILRD)**

| | AdMob | LevelPlay | AppLovin MAX |
|---|---|---|---|
| 진입점 | `OnAdPaid(AdValue)` — **광고 객체별** | `OnImpressionDataReady(ImpressionData)` — **전역 1개** | `OnAdRevenuePaidEvent(id, AdInfo)` — **포맷별 정적** |
| 금액 | `AdValue.Value` (**마이크로 단위, ÷1,000,000**) | `revenue` (double) | `AdInfo.Revenue` (double) |
| 통화 | `AdValue.CurrencyCode` — **퍼블리셔 통화, USD 아닐 수 있음** | USD | USD |
| 정밀도 | `AdValue.Precision` | `precision` 문자열 | `RevenuePrecision` 문자열 |
| 네트워크명 | `ResponseInfo.GetLoadedAdapterResponseInfo().AdSourceName` — **AdValue에 없음, 별도 조회** | `adNetwork` | `AdInfo.NetworkName` |
| 인스턴스 | `AdSourceInstanceName` | `instanceName` | `NetworkPlacement` |
| 크리에이티브 | — | `creativeId` | `CreativeIdentifier` |

LevelPlay의 임프레션 데이터는 광고 객체가 아니라 **SDK 전역 이벤트 하나**로 온다. 어댑터에만 `Paid`를 두면 특히 **배너 자동 갱신 임프레션**이 매칭되지 않아 수익이 조용히 누락된다. 그래서 `IAdProvider.ImpressionPaid`를 별도 경로로 두고, `AdService`가 어댑터별 `Paid`와 provider의 `ImpressionPaid`를 **하나의 공개 `Paid` 이벤트로 합친다.** AdMob·MAX는 어댑터 경로, LevelPlay는 provider 경로를 쓴다. 게임 코드 입장에선 차이가 없다.

> 위 필드명·타입은 SDK 버전에 따라 달라질 수 있다. 실제 어댑터 작성 시 이 표와 대조하고, 어긋나면 표를 갱신한다.

### 7. `IAdDispatcher` (`Dispatch/`)

```csharp
public interface IAdDispatcher
{
    void Post(Action action);                         // 메인스레드 마샬링
    IDisposable Delay(float seconds, Action action);  // 취소 가능한 백오프 지연
    IDisposable NextFrames(int count, Action action); // 보상 유예
}
```

기본 구현 `UnityAdDispatcher`는 숨겨진 `AdServiceRunner` MonoBehaviour(`DontDestroyOnLoad`)가 큐를 `Update`에서 펌프한다. 두 가지 목적이 있다:

1. 세 SDK 모두 네이티브 스레드에서 콜백이 올라올 수 있다 (레퍼런스 코드의 `CallEventInMainThread`와 같은 역할).
2. **백오프와 유예 프레임을 가짜 시계로 테스트할 수 있게 한다.** 이쪽이 더 큰 이유다.

### 8. 설정 & DI

`AdServiceSettings : ScriptableObject`

| 항목 | 기본값 |
|---|---|
| `provider` (`AdProviderType`) | `Dummy` |
| provider별 키 컨테이너 (app/sdk key, 포맷별 `AdUnitId`) | — |
| `autoLoadOnInitialize` | `true` |
| `maxRetryAttempts` | `5` |
| `retryBaseSeconds` | `2` |
| `maxRetryDelaySeconds` | `64` |
| `rewardGraceFrames` | `1` |
| `bannerPosition` / `bannerSize` / `useAdaptiveBanner` | `Bottom` / `Adaptive` / `true` |
| `verboseLogging`, `testDeviceIds` | `false`, 빈 배열 |
| `forceDummyInEditor` | `true` |

```csharp
public static IContainerBuilder RegisterAdService(this IContainerBuilder builder,
                                                  AdServiceSettings settings)
{
    builder.RegisterInstance(settings);
    builder.Register<IAdRemovalStorage, PlayerPrefsAdRemovalStorage>(Lifetime.Singleton);
    builder.Register<IAdDispatcher, UnityAdDispatcher>(Lifetime.Singleton);
    builder.Register<IAdProviderFactory, AdProviderFactory>(Lifetime.Singleton);
    builder.Register<IAdService, AdService>(Lifetime.Singleton);
    return builder;
}
```

`AdProviderFactory`가 `settings.provider` + 스크립팅 심볼(`FOUNDATIONDI_ADMOB` / `_LEVELPLAY` / `_APPLOVIN`)로 provider를 고른다. **심볼이 없으면 Dummy로 폴백 + 경고 로그.** 이번 범위에서는 항상 이 경로를 탄다.

`AdsRemoved`는 `IAdRemovalStorage` seam(기본 `PlayerPrefsAdRemovalStorage`)으로 영속화한다 — SoundService의 `ISoundVolumeStorage`와 같은 패턴. IAP 자체는 범위 밖이고, 구매 성공 시 게임 코드가 `AdsRemoved = true`를 넣는다.

### 9. Dummy provider (`Providers/Dummy/`)

자체 생성 Canvas(최상단 `sortingOrder`). `UIService`에 의존하지 않는 자립형이다.

- **전면**: 반투명 패널 + "Interstitial" 라벨 + N초 카운트다운 후 Close 버튼 → `Shown`
- **리워드**: 카운트다운 완주 시 `Rewarded`, 중간 Skip 시 `Dismissed`
- **배너**: 지정 높이의 상단/하단 컬러 바, 설정된 높이를 `HeightChanged`로 보고
- **로드 지연/실패 확률을 설정으로 시뮬레이션** → 재시도·백오프를 실기에서 눈으로 검증
- **가짜 네트워크명과 난수 단가로 임프레션 발행** → 어댑터가 없는 지금도 수익 추적 경로를 실기 검증

### 10. 수익 추적 연동

`Paid` 이벤트가 통합 지점이다. ADService는 분석 SDK를 알지 않는다 — 알게 되면 "어떤 네트워크든 동일"이라는 목표에 무관한 의존이 생긴다.

```csharp
_ads.Paid += imp => _analytics.Log("ad_impression", new {
    ad_platform = imp.AdPlatform, ad_source = imp.NetworkName,
    ad_format = imp.Format.ToString(), ad_unit_name = imp.AdUnitId,
    currency = imp.Currency, value = imp.Revenue });
```

Firebase 표준 `ad_impression` 6개 파라미터와 AppsFlyer/Adjust 광고수익 API가 요구하는 `precision`까지 `AdImpression`으로 전부 채울 수 있다.

## 테스트

EditMode, `FoundationDI.Tests` 어셈블리. NSubstitute로 `IFullScreenAdapter` / `IBannerAdapter` / `IAdRemovalStorage`를 대체하고, `IAdDispatcher`는 수동 tick이 가능한 가짜 구현을 쓴다. 테스트 함수명은 한국어 `should~` 의도로 작성한다.

1. 초기화에 성공하면 IsInitialized가 참이 되고 자동 로드가 시작된다
2. 초기화에 실패하면 false를 반환하고 광고를 요청하지 않는다
3. 로드에 실패하면 지수 백오프 지연으로 재시도한다
4. 최대 재시도 횟수를 초과하면 더 이상 재시도하지 않는다
5. 재시도 지연은 최대 지연 시간으로 상한이 걸린다
6. 로드에 성공하면 재시도 카운터가 초기화된다
7. 준비되지 않은 상태의 ShowAsync는 NotReady를 즉시 반환하고 로드를 시작한다
8. 보상 이벤트 후 닫히면 Rewarded와 보상 정보를 반환한다
9. 보상 없이 닫히면 Dismissed를 반환한다
10. 닫힘이 보상보다 먼저 와도 유예 프레임 안에서 Rewarded로 확정된다
11. 전면 광고는 보상 없이 닫히면 Shown을 반환한다
12. 표시에 실패하면 Failed를 반환하고 다시 로드한다
13. 광고가 닫히면 다음 광고를 자동으로 로드한다
14. 표시 중 ShowAsync를 다시 호출하면 Failed를 반환한다
15. AdsRemoved가 켜지면 전면 광고 ShowAsync는 Blocked를 반환한다
16. AdsRemoved가 켜져도 리워드 광고는 정상 표시된다
17. AdsRemoved가 켜지면 배너가 숨겨지고 Show 호출이 무시된다
18. AdsRemoved 상태가 저장소에 영속화되고 복원된다
19. 어댑터 이벤트가 서비스 레벨 이벤트로 포맷과 함께 전파된다
20. provider의 전역 임프레션과 어댑터 임프레션이 모두 Paid로 합류한다
21. Dispose는 어댑터를 정리하고 예약된 재시도를 취소한다

### 구현 시 조기 검증할 리스크

`Awaitable`을 EditMode에서 펌핑하는 문제는 InitializeService에서 이미 겪었다. `ShowAsync` 테스트는 **`await` 이전에 단언**하거나, 가짜 디스패처로 완료를 동기적으로 트리거한 뒤 `await` 한 번만 하는 형태로 작성한다. `Awaitable`은 단일 사용이므로 `await` 후 `IsCompleted`에 접근하지 않는다.

## 범위 밖

- **3사 실제 어댑터 구현** — 계약과 매핑표만 준비. SDK 설치 후 별도 계획.
- IAP / 구매 처리 (`AdsRemoved` 값을 받기만 한다)
- 전면광고 쿨다운·최초지연 게이트 (게임 코드가 결정)
- AppOpen / MREC / Native 광고
- 리모트 컨피그 연동
- 분석 SDK 연동 (`Paid` 이벤트 구독은 게임 코드 몫)
