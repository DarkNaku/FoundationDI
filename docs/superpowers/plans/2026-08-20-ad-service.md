# ADService Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** AdMob 미디에이션 / Unity LevelPlay / AppLovin MAX 어느 것을 쓰더라도 게임 코드가 동일한 API로 배너·전면·보상 광고를 다루는 `IAdService`를 만든다.

**Architecture:** 3계층. **공개 계약**(`IAdService` + 포맷 핸들) → **정책 계층**(`FullScreenAdUnit`/`BannerAdUnit`: 재시도·자동 재로드·보상 확정·광고제거 게이트) → **얇은 SDK 어댑터**(`IFullScreenAdapter`/`IBannerAdapter`). SDK별 차이(인스턴스 vs 정적 API, 1회용 vs 재사용 객체, 이벤트 순서 무보장)는 전부 어댑터 경계 안쪽에 갇힌다. 이번 범위에서 실제 어댑터는 **Dummy 하나**만 구현하고, 3사 어댑터는 후속 작업이다.

**Tech Stack:** Unity 6000.3.17f1, VContainer(DI), UnityEngine `Awaitable`(비동기 — UniTask 아님), NUnit + NSubstitute 5.3.0(EditMode 테스트), uGUI(Dummy 광고 화면).

**Spec:** `docs/superpowers/specs/2026-08-20-adservice-design.md`

## Global Constraints

- 네임스페이스는 **`DarkNaku.FoundationDI`** 단일. 예외 없음.
- 런타임 코드는 전부 `Assets/FoundationDI/Runtime/Services/AdService/` 아래. `Assets/Scripts/`는 호스트 프로젝트 전용이므로 건드리지 않는다.
- 비동기는 **`Awaitable`**. 새 프로덕션 코드에 `UniTask`/`Task`를 쓰지 않는다. (테스트 코드에서 `UniTask.ToCoroutine`을 코루틴 어댑터로 쓰는 것은 기존 관례이므로 허용.)
- **`Awaitable`은 단일 사용이다.** `await` 이후 같은 인스턴스의 `.IsCompleted` 등에 접근하지 않는다. 호출자마다 `AwaitableCompletionSource`를 새로 만든다.
- 테스트는 `Assets/FoundationDI/Tests/` 에 두고 어셈블리는 기존 **`FoundationDI.Tests`**(EditMode, `overrideReferences: true`)를 그대로 쓴다. 새 asmdef를 만들지 않는다.
- 테스트 함수 이름은 **한국어**, `should~` 의도. 형식은 `[UnityTest] public IEnumerator 이름() => UniTask.ToCoroutine(async () => { ... });`
- **모든 `[UnityTest]` 메서드에 `[Timeout(5000)]`을 함께 붙인다.** 완료되지 않는 `Awaitable`을 `await` 하면 EditMode 러너가 무한 대기하며 Unity Editor를 붙잡는다 — 이 계획의 여러 "실패를 확인한다" 단계가 정확히 그 상황을 의도적으로 만든다. `using NUnit.Framework;`에 포함된 `TimeoutAttribute`를 쓴다.
- **구조적 변경과 행동적 변경을 같은 커밋에 섞지 않는다.** 커밋 제목에 `[STRUCTURAL]` 또는 `[BEHAVIORAL]` 접두어를 단다.
- 한 번에 하나의 테스트만 작성하고, 매번 전체 테스트를 돌린다.
- 컴파일·테스트는 **UnityMCP**로만 가능하다. Unity Editor가 떠 있어야 하고 `.mcp.json`의 `http://127.0.0.1:8086/mcp`에 연결되어 있어야 한다. CLI 빌드 명령은 없다.
- 스크립트 생성/수정 후 `read_console`로 컴파일 에러를 먼저 확인한다. `editor_state.isCompiling == false`가 되어야 새 타입을 쓸 수 있다.
- 재시도 기본값: `maxAttempts=5`, `retryBaseSeconds=2`, `maxRetryDelaySeconds=64`. 보상 유예 기본값: `rewardGraceFrames=1`.
- 작업 브랜치는 **`feature/ad-service`** (이미 생성됨, spec 커밋 완료). 이 프로젝트는 worktree를 쓰지 않는다.

## 테스트 실행 방법 (모든 Task 공통)

UnityMCP `run_tests` 툴을 쓴다:

```
run_tests(mode="EditMode", testFilter="AdServiceTest")          # 파일 단위
run_tests(mode="EditMode", testFilter="AdServiceTest.테스트이름")  # 단일 테스트
run_tests(mode="EditMode")                                       # 전체
```

`run_tests`가 job id를 반환하면 `get_test_job`으로 결과를 조회한다. 테스트 파일을 수정할 때는 **`Write`로 파일 전체를 다시 쓴다** (부분 편집은 이 프로젝트에서 실패한 이력이 있다).

## File Structure

```
Assets/FoundationDI/Runtime/Services/AdService/
  AdTypes.cs                       값 타입 전부 (enum, AdReward/AdError/AdShowResult/AdImpression/AdRetryPolicy)
  AdUnitId.cs                      플랫폼별 광고 단위 ID 해석
  IAdService.cs                    공개 계약 루트 (IAdService)
  AdService.cs                     조립 + 이벤트 합류 + AdsRemoved 소유
  Ads/
    IFullScreenAd.cs               IFullScreenAd, IInterstitialAd, IRewardedAd
    FullScreenAdUnit.cs            전면/보상 정책 계층 (이 서비스의 두뇌)
    IBannerAd.cs                   IBannerAd
    BannerAdUnit.cs                배너 정책 계층
  Providers/
    IAdProvider.cs                 provider seam + AdProviderContext + BannerOptions
    IFullScreenAdapter.cs          전면/보상 어댑터 seam
    IBannerAdapter.cs              배너 어댑터 seam
    IAdProviderFactory.cs          provider 선택 seam
    AdProviderFactory.cs           설정+심볼로 provider 선택, 없으면 Dummy 폴백
    Dummy/
      DummyAdProvider.cs           Dummy provider 루트
      DummyFullScreenAdapter.cs    가짜 전면/보상
      DummyBannerAdapter.cs        가짜 배너
      DummyAdCanvas.cs             자립형 uGUI 화면
  Consent/
    IAdConsent.cs                  동의 seam
    NoopAdConsent.cs               항상 허용하는 기본 구현
  Dispatch/
    IAdDispatcher.cs               메인스레드 마샬링 + 시간 seam
    UnityAdDispatcher.cs           실제 구현
    AdServiceRunner.cs             숨겨진 MonoBehaviour 펌프
  Storage/
    IAdRemovalStorage.cs           광고제거 상태 영속화 seam
    PlayerPrefsAdRemovalStorage.cs 기본 구현
  Settings/
    AdProviderType.cs              enum
    BannerOptions.cs               배너 위치/사이즈
    AdServiceSettings.cs           ScriptableObject
  AdServiceRegistration.cs         builder.RegisterAdService(settings)
  README.md

Assets/FoundationDI/Tests/
  AdTypesTest.cs                   Task 1
  AdTestDoubles.cs                 Task 2, 9 — 테스트 하네스 (Fake* 전부)
  AdTestDoublesTest.cs             Task 2 — 하네스 자체 검증
  FullScreenAdUnitTest.cs          Task 3~6, 8
  BannerAdUnitTest.cs              Task 7, 8
  AdRemovalStorageTest.cs          Task 8
  AdServiceTest.cs                 Task 9
  UnityAdDispatcherTest.cs         Task 10
  DummyAdProviderTest.cs           Task 11
  AdProviderFactoryTest.cs         Task 12
```

**파일 분해 근거:** `FullScreenAdUnit`이 이 서비스에서 유일하게 복잡한 파일이고 테스트의 대부분이 여기에 걸린다. 나머지는 각각 한 가지 책임만 갖는 작은 파일이다. seam 인터페이스를 파일당 하나씩 나눈 것은 3사 어댑터를 나중에 작성할 사람이 필요한 계약만 열어보게 하기 위함이다.

---

### Task 1: 값 타입과 재시도 정책

가장 아래층. 다른 모든 Task가 이 타입들을 쓴다. 로직이 있는 건 `AdRetryPolicy.DelayFor`와 `AdUnitId.Current` 둘뿐이고, 나머지는 데이터 홀더다.

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/AdService/AdTypes.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/AdUnitId.cs`
- Test: `Assets/FoundationDI/Tests/AdTypesTest.cs`

**Interfaces:**
- Consumes: 없음 (첫 Task)
- Produces:
  - `enum AdFormat { Banner, Interstitial, Rewarded }`
  - `enum AdShowOutcome { Shown, Rewarded, Dismissed, NotReady, Failed, Blocked }`
  - `enum AdRevenuePrecision { Unknown, Estimated, PublisherDefined, Exact }`
  - `readonly struct AdReward { string Label; double Amount; }` — 생성자 `AdReward(string label, double amount)`
  - `readonly struct AdError { int Code; string Message; }` — 생성자 `AdError(int code, string message)`
  - `readonly struct AdShowResult` — 정적 팩토리 `Shown()`, `Rewarded(AdReward)`, `Dismissed()`, `NotReady()`, `Failed(AdError)`, `Blocked()`; 프로퍼티 `Outcome`, `Reward`, `Error`, `IsRewarded`, `WasShown`
  - `readonly struct AdImpression` — 생성자로 10개 필드 전부 받음
  - `readonly struct AdRetryPolicy` — 생성자 `AdRetryPolicy(int maxAttempts, float baseSeconds, float maxDelaySeconds)`, 정적 `Default`, 메서드 `float DelayFor(int attempt)`
  - `readonly struct AdUnitId { string Android; string iOS; string Current; }` — 생성자 `AdUnitId(string android, string ios)`

- [ ] **Step 1: 첫 실패 테스트를 작성한다**

`Assets/FoundationDI/Tests/AdTypesTest.cs` 를 `Write`로 생성:

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class AdTypesTest
{
    [Test]
    public void 재시도_지연은_시도횟수에_대해_지수적으로_증가한다()
    {
        var policy = new AdRetryPolicy(maxAttempts: 5, baseSeconds: 2f, maxDelaySeconds: 64f);

        Assert.AreEqual(2f, policy.DelayFor(1), 0.001f);
        Assert.AreEqual(4f, policy.DelayFor(2), 0.001f);
        Assert.AreEqual(8f, policy.DelayFor(3), 0.001f);
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

UnityMCP: `read_console` 로 컴파일 에러 확인. `AdRetryPolicy` 타입이 없어서 컴파일 실패해야 한다.
Expected: `error CS0246: The type or namespace name 'AdRetryPolicy' could not be found`

- [ ] **Step 3: 최소 구현을 작성한다**

`Assets/FoundationDI/Runtime/Services/AdService/AdTypes.cs` 생성:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public readonly struct AdRetryPolicy
    {
        public int MaxAttempts { get; }
        public float BaseSeconds { get; }
        public float MaxDelaySeconds { get; }

        public AdRetryPolicy(int maxAttempts, float baseSeconds, float maxDelaySeconds)
        {
            MaxAttempts = maxAttempts;
            BaseSeconds = baseSeconds;
            MaxDelaySeconds = maxDelaySeconds;
        }

        public static AdRetryPolicy Default => new(5, 2f, 64f);

        // 지연 = base^attempt, 단 상한으로 클램프. attempt는 1부터 시작한다.
        public float DelayFor(int attempt)
        {
            return Mathf.Min(Mathf.Pow(BaseSeconds, attempt), MaxDelaySeconds);
        }
    }
}
```

- [ ] **Step 4: 통과를 확인한다**

`read_console`로 컴파일 완료 확인 후:
Run: `run_tests(mode="EditMode", testFilter="AdTypesTest")`
Expected: PASS 1건

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/AdTypes.cs \
        Assets/FoundationDI/Runtime/Services/AdService/AdTypes.cs.meta \
        Assets/FoundationDI/Tests/AdTypesTest.cs \
        Assets/FoundationDI/Tests/AdTypesTest.cs.meta
git commit -m "[BEHAVIORAL] AdRetryPolicy 지수 백오프 지연 계산 추가"
```

- [ ] **Step 6: 상한 테스트를 추가한다**

`AdTypesTest.cs`를 `Write`로 다시 쓰되, 위 테스트 뒤에 추가:

```csharp
    [Test]
    public void 재시도_지연은_최대_지연시간을_넘지_않는다()
    {
        var policy = new AdRetryPolicy(maxAttempts: 10, baseSeconds: 2f, maxDelaySeconds: 10f);

        Assert.AreEqual(8f, policy.DelayFor(3), 0.001f);
        Assert.AreEqual(10f, policy.DelayFor(4), 0.001f);   // 16 → 10으로 클램프
        Assert.AreEqual(10f, policy.DelayFor(9), 0.001f);
    }
```

- [ ] **Step 7: 통과를 확인한다** — Step 3의 `Mathf.Min`이 이미 처리하므로 바로 통과한다.

Run: `run_tests(mode="EditMode", testFilter="AdTypesTest")`
Expected: PASS 2건

이 테스트가 처음부터 통과하는 것은 정상이다. `Mathf.Min`을 쓴 구현이 이미 상한을 만족하기 때문이고, 이 테스트는 **회귀 방지**가 목적이다. (만약 실패한다면 Step 3 구현이 잘못된 것이다.)

- [ ] **Step 8: `AdShowResult` 실패 테스트를 추가한다**

`AdTypesTest.cs`에 추가:

```csharp
    [Test]
    public void 보상_결과는_보상정보를_담고_노출된_것으로_간주된다()
    {
        var result = AdShowResult.Rewarded(new AdReward("coins", 50));

        Assert.AreEqual(AdShowOutcome.Rewarded, result.Outcome);
        Assert.IsTrue(result.IsRewarded);
        Assert.IsTrue(result.WasShown);
        Assert.AreEqual("coins", result.Reward.Label);
        Assert.AreEqual(50, result.Reward.Amount, 0.001);
    }

    [Test]
    public void 보상없이_닫힘과_정상노출은_노출된_것이지만_보상은_아니다()
    {
        Assert.IsTrue(AdShowResult.Dismissed().WasShown);
        Assert.IsFalse(AdShowResult.Dismissed().IsRewarded);
        Assert.IsTrue(AdShowResult.Shown().WasShown);
        Assert.IsFalse(AdShowResult.Shown().IsRewarded);
    }

    [Test]
    public void 준비안됨_실패_차단은_노출되지_않은_것으로_간주된다()
    {
        Assert.IsFalse(AdShowResult.NotReady().WasShown);
        Assert.IsFalse(AdShowResult.Blocked().WasShown);
        Assert.IsFalse(AdShowResult.Failed(new AdError(3, "no fill")).WasShown);
        Assert.AreEqual(3, AdShowResult.Failed(new AdError(3, "no fill")).Error.Code);
    }
```

- [ ] **Step 9: 실패를 확인한다**

`read_console`: `AdShowResult` / `AdReward` / `AdError` / `AdShowOutcome` 미정의로 컴파일 실패.

- [ ] **Step 10: 값 타입들을 구현한다**

`AdTypes.cs`에 `AdRetryPolicy` 위쪽으로 추가:

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

    public readonly struct AdReward
    {
        public string Label { get; }
        public double Amount { get; }
        public AdReward(string label, double amount) { Label = label; Amount = amount; }
    }

    public readonly struct AdError
    {
        public int Code { get; }
        public string Message { get; }
        public AdError(int code, string message) { Code = code; Message = message; }
        public override string ToString() => $"({Code}) {Message}";
    }

    public readonly struct AdShowResult
    {
        public AdShowOutcome Outcome { get; }
        public AdReward Reward { get; }   // Outcome == Rewarded 일 때만 유효
        public AdError Error { get; }     // Outcome == Failed 일 때만 유효

        private AdShowResult(AdShowOutcome outcome, AdReward reward, AdError error)
        {
            Outcome = outcome;
            Reward = reward;
            Error = error;
        }

        public static AdShowResult Shown() => new(AdShowOutcome.Shown, default, default);
        public static AdShowResult Rewarded(AdReward reward) => new(AdShowOutcome.Rewarded, reward, default);
        public static AdShowResult Dismissed() => new(AdShowOutcome.Dismissed, default, default);
        public static AdShowResult NotReady() => new(AdShowOutcome.NotReady, default, default);
        public static AdShowResult Failed(AdError error) => new(AdShowOutcome.Failed, default, error);
        public static AdShowResult Blocked() => new(AdShowOutcome.Blocked, default, default);

        public bool IsRewarded => Outcome == AdShowOutcome.Rewarded;

        // 광고가 실제로 화면에 떴는지. 보상 여부와 무관하다.
        public bool WasShown => Outcome is AdShowOutcome.Shown
                                        or AdShowOutcome.Rewarded
                                        or AdShowOutcome.Dismissed;
    }

    public readonly struct AdImpression
    {
        public AdFormat Format { get; }
        public string AdPlatform { get; }        // "AdMob"/"LevelPlay"/"AppLovin" → ad_platform
        public string NetworkName { get; }       // 실제 채운 네트워크           → ad_source
        public string AdUnitId { get; }          //                             → ad_unit_name
        public string NetworkPlacement { get; }  // instanceName / NetworkPlacement
        public string Placement { get; }         // 게임이 ShowAsync에 넘긴 배치명
        public double Revenue { get; }
        public string Currency { get; }          // AdMob은 USD가 아닐 수 있다 — 반드시 함께 사용
        public AdRevenuePrecision Precision { get; }
        public string CreativeId { get; }        // 없으면 null

        public AdImpression(AdFormat format, string adPlatform, string networkName, string adUnitId,
                            string networkPlacement, string placement, double revenue, string currency,
                            AdRevenuePrecision precision, string creativeId)
        {
            Format = format;
            AdPlatform = adPlatform;
            NetworkName = networkName;
            AdUnitId = adUnitId;
            NetworkPlacement = networkPlacement;
            Placement = placement;
            Revenue = revenue;
            Currency = currency;
            Precision = precision;
            CreativeId = creativeId;
        }
    }
```

- [ ] **Step 11: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="AdTypesTest")`
Expected: PASS 5건

- [ ] **Step 12: `AdUnitId`를 추가한다**

`AdTypesTest.cs`에 추가:

```csharp
    [Test]
    public void 광고단위ID는_현재_플랫폼에_해당하는_값을_돌려준다()
    {
        var id = new AdUnitId("android-unit", "ios-unit");

#if UNITY_ANDROID
        Assert.AreEqual("android-unit", id.Current);
#elif UNITY_IOS
        Assert.AreEqual("ios-unit", id.Current);
#else
        Assert.IsTrue(string.IsNullOrEmpty(id.Current));
#endif
    }
```

- [ ] **Step 13: 실패를 확인한다** — `read_console`: `AdUnitId` 미정의.

- [ ] **Step 14: `AdUnitId`를 구현한다**

`Assets/FoundationDI/Runtime/Services/AdService/AdUnitId.cs` 생성:

```csharp
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 인스펙터에서 편집 가능해야 하므로 [Serializable] + SerializeField.
    // readonly struct가 아닌 이유가 이것이다.
    [Serializable]
    public struct AdUnitId
    {
        [SerializeField] private string _android;
        [SerializeField] private string _ios;

        public AdUnitId(string android, string ios) { _android = android; _ios = ios; }

        public string Android => _android;
        public string iOS => _ios;

        // 에디터에서는 UNITY_ANDROID/UNITY_IOS가 빌드 타깃을 따라가므로
        // 에디터 실행 중에도 현재 타깃의 ID가 나온다.
        public string Current
        {
#if UNITY_ANDROID
            get => _android;
#elif UNITY_IOS
            get => _ios;
#else
            get => string.Empty;
#endif
        }

        public bool IsValid => !string.IsNullOrEmpty(Current);
    }
}
```

- [ ] **Step 15: 통과를 확인한다**

Run: `run_tests(mode="EditMode")` (전체)
Expected: `AdTypesTest` 6건 PASS + 기존 테스트 전부 PASS

- [ ] **Step 16: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/ Assets/FoundationDI/Tests/AdTypesTest.cs*
git commit -m "[BEHAVIORAL] ADService 값 타입과 플랫폼별 광고단위 ID 추가"
```

---

### Task 2: seam 인터페이스와 테스트 하네스

정책 계층을 테스트하려면 **시간을 손으로 돌릴 수 있어야** 한다. 이 Task는 세 개의 seam 인터페이스를 정의하고, 이후 모든 Task가 쓸 테스트 더블을 만든다. 하네스 자체도 버그가 날 수 있으므로 테스트를 붙인다.

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Dispatch/IAdDispatcher.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Providers/IFullScreenAdapter.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Providers/IBannerAdapter.cs`
- Create: `Assets/FoundationDI/Tests/AdTestDoubles.cs`
- Test: `Assets/FoundationDI/Tests/AdTestDoublesTest.cs`

**Interfaces:**
- Consumes: Task 1의 `AdError`, `AdReward`, `AdImpression`
- Produces:
  - `interface IAdDispatcher { void Post(Action); IDisposable Delay(float, Action); IDisposable NextFrames(int, Action); }`
  - `interface IFullScreenAdapter : IDisposable` — `bool IsReady`, `void Load()`, `void Show()`, 이벤트 `Loaded`, `LoadFailed(AdError)`, `Displayed`, `DisplayFailed(AdError)`, `Closed`, `Rewarded(AdReward)`, `Paid(AdImpression)`
  - `interface IBannerAdapter : IDisposable` — `float Height`, `void Show()`, `void Hide()`, 이벤트 `HeightChanged(float)`, `Paid(AdImpression)`
  - `class FakeAdDispatcher : IAdDispatcher` — `void Advance(float seconds)`, `void TickFrames(int count)`, `int PendingCount`
  - `class FakeFullScreenAdapter : IFullScreenAdapter` — `int LoadCount`, `int ShowCount`, `bool IsReady { get; set; }`, `bool IsDisposed`, 발화 메서드 `RaiseLoaded()`, `RaiseLoadFailed(AdError)`, `RaiseDisplayed()`, `RaiseDisplayFailed(AdError)`, `RaiseClosed()`, `RaiseRewarded(AdReward)`, `RaisePaid(AdImpression)`
  - `class FakeBannerAdapter : IBannerAdapter` — `int ShowCount`, `int HideCount`, `bool IsDisposed`, `void SetHeight(float)`, `void RaisePaid(AdImpression)`

- [ ] **Step 1: 실패 테스트를 작성한다**

`Assets/FoundationDI/Tests/AdTestDoublesTest.cs` 를 `Write`로 생성:

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class AdTestDoublesTest
{
    [Test]
    public void 가짜_디스패처는_지정_시간이_지나야_지연작업을_실행한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var ran = 0;

        dispatcher.Delay(5f, () => ran++);

        dispatcher.Advance(4.9f);
        Assert.AreEqual(0, ran, "아직 시간이 안 됐는데 실행됐다");

        dispatcher.Advance(0.2f);
        Assert.AreEqual(1, ran, "시간이 지났는데 실행되지 않았다");

        dispatcher.Advance(100f);
        Assert.AreEqual(1, ran, "한 번 실행된 작업이 다시 실행됐다");
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

`read_console`: `FakeAdDispatcher` 미정의로 컴파일 실패.

- [ ] **Step 3: seam 인터페이스를 작성한다**

`Dispatch/IAdDispatcher.cs`:

```csharp
using System;

namespace DarkNaku.FoundationDI
{
    // 두 가지 목적이 있다.
    // 1) 세 광고 SDK 모두 네이티브 스레드에서 콜백이 올라올 수 있어 메인스레드 마샬링이 필요하다.
    // 2) 백오프 지연과 보상 유예 프레임을 가짜 시계로 테스트할 수 있게 한다. 이쪽이 더 큰 이유다.
    public interface IAdDispatcher
    {
        // 메인 스레드에서 실행되도록 큐에 넣는다. 이미 메인 스레드여도 큐를 거친다.
        void Post(Action action);

        // seconds 후 실행. 반환된 IDisposable을 Dispose하면 취소된다.
        IDisposable Delay(float seconds, Action action);

        // count 프레임 후 실행. count가 0이면 즉시 실행한다. 반환값 Dispose로 취소.
        IDisposable NextFrames(int count, Action action);
    }
}
```

`Providers/IFullScreenAdapter.cs`:

```csharp
using System;

namespace DarkNaku.FoundationDI
{
    // 전면/보상 광고 단위 하나를 나타내는 얇은 SDK 래퍼.
    // 재시도, 자동 재로드, 보상 확정은 여기 책임이 아니다 — FullScreenAdUnit이 한다.
    // 구현체는 이벤트를 반드시 메인 스레드에서 발화시켜야 한다.
    public interface IFullScreenAdapter : IDisposable
    {
        bool IsReady { get; }
        void Load();
        void Show();

        event Action Loaded;
        event Action<AdError> LoadFailed;
        event Action Displayed;
        event Action<AdError> DisplayFailed;
        event Action Closed;

        // 보상 어댑터에서만 발화한다. 전면 어댑터는 발화시키지 않는다.
        // Closed와의 순서는 SDK/네트워크마다 다르므로 보장하지 않아도 된다.
        event Action<AdReward> Rewarded;

        event Action<AdImpression> Paid;
    }
}
```

`Providers/IBannerAdapter.cs`:

```csharp
using System;

namespace DarkNaku.FoundationDI
{
    // 배너는 갱신을 SDK가 자동 처리하므로 Load/재시도 개념을 노출하지 않는다.
    // 구현체는 이벤트를 반드시 메인 스레드에서 발화시켜야 한다.
    public interface IBannerAdapter : IDisposable
    {
        float Height { get; }   // 화면 픽셀. 미로드/미표시면 0
        void Show();
        void Hide();

        event Action<float> HeightChanged;
        event Action<AdImpression> Paid;
    }
}
```

- [ ] **Step 4: 테스트 하네스를 작성한다**

`Assets/FoundationDI/Tests/AdTestDoubles.cs` 를 `Write`로 생성:

```csharp
using System;
using System.Collections.Generic;
using DarkNaku.FoundationDI;

// 정책 계층 테스트용 시계. 실제 시간을 쓰지 않고 Advance/TickFrames로 손으로 돌린다.
public class FakeAdDispatcher : IAdDispatcher
{
    private class Entry
    {
        public float DueAt;        // Delay용 (누적 시간 기준)
        public int FramesLeft;     // NextFrames용
        public bool IsFrameBased;
        public Action Action;
        public bool Cancelled;
    }

    private class Handle : IDisposable
    {
        private readonly Entry _entry;
        public Handle(Entry entry) { _entry = entry; }
        public void Dispose() { _entry.Cancelled = true; }
    }

    private readonly List<Entry> _entries = new();
    private float _now;

    public int PendingCount
    {
        get
        {
            var count = 0;
            foreach (var e in _entries) if (!e.Cancelled) count++;
            return count;
        }
    }

    // Post는 즉시 실행한다. 테스트에서 마샬링 지연을 재현할 이유가 없다.
    public void Post(Action action) => action?.Invoke();

    public IDisposable Delay(float seconds, Action action)
    {
        var entry = new Entry { DueAt = _now + seconds, IsFrameBased = false, Action = action };
        _entries.Add(entry);
        return new Handle(entry);
    }

    public IDisposable NextFrames(int count, Action action)
    {
        if (count <= 0)
        {
            action?.Invoke();
            return new Handle(new Entry { Cancelled = true });
        }

        var entry = new Entry { FramesLeft = count, IsFrameBased = true, Action = action };
        _entries.Add(entry);
        return new Handle(entry);
    }

    public void Advance(float seconds)
    {
        _now += seconds;
        Flush(e => !e.IsFrameBased && e.DueAt <= _now);
    }

    public void TickFrames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            foreach (var e in _entries) if (e.IsFrameBased) e.FramesLeft--;
            Flush(e => e.IsFrameBased && e.FramesLeft <= 0);
        }
    }

    // 실행 중에 새 작업이 예약될 수 있으므로(자동 재로드 등) 스냅샷을 뜬 뒤 순회한다.
    private void Flush(Func<Entry, bool> isDue)
    {
        var due = new List<Entry>();
        foreach (var e in _entries) if (!e.Cancelled && isDue(e)) due.Add(e);
        foreach (var e in due) e.Cancelled = true;
        _entries.RemoveAll(e => e.Cancelled);
        foreach (var e in due) e.Action?.Invoke();
    }
}

public class FakeFullScreenAdapter : IFullScreenAdapter
{
    public int LoadCount { get; private set; }
    public int ShowCount { get; private set; }
    public bool IsReady { get; set; }
    public bool IsDisposed { get; private set; }

    public void Load() => LoadCount++;
    public void Show() => ShowCount++;
    public void Dispose() => IsDisposed = true;

    public event Action Loaded;
    public event Action<AdError> LoadFailed;
    public event Action Displayed;
    public event Action<AdError> DisplayFailed;
    public event Action Closed;
    public event Action<AdReward> Rewarded;
    public event Action<AdImpression> Paid;

    // 준비 상태를 함께 바꿔주는 편의 발화기. 테스트가 IsReady를 따로 세팅할 필요를 없앤다.
    public void RaiseLoaded() { IsReady = true; Loaded?.Invoke(); }
    public void RaiseLoadFailed(AdError error) { IsReady = false; LoadFailed?.Invoke(error); }
    public void RaiseDisplayed() => Displayed?.Invoke();
    public void RaiseDisplayFailed(AdError error) { IsReady = false; DisplayFailed?.Invoke(error); }
    public void RaiseClosed() { IsReady = false; Closed?.Invoke(); }
    public void RaiseRewarded(AdReward reward) => Rewarded?.Invoke(reward);
    public void RaisePaid(AdImpression impression) => Paid?.Invoke(impression);
}

public class FakeBannerAdapter : IBannerAdapter
{
    public int ShowCount { get; private set; }
    public int HideCount { get; private set; }
    public bool IsDisposed { get; private set; }
    public float Height { get; private set; }

    public void Show() => ShowCount++;
    public void Hide() => HideCount++;
    public void Dispose() => IsDisposed = true;

    public event Action<float> HeightChanged;
    public event Action<AdImpression> Paid;

    public void SetHeight(float height) { Height = height; HeightChanged?.Invoke(height); }
    public void RaisePaid(AdImpression impression) => Paid?.Invoke(impression);
}
```

- [ ] **Step 5: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="AdTestDoublesTest")`
Expected: PASS 1건

- [ ] **Step 6: 취소와 프레임 틱 테스트를 추가한다**

`AdTestDoublesTest.cs`를 `Write`로 다시 쓰되, Step 1의 테스트 뒤에 추가:

```csharp
    [Test]
    public void 가짜_디스패처는_취소된_지연작업을_실행하지_않는다()
    {
        var dispatcher = new FakeAdDispatcher();
        var ran = 0;

        var handle = dispatcher.Delay(5f, () => ran++);
        handle.Dispose();

        dispatcher.Advance(10f);

        Assert.AreEqual(0, ran);
    }

    [Test]
    public void 가짜_디스패처는_지정_프레임수가_지나야_작업을_실행한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var ran = 0;

        dispatcher.NextFrames(2, () => ran++);

        dispatcher.TickFrames(1);
        Assert.AreEqual(0, ran);

        dispatcher.TickFrames(1);
        Assert.AreEqual(1, ran);
    }

    [Test]
    public void 가짜_디스패처는_프레임수가_0이면_즉시_실행한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var ran = 0;

        dispatcher.NextFrames(0, () => ran++);

        Assert.AreEqual(1, ran);
    }

    [Test]
    public void 가짜_디스패처는_실행중에_예약된_작업을_같은_틱에_실행하지_않는다()
    {
        // 자동 재로드가 재시도를 예약하는 상황을 재현한다.
        // 스냅샷 순회가 깨지면 여기서 무한 루프나 조기 실행이 잡힌다.
        var dispatcher = new FakeAdDispatcher();
        var outer = 0;
        var inner = 0;

        dispatcher.Delay(1f, () =>
        {
            outer++;
            dispatcher.Delay(1f, () => inner++);
        });

        dispatcher.Advance(1f);
        Assert.AreEqual(1, outer);
        Assert.AreEqual(0, inner, "중첩 예약이 같은 틱에 실행됐다");

        dispatcher.Advance(1f);
        Assert.AreEqual(1, inner);
    }
```

- [ ] **Step 7: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="AdTestDoublesTest")`
Expected: PASS 5건

마지막 테스트가 실패하면 `Flush`의 스냅샷 순회가 잘못된 것이다. `_entries`를 직접 순회하면서 실행하면 실행 중 추가된 항목까지 같은 틱에 처리되어 자동 재로드 테스트가 전부 거짓 통과한다.

- [ ] **Step 8: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Dispatch/ \
        Assets/FoundationDI/Runtime/Services/AdService/Providers/ \
        Assets/FoundationDI/Tests/AdTestDoubles*.cs*
git commit -m "[STRUCTURAL] ADService 어댑터/디스패처 seam과 테스트 하네스 추가"
```

---

### Task 3: FullScreenAdUnit — 로드와 지수 백오프 재시도

정책 계층의 첫 조각. 여기부터 Task 6까지가 이 서비스의 핵심이며, 세 SDK의 차이를 흡수하는 코드가 전부 여기 모인다.

**`blockWhenAdsRemoved`는 생성자 파라미터가 아니라 `format`에서 유도한다** — 전면은 차단, 보상은 허용이 스펙상 고정이므로 호출자가 틀리게 넘길 여지를 없앤다.

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Ads/IFullScreenAd.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Ads/FullScreenAdUnit.cs`
- Test: `Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs`

**Interfaces:**
- Consumes: Task 1의 `AdFormat`/`AdError`/`AdReward`/`AdImpression`/`AdShowResult`/`AdRetryPolicy`, Task 2의 `IFullScreenAdapter`/`IAdDispatcher`/`FakeFullScreenAdapter`/`FakeAdDispatcher`
- Produces:
  - `interface IFullScreenAd { bool IsReady { get; } void Load(); Awaitable<AdShowResult> ShowAsync(string placement = null); }`
  - `interface IInterstitialAd : IFullScreenAd { }`
  - `interface IRewardedAd : IFullScreenAd { }`
  - `class FullScreenAdUnit : IInterstitialAd, IRewardedAd, IDisposable`
    - 생성자 `FullScreenAdUnit(IFullScreenAdapter adapter, IAdDispatcher dispatcher, AdFormat format, AdRetryPolicy retryPolicy, int rewardGraceFrames, Func<bool> adsRemoved)`
    - 이벤트 `event Action Loaded; event Action Displayed; event Action Closed; event Action<AdImpression> Paid;` (AdService가 구독해 포맷과 함께 재발행)

- [ ] **Step 1: 실패 테스트를 작성한다**

`Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs` 를 `Write`로 생성:

```csharp
using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class FullScreenAdUnitTest
{
    private static readonly AdRetryPolicy Policy = new(maxAttempts: 3, baseSeconds: 2f, maxDelaySeconds: 64f);

    // 테스트마다 반복되는 조립을 한 곳으로 모은다. adsRemoved 기본은 false.
    private static FullScreenAdUnit NewUnit(FakeFullScreenAdapter adapter, FakeAdDispatcher dispatcher,
                                           AdFormat format = AdFormat.Interstitial,
                                           int rewardGraceFrames = 1,
                                           Func<bool> adsRemoved = null)
    {
        return new FullScreenAdUnit(adapter, dispatcher, format, Policy, rewardGraceFrames, adsRemoved);
    }

    [Test]
    public void 로드에_실패하면_지수_백오프_지연으로_재시도한다()
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        sut.Load();
        Assert.AreEqual(1, adapter.LoadCount, "최초 로드가 호출되지 않았다");

        adapter.RaiseLoadFailed(new AdError(3, "no fill"));

        dispatcher.Advance(1.9f);
        Assert.AreEqual(1, adapter.LoadCount, "2초 전에 재시도했다");

        dispatcher.Advance(0.2f);   // 누적 2.1초 — 첫 재시도는 2^1 = 2초
        Assert.AreEqual(2, adapter.LoadCount, "2초 후 재시도하지 않았다");

        adapter.RaiseLoadFailed(new AdError(3, "no fill"));

        dispatcher.Advance(3.9f);
        Assert.AreEqual(2, adapter.LoadCount, "4초 전에 재시도했다");

        dispatcher.Advance(0.2f);   // 두 번째 재시도는 2^2 = 4초
        Assert.AreEqual(3, adapter.LoadCount);
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

`read_console`: `FullScreenAdUnit` 미정의로 컴파일 실패.
Expected: `error CS0246: ... 'FullScreenAdUnit' could not be found`

- [ ] **Step 3: 공개 계약을 작성한다**

`Ads/IFullScreenAd.cs` 생성:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 전면/보상 광고의 공통 계약. 게임 코드는 IAdService.Interstitial / .Rewarded 로 접근한다.
    public interface IFullScreenAd
    {
        bool IsReady { get; }

        // 수동 로드. 자동 재로드가 기본이라 평소에는 부를 일이 없다.
        void Load();

        // placement는 분석 이벤트에 실릴 배치명이며 광고 표시 자체에는 영향을 주지 않는다.
        Awaitable<AdShowResult> ShowAsync(string placement = null);
    }

    // 현재 IFullScreenAd와 동일하지만 호출부 타입 안전성과 향후 분화를 위해 분리한다.
    public interface IInterstitialAd : IFullScreenAd { }
    public interface IRewardedAd : IFullScreenAd { }
}
```

- [ ] **Step 4: 최소 구현을 작성한다**

`Ads/FullScreenAdUnit.cs` 생성. 이 단계에서는 로드/재시도만 만든다 — `ShowAsync`는 Task 4에서 채운다.

```csharp
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 전면/보상 광고의 정책 계층. provider를 전혀 모르고 어댑터 seam만 안다.
    // 재시도, 자동 재로드, 보상 확정, 광고제거 게이트가 전부 여기 있다 —
    // 어댑터마다 복붙되지 않도록 하는 것이 이 클래스의 존재 이유다.
    public class FullScreenAdUnit : IInterstitialAd, IRewardedAd, IDisposable
    {
        private readonly IFullScreenAdapter _adapter;
        private readonly IAdDispatcher _dispatcher;
        private readonly AdFormat _format;
        private readonly AdRetryPolicy _retryPolicy;
        private readonly int _rewardGraceFrames;
        private readonly Func<bool> _adsRemoved;

        // 전면은 광고제거 시 차단, 보상은 항상 허용. format에서 유도해 호출자가 틀릴 여지를 없앤다.
        private readonly bool _blockWhenAdsRemoved;

        private int _retryAttempt;
        private IDisposable _scheduledRetry;
        private bool _isDisposed;

        public event Action Loaded;
        public event Action Displayed;
        public event Action Closed;
        public event Action<AdImpression> Paid;

        public FullScreenAdUnit(IFullScreenAdapter adapter, IAdDispatcher dispatcher, AdFormat format,
                                AdRetryPolicy retryPolicy, int rewardGraceFrames, Func<bool> adsRemoved)
        {
            _adapter = adapter;
            _dispatcher = dispatcher;
            _format = format;
            _retryPolicy = retryPolicy;
            _rewardGraceFrames = Mathf.Max(0, rewardGraceFrames);
            _adsRemoved = adsRemoved ?? (() => false);
            _blockWhenAdsRemoved = format == AdFormat.Interstitial;

            _adapter.Loaded += OnLoaded;
            _adapter.LoadFailed += OnLoadFailed;
            _adapter.Displayed += OnDisplayed;
            _adapter.DisplayFailed += OnDisplayFailed;
            _adapter.Closed += OnClosed;
            _adapter.Rewarded += OnRewarded;
            _adapter.Paid += OnPaid;
        }

        public bool IsReady => !_isDisposed && _adapter.IsReady;

        public void Load()
        {
            if (_isDisposed) return;

            CancelScheduledRetry();
            _adapter.Load();
        }

        public Awaitable<AdShowResult> ShowAsync(string placement = null)
        {
            // Task 4에서 구현한다.
            var source = new AwaitableCompletionSource<AdShowResult>();
            source.SetResult(AdShowResult.NotReady());
            return source.Awaitable;
        }

        private void OnLoaded()
        {
            _retryAttempt = 0;
            Loaded?.Invoke();
        }

        private void OnLoadFailed(AdError error)
        {
            ScheduleRetry(error);
        }

        private void ScheduleRetry(AdError error)
        {
            _retryAttempt++;

            if (_retryAttempt > _retryPolicy.MaxAttempts)
            {
                Debug.LogError($"[AdService] {_format} 로드가 {_retryPolicy.MaxAttempts}회 재시도 후에도 실패했다: {error}");
                return;
            }

            var delay = _retryPolicy.DelayFor(_retryAttempt);
            CancelScheduledRetry();
            _scheduledRetry = _dispatcher.Delay(delay, () =>
            {
                _scheduledRetry = null;
                if (!_isDisposed) _adapter.Load();
            });
        }

        private void CancelScheduledRetry()
        {
            _scheduledRetry?.Dispose();
            _scheduledRetry = null;
        }

        private void OnDisplayed() => Displayed?.Invoke();
        private void OnDisplayFailed(AdError error) { }   // Task 4
        private void OnClosed() { }                        // Task 5
        private void OnRewarded(AdReward reward) { }       // Task 5
        private void OnPaid(AdImpression impression) => Paid?.Invoke(impression);

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            CancelScheduledRetry();

            _adapter.Loaded -= OnLoaded;
            _adapter.LoadFailed -= OnLoadFailed;
            _adapter.Displayed -= OnDisplayed;
            _adapter.DisplayFailed -= OnDisplayFailed;
            _adapter.Closed -= OnClosed;
            _adapter.Rewarded -= OnRewarded;
            _adapter.Paid -= OnPaid;

            _adapter.Dispose();
        }
    }
}
```

> `Load()`가 `CancelScheduledRetry()`를 먼저 부르는 이유: 예약된 재시도가 남아 있는데 외부에서 `Load()`를 부르면 잠시 후 중복 로드가 발생한다. 세 SDK 모두 중복 `Load` 호출을 경고 또는 에러로 처리한다.

- [ ] **Step 5: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="FullScreenAdUnitTest")`
Expected: PASS 1건

- [ ] **Step 6: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Ads/ Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs*
git commit -m "[BEHAVIORAL] 전면/보상 광고 로드 실패 시 지수 백오프 재시도 추가"
```

- [ ] **Step 7: 재시도 한도 테스트를 추가한다**

`FullScreenAdUnitTest.cs`를 `Write`로 다시 쓰되 Step 1의 내용 뒤에 추가:

```csharp
    [Test]
    public void 최대_재시도_횟수를_초과하면_더_이상_재시도하지_않는다()
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);   // maxAttempts = 3

        sut.Load();

        // 3번의 재시도를 모두 소진시킨다.
        for (var i = 0; i < 3; i++)
        {
            adapter.RaiseLoadFailed(new AdError(3, "no fill"));
            dispatcher.Advance(200f);
        }

        Assert.AreEqual(4, adapter.LoadCount, "최초 1회 + 재시도 3회여야 한다");

        // 4번째 실패 — 한도를 넘었으므로 재시도가 예약되면 안 된다.
        LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("재시도 후에도 실패"));
        adapter.RaiseLoadFailed(new AdError(3, "no fill"));

        Assert.AreEqual(0, dispatcher.PendingCount, "한도 초과 후에도 재시도가 예약됐다");

        dispatcher.Advance(200f);
        Assert.AreEqual(4, adapter.LoadCount, "한도 초과 후에도 재시도했다");
    }
```

`LogAssert.Expect`가 필요한 이유: 구현이 `Debug.LogError`를 호출하는데, Unity Test Framework는 테스트 중 발생한 에러 로그를 실패로 처리한다. 기대를 선언해두면 그 로그가 나와야만 통과한다 — 즉 로그 자체도 검증된다.

- [ ] **Step 8: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="FullScreenAdUnitTest")`
Expected: PASS 2건. Step 4의 구현이 이미 한도를 처리하므로 바로 통과한다.

- [ ] **Step 9: 카운터 리셋 테스트를 추가한다**

`FullScreenAdUnitTest.cs`에 추가:

```csharp
    [Test]
    public void 로드에_성공하면_재시도_카운터가_초기화된다()
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        sut.Load();
        adapter.RaiseLoadFailed(new AdError(3, "no fill"));
        dispatcher.Advance(2.1f);          // 재시도 1회 소진 (2^1)
        adapter.RaiseLoaded();             // 성공 → 카운터 리셋

        var loadCountBefore = adapter.LoadCount;
        adapter.RaiseLoadFailed(new AdError(3, "no fill"));

        // 리셋됐다면 다음 지연은 다시 2초여야 한다. 리셋 안 됐다면 4초다.
        dispatcher.Advance(2.1f);
        Assert.AreEqual(loadCountBefore + 1, adapter.LoadCount,
                        "카운터가 리셋되지 않아 지연이 2초가 아니었다");
    }
```

- [ ] **Step 10: 통과를 확인한다**

Run: `run_tests(mode="EditMode")` (전체)
Expected: `FullScreenAdUnitTest` 3건 PASS + 기존 테스트 전부 PASS

- [ ] **Step 11: 커밋**

```bash
git add Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs
git commit -m "[BEHAVIORAL] 재시도 한도와 카운터 리셋 동작 검증 추가"
```

---

### Task 4: FullScreenAdUnit — ShowAsync 진입 가드와 표시 실패

`ShowAsync`의 뼈대를 만든다. 보상 확정은 Task 5, 자동 재로드는 Task 6이다.

**테스트에서 `Awaitable`을 다루는 방법이 여기서 확정된다.** 결과가 즉시 나오는 경우(`NotReady`/`Blocked`)는 바로 `await` 해도 되지만, 이벤트를 거쳐 완료되는 경우는 **`ShowAsync()`의 반환값을 변수에 담아두고 → 어댑터 이벤트를 발화시킨 뒤 → `await`** 한다. `Awaitable`은 단일 사용이라 `await` 이후에는 상태를 조회할 수 없으므로 이 순서를 지켜야 한다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/AdService/Ads/FullScreenAdUnit.cs`
- Test: `Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs`

**Interfaces:**
- Consumes: Task 3의 `FullScreenAdUnit`
- Produces: `FullScreenAdUnit.ShowAsync(string placement)`의 실제 동작. 시그니처 변경 없음.

- [ ] **Step 1: 실패 테스트를 작성한다**

`FullScreenAdUnitTest.cs`에 추가 (파일을 `Write`로 다시 쓴다):

```csharp
    [UnityTest]
    public IEnumerator 준비되지_않은_상태의_ShowAsync는_NotReady를_반환하고_로드를_시작한다() =>
        UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter { IsReady = false };
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        var result = await sut.ShowAsync();

        Assert.AreEqual(AdShowOutcome.NotReady, result.Outcome);
        Assert.AreEqual(0, adapter.ShowCount, "준비도 안 됐는데 Show를 호출했다");
        Assert.AreEqual(1, adapter.LoadCount, "NotReady일 때 로드를 트리거하지 않았다");
    });
```

이 테스트는 Task 3의 임시 구현(항상 `NotReady` 반환) 때문에 `LoadCount` 단언에서 실패한다 — 그게 맞다.

- [ ] **Step 2: 실패를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="FullScreenAdUnitTest")`
Expected: FAIL — `NotReady일 때 로드를 트리거하지 않았다  Expected: 1  But was: 0`

- [ ] **Step 3: ShowAsync를 구현한다**

`FullScreenAdUnit.cs`에서 필드를 추가하고 `ShowAsync`/`OnDisplayFailed`를 교체한다.

필드 추가 (`_isDisposed` 아래):

```csharp
        private AwaitableCompletionSource<AdShowResult> _showCompletion;
        private string _activePlacement;
```

`ShowAsync` 교체:

```csharp
        public Awaitable<AdShowResult> ShowAsync(string placement = null)
        {
            if (_isDisposed) return Immediate(AdShowResult.Failed(new AdError(-1, "서비스가 이미 해제됐다")));

            // 순서가 중요하다. 광고제거는 로드조차 트리거하지 않아야 하므로 가장 먼저 본다.
            if (_blockWhenAdsRemoved && _adsRemoved()) return Immediate(AdShowResult.Blocked());

            if (_showCompletion != null) return Immediate(AdShowResult.Failed(new AdError(-2, "이미 표시 중이다")));

            if (!_adapter.IsReady)
            {
                Load();   // 다음 기회를 위해 미리 채워둔다
                return Immediate(AdShowResult.NotReady());
            }

            _activePlacement = placement;
            _showCompletion = new AwaitableCompletionSource<AdShowResult>();

            var awaitable = _showCompletion.Awaitable;
            _adapter.Show();
            return awaitable;
        }

        // Awaitable은 단일 사용이므로 호출자마다 새 completion source를 만든다.
        private static Awaitable<AdShowResult> Immediate(AdShowResult result)
        {
            var source = new AwaitableCompletionSource<AdShowResult>();
            source.SetResult(result);
            return source.Awaitable;
        }

        // 완료는 반드시 이 한 곳을 거친다. 이중 완료를 막고 상태를 함께 청소한다.
        private void Complete(AdShowResult result)
        {
            var completion = _showCompletion;
            if (completion == null) return;

            _showCompletion = null;
            _activePlacement = null;
            completion.SetResult(result);
        }
```

`OnDisplayFailed` 교체:

```csharp
        private void OnDisplayFailed(AdError error)
        {
            Debug.LogWarning($"[AdService] {_format} 표시 실패: {error}");
            Complete(AdShowResult.Failed(error));
        }
```

> `_adapter.Show()`를 **`_showCompletion.Awaitable`을 지역 변수에 담은 뒤에** 호출하는 이유: 일부 SDK는 `Show()` 호출 스택 안에서 곧바로 `DisplayFailed`를 발화시킨다. 그러면 `Complete`가 `_showCompletion`을 null로 만들어버려, 그 뒤에 `_showCompletion.Awaitable`을 읽으면 NullReferenceException이 난다.

- [ ] **Step 4: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="FullScreenAdUnitTest")`
Expected: PASS 4건

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Ads/FullScreenAdUnit.cs \
        Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs
git commit -m "[BEHAVIORAL] ShowAsync 진입 가드와 표시 실패 처리 추가"
```

- [ ] **Step 6: 중복 호출과 표시 실패 테스트를 추가한다**

`FullScreenAdUnitTest.cs`에 추가:

```csharp
    [UnityTest]
    public IEnumerator 표시에_실패하면_Failed와_에러를_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        adapter.RaiseLoaded();

        // Awaitable을 먼저 잡아두고 이벤트를 발화시킨 뒤 await 한다.
        var pending = sut.ShowAsync();
        LogAssert.Expect(UnityEngine.LogType.Warning,
                         new System.Text.RegularExpressions.Regex("표시 실패"));
        adapter.RaiseDisplayFailed(new AdError(7, "no ad to show"));

        var result = await pending;

        Assert.AreEqual(AdShowOutcome.Failed, result.Outcome);
        Assert.AreEqual(7, result.Error.Code);
    });

    [UnityTest]
    public IEnumerator 표시_중에_ShowAsync를_다시_호출하면_Failed를_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        adapter.RaiseLoaded();

        var first = sut.ShowAsync();
        var second = await sut.ShowAsync();   // 아직 첫 번째가 안 끝났다

        Assert.AreEqual(AdShowOutcome.Failed, second.Outcome);
        Assert.AreEqual(1, adapter.ShowCount, "중복 호출이 Show를 두 번 불렀다");

        // 첫 번째를 정리해서 테스트가 미완료 Awaitable을 남기지 않게 한다.
        LogAssert.Expect(UnityEngine.LogType.Warning,
                         new System.Text.RegularExpressions.Regex("표시 실패"));
        adapter.RaiseDisplayFailed(new AdError(0, "cleanup"));
        await first;
    });
```

- [ ] **Step 7: 통과를 확인한다**

Run: `run_tests(mode="EditMode")` (전체)
Expected: `FullScreenAdUnitTest` 6건 PASS + 기존 전부 PASS

- [ ] **Step 8: 커밋**

```bash
git add Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs
git commit -m "[BEHAVIORAL] ShowAsync 중복 호출과 표시 실패 동작 검증 추가"
```

---

### Task 5: FullScreenAdUnit — 보상 확정과 유예 프레임

**이 서비스에서 가장 중요한 Task다.** 세 SDK 모두 보상 이벤트와 닫힘 이벤트의 순서를 보장하지 않는다. 순진하게 닫힘에서 곧바로 확정하면, 닫힘이 먼저 오는 SDK/네트워크 조합에서 유저가 광고를 끝까지 봤는데도 보상을 못 받는다. 그 버그는 재현이 어렵고 리뷰로 잡히지 않는다.

규칙: **보상은 래치만 하고, 닫힘에서 유예 프레임을 기다린 뒤 확정한다.**

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/AdService/Ads/FullScreenAdUnit.cs`
- Test: `Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs`

**Interfaces:**
- Consumes: Task 4의 `Complete`, `_showCompletion`
- Produces: 시그니처 변경 없음. `OnRewarded`/`OnClosed`의 동작이 채워진다.

- [ ] **Step 1: 실패 테스트를 작성한다**

`FullScreenAdUnitTest.cs`에 추가:

```csharp
    [UnityTest]
    public IEnumerator 보상_이벤트_후_닫히면_Rewarded와_보상정보를_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Rewarded);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync("double_coins");

        adapter.RaiseDisplayed();
        adapter.RaiseRewarded(new AdReward("coins", 50));
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);           // 유예 프레임 소진

        var result = await pending;

        Assert.AreEqual(AdShowOutcome.Rewarded, result.Outcome);
        Assert.AreEqual("coins", result.Reward.Label);
        Assert.AreEqual(50, result.Reward.Amount, 0.001);
    });
```

- [ ] **Step 2: 실패를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="FullScreenAdUnitTest")`
Expected: FAIL — `[Timeout(5000)]`에 걸려 5초 후 타임아웃. Task 4의 `OnClosed`가 비어 있어 `pending`이 영원히 완료되지 않기 때문이다. **Timeout 속성이 없으면 여기서 Unity Editor가 멈춘다.**

- [ ] **Step 3: 보상 래치와 닫힘 확정을 구현한다**

`FullScreenAdUnit.cs`에 필드 추가:

```csharp
        private AdReward? _pendingReward;
        private IDisposable _scheduledClose;
```

`OnRewarded`/`OnClosed` 교체:

```csharp
        // 보상은 래치만 한다. 여기서 완료시키면, 보상 후 닫힘 사이에 유저가 앱을 떠나는
        // 경우와 닫힘이 먼저 오는 경우를 구분할 수 없게 된다.
        private void OnRewarded(AdReward reward)
        {
            _pendingReward = reward;
        }

        private void OnClosed()
        {
            // 닫힘이 보상보다 먼저 오는 SDK/네트워크가 있다. 유예 프레임을 두고 기다린다.
            _scheduledClose?.Dispose();
            _scheduledClose = _dispatcher.NextFrames(_rewardGraceFrames, () =>
            {
                _scheduledClose = null;
                FinalizeClose();
            });
        }

        private void FinalizeClose()
        {
            var reward = _pendingReward;
            _pendingReward = null;

            AdShowResult result;
            if (reward.HasValue) result = AdShowResult.Rewarded(reward.Value);
            else if (_format == AdFormat.Rewarded) result = AdShowResult.Dismissed();
            else result = AdShowResult.Shown();

            Complete(result);
            Closed?.Invoke();
        }
```

`Dispose()`에 정리 추가 (`CancelScheduledRetry();` 바로 아래):

```csharp
            _scheduledClose?.Dispose();
            _scheduledClose = null;
```

- [ ] **Step 4: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="FullScreenAdUnitTest")`
Expected: PASS 7건

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Ads/FullScreenAdUnit.cs \
        Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs
git commit -m "[BEHAVIORAL] 보상 래치 후 닫힘에서 확정하는 규칙 추가"
```

- [ ] **Step 6: 나머지 확정 경로 테스트를 추가한다**

`FullScreenAdUnitTest.cs`에 추가:

```csharp
    [UnityTest]
    public IEnumerator 보상없이_닫히면_Dismissed를_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Rewarded);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();

        adapter.RaiseDisplayed();
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);

        var result = await pending;

        Assert.AreEqual(AdShowOutcome.Dismissed, result.Outcome);
        Assert.IsTrue(result.WasShown, "노출은 됐으므로 WasShown이어야 한다");
    });

    [UnityTest]
    public IEnumerator 전면광고는_보상없이_닫히면_Shown을_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();

        adapter.RaiseDisplayed();
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);

        var result = await pending;

        Assert.AreEqual(AdShowOutcome.Shown, result.Outcome);
    });

    [UnityTest]
    public IEnumerator 닫힘이_보상보다_먼저_와도_유예_프레임_안에서_Rewarded로_확정된다() =>
        UniTask.ToCoroutine(async () =>
    {
        // 일부 미디에이션 네트워크가 실제로 이 순서로 이벤트를 보낸다.
        // 유예 프레임이 없으면 유저가 광고를 다 봤는데도 보상을 잃는다.
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Rewarded, rewardGraceFrames: 1);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();

        adapter.RaiseDisplayed();
        adapter.RaiseClosed();                              // 닫힘이 먼저
        adapter.RaiseRewarded(new AdReward("coins", 10));   // 보상이 나중
        dispatcher.TickFrames(1);

        var result = await pending;

        Assert.AreEqual(AdShowOutcome.Rewarded, result.Outcome);
        Assert.AreEqual(10, result.Reward.Amount, 0.001);
    });

    [UnityTest]
    public IEnumerator 유예_프레임이_0이면_닫힘_즉시_확정한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Rewarded, rewardGraceFrames: 0);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();

        adapter.RaiseRewarded(new AdReward("coins", 10));
        adapter.RaiseClosed();   // TickFrames 없이 바로 확정돼야 한다

        var result = await pending;

        Assert.AreEqual(AdShowOutcome.Rewarded, result.Outcome);
    });
```

- [ ] **Step 7: 통과를 확인한다**

Run: `run_tests(mode="EditMode")` (전체)
Expected: `FullScreenAdUnitTest` 11건 PASS + 기존 전부 PASS

`유예_프레임이_0이면` 테스트가 멈춘다면 `FakeAdDispatcher.NextFrames`의 `count <= 0` 즉시 실행 분기가 동작하지 않는 것이다 (Task 2 Step 4).

- [ ] **Step 8: 커밋**

```bash
git add Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs
git commit -m "[BEHAVIORAL] 보상/닫힘 이벤트 순서 역전과 유예 프레임 동작 검증 추가"
```

---

### Task 6: FullScreenAdUnit — 자동 재로드와 해제

세 SDK 공식 문서가 모두 "광고가 닫히면 즉시 다음 광고를 로드하라"고 권고한다. 로드에 수 초가 걸리므로, 닫힌 뒤에 로드를 시작하지 않으면 다음 표시 기회를 놓친다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/AdService/Ads/FullScreenAdUnit.cs`
- Test: `Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs`

**Interfaces:**
- Consumes: Task 5의 `FinalizeClose`, Task 4의 `OnDisplayFailed`
- Produces: 시그니처 변경 없음.

- [ ] **Step 1: 실패 테스트를 작성한다**

`FullScreenAdUnitTest.cs`에 추가:

```csharp
    [UnityTest]
    public IEnumerator 광고가_닫히면_다음_광고를_자동으로_로드한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial);

        adapter.RaiseLoaded();
        var loadCountBefore = adapter.LoadCount;

        var pending = sut.ShowAsync();
        adapter.RaiseDisplayed();
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);
        await pending;

        Assert.AreEqual(loadCountBefore + 1, adapter.LoadCount, "닫힘 후 자동 재로드가 없었다");
    });

    [UnityTest]
    public IEnumerator 표시에_실패하면_다음_광고를_자동으로_로드한다() => UniTask.ToCoroutine(async () =>
    {
        // 표시 실패는 대개 만료·소진된 광고가 원인이라 즉시 새로 받아와야 한다.
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial);

        adapter.RaiseLoaded();
        var loadCountBefore = adapter.LoadCount;

        var pending = sut.ShowAsync();
        LogAssert.Expect(UnityEngine.LogType.Warning,
                         new System.Text.RegularExpressions.Regex("표시 실패"));
        adapter.RaiseDisplayFailed(new AdError(7, "expired"));
        await pending;

        Assert.AreEqual(loadCountBefore + 1, adapter.LoadCount, "표시 실패 후 재로드가 없었다");
    });
```

- [ ] **Step 2: 실패를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="FullScreenAdUnitTest.광고가_닫히면_다음_광고를_자동으로_로드한다")`
Expected: FAIL — `Expected: 1  But was: 0`

- [ ] **Step 3: 자동 재로드를 구현한다**

`FullScreenAdUnit.cs`의 `FinalizeClose` 마지막 줄 뒤에 추가:

```csharp
            Complete(result);
            Closed?.Invoke();

            // 세 SDK 모두 "닫히면 즉시 다음 광고를 로드하라"고 권고한다.
            // 로드에 수 초가 걸리므로 여기서 시작하지 않으면 다음 기회를 놓친다.
            Load();
```

`OnDisplayFailed`도 교체:

```csharp
        private void OnDisplayFailed(AdError error)
        {
            Debug.LogWarning($"[AdService] {_format} 표시 실패: {error}");
            Complete(AdShowResult.Failed(error));

            // 표시 실패는 대개 만료되거나 소진된 광고가 원인이다. 새로 받아온다.
            Load();
        }
```

- [ ] **Step 4: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="FullScreenAdUnitTest")`
Expected: PASS 13건.

`표시에_실패하면_Failed와_에러를_반환한다`(Task 4)가 함께 통과하는지 확인한다 — `LoadCount`를 단언하지 않으므로 영향이 없어야 한다.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Ads/FullScreenAdUnit.cs \
        Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs
git commit -m "[BEHAVIORAL] 닫힘과 표시 실패 후 자동 재로드 추가"
```

- [ ] **Step 6: 해제 테스트를 추가한다**

`FullScreenAdUnitTest.cs`에 추가:

```csharp
    [Test]
    public void Dispose는_어댑터를_정리하고_예약된_재시도를_취소한다()
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        sut.Load();
        adapter.RaiseLoadFailed(new AdError(3, "no fill"));
        Assert.AreEqual(1, dispatcher.PendingCount, "재시도가 예약되지 않았다");

        sut.Dispose();

        Assert.IsTrue(adapter.IsDisposed, "어댑터가 해제되지 않았다");
        Assert.AreEqual(0, dispatcher.PendingCount, "예약된 재시도가 취소되지 않았다");

        var loadCountBefore = adapter.LoadCount;
        dispatcher.Advance(200f);
        Assert.AreEqual(loadCountBefore, adapter.LoadCount, "해제 후에도 재시도가 실행됐다");
    }

    [UnityTest]
    public IEnumerator 해제된_뒤의_ShowAsync는_Failed를_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher);

        adapter.RaiseLoaded();
        sut.Dispose();

        var result = await sut.ShowAsync();

        Assert.AreEqual(AdShowOutcome.Failed, result.Outcome);
        Assert.AreEqual(0, adapter.ShowCount);
    });
```

- [ ] **Step 7: 통과를 확인한다**

Run: `run_tests(mode="EditMode")` (전체)
Expected: `FullScreenAdUnitTest` 15건 PASS + 기존 전부 PASS

- [ ] **Step 8: 커밋**

```bash
git add Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs
git commit -m "[BEHAVIORAL] 해제 시 재시도 취소와 어댑터 정리 동작 검증 추가"
```

---

### Task 7: BannerAdUnit

배너는 전면/보상과 다르다. **재시도도 자동 재로드도 하지 않는다** — 세 SDK 모두 배너 갱신을 SDK가 자동 처리하고, MAX 문서는 명시적으로 배너를 화면에 유지하라고 권고한다. 여기서 할 일은 높이 중계와 `Destroy()` 이후 재부착이다.

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Ads/IBannerAd.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Ads/BannerAdUnit.cs`
- Test: `Assets/FoundationDI/Tests/BannerAdUnitTest.cs`

**Interfaces:**
- Consumes: Task 2의 `IBannerAdapter`/`FakeBannerAdapter`, Task 1의 `AdImpression`
- Produces:
  - `interface IBannerAd { bool IsVisible { get; } float Height { get; } void Show(); void Hide(); void Destroy(); event Action<float> HeightChanged; }`
  - `class BannerAdUnit : IBannerAd, IDisposable`
    - 생성자 `BannerAdUnit(Func<IBannerAdapter> adapterFactory, Func<bool> adsRemoved)`
    - 이벤트 `event Action<AdImpression> Paid;`
    - 메서드 `void OnAdsRemovedChanged(bool removed)`

> **어댑터가 아니라 팩토리를 받는 이유:** `Destroy()`는 영구 종료가 아니라 리소스 해제이고, 이후 `Show()`는 어댑터를 새로 만들어 다시 붙여야 한다. 어댑터 인스턴스를 직접 받으면 한 번 파괴한 뒤 되살릴 방법이 없다.

- [ ] **Step 1: 실패 테스트를 작성한다**

`Assets/FoundationDI/Tests/BannerAdUnitTest.cs` 를 `Write`로 생성:

```csharp
using System;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class BannerAdUnitTest
{
    // 생성된 어댑터를 전부 기록해서 Destroy 후 재생성을 검증할 수 있게 한다.
    private class AdapterFactory
    {
        public readonly List<FakeBannerAdapter> Created = new();
        public FakeBannerAdapter Last => Created.Count > 0 ? Created[^1] : null;

        public IBannerAdapter Create()
        {
            var adapter = new FakeBannerAdapter();
            Created.Add(adapter);
            return adapter;
        }
    }

    [Test]
    public void 배너를_표시하면_어댑터를_만들어_Show를_호출하고_높이를_보고한다()
    {
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => false);

        sut.Show();

        Assert.AreEqual(1, factory.Created.Count, "어댑터가 생성되지 않았다");
        Assert.AreEqual(1, factory.Last.ShowCount);
        Assert.IsTrue(sut.IsVisible);

        var reported = -1f;
        sut.HeightChanged += h => reported = h;
        factory.Last.SetHeight(120f);

        Assert.AreEqual(120f, sut.Height, 0.001f);
        Assert.AreEqual(120f, reported, 0.001f, "HeightChanged가 중계되지 않았다");
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

`read_console`: `BannerAdUnit` 미정의로 컴파일 실패.

- [ ] **Step 3: 계약과 구현을 작성한다**

`Ads/IBannerAd.cs`:

```csharp
using System;

namespace DarkNaku.FoundationDI
{
    public interface IBannerAd
    {
        bool IsVisible { get; }

        // 화면 픽셀 단위 높이. 미표시/미로드면 0. UI 레이아웃이 배너를 피하는 데 쓴다.
        float Height { get; }

        void Show();
        void Hide();

        // 영구 종료가 아니라 리소스 해제다. 이후 Show()는 어댑터를 새로 만들어 다시 붙인다.
        void Destroy();

        event Action<float> HeightChanged;
    }
}
```

`Ads/BannerAdUnit.cs`:

```csharp
using System;

namespace DarkNaku.FoundationDI
{
    // 배너 정책 계층. 전면/보상과 달리 재시도·자동 재로드를 하지 않는다 —
    // 세 SDK 모두 배너 갱신을 SDK가 처리하고, 배너는 화면에 계속 두는 것이 권장된다.
    public class BannerAdUnit : IBannerAd, IDisposable
    {
        private readonly Func<IBannerAdapter> _adapterFactory;
        private readonly Func<bool> _adsRemoved;

        private IBannerAdapter _adapter;
        private bool _wantsVisible;
        private bool _isDisposed;

        public event Action<float> HeightChanged;
        public event Action<AdImpression> Paid;

        public BannerAdUnit(Func<IBannerAdapter> adapterFactory, Func<bool> adsRemoved)
        {
            _adapterFactory = adapterFactory;
            _adsRemoved = adsRemoved ?? (() => false);
        }

        // 광고가 제거됐으면 보이지 않는 것으로 취급한다 — 호출자가 두 조건을 따로 볼 필요가 없다.
        public bool IsVisible => !_isDisposed && _wantsVisible && !_adsRemoved();

        public float Height => IsVisible && _adapter != null ? _adapter.Height : 0f;

        public void Show()
        {
            if (_isDisposed) return;

            _wantsVisible = true;

            // 광고제거 상태에서는 어댑터를 만들지도 않는다. SDK가 배너를 요청하면
            // 임프레션이 발생하고 수익 리포트가 오염된다.
            if (_adsRemoved()) return;

            EnsureAdapter();
            _adapter.Show();
        }

        public void Hide()
        {
            _wantsVisible = false;
            _adapter?.Hide();
            HeightChanged?.Invoke(0f);
        }

        public void Destroy()
        {
            _wantsVisible = false;
            DetachAdapter();
            HeightChanged?.Invoke(0f);
        }

        // AdService가 AdsRemoved 변경 시 호출한다.
        public void OnAdsRemovedChanged(bool removed)
        {
            if (removed) { DetachAdapter(); HeightChanged?.Invoke(0f); }
            else if (_wantsVisible) Show();
        }

        private void EnsureAdapter()
        {
            if (_adapter != null) return;

            _adapter = _adapterFactory();
            _adapter.HeightChanged += OnAdapterHeightChanged;
            _adapter.Paid += OnAdapterPaid;
        }

        private void DetachAdapter()
        {
            if (_adapter == null) return;

            _adapter.HeightChanged -= OnAdapterHeightChanged;
            _adapter.Paid -= OnAdapterPaid;
            _adapter.Dispose();
            _adapter = null;
        }

        private void OnAdapterHeightChanged(float height) => HeightChanged?.Invoke(IsVisible ? height : 0f);
        private void OnAdapterPaid(AdImpression impression) => Paid?.Invoke(impression);

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _wantsVisible = false;
            DetachAdapter();
        }
    }
}
```

- [ ] **Step 4: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="BannerAdUnitTest")`
Expected: PASS 1건

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Ads/IBannerAd.cs \
        Assets/FoundationDI/Runtime/Services/AdService/Ads/BannerAdUnit.cs \
        Assets/FoundationDI/Tests/BannerAdUnitTest.cs
git commit -m "[BEHAVIORAL] 배너 정책 계층 추가"
```

- [ ] **Step 6: 숨김/파괴/재부착 테스트를 추가한다**

`BannerAdUnitTest.cs`에 추가:

```csharp
    [Test]
    public void 배너를_숨기면_어댑터를_유지한_채_높이를_0으로_보고한다()
    {
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => false);

        sut.Show();
        factory.Last.SetHeight(120f);

        var reported = -1f;
        sut.HeightChanged += h => reported = h;
        sut.Hide();

        Assert.IsFalse(sut.IsVisible);
        Assert.AreEqual(0f, sut.Height, 0.001f);
        Assert.AreEqual(0f, reported, 0.001f);
        Assert.AreEqual(1, factory.Last.HideCount);
        Assert.IsFalse(factory.Last.IsDisposed, "Hide가 어댑터를 파괴했다");
        Assert.AreEqual(1, factory.Created.Count);
    }

    [Test]
    public void 배너를_파괴하면_어댑터를_해제하고_다음_표시에서_새로_만든다()
    {
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => false);

        sut.Show();
        var first = factory.Last;

        sut.Destroy();

        Assert.IsTrue(first.IsDisposed, "어댑터가 해제되지 않았다");
        Assert.AreEqual(0f, sut.Height, 0.001f);

        sut.Show();

        Assert.AreEqual(2, factory.Created.Count, "파괴 후 어댑터를 새로 만들지 않았다");
        Assert.AreNotSame(first, factory.Last);
        Assert.AreEqual(1, factory.Last.ShowCount);
        Assert.IsTrue(sut.IsVisible);
    }

    [Test]
    public void 배너_임프레션은_그대로_중계된다()
    {
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => false);
        AdImpression? received = null;
        sut.Paid += imp => received = imp;

        sut.Show();
        factory.Last.RaisePaid(new AdImpression(AdFormat.Banner, "Dummy", "TestNetwork", "banner-unit",
                                                "inst", null, 0.004, "USD",
                                                AdRevenuePrecision.Estimated, "creative-1"));

        Assert.IsTrue(received.HasValue, "임프레션이 중계되지 않았다");
        Assert.AreEqual("TestNetwork", received.Value.NetworkName);
        Assert.AreEqual(0.004, received.Value.Revenue, 0.0001);
    }
```

- [ ] **Step 7: 통과를 확인한다**

Run: `run_tests(mode="EditMode")` (전체)
Expected: `BannerAdUnitTest` 4건 PASS + 기존 전부 PASS

- [ ] **Step 8: 커밋**

```bash
git add Assets/FoundationDI/Tests/BannerAdUnitTest.cs
git commit -m "[BEHAVIORAL] 배너 숨김/파괴/재부착과 임프레션 중계 검증 추가"
```

---

### Task 8: 광고제거(AdsRemoved) 상태와 게이트

구매로 광고를 제거한 유저에게 전면광고와 배너는 나오면 안 되지만, **보상형 광고는 계속 나와야 한다** — 유저가 자발적으로 보상을 얻으려고 보는 것이고, 이게 광고제거 상품의 표준 동작이다.

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Storage/IAdRemovalStorage.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Storage/PlayerPrefsAdRemovalStorage.cs`
- Test: `Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs`, `Assets/FoundationDI/Tests/BannerAdUnitTest.cs`, `Assets/FoundationDI/Tests/AdRemovalStorageTest.cs`

**Interfaces:**
- Consumes: Task 3~7의 `FullScreenAdUnit`/`BannerAdUnit` (이미 `Func<bool> adsRemoved`를 받는다 — 구현 변경 없음)
- Produces:
  - `interface IAdRemovalStorage { bool Load(); void Save(bool removed); }`
  - `class PlayerPrefsAdRemovalStorage : IAdRemovalStorage` — 키 `"FOUNDATIONDI_ADS_REMOVED"`

- [ ] **Step 1: 전면 차단 테스트를 작성한다**

`FullScreenAdUnitTest.cs`에 추가:

```csharp
    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 광고제거_상태에서_전면광고는_Blocked를_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Interstitial, adsRemoved: () => true);

        adapter.RaiseLoaded();
        var result = await sut.ShowAsync();

        Assert.AreEqual(AdShowOutcome.Blocked, result.Outcome);
        Assert.AreEqual(0, adapter.ShowCount, "차단됐는데 Show가 호출됐다");
        Assert.IsFalse(result.WasShown);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 광고제거_상태에서도_보상형_광고는_정상_표시된다() => UniTask.ToCoroutine(async () =>
    {
        // 보상형은 유저가 자발적으로 보는 것이라 광고제거 대상이 아니다.
        var adapter = new FakeFullScreenAdapter();
        var dispatcher = new FakeAdDispatcher();
        var sut = NewUnit(adapter, dispatcher, AdFormat.Rewarded, adsRemoved: () => true);

        adapter.RaiseLoaded();
        var pending = sut.ShowAsync();

        Assert.AreEqual(1, adapter.ShowCount, "보상형이 차단됐다");

        adapter.RaiseRewarded(new AdReward("coins", 5));
        adapter.RaiseClosed();
        dispatcher.TickFrames(1);

        var result = await pending;
        Assert.AreEqual(AdShowOutcome.Rewarded, result.Outcome);
    });
```

- [ ] **Step 2: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="FullScreenAdUnitTest")`
Expected: PASS 17건.

Task 3의 `_blockWhenAdsRemoved = format == AdFormat.Interstitial`과 Task 4의 진입 가드가 이미 이 동작을 구현했으므로 **바로 통과한다.** 이 두 테스트는 그 유도 규칙이 나중에 깨지지 않게 잠그는 것이 목적이다. 실패한다면 Task 3/4 구현이 잘못된 것이다.

- [ ] **Step 3: 배너 차단 테스트를 작성한다**

`BannerAdUnitTest.cs`에 추가:

```csharp
    [Test]
    public void 광고제거_상태에서는_배너를_표시하지_않고_어댑터도_만들지_않는다()
    {
        // 어댑터를 만들면 SDK가 배너를 요청하고 임프레션이 발생해 수익 리포트가 오염된다.
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => true);

        sut.Show();

        Assert.AreEqual(0, factory.Created.Count, "광고제거 상태인데 어댑터를 만들었다");
        Assert.IsFalse(sut.IsVisible);
        Assert.AreEqual(0f, sut.Height, 0.001f);
    }

    [Test]
    public void 광고제거가_켜지면_표시중인_배너를_해제하고_높이를_0으로_알린다()
    {
        var adsRemoved = false;
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => adsRemoved);

        sut.Show();
        factory.Last.SetHeight(120f);
        var first = factory.Last;

        var reported = -1f;
        sut.HeightChanged += h => reported = h;

        adsRemoved = true;
        sut.OnAdsRemovedChanged(true);

        Assert.IsTrue(first.IsDisposed, "배너 어댑터가 해제되지 않았다");
        Assert.IsFalse(sut.IsVisible);
        Assert.AreEqual(0f, sut.Height, 0.001f);
        Assert.AreEqual(0f, reported, 0.001f);
    }

    [Test]
    public void 광고제거가_해제되면_원래_표시중이던_배너를_다시_띄운다()
    {
        var adsRemoved = false;
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => adsRemoved);

        sut.Show();
        adsRemoved = true;
        sut.OnAdsRemovedChanged(true);

        adsRemoved = false;
        sut.OnAdsRemovedChanged(false);

        Assert.AreEqual(2, factory.Created.Count, "배너가 복구되지 않았다");
        Assert.IsTrue(sut.IsVisible);
        Assert.AreEqual(1, factory.Last.ShowCount);
    }

    [Test]
    public void 숨긴_상태에서_광고제거가_해제돼도_배너를_띄우지_않는다()
    {
        var adsRemoved = false;
        var factory = new AdapterFactory();
        var sut = new BannerAdUnit(factory.Create, () => adsRemoved);

        sut.Show();
        sut.Hide();                       // 게임이 명시적으로 숨겼다
        adsRemoved = true;
        sut.OnAdsRemovedChanged(true);

        adsRemoved = false;
        sut.OnAdsRemovedChanged(false);

        Assert.IsFalse(sut.IsVisible, "게임이 숨긴 배너가 멋대로 복구됐다");
    }
```

- [ ] **Step 4: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="BannerAdUnitTest")`
Expected: PASS 8건. Task 7의 `_wantsVisible` 분리와 `OnAdsRemovedChanged`가 이미 이 네 가지를 구현한다.

`숨긴_상태에서_광고제거가_해제돼도` 가 실패한다면 `OnAdsRemovedChanged(false)`가 `_wantsVisible`을 확인하지 않고 무조건 `Show()`를 부르는 것이다.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Tests/FullScreenAdUnitTest.cs Assets/FoundationDI/Tests/BannerAdUnitTest.cs
git commit -m "[BEHAVIORAL] 광고제거 상태의 포맷별 게이트 동작 검증 추가"
```

- [ ] **Step 6: 영속화 실패 테스트를 작성한다**

`Assets/FoundationDI/Tests/AdRemovalStorageTest.cs` 를 `Write`로 생성:

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class AdRemovalStorageTest
{
    private const string Key = "FOUNDATIONDI_ADS_REMOVED";

    // 실제 PlayerPrefs를 건드리므로 앞뒤로 반드시 청소한다.
    // 청소하지 않으면 개발자의 에디터 설정에 광고제거 플래그가 남는다.
    [SetUp]
    public void SetUp() => PlayerPrefs.DeleteKey(Key);

    [TearDown]
    public void TearDown() => PlayerPrefs.DeleteKey(Key);

    [Test]
    public void 저장된_값이_없으면_광고제거는_거짓이다()
    {
        var sut = new PlayerPrefsAdRemovalStorage();

        Assert.IsFalse(sut.Load());
    }

    [Test]
    public void 저장한_광고제거_상태가_새_인스턴스에서_복원된다()
    {
        new PlayerPrefsAdRemovalStorage().Save(true);

        Assert.IsTrue(new PlayerPrefsAdRemovalStorage().Load());

        new PlayerPrefsAdRemovalStorage().Save(false);

        Assert.IsFalse(new PlayerPrefsAdRemovalStorage().Load());
    }
}
```

- [ ] **Step 7: 실패를 확인한다**

`read_console`: `PlayerPrefsAdRemovalStorage` 미정의로 컴파일 실패.

- [ ] **Step 8: 저장소를 구현한다**

`Storage/IAdRemovalStorage.cs`:

```csharp
namespace DarkNaku.FoundationDI
{
    // 광고제거(인앱 구매) 상태의 영속화 seam. SoundService의 ISoundVolumeStorage와 같은 패턴이다.
    // 서버 권위 저장소를 쓰는 프로젝트는 이 인터페이스를 갈아끼우면 된다.
    public interface IAdRemovalStorage
    {
        bool Load();
        void Save(bool removed);
    }
}
```

`Storage/PlayerPrefsAdRemovalStorage.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public class PlayerPrefsAdRemovalStorage : IAdRemovalStorage
    {
        private const string KEY = "FOUNDATIONDI_ADS_REMOVED";

        public bool Load() => PlayerPrefs.GetInt(KEY, 0) != 0;

        public void Save(bool removed)
        {
            PlayerPrefs.SetInt(KEY, removed ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
```

- [ ] **Step 9: 통과를 확인한다**

Run: `run_tests(mode="EditMode")` (전체)
Expected: `AdRemovalStorageTest` 2건 PASS + 기존 전부 PASS

- [ ] **Step 10: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Storage/ Assets/FoundationDI/Tests/AdRemovalStorageTest.cs*
git commit -m "[BEHAVIORAL] 광고제거 상태 영속화 저장소 추가"
```

---

### Task 9: AdService — 조립과 이벤트 합류

지금까지 만든 조각을 하나로 묶는다. `AdService`가 하는 일은 네 가지뿐이다: provider 초기화, 포맷 핸들 조립, **어댑터 이벤트와 provider 전역 이벤트를 하나의 `Paid`로 합류**, `AdsRemoved` 소유.

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Providers/IAdProvider.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Consent/IAdConsent.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Consent/NoopAdConsent.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/IAdService.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/AdService.cs`
- Modify: `Assets/FoundationDI/Tests/AdTestDoubles.cs` (FakeAdProvider 추가)
- Test: `Assets/FoundationDI/Tests/AdServiceTest.cs`

**Interfaces:**
- Consumes: Task 1~8 전부
- Produces:
  - `readonly struct AdProviderContext` — 생성자 `(string appKey, bool verboseLogging, bool testMode, IReadOnlyList<string> testDeviceIds)`
  - `enum BannerPosition { Bottom, Top }`, `enum BannerSize { Standard, Large, MediumRectangle, Leaderboard, Adaptive }`
  - `readonly struct BannerOptions` — 생성자 `(BannerPosition position, BannerSize size, bool useAdaptive)`
  - `interface IAdProvider : IDisposable` — 위 spec 그대로
  - `interface IAdConsent`, `class NoopAdConsent : IAdConsent`
  - `interface IAdService : IDisposable` — 위 spec 그대로
  - `class AdService : IAdService` — 생성자 `AdService(IAdProvider provider, IAdDispatcher dispatcher, AdServiceOptions options, IAdRemovalStorage removalStorage)`
  - `readonly struct AdServiceOptions` — 생성자 `(AdUnitId banner, AdUnitId interstitial, AdUnitId rewarded, BannerOptions bannerOptions, AdProviderContext providerContext, AdRetryPolicy retryPolicy, int rewardGraceFrames, bool autoLoadOnInitialize)`
  - `class FakeAdProvider : IAdProvider` (테스트 하네스) — `FakeFullScreenAdapter InterstitialAdapter`, `RewardedAdapter`, `FakeBannerAdapter BannerAdapter`, `bool InitializeResult { get; set; }`, `void RaiseImpressionPaid(AdImpression)`, `bool IsDisposed`

> **`AdServiceOptions`를 따로 두는 이유:** `AdService`가 `AdServiceSettings`(ScriptableObject)를 직접 받으면 EditMode 테스트마다 SO를 만들어야 하고, 서비스가 에셋 형식에 묶인다. Task 10에서 `AdServiceSettings` → `AdServiceOptions` 변환을 담당한다.

- [ ] **Step 1: FakeAdProvider를 하네스에 추가한다**

`Assets/FoundationDI/Tests/AdTestDoubles.cs` 를 `Write`로 다시 쓰되, 기존 세 클래스 뒤에 추가:

```csharp
public class FakeAdProvider : IAdProvider
{
    public string Name => "Fake";
    public bool InitializeResult { get; set; } = true;
    public bool IsDisposed { get; private set; }
    public AdProviderContext ReceivedContext { get; private set; }

    public readonly FakeFullScreenAdapter InterstitialAdapter = new();
    public readonly FakeFullScreenAdapter RewardedAdapter = new();
    public readonly FakeBannerAdapter BannerAdapter = new();

    public IAdConsent Consent { get; } = new NoopAdConsent();

    public event Action<AdImpression> ImpressionPaid;

    public Awaitable<bool> InitializeAsync(AdProviderContext context)
    {
        ReceivedContext = context;
        var source = new AwaitableCompletionSource<bool>();
        source.SetResult(InitializeResult);
        return source.Awaitable;
    }

    public IFullScreenAdapter CreateInterstitial(string adUnitId) => InterstitialAdapter;
    public IFullScreenAdapter CreateRewarded(string adUnitId) => RewardedAdapter;
    public IBannerAdapter CreateBanner(string adUnitId, BannerOptions options) => BannerAdapter;

    public void RaiseImpressionPaid(AdImpression impression) => ImpressionPaid?.Invoke(impression);

    public void Dispose() => IsDisposed = true;
}
```

파일 상단 `using`에 `using UnityEngine;` 를 추가한다 (`Awaitable`/`AwaitableCompletionSource` 때문).

- [ ] **Step 2: 실패 테스트를 작성한다**

`Assets/FoundationDI/Tests/AdServiceTest.cs` 를 `Write`로 생성:

```csharp
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class AdServiceTest
{
    private class FakeRemovalStorage : IAdRemovalStorage
    {
        public bool Value;
        public int SaveCount;
        public bool Load() => Value;
        public void Save(bool removed) { Value = removed; SaveCount++; }
    }

    private static AdServiceOptions NewOptions(bool autoLoad = true)
    {
        return new AdServiceOptions(
            banner: new AdUnitId("banner-a", "banner-i"),
            interstitial: new AdUnitId("inter-a", "inter-i"),
            rewarded: new AdUnitId("reward-a", "reward-i"),
            bannerOptions: new BannerOptions(BannerPosition.Bottom, BannerSize.Adaptive, true),
            providerContext: new AdProviderContext("app-key", false, false, new List<string>()),
            retryPolicy: new AdRetryPolicy(3, 2f, 64f),
            rewardGraceFrames: 1,
            autoLoadOnInitialize: autoLoad);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화에_성공하면_IsInitialized가_참이_되고_전면과_보상을_로드한다() =>
        UniTask.ToCoroutine(async () =>
    {
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());

        Assert.IsFalse(sut.IsInitialized, "초기화 전인데 IsInitialized가 참이다");

        var ok = await sut.InitializeAsync();

        Assert.IsTrue(ok);
        Assert.IsTrue(sut.IsInitialized);
        Assert.AreEqual("app-key", provider.ReceivedContext.AppKey);
        Assert.AreEqual(1, provider.InterstitialAdapter.LoadCount, "전면을 미리 로드하지 않았다");
        Assert.AreEqual(1, provider.RewardedAdapter.LoadCount, "보상을 미리 로드하지 않았다");
    });
}
```

- [ ] **Step 3: 실패를 확인한다**

`read_console`: `AdService`/`AdServiceOptions`/`IAdProvider` 등 미정의로 컴파일 실패.

- [ ] **Step 4: provider seam과 동의 seam을 작성한다**

`Providers/IAdProvider.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public enum BannerPosition { Bottom, Top }
    public enum BannerSize { Standard, Large, MediumRectangle, Leaderboard, Adaptive }

    public readonly struct BannerOptions
    {
        public BannerPosition Position { get; }
        public BannerSize Size { get; }
        public bool UseAdaptive { get; }

        public BannerOptions(BannerPosition position, BannerSize size, bool useAdaptive)
        {
            Position = position;
            Size = size;
            UseAdaptive = useAdaptive;
        }
    }

    // provider가 초기화에 필요로 하는 것만 담는다. AdServiceSettings 전체를 넘기지 않는 이유는
    // 재시도·유예 프레임 같은 정책값이 상위 계층 소관이라 provider가 볼 이유가 없기 때문이다.
    public readonly struct AdProviderContext
    {
        public string AppKey { get; }       // LevelPlay appKey / MAX sdkKey. AdMob은 불필요(null)
        public bool VerboseLogging { get; }
        public bool TestMode { get; }
        public IReadOnlyList<string> TestDeviceIds { get; }

        public AdProviderContext(string appKey, bool verboseLogging, bool testMode,
                                 IReadOnlyList<string> testDeviceIds)
        {
            AppKey = appKey;
            VerboseLogging = verboseLogging;
            TestMode = testMode;
            TestDeviceIds = testDeviceIds;
        }
    }

    public interface IAdProvider : IDisposable
    {
        string Name { get; }
        Awaitable<bool> InitializeAsync(AdProviderContext context);
        IAdConsent Consent { get; }

        IFullScreenAdapter CreateInterstitial(string adUnitId);
        IFullScreenAdapter CreateRewarded(string adUnitId);
        IBannerAdapter CreateBanner(string adUnitId, BannerOptions options);

        // 전역/미매칭 임프레션 경로. LevelPlay는 임프레션 데이터가 광고 객체가 아니라
        // SDK 전역 이벤트 하나로 오기 때문에 어댑터별 Paid만으로는 배너 갱신 수익이 누락된다.
        event Action<AdImpression> ImpressionPaid;
    }
}
```

`Consent/IAdConsent.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // AdMob은 UMP, LevelPlay는 SetConsent, MAX는 T&P Flow로 각각 매핑된다.
    public interface IAdConsent
    {
        bool CanRequestAds { get; }
        bool IsPrivacyOptionsRequired { get; }

        // 필요하면 동의 폼을 띄운다. 완료 시 CanRequestAds가 갱신된다.
        Awaitable<bool> RequestAsync();

        Awaitable ShowPrivacyOptionsAsync();
    }
}
```

`Consent/NoopAdConsent.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 동의 개념이 없는 provider(Dummy 등)의 기본 구현. 항상 요청 가능으로 답한다.
    public class NoopAdConsent : IAdConsent
    {
        public bool CanRequestAds => true;
        public bool IsPrivacyOptionsRequired => false;

        public Awaitable<bool> RequestAsync()
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(true);
            return source.Awaitable;
        }

        public Awaitable ShowPrivacyOptionsAsync()
        {
            var source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }
    }
}
```

- [ ] **Step 5: 공개 계약과 서비스를 작성한다**

`IAdService.cs`:

```csharp
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface IAdService : IDisposable
    {
        bool IsInitialized { get; }
        Awaitable<bool> InitializeAsync();

        IInterstitialAd Interstitial { get; }
        IRewardedAd Rewarded { get; }
        IBannerAd Banner { get; }
        IAdConsent Consent { get; }

        // 인앱 구매로 광고를 제거한 상태. 전면·배너는 차단되고 보상형은 계속 동작한다.
        bool AdsRemoved { get; set; }

        event Action<AdFormat> Loaded;
        event Action<AdFormat> Displayed;
        event Action<AdFormat> Closed;
        event Action<AdImpression> Paid;
        event Action<bool> AdsRemovedChanged;
    }
}
```

`AdService.cs`:

```csharp
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // AdServiceSettings(ScriptableObject)가 아니라 이 구조체를 받는다.
    // 서비스가 에셋 형식에 묶이지 않고, EditMode 테스트가 SO를 만들 필요도 없다.
    public readonly struct AdServiceOptions
    {
        public AdUnitId Banner { get; }
        public AdUnitId Interstitial { get; }
        public AdUnitId Rewarded { get; }
        public BannerOptions BannerOptions { get; }
        public AdProviderContext ProviderContext { get; }
        public AdRetryPolicy RetryPolicy { get; }
        public int RewardGraceFrames { get; }
        public bool AutoLoadOnInitialize { get; }

        public AdServiceOptions(AdUnitId banner, AdUnitId interstitial, AdUnitId rewarded,
                                BannerOptions bannerOptions, AdProviderContext providerContext,
                                AdRetryPolicy retryPolicy, int rewardGraceFrames, bool autoLoadOnInitialize)
        {
            Banner = banner;
            Interstitial = interstitial;
            Rewarded = rewarded;
            BannerOptions = bannerOptions;
            ProviderContext = providerContext;
            RetryPolicy = retryPolicy;
            RewardGraceFrames = rewardGraceFrames;
            AutoLoadOnInitialize = autoLoadOnInitialize;
        }
    }

    public class AdService : IAdService
    {
        private readonly IAdProvider _provider;
        private readonly IAdDispatcher _dispatcher;
        private readonly AdServiceOptions _options;
        private readonly IAdRemovalStorage _removalStorage;

        private FullScreenAdUnit _interstitial;
        private FullScreenAdUnit _rewarded;
        private BannerAdUnit _banner;

        private bool _adsRemoved;
        private bool _isDisposed;

        public event Action<AdFormat> Loaded;
        public event Action<AdFormat> Displayed;
        public event Action<AdFormat> Closed;
        public event Action<AdImpression> Paid;
        public event Action<bool> AdsRemovedChanged;

        public AdService(IAdProvider provider, IAdDispatcher dispatcher,
                         AdServiceOptions options, IAdRemovalStorage removalStorage)
        {
            _provider = provider;
            _dispatcher = dispatcher;
            _options = options;
            _removalStorage = removalStorage;
            _adsRemoved = removalStorage?.Load() ?? false;
        }

        public bool IsInitialized { get; private set; }

        public IAdConsent Consent => _provider.Consent;

        // InitializeAsync가 성공하기 전에는 null이다. provider가 초기화되기 전에는
        // 어댑터를 만들 수 없기 때문이다. 게임 코드는 초기화를 먼저 await 해야 한다.
        public IInterstitialAd Interstitial => _interstitial;
        public IRewardedAd Rewarded => _rewarded;
        public IBannerAd Banner => _banner;

        public async Awaitable<bool> InitializeAsync()
        {
            if (IsInitialized) return true;

            var ok = await _provider.InitializeAsync(_options.ProviderContext);
            if (!ok)
            {
                Debug.LogError($"[AdService] {_provider.Name} 초기화에 실패했다. 광고를 요청하지 않는다.");
                return false;
            }

            BuildAdUnits();
            IsInitialized = true;

            if (_options.AutoLoadOnInitialize)
            {
                _interstitial.Load();
                _rewarded.Load();
            }

            return true;
        }

        private void BuildAdUnits()
        {
            _interstitial = new FullScreenAdUnit(
                _provider.CreateInterstitial(_options.Interstitial.Current), _dispatcher,
                AdFormat.Interstitial, _options.RetryPolicy, _options.RewardGraceFrames, () => _adsRemoved);

            _rewarded = new FullScreenAdUnit(
                _provider.CreateRewarded(_options.Rewarded.Current), _dispatcher,
                AdFormat.Rewarded, _options.RetryPolicy, _options.RewardGraceFrames, () => _adsRemoved);

            _banner = new BannerAdUnit(
                () => _provider.CreateBanner(_options.Banner.Current, _options.BannerOptions),
                () => _adsRemoved);

            Wire(_interstitial, AdFormat.Interstitial);
            Wire(_rewarded, AdFormat.Rewarded);

            _banner.Paid += OnPaid;

            // 어댑터별 Paid와 provider 전역 ImpressionPaid를 하나의 공개 이벤트로 합류시킨다.
            _provider.ImpressionPaid += OnPaid;
        }

        private void Wire(FullScreenAdUnit unit, AdFormat format)
        {
            unit.Loaded += () => Loaded?.Invoke(format);
            unit.Displayed += () => Displayed?.Invoke(format);
            unit.Closed += () => Closed?.Invoke(format);
            unit.Paid += OnPaid;
        }

        private void OnPaid(AdImpression impression) => Paid?.Invoke(impression);

        public bool AdsRemoved
        {
            get => _adsRemoved;
            set
            {
                if (_adsRemoved == value) return;

                _adsRemoved = value;
                _removalStorage?.Save(value);
                _banner?.OnAdsRemovedChanged(value);
                AdsRemovedChanged?.Invoke(value);
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_provider != null) _provider.ImpressionPaid -= OnPaid;

            _interstitial?.Dispose();
            _rewarded?.Dispose();
            _banner?.Dispose();
            _provider?.Dispose();

            IsInitialized = false;
        }
    }
}
```

- [ ] **Step 6: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="AdServiceTest")`
Expected: PASS 1건

- [ ] **Step 7: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/ Assets/FoundationDI/Tests/AdServiceTest.cs* \
        Assets/FoundationDI/Tests/AdTestDoubles.cs
git commit -m "[BEHAVIORAL] AdService 조립과 초기화 추가"
```

- [ ] **Step 8: 나머지 서비스 동작 테스트를 추가한다**

`AdServiceTest.cs`에 추가:

```csharp
    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 초기화에_실패하면_false를_반환하고_광고를_요청하지_않는다() =>
        UniTask.ToCoroutine(async () =>
    {
        var provider = new FakeAdProvider { InitializeResult = false };
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());

        LogAssert.Expect(UnityEngine.LogType.Error,
                         new System.Text.RegularExpressions.Regex("초기화에 실패"));
        var ok = await sut.InitializeAsync();

        Assert.IsFalse(ok);
        Assert.IsFalse(sut.IsInitialized);
        Assert.AreEqual(0, provider.InterstitialAdapter.LoadCount);
        Assert.AreEqual(0, provider.RewardedAdapter.LoadCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 자동로드가_꺼져있으면_초기화해도_광고를_로드하지_않는다() =>
        UniTask.ToCoroutine(async () =>
    {
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(),
                                NewOptions(autoLoad: false), new FakeRemovalStorage());

        await sut.InitializeAsync();

        Assert.AreEqual(0, provider.InterstitialAdapter.LoadCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 어댑터_이벤트가_포맷과_함께_서비스_이벤트로_전파된다() => UniTask.ToCoroutine(async () =>
    {
        var provider = new FakeAdProvider();
        var dispatcher = new FakeAdDispatcher();
        var sut = new AdService(provider, dispatcher, NewOptions(), new FakeRemovalStorage());
        await sut.InitializeAsync();

        var loaded = new List<AdFormat>();
        var displayed = new List<AdFormat>();
        var closed = new List<AdFormat>();
        sut.Loaded += f => loaded.Add(f);
        sut.Displayed += f => displayed.Add(f);
        sut.Closed += f => closed.Add(f);

        provider.RewardedAdapter.RaiseLoaded();
        var pending = sut.Rewarded.ShowAsync();
        provider.RewardedAdapter.RaiseDisplayed();
        provider.RewardedAdapter.RaiseClosed();
        dispatcher.TickFrames(1);
        await pending;

        CollectionAssert.Contains(loaded, AdFormat.Rewarded);
        CollectionAssert.Contains(displayed, AdFormat.Rewarded);
        CollectionAssert.Contains(closed, AdFormat.Rewarded);
        CollectionAssert.DoesNotContain(displayed, AdFormat.Interstitial);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 어댑터_임프레션과_provider_전역_임프레션이_모두_Paid로_합류한다() =>
        UniTask.ToCoroutine(async () =>
    {
        // LevelPlay는 전역 경로, AdMob/MAX는 어댑터 경로를 쓴다. 둘 다 새지 않아야 한다.
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());
        await sut.InitializeAsync();

        var received = new List<string>();
        sut.Paid += imp => received.Add(imp.NetworkName);

        provider.InterstitialAdapter.RaisePaid(NewImpression(AdFormat.Interstitial, "FromAdapter"));
        provider.RaiseImpressionPaid(NewImpression(AdFormat.Banner, "FromProvider"));

        CollectionAssert.AreEquivalent(new[] { "FromAdapter", "FromProvider" }, received);
    });

    private static AdImpression NewImpression(AdFormat format, string network)
    {
        return new AdImpression(format, "Fake", network, "unit", "inst", "place",
                                0.01, "USD", AdRevenuePrecision.Estimated, null);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 광고제거_상태는_저장소에_영속화되고_생성시_복원된다() => UniTask.ToCoroutine(async () =>
    {
        var storage = new FakeRemovalStorage { Value = true };
        var sut = new AdService(new FakeAdProvider(), new FakeAdDispatcher(), NewOptions(), storage);

        Assert.IsTrue(sut.AdsRemoved, "저장된 광고제거 상태가 복원되지 않았다");

        var changes = new List<bool>();
        sut.AdsRemovedChanged += v => changes.Add(v);

        sut.AdsRemoved = false;

        Assert.IsFalse(storage.Value, "저장소에 반영되지 않았다");
        CollectionAssert.AreEqual(new[] { false }, changes);

        var saveCountBefore = storage.SaveCount;
        sut.AdsRemoved = false;   // 같은 값 재설정
        Assert.AreEqual(saveCountBefore, storage.SaveCount, "값이 안 바뀌었는데 저장했다");
        Assert.AreEqual(1, changes.Count, "값이 안 바뀌었는데 이벤트를 쐈다");

        await UniTask.Yield();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose는_provider와_모든_광고_유닛을_해제한다() => UniTask.ToCoroutine(async () =>
    {
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());
        await sut.InitializeAsync();

        sut.Dispose();

        Assert.IsTrue(provider.IsDisposed, "provider가 해제되지 않았다");
        Assert.IsTrue(provider.InterstitialAdapter.IsDisposed);
        Assert.IsTrue(provider.RewardedAdapter.IsDisposed);
        Assert.IsFalse(sut.IsInitialized);
    });
```

- [ ] **Step 9: 통과를 확인한다**

Run: `run_tests(mode="EditMode")` (전체)
Expected: `AdServiceTest` 7건 PASS + 기존 전부 PASS

- [ ] **Step 10: 커밋**

```bash
git add Assets/FoundationDI/Tests/AdServiceTest.cs
git commit -m "[BEHAVIORAL] 서비스 이벤트 전파와 임프레션 합류, 광고제거 영속화 검증 추가"
```

---

### Task 10: UnityAdDispatcher — 실제 메인스레드 펌프

여기까지는 `FakeAdDispatcher`로만 돌았다. 이제 실제 구현이 필요하다.

**핵심 설계:** 큐와 타이머 로직을 **`UnityAdDispatcher`에 전부 두고, MonoBehaviour는 `Pump(deltaTime)`만 호출한다.** 그래야 EditMode에서 MonoBehaviour 없이 펌프 로직을 테스트할 수 있다. MonoBehaviour 안에 로직을 넣으면 PlayMode 테스트가 강제되고 훨씬 느려진다.

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Dispatch/UnityAdDispatcher.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Dispatch/AdServiceRunner.cs`
- Test: `Assets/FoundationDI/Tests/UnityAdDispatcherTest.cs`

**Interfaces:**
- Consumes: Task 2의 `IAdDispatcher`
- Produces:
  - `class UnityAdDispatcher : IAdDispatcher, IDisposable` — 생성자 `UnityAdDispatcher(bool createRunner = true)`, 메서드 `void Pump(float deltaTime)`
  - `class AdServiceRunner : MonoBehaviour` — 정적 `AdServiceRunner Create(UnityAdDispatcher dispatcher)`

- [ ] **Step 1: 실패 테스트를 작성한다**

`Assets/FoundationDI/Tests/UnityAdDispatcherTest.cs` 를 `Write`로 생성:

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class UnityAdDispatcherTest
{
    // createRunner: false 로 MonoBehaviour 없이 순수 큐 로직만 검증한다.
    private static UnityAdDispatcher NewDispatcher() => new(createRunner: false);

    [Test]
    public void Post한_작업은_다음_펌프에서_실행된다()
    {
        var sut = NewDispatcher();
        var ran = 0;

        sut.Post(() => ran++);
        Assert.AreEqual(0, ran, "Post가 즉시 실행됐다 — 마샬링 의미가 없다");

        sut.Pump(0.016f);
        Assert.AreEqual(1, ran);

        sut.Pump(0.016f);
        Assert.AreEqual(1, ran, "한 번 실행된 작업이 다시 실행됐다");
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

`read_console`: `UnityAdDispatcher` 미정의로 컴파일 실패.

- [ ] **Step 3: 구현을 작성한다**

`Dispatch/UnityAdDispatcher.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    // 큐/타이머 로직을 전부 여기 두고 MonoBehaviour는 Pump만 호출한다.
    // 그래야 EditMode에서 MonoBehaviour 없이 이 클래스를 테스트할 수 있다.
    public class UnityAdDispatcher : IAdDispatcher, IDisposable
    {
        private class Entry
        {
            public float SecondsLeft;
            public int FramesLeft;
            public bool IsFrameBased;
            public Action Action;
            public bool Cancelled;
        }

        private class Handle : IDisposable
        {
            private readonly Entry _entry;
            public Handle(Entry entry) { _entry = entry; }
            public void Dispose() { _entry.Cancelled = true; }
        }

        // 광고 SDK 콜백은 네이티브 스레드에서 올 수 있다. Post만 락으로 보호하면 충분하다 —
        // Delay/NextFrames는 이미 메인 스레드인 정책 계층에서만 호출된다.
        private readonly object _postLock = new();
        private readonly Queue<Action> _posted = new();
        private readonly List<Action> _drained = new();

        private readonly List<Entry> _entries = new();
        private readonly List<Entry> _due = new();

        private AdServiceRunner _runner;
        private bool _isDisposed;

        // [Inject]가 없으면 VContainer가 파라미터가 더 많은 (bool) 생성자를 고르고
        // bool을 해석하지 못해 등록이 실패한다. 반드시 붙인다.
        [Inject]
        public UnityAdDispatcher() : this(true) { }

        public UnityAdDispatcher(bool createRunner)
        {
            if (createRunner) _runner = AdServiceRunner.Create(this);
        }

        public void Post(Action action)
        {
            if (action == null || _isDisposed) return;
            lock (_postLock) _posted.Enqueue(action);
        }

        public IDisposable Delay(float seconds, Action action)
        {
            var entry = new Entry { SecondsLeft = seconds, IsFrameBased = false, Action = action };
            _entries.Add(entry);
            return new Handle(entry);
        }

        public IDisposable NextFrames(int count, Action action)
        {
            if (count <= 0)
            {
                action?.Invoke();
                return new Handle(new Entry { Cancelled = true });
            }

            var entry = new Entry { FramesLeft = count, IsFrameBased = true, Action = action };
            _entries.Add(entry);
            return new Handle(entry);
        }

        public void Pump(float deltaTime)
        {
            if (_isDisposed) return;

            DrainPosted();
            AdvanceEntries(deltaTime);
        }

        private void DrainPosted()
        {
            _drained.Clear();

            lock (_postLock)
            {
                while (_posted.Count > 0) _drained.Add(_posted.Dequeue());
            }

            // 락 밖에서 실행한다. 콜백이 다시 Post를 부를 수 있어 락 안에서 실행하면 데드락이다.
            foreach (var action in _drained) SafeInvoke(action);
        }

        private void AdvanceEntries(float deltaTime)
        {
            _due.Clear();

            foreach (var entry in _entries)
            {
                if (entry.Cancelled) continue;

                if (entry.IsFrameBased) entry.FramesLeft--;
                else entry.SecondsLeft -= deltaTime;

                var isDue = entry.IsFrameBased ? entry.FramesLeft <= 0 : entry.SecondsLeft <= 0f;
                if (isDue) _due.Add(entry);
            }

            // 실행 중에 새 항목이 예약될 수 있으므로(자동 재로드 등) 먼저 목록에서 걷어낸 뒤 실행한다.
            foreach (var entry in _due) entry.Cancelled = true;
            _entries.RemoveAll(e => e.Cancelled);
            foreach (var entry in _due) SafeInvoke(entry.Action);
        }

        // 하나의 콜백이 던진 예외가 나머지 큐를 막지 않게 한다.
        private static void SafeInvoke(Action action)
        {
            try { action?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _entries.Clear();
            lock (_postLock) _posted.Clear();

            if (_runner != null)
            {
                _runner.Detach();
                _runner = null;
            }
        }
    }
}
```

`Dispatch/AdServiceRunner.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 씬에 보이지 않는 펌프. 로직은 없다 — UnityAdDispatcher.Pump를 부르기만 한다.
    [DefaultExecutionOrder(-100)]
    public class AdServiceRunner : MonoBehaviour
    {
        private UnityAdDispatcher _dispatcher;

        public static AdServiceRunner Create(UnityAdDispatcher dispatcher)
        {
            var go = new GameObject("[AdService] Runner") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);

            var runner = go.AddComponent<AdServiceRunner>();
            runner._dispatcher = dispatcher;
            return runner;
        }

        // Time.unscaledDeltaTime을 쓴다. 전면광고 표시 중에는 게임이 timeScale=0으로
        // 멈춰 있는 경우가 많은데, 그때도 재시도 타이머는 흘러야 한다.
        private void Update() => _dispatcher?.Pump(Time.unscaledDeltaTime);

        public void Detach()
        {
            _dispatcher = null;
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }
}
```

- [ ] **Step 4: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UnityAdDispatcherTest")`
Expected: PASS 1건

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Dispatch/ \
        Assets/FoundationDI/Tests/UnityAdDispatcherTest.cs*
git commit -m "[BEHAVIORAL] 메인스레드 펌프 디스패처 구현 추가"
```

- [ ] **Step 6: 지연/프레임/예외 테스트를 추가한다**

`UnityAdDispatcherTest.cs`에 추가:

```csharp
    [Test]
    public void 지연작업은_누적_deltaTime이_지연시간에_도달하면_실행된다()
    {
        var sut = NewDispatcher();
        var ran = 0;

        sut.Delay(0.1f, () => ran++);

        sut.Pump(0.04f);
        sut.Pump(0.04f);
        Assert.AreEqual(0, ran, "0.08초에 실행됐다");

        sut.Pump(0.04f);
        Assert.AreEqual(1, ran, "0.12초인데 실행되지 않았다");
    }

    [Test]
    public void 취소된_지연작업은_실행되지_않는다()
    {
        var sut = NewDispatcher();
        var ran = 0;

        sut.Delay(0.1f, () => ran++).Dispose();

        sut.Pump(1f);

        Assert.AreEqual(0, ran);
    }

    [Test]
    public void 프레임_기반_작업은_지정_펌프_횟수_후_실행된다()
    {
        var sut = NewDispatcher();
        var ran = 0;

        sut.NextFrames(2, () => ran++);

        sut.Pump(0.016f);
        Assert.AreEqual(0, ran);

        sut.Pump(0.016f);
        Assert.AreEqual(1, ran);
    }

    [Test]
    public void 실행중에_예약된_작업은_같은_펌프에서_실행되지_않는다()
    {
        var sut = NewDispatcher();
        var inner = 0;

        sut.Delay(0.01f, () => sut.Delay(0.01f, () => inner++));

        sut.Pump(1f);
        Assert.AreEqual(0, inner, "중첩 예약이 같은 펌프에서 실행됐다");

        sut.Pump(1f);
        Assert.AreEqual(1, inner);
    }

    [Test]
    public void 한_작업이_예외를_던져도_나머지_작업은_실행된다()
    {
        var sut = NewDispatcher();
        var ran = 0;

        sut.Post(() => throw new System.InvalidOperationException("boom"));
        sut.Post(() => ran++);

        UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception,
            new System.Text.RegularExpressions.Regex("InvalidOperationException"));
        sut.Pump(0.016f);

        Assert.AreEqual(1, ran, "앞 작업의 예외가 뒤 작업을 막았다");
    }

    [Test]
    public void Dispose하면_예약된_작업이_더_이상_실행되지_않는다()
    {
        var sut = NewDispatcher();
        var ran = 0;

        sut.Delay(0.1f, () => ran++);
        sut.Post(() => ran++);

        sut.Dispose();
        sut.Pump(1f);

        Assert.AreEqual(0, ran);
    }
```

- [ ] **Step 7: 통과를 확인한다**

Run: `run_tests(mode="EditMode")` (전체)
Expected: `UnityAdDispatcherTest` 7건 PASS + 기존 전부 PASS

- [ ] **Step 8: 커밋**

```bash
git add Assets/FoundationDI/Tests/UnityAdDispatcherTest.cs
git commit -m "[BEHAVIORAL] 디스패처 지연/프레임/예외 격리 동작 검증 추가"
```

---

### Task 11: Dummy provider

SDK 없이 전체 흐름을 실기에서 돌려보기 위한 provider. **에디터 확인용 장난감이 아니라, 3사 어댑터가 없는 지금 상태에서 정책 계층·분석 연동·UI 레이아웃을 전부 검증하는 유일한 수단이다.**

화면 생성은 `IDummyAdScreen` seam 뒤에 둔다. 그래야 어댑터의 지연/실패/보상 로직을 uGUI 없이 EditMode에서 테스트할 수 있다.

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Providers/Dummy/DummyAdOptions.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Providers/Dummy/IDummyAdScreen.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Providers/Dummy/DummyFullScreenAdapter.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Providers/Dummy/DummyBannerAdapter.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Providers/Dummy/DummyAdProvider.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Providers/Dummy/DummyAdCanvas.cs`
- Test: `Assets/FoundationDI/Tests/DummyAdProviderTest.cs`

**Interfaces:**
- Consumes: Task 2의 `IFullScreenAdapter`/`IBannerAdapter`/`IAdDispatcher`, Task 9의 `IAdProvider`/`BannerOptions`/`NoopAdConsent`
- Produces:
  - `readonly struct DummyAdOptions` — 생성자 `(float loadDelaySeconds, float failureRate, float adDurationSeconds, float bannerHeight)`, 정적 `Default`
  - `interface IDummyAdScreen : IDisposable` — `void ShowFullScreen(AdFormat format, float duration, Action onSkip, Action onComplete)`, `void ShowBanner(BannerPosition position, float height)`, `void HideBanner()`
  - `class DummyFullScreenAdapter : IFullScreenAdapter` — 생성자 `(AdFormat format, IAdDispatcher dispatcher, IDummyAdScreen screen, DummyAdOptions options, Func<float> random)`
  - `class DummyBannerAdapter : IBannerAdapter` — 생성자 `(IDummyAdScreen screen, BannerOptions bannerOptions, DummyAdOptions options)`
  - `class DummyAdProvider : IAdProvider` — 생성자 `(IAdDispatcher dispatcher, DummyAdOptions options, IDummyAdScreen screen = null, Func<float> random = null)`
  - `class DummyAdCanvas : IDummyAdScreen` — 인자 없는 생성자

- [ ] **Step 1: 실패 테스트를 작성한다**

`Assets/FoundationDI/Tests/DummyAdProviderTest.cs` 를 `Write`로 생성:

```csharp
using System;
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class DummyAdProviderTest
{
    private class FakeScreen : IDummyAdScreen
    {
        public int FullScreenCount;
        public int BannerShowCount;
        public int BannerHideCount;
        public Action OnSkip;
        public Action OnComplete;
        public bool IsDisposed;

        public void ShowFullScreen(AdFormat format, float duration, Action onSkip, Action onComplete)
        {
            FullScreenCount++;
            OnSkip = onSkip;
            OnComplete = onComplete;
        }

        public void ShowBanner(BannerPosition position, float height) => BannerShowCount++;
        public void HideBanner() => BannerHideCount++;
        public void Dispose() => IsDisposed = true;
    }

    private static readonly DummyAdOptions NeverFails =
        new(loadDelaySeconds: 1f, failureRate: 0f, adDurationSeconds: 3f, bannerHeight: 100f);

    [Test]
    public void 더미_전면광고는_설정된_지연_후_로드에_성공한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var screen = new FakeScreen();
        var sut = new DummyFullScreenAdapter(AdFormat.Interstitial, dispatcher, screen, NeverFails, () => 0.5f);

        var loaded = 0;
        sut.Loaded += () => loaded++;

        sut.Load();
        Assert.AreEqual(0, loaded, "지연 없이 즉시 로드됐다");
        Assert.IsFalse(sut.IsReady);

        dispatcher.Advance(1.1f);

        Assert.AreEqual(1, loaded);
        Assert.IsTrue(sut.IsReady);
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

`read_console`: `DummyAdOptions`/`IDummyAdScreen`/`DummyFullScreenAdapter` 미정의로 컴파일 실패.

- [ ] **Step 3: 옵션과 화면 seam을 작성한다**

`Providers/Dummy/DummyAdOptions.cs`:

```csharp
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [Serializable]
    public struct DummyAdOptions
    {
        [Tooltip("가짜 광고 로드에 걸리는 시간(초). 실제 SDK의 로드 지연을 흉내낸다.")]
        [SerializeField] private float _loadDelaySeconds;

        [Tooltip("로드 실패 확률(0~1). 재시도·백오프를 실기에서 검증하려면 0.3 정도로 올린다.")]
        [Range(0f, 1f)]
        [SerializeField] private float _failureRate;

        [Tooltip("가짜 전면/보상 광고가 화면에 떠 있는 시간(초).")]
        [SerializeField] private float _adDurationSeconds;

        [Tooltip("가짜 배너의 높이(화면 픽셀).")]
        [SerializeField] private float _bannerHeight;

        public DummyAdOptions(float loadDelaySeconds, float failureRate,
                              float adDurationSeconds, float bannerHeight)
        {
            _loadDelaySeconds = loadDelaySeconds;
            _failureRate = failureRate;
            _adDurationSeconds = adDurationSeconds;
            _bannerHeight = bannerHeight;
        }

        public float LoadDelaySeconds => _loadDelaySeconds;
        public float FailureRate => _failureRate;
        public float AdDurationSeconds => _adDurationSeconds;
        public float BannerHeight => _bannerHeight;

        public static DummyAdOptions Default => new(1f, 0f, 3f, 100f);
    }
}
```

`Providers/Dummy/IDummyAdScreen.cs`:

```csharp
using System;

namespace DarkNaku.FoundationDI
{
    // 가짜 광고 화면 seam. 어댑터의 지연/실패/보상 로직을 uGUI 없이 테스트하기 위해 분리한다.
    public interface IDummyAdScreen : IDisposable
    {
        // onComplete는 카운트다운 완주, onSkip은 중간 닫기. 둘 중 하나만 호출된다.
        void ShowFullScreen(AdFormat format, float duration, Action onSkip, Action onComplete);

        void ShowBanner(BannerPosition position, float height);
        void HideBanner();
    }
}
```

- [ ] **Step 4: 전면 어댑터를 작성한다**

`Providers/Dummy/DummyFullScreenAdapter.cs`:

```csharp
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public class DummyFullScreenAdapter : IFullScreenAdapter
    {
        private readonly AdFormat _format;
        private readonly IAdDispatcher _dispatcher;
        private readonly IDummyAdScreen _screen;
        private readonly DummyAdOptions _options;
        private readonly Func<float> _random;

        private IDisposable _pendingLoad;
        private bool _isLoading;
        private bool _isDisposed;

        public bool IsReady { get; private set; }

        public event Action Loaded;
        public event Action<AdError> LoadFailed;
        public event Action Displayed;
        public event Action<AdError> DisplayFailed;
        public event Action Closed;
        public event Action<AdReward> Rewarded;
        public event Action<AdImpression> Paid;

        public DummyFullScreenAdapter(AdFormat format, IAdDispatcher dispatcher, IDummyAdScreen screen,
                                      DummyAdOptions options, Func<float> random)
        {
            _format = format;
            _dispatcher = dispatcher;
            _screen = screen;
            _options = options;
            _random = random ?? (() => UnityEngine.Random.value);
        }

        public void Load()
        {
            if (_isDisposed || IsReady || _isLoading) return;

            _isLoading = true;
            _pendingLoad = _dispatcher.Delay(_options.LoadDelaySeconds, () =>
            {
                _pendingLoad = null;
                _isLoading = false;
                if (_isDisposed) return;

                if (_random() < _options.FailureRate)
                {
                    LoadFailed?.Invoke(new AdError(3, "dummy: no fill"));
                    return;
                }

                IsReady = true;
                Loaded?.Invoke();
            });
        }

        public void Show()
        {
            if (_isDisposed) return;

            if (!IsReady)
            {
                DisplayFailed?.Invoke(new AdError(-3, "dummy: 준비되지 않은 광고를 표시하려 했다"));
                return;
            }

            IsReady = false;
            Displayed?.Invoke();
            EmitImpression();

            _screen.ShowFullScreen(_format, _options.AdDurationSeconds,
                onSkip: () => Closed?.Invoke(),
                onComplete: () =>
                {
                    // 보상은 닫힘보다 먼저 보낸다 — AdMob/MAX의 일반적인 순서를 흉내낸다.
                    if (_format == AdFormat.Rewarded) Rewarded?.Invoke(new AdReward("dummy_reward", 1));
                    Closed?.Invoke();
                });
        }

        // 가짜 네트워크명과 난수 단가로 임프레션을 발행한다.
        // 3사 어댑터가 없는 지금도 분석 연동 경로를 실기에서 검증할 수 있게 하는 것이 목적이다.
        private void EmitImpression()
        {
            var revenue = 0.001 + _random() * 0.05;
            Paid?.Invoke(new AdImpression(
                _format, "Dummy", "DummyNetwork", $"dummy-{_format}".ToLowerInvariant(),
                "dummy-instance", null, revenue, "USD", AdRevenuePrecision.Estimated, "dummy-creative"));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _pendingLoad?.Dispose();
            _pendingLoad = null;
            IsReady = false;
        }
    }
}
```

- [ ] **Step 5: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="DummyAdProviderTest")`
Expected: PASS 1건

- [ ] **Step 6: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Providers/Dummy/ \
        Assets/FoundationDI/Tests/DummyAdProviderTest.cs*
git commit -m "[BEHAVIORAL] 더미 전면/보상 어댑터 추가"
```

- [ ] **Step 7: 실패율·보상·스킵 테스트를 추가한다**

`DummyAdProviderTest.cs`에 추가:

```csharp
    [Test]
    public void 더미_광고는_실패율이_1이면_로드에_실패한다()
    {
        var options = new DummyAdOptions(1f, failureRate: 1f, adDurationSeconds: 3f, bannerHeight: 100f);
        var dispatcher = new FakeAdDispatcher();
        var sut = new DummyFullScreenAdapter(AdFormat.Interstitial, dispatcher, new FakeScreen(),
                                             options, () => 0.5f);

        AdError? failed = null;
        sut.LoadFailed += e => failed = e;

        sut.Load();
        dispatcher.Advance(1.1f);

        Assert.IsTrue(failed.HasValue, "실패율 1인데 로드에 성공했다");
        Assert.IsFalse(sut.IsReady);
    }

    [Test]
    public void 더미_보상광고는_완주하면_보상_후_닫힘을_발화한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var screen = new FakeScreen();
        var sut = new DummyFullScreenAdapter(AdFormat.Rewarded, dispatcher, screen, NeverFails, () => 0.5f);

        var order = new System.Collections.Generic.List<string>();
        sut.Rewarded += _ => order.Add("rewarded");
        sut.Closed += () => order.Add("closed");

        sut.Load();
        dispatcher.Advance(1.1f);
        sut.Show();

        Assert.AreEqual(1, screen.FullScreenCount, "화면이 요청되지 않았다");

        screen.OnComplete();

        CollectionAssert.AreEqual(new[] { "rewarded", "closed" }, order);
    }

    [Test]
    public void 더미_보상광고는_중간에_닫으면_보상없이_닫힘만_발화한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var screen = new FakeScreen();
        var sut = new DummyFullScreenAdapter(AdFormat.Rewarded, dispatcher, screen, NeverFails, () => 0.5f);

        var rewarded = 0;
        var closed = 0;
        sut.Rewarded += _ => rewarded++;
        sut.Closed += () => closed++;

        sut.Load();
        dispatcher.Advance(1.1f);
        sut.Show();
        screen.OnSkip();

        Assert.AreEqual(0, rewarded, "중간에 닫았는데 보상이 나왔다");
        Assert.AreEqual(1, closed);
    }

    [Test]
    public void 더미_광고는_표시할_때_임프레션을_발행한다()
    {
        var dispatcher = new FakeAdDispatcher();
        var sut = new DummyFullScreenAdapter(AdFormat.Interstitial, dispatcher, new FakeScreen(),
                                             NeverFails, () => 0.5f);

        AdImpression? impression = null;
        sut.Paid += imp => impression = imp;

        sut.Load();
        dispatcher.Advance(1.1f);
        sut.Show();

        Assert.IsTrue(impression.HasValue, "임프레션이 발행되지 않았다");
        Assert.AreEqual("Dummy", impression.Value.AdPlatform);
        Assert.AreEqual("DummyNetwork", impression.Value.NetworkName);
        Assert.AreEqual("USD", impression.Value.Currency);
        Assert.Greater(impression.Value.Revenue, 0.0);
    }

    [Test]
    public void 더미_광고를_준비도_안_된_상태에서_표시하면_표시_실패를_발화한다()
    {
        var sut = new DummyFullScreenAdapter(AdFormat.Interstitial, new FakeAdDispatcher(),
                                             new FakeScreen(), NeverFails, () => 0.5f);

        AdError? failed = null;
        sut.DisplayFailed += e => failed = e;

        sut.Show();

        Assert.IsTrue(failed.HasValue);
    }
```

- [ ] **Step 8: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="DummyAdProviderTest")`
Expected: PASS 6건

- [ ] **Step 9: 배너 어댑터와 provider를 작성한다**

`Providers/Dummy/DummyBannerAdapter.cs`:

```csharp
using System;

namespace DarkNaku.FoundationDI
{
    public class DummyBannerAdapter : IBannerAdapter
    {
        private readonly IDummyAdScreen _screen;
        private readonly BannerOptions _bannerOptions;
        private readonly DummyAdOptions _options;
        private bool _isDisposed;

        public float Height { get; private set; }

        public event Action<float> HeightChanged;
        public event Action<AdImpression> Paid;

        public DummyBannerAdapter(IDummyAdScreen screen, BannerOptions bannerOptions, DummyAdOptions options)
        {
            _screen = screen;
            _bannerOptions = bannerOptions;
            _options = options;
        }

        public void Show()
        {
            if (_isDisposed) return;

            _screen.ShowBanner(_bannerOptions.Position, _options.BannerHeight);

            Height = _options.BannerHeight;
            HeightChanged?.Invoke(Height);

            Paid?.Invoke(new AdImpression(AdFormat.Banner, "Dummy", "DummyNetwork", "dummy-banner",
                                          "dummy-instance", null, 0.002, "USD",
                                          AdRevenuePrecision.Estimated, "dummy-creative"));
        }

        public void Hide()
        {
            if (_isDisposed) return;

            _screen.HideBanner();
            Height = 0f;
            HeightChanged?.Invoke(0f);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _screen.HideBanner();
            Height = 0f;
        }
    }
}
```

`Providers/Dummy/DummyAdProvider.cs`:

```csharp
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // SDK 없이 전체 흐름을 실기에서 검증하기 위한 provider.
    // 로드 지연과 실패 확률을 설정으로 흉내내므로 재시도·백오프를 눈으로 확인할 수 있다.
    public class DummyAdProvider : IAdProvider
    {
        private readonly IAdDispatcher _dispatcher;
        private readonly DummyAdOptions _options;
        private readonly Func<float> _random;
        private readonly bool _ownsScreen;

        private IDummyAdScreen _screen;
        private bool _isDisposed;

        public string Name => "Dummy";
        public IAdConsent Consent { get; } = new NoopAdConsent();

        // Dummy는 어댑터별 Paid만 쓴다. 전역 경로는 LevelPlay 어댑터를 위한 자리다.
        public event Action<AdImpression> ImpressionPaid;

        public DummyAdProvider(IAdDispatcher dispatcher, DummyAdOptions options,
                               IDummyAdScreen screen = null, Func<float> random = null)
        {
            _dispatcher = dispatcher;
            _options = options;
            _random = random;
            _ownsScreen = screen == null;
            _screen = screen;
        }

        public Awaitable<bool> InitializeAsync(AdProviderContext context)
        {
            _screen ??= new DummyAdCanvas();

            if (context.VerboseLogging) Debug.Log("[AdService] Dummy provider 초기화 완료");

            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(true);
            return source.Awaitable;
        }

        public IFullScreenAdapter CreateInterstitial(string adUnitId) =>
            new DummyFullScreenAdapter(AdFormat.Interstitial, _dispatcher, _screen, _options, _random);

        public IFullScreenAdapter CreateRewarded(string adUnitId) =>
            new DummyFullScreenAdapter(AdFormat.Rewarded, _dispatcher, _screen, _options, _random);

        public IBannerAdapter CreateBanner(string adUnitId, BannerOptions options) =>
            new DummyBannerAdapter(_screen, options, _options);

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // 외부에서 받은 화면은 소유권이 없으므로 해제하지 않는다.
            if (_ownsScreen) _screen?.Dispose();
            _screen = null;
            ImpressionPaid = null;
        }
    }
}
```

- [ ] **Step 10: 배너/provider 테스트를 추가한다**

`DummyAdProviderTest.cs`에 추가:

```csharp
    [Test]
    public void 더미_배너는_표시하면_설정된_높이를_보고하고_임프레션을_발행한다()
    {
        var screen = new FakeScreen();
        var sut = new DummyBannerAdapter(screen,
            new BannerOptions(BannerPosition.Bottom, BannerSize.Adaptive, true), NeverFails);

        var reported = -1f;
        AdImpression? impression = null;
        sut.HeightChanged += h => reported = h;
        sut.Paid += imp => impression = imp;

        sut.Show();

        Assert.AreEqual(1, screen.BannerShowCount);
        Assert.AreEqual(100f, sut.Height, 0.001f);
        Assert.AreEqual(100f, reported, 0.001f);
        Assert.IsTrue(impression.HasValue);
        Assert.AreEqual(AdFormat.Banner, impression.Value.Format);

        sut.Hide();

        Assert.AreEqual(1, screen.BannerHideCount);
        Assert.AreEqual(0f, sut.Height, 0.001f);
    }

    [Test]
    public void 더미_provider는_외부에서_받은_화면을_해제하지_않는다()
    {
        // 화면 소유권을 잘못 잡으면 provider 재생성 시 남의 Canvas를 파괴한다.
        var screen = new FakeScreen();
        var sut = new DummyAdProvider(new FakeAdDispatcher(), NeverFails, screen);

        sut.Dispose();

        Assert.IsFalse(screen.IsDisposed);
    }
```

- [ ] **Step 11: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="DummyAdProviderTest")`
Expected: PASS 8건

- [ ] **Step 12: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Providers/Dummy/ \
        Assets/FoundationDI/Tests/DummyAdProviderTest.cs
git commit -m "[BEHAVIORAL] 더미 배너 어댑터와 Dummy provider 추가"
```

- [ ] **Step 13: 실제 uGUI 화면을 작성한다**

`Providers/Dummy/DummyAdCanvas.cs`. 테스트 대상이 아니라 눈으로 확인하는 코드다 — 자립형이며 `UIService`에 의존하지 않는다.

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    // 자립형 가짜 광고 화면. UIService에 의존하지 않는다 —
    // ADService가 UI 시스템에 묶이면 "어떤 네트워크든 동일"이라는 목표와 무관한 결합이 생긴다.
    public class DummyAdCanvas : IDummyAdScreen
    {
        private const int SORTING_ORDER = 32767;   // 항상 최상단

        private GameObject _root;
        private GameObject _fullScreenPanel;
        private GameObject _bannerPanel;
        private Text _label;
        private Text _countdown;
        private Button _closeButton;

        private float _remaining;
        private Action _onSkip;
        private Action _onComplete;
        private DummyAdTicker _ticker;

        public void ShowFullScreen(AdFormat format, float duration, Action onSkip, Action onComplete)
        {
            EnsureRoot();

            _onSkip = onSkip;
            _onComplete = onComplete;
            _remaining = duration;

            _label.text = $"{format}\n(Dummy Ad)";
            _fullScreenPanel.SetActive(true);
            UpdateCountdown();
        }

        public void ShowBanner(BannerPosition position, float height)
        {
            EnsureRoot();

            var rect = _bannerPanel.GetComponent<RectTransform>();
            var top = position == BannerPosition.Top;
            rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rect.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = Vector2.zero;

            _bannerPanel.SetActive(true);
        }

        public void HideBanner()
        {
            if (_bannerPanel != null) _bannerPanel.SetActive(false);
        }

        // 매 프레임 카운트다운을 갱신한다. 전면광고 중에는 timeScale이 0인 경우가 많아
        // unscaledDeltaTime을 쓴다.
        private void Tick()
        {
            if (_fullScreenPanel == null || !_fullScreenPanel.activeSelf) return;

            _remaining -= Time.unscaledDeltaTime;
            UpdateCountdown();

            if (_remaining > 0f) return;

            _fullScreenPanel.SetActive(false);
            var complete = _onComplete;
            _onSkip = null;
            _onComplete = null;
            complete?.Invoke();
        }

        private void UpdateCountdown()
        {
            var canClose = _remaining <= 0f;
            _countdown.text = canClose ? "" : $"{Mathf.CeilToInt(_remaining)}";
            _closeButton.gameObject.SetActive(true);
        }

        private void OnCloseClicked()
        {
            _fullScreenPanel.SetActive(false);
            var skip = _onSkip;
            _onSkip = null;
            _onComplete = null;
            skip?.Invoke();
        }

        private void EnsureRoot()
        {
            if (_root != null) return;

            _root = new GameObject("[AdService] Dummy Canvas") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_root);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;
            _root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _root.AddComponent<GraphicRaycaster>();

            _ticker = _root.AddComponent<DummyAdTicker>();
            _ticker.OnTick = Tick;

            _fullScreenPanel = CreatePanel("FullScreen", new Color(0f, 0f, 0f, 0.85f), stretch: true);
            _label = CreateText(_fullScreenPanel.transform, "Label", 48, new Vector2(0f, 60f));
            _countdown = CreateText(_fullScreenPanel.transform, "Countdown", 36, new Vector2(0f, -20f));

            _closeButton = CreateCloseButton(_fullScreenPanel.transform);
            _closeButton.onClick.AddListener(OnCloseClicked);
            _fullScreenPanel.SetActive(false);

            _bannerPanel = CreatePanel("Banner", new Color(0.1f, 0.4f, 0.8f, 0.9f), stretch: false);
            CreateText(_bannerPanel.transform, "BannerLabel", 24, Vector2.zero).text = "Dummy Banner";
            _bannerPanel.SetActive(false);
        }

        private GameObject CreatePanel(string name, Color color, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root.transform, false);
            go.GetComponent<Image>().color = color;

            var rect = go.GetComponent<RectTransform>();
            if (stretch)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.sizeDelta = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
            }

            return go;
        }

        private static Text CreateText(Transform parent, string name, int size, Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(800f, 120f);
            rect.anchoredPosition = offset;

            return text;
        }

        private static Button CreateCloseButton(Transform parent)
        {
            var go = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(120f, 80f);
            rect.anchoredPosition = new Vector2(-24f, -24f);

            CreateText(go.transform, "X", 36, Vector2.zero).text = "X";

            return go.GetComponent<Button>();
        }

        public void Dispose()
        {
            if (_root == null) return;

            if (_ticker != null) _ticker.OnTick = null;

            if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
            else UnityEngine.Object.DestroyImmediate(_root);

            _root = null;
        }

        // Canvas에 붙어 매 프레임 콜백만 흘려주는 최소 MonoBehaviour.
        private class DummyAdTicker : MonoBehaviour
        {
            public Action OnTick;
            private void Update() => OnTick?.Invoke();
        }
    }
}
```

- [ ] **Step 14: 컴파일과 전체 테스트를 확인한다**

`read_console`로 컴파일 에러가 없는지 확인한다. `DummyAdCanvas`는 uGUI를 쓰므로 `FoundationDI.asmdef`가 `UnityEngine.UI`를 참조하는지 확인해야 한다 — UIService가 이미 uGUI를 쓰므로 참조가 있을 것이다. 없다면 asmdef에 추가하고 **별도 `[STRUCTURAL]` 커밋**으로 분리한다.

Run: `run_tests(mode="EditMode")` (전체)
Expected: 기존 전부 PASS

- [ ] **Step 15: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Providers/Dummy/DummyAdCanvas.cs*
git commit -m "[BEHAVIORAL] 더미 광고 화면 uGUI 구현 추가"
```

---

### Task 12: 설정, provider 선택, DI 등록

마지막 조립. `AdServiceSettings`(ScriptableObject)가 인스펙터 편집면이고, `AdProviderFactory`가 설정과 스크립팅 심볼을 보고 provider를 고르며, `RegisterAdService`가 VContainer에 묶는다.

**provider 선택은 순수 함수로 분리한다.** `AdProviderFactory.Resolve(requested, forceDummy, out warning)`가 "무엇을 쓸지"를 결정하고, 실제 인스턴스 생성은 그 결과를 받아서 한다. 그래야 심볼 조합별 폴백 동작을 EditMode에서 검증할 수 있다.

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Settings/AdProviderType.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Settings/AdServiceSettings.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Providers/IAdProviderFactory.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/Providers/AdProviderFactory.cs`
- Create: `Assets/FoundationDI/Runtime/Services/AdService/AdServiceRegistration.cs`
- Test: `Assets/FoundationDI/Tests/AdProviderFactoryTest.cs`

**Interfaces:**
- Consumes: Task 9의 `IAdProvider`/`AdServiceOptions`/`AdProviderContext`/`BannerOptions`, Task 11의 `DummyAdProvider`/`DummyAdOptions`
- Produces:
  - `enum AdProviderType { Dummy, AdMob, LevelPlay, AppLovin }`
  - `class AdServiceSettings : ScriptableObject` — `AdProviderType Provider`, `AdServiceOptions ToOptions()`, `DummyAdOptions DummyOptions`, `bool ForceDummyInEditor`
  - `interface IAdProviderFactory { IAdProvider Create(AdProviderType type, DummyAdOptions dummyOptions, bool forceDummy); }`
  - `class AdProviderFactory : IAdProviderFactory` — 생성자 `(IAdDispatcher dispatcher)`, 정적 `bool IsAvailable(AdProviderType)`, 정적 `AdProviderType Resolve(AdProviderType requested, bool forceDummy, out string warning)`
  - `static class AdServiceRegistration` — `IContainerBuilder RegisterAdService(this IContainerBuilder, AdServiceSettings)`

- [ ] **Step 1: 실패 테스트를 작성한다**

`Assets/FoundationDI/Tests/AdProviderFactoryTest.cs` 를 `Write`로 생성:

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class AdProviderFactoryTest
{
    [Test]
    public void SDK_심볼이_없는_provider를_요청하면_경고와_함께_Dummy로_폴백한다()
    {
        var effective = AdProviderFactory.Resolve(AdProviderType.AdMob, forceDummy: false, out var warning);

        // 이 리포지토리에는 아직 어떤 광고 SDK도 설치되어 있지 않다.
        Assert.AreEqual(AdProviderType.Dummy, effective);
        Assert.IsNotNull(warning, "폴백했는데 경고가 없다");
        StringAssert.Contains("AdMob", warning);
    }

    [Test]
    public void Dummy를_요청하면_경고_없이_Dummy를_쓴다()
    {
        var effective = AdProviderFactory.Resolve(AdProviderType.Dummy, forceDummy: false, out var warning);

        Assert.AreEqual(AdProviderType.Dummy, effective);
        Assert.IsNull(warning);
    }

    [Test]
    public void 강제_더미가_켜지면_요청과_무관하게_Dummy를_쓰고_경고하지_않는다()
    {
        // 에디터 강제 더미는 의도된 설정이므로 경고를 띄우면 매 실행마다 소음이 된다.
        var effective = AdProviderFactory.Resolve(AdProviderType.LevelPlay, forceDummy: true, out var warning);

        Assert.AreEqual(AdProviderType.Dummy, effective);
        Assert.IsNull(warning);
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

`read_console`: `AdProviderFactory`/`AdProviderType` 미정의로 컴파일 실패.

- [ ] **Step 3: provider 타입과 팩토리를 작성한다**

`Settings/AdProviderType.cs`:

```csharp
namespace DarkNaku.FoundationDI
{
    public enum AdProviderType
    {
        Dummy = 0,
        AdMob = 1,
        LevelPlay = 2,
        AppLovin = 3,
    }
}
```

`Providers/IAdProviderFactory.cs`:

```csharp
namespace DarkNaku.FoundationDI
{
    public interface IAdProviderFactory
    {
        IAdProvider Create(AdProviderType type, DummyAdOptions dummyOptions, bool forceDummy);
    }
}
```

`Providers/AdProviderFactory.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public class AdProviderFactory : IAdProviderFactory
    {
        private readonly IAdDispatcher _dispatcher;

        public AdProviderFactory(IAdDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        // SDK가 설치되고 스크립팅 심볼이 정의됐는지. 3사 어댑터를 추가할 때
        // 여기와 CreateReal만 손대면 된다.
        public static bool IsAvailable(AdProviderType type)
        {
            switch (type)
            {
                case AdProviderType.Dummy:
                    return true;
                case AdProviderType.AdMob:
#if FOUNDATIONDI_ADMOB
                    return true;
#else
                    return false;
#endif
                case AdProviderType.LevelPlay:
#if FOUNDATIONDI_LEVELPLAY
                    return true;
#else
                    return false;
#endif
                case AdProviderType.AppLovin:
#if FOUNDATIONDI_APPLOVIN
                    return true;
#else
                    return false;
#endif
                default:
                    return false;
            }
        }

        // "무엇을 쓸지"만 결정하는 순수 함수. 인스턴스를 만들지 않으므로 테스트가 쉽다.
        public static AdProviderType Resolve(AdProviderType requested, bool forceDummy, out string warning)
        {
            warning = null;

            // 강제 더미는 의도된 설정이다. 경고하면 매 실행마다 소음이 된다.
            if (forceDummy) return AdProviderType.Dummy;

            if (IsAvailable(requested)) return requested;

            warning = $"[AdService] {requested} provider를 요청했지만 SDK 또는 스크립팅 심볼이 없다. " +
                      $"Dummy provider로 대체한다. (필요한 심볼: FOUNDATIONDI_{requested.ToString().ToUpperInvariant()})";
            return AdProviderType.Dummy;
        }

        public IAdProvider Create(AdProviderType type, DummyAdOptions dummyOptions, bool forceDummy)
        {
            var effective = Resolve(type, forceDummy, out var warning);

            if (warning != null) Debug.LogWarning(warning);

            // 3사 어댑터가 추가되면 여기에 분기가 생긴다. 지금은 Dummy만 존재한다.
            return new DummyAdProvider(_dispatcher, dummyOptions);
        }
    }
}
```

- [ ] **Step 4: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="AdProviderFactoryTest")`
Expected: PASS 3건

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Settings/AdProviderType.cs* \
        Assets/FoundationDI/Runtime/Services/AdService/Providers/IAdProviderFactory.cs* \
        Assets/FoundationDI/Runtime/Services/AdService/Providers/AdProviderFactory.cs* \
        Assets/FoundationDI/Tests/AdProviderFactoryTest.cs*
git commit -m "[BEHAVIORAL] provider 선택과 Dummy 폴백 규칙 추가"
```

- [ ] **Step 6: 설정 ScriptableObject를 작성한다**

`Settings/AdServiceSettings.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "AdServiceSettings", menuName = "FoundationDI/Ad Service Settings")]
    public class AdServiceSettings : ScriptableObject
    {
        [Header("Provider")]
        [SerializeField] private AdProviderType _provider = AdProviderType.Dummy;

        [Tooltip("에디터에서는 항상 Dummy provider를 쓴다. 실기 테스트가 필요할 때만 끈다.")]
        [SerializeField] private bool _forceDummyInEditor = true;

        [Tooltip("LevelPlay의 appKey / AppLovin MAX의 sdkKey. AdMob은 비워둔다.")]
        [SerializeField] private AdUnitId _appKey;

        [Header("Ad Units")]
        [SerializeField] private AdUnitId _bannerUnitId;
        [SerializeField] private AdUnitId _interstitialUnitId;
        [SerializeField] private AdUnitId _rewardedUnitId;

        [Header("Banner")]
        [SerializeField] private BannerPosition _bannerPosition = BannerPosition.Bottom;
        [SerializeField] private BannerSize _bannerSize = BannerSize.Adaptive;
        [SerializeField] private bool _useAdaptiveBanner = true;

        [Header("Policy")]
        [Tooltip("초기화 직후 전면/보상 광고를 미리 로드한다.")]
        [SerializeField] private bool _autoLoadOnInitialize = true;

        [SerializeField] private int _maxRetryAttempts = 5;
        [SerializeField] private float _retryBaseSeconds = 2f;
        [SerializeField] private float _maxRetryDelaySeconds = 64f;

        [Tooltip("닫힘 이벤트 후 늦게 오는 보상 이벤트를 기다릴 프레임 수. " +
                 "0으로 두면 닫힘이 보상보다 먼저 오는 네트워크에서 보상을 잃는다.")]
        [SerializeField] private int _rewardGraceFrames = 1;

        [Header("Debug")]
        [SerializeField] private bool _verboseLogging;
        [SerializeField] private bool _testMode;
        [SerializeField] private List<string> _testDeviceIds = new();

        [Header("Dummy Provider")]
        [SerializeField] private DummyAdOptions _dummyOptions = DummyAdOptions.Default;

        public AdProviderType Provider => _provider;
        public bool ForceDummyInEditor => _forceDummyInEditor;
        public DummyAdOptions DummyOptions => _dummyOptions;

        public AdServiceOptions ToOptions()
        {
            return new AdServiceOptions(
                banner: _bannerUnitId,
                interstitial: _interstitialUnitId,
                rewarded: _rewardedUnitId,
                bannerOptions: new BannerOptions(_bannerPosition, _bannerSize, _useAdaptiveBanner),
                providerContext: new AdProviderContext(_appKey.Current, _verboseLogging, _testMode, _testDeviceIds),
                retryPolicy: new AdRetryPolicy(_maxRetryAttempts, _retryBaseSeconds, _maxRetryDelaySeconds),
                rewardGraceFrames: _rewardGraceFrames,
                autoLoadOnInitialize: _autoLoadOnInitialize);
        }
    }
}
```

- [ ] **Step 7: 설정 변환 테스트를 추가한다**

`AdProviderFactoryTest.cs`에 추가:

```csharp
    [Test]
    public void 설정은_인스펙터_값을_그대로_서비스_옵션으로_옮긴다()
    {
        var settings = UnityEngine.ScriptableObject.CreateInstance<AdServiceSettings>();

        var options = settings.ToOptions();

        // 기본값이 스펙과 일치하는지 확인한다. 여기가 어긋나면 재시도 동작이 조용히 달라진다.
        Assert.AreEqual(5, options.RetryPolicy.MaxAttempts);
        Assert.AreEqual(2f, options.RetryPolicy.BaseSeconds, 0.001f);
        Assert.AreEqual(64f, options.RetryPolicy.MaxDelaySeconds, 0.001f);
        Assert.AreEqual(1, options.RewardGraceFrames);
        Assert.IsTrue(options.AutoLoadOnInitialize);
        Assert.AreEqual(BannerPosition.Bottom, options.BannerOptions.Position);

        UnityEngine.ScriptableObject.DestroyImmediate(settings);
    }
```

- [ ] **Step 8: 통과를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="AdProviderFactoryTest")`
Expected: PASS 4건

- [ ] **Step 9: DI 등록을 작성한다**

`AdServiceRegistration.cs`:

```csharp
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class AdServiceRegistration
    {
        // 루트 LifetimeScope의 Configure에서 호출한다.
        //   builder.RegisterAdService(_adServiceSettings);
        public static IContainerBuilder RegisterAdService(this IContainerBuilder builder,
                                                          AdServiceSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[AdService] AdServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterInstance(settings);
            builder.Register<IAdRemovalStorage, PlayerPrefsAdRemovalStorage>(Lifetime.Singleton);
            builder.Register<IAdDispatcher, UnityAdDispatcher>(Lifetime.Singleton);
            builder.Register<IAdProviderFactory, AdProviderFactory>(Lifetime.Singleton);

            builder.Register<IAdService>(container =>
            {
                var factory = container.Resolve<IAdProviderFactory>();
                var dispatcher = container.Resolve<IAdDispatcher>();
                var storage = container.Resolve<IAdRemovalStorage>();

                var forceDummy = settings.ForceDummyInEditor && Application.isEditor;
                var provider = factory.Create(settings.Provider, settings.DummyOptions, forceDummy);

                return new AdService(provider, dispatcher, settings.ToOptions(), storage);
            }, Lifetime.Singleton);

            return builder;
        }
    }
}
```

> `IAdService`를 팩토리 델리게이트로 등록하는 이유: `AdService`의 생성자가 `IAdProvider`를 요구하는데, 어떤 provider를 만들지는 설정과 심볼을 봐야 정해진다. 컨테이너에 `IAdProvider`를 따로 등록하면 `forceDummy` 계산이 두 곳으로 흩어진다.

- [ ] **Step 10: 컴파일과 전체 테스트를 확인한다**

`read_console`로 컴파일 에러가 없는지 확인한다.

Run: `run_tests(mode="EditMode")` (전체)
Expected: 전부 PASS

- [ ] **Step 11: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Settings/ \
        Assets/FoundationDI/Runtime/Services/AdService/AdServiceRegistration.cs* \
        Assets/FoundationDI/Tests/AdProviderFactoryTest.cs
git commit -m "[BEHAVIORAL] AdService 설정 에셋과 DI 등록 추가"
```

---

### Task 13: 실기 스모크 확인과 문서화

여기까지 전부 EditMode 테스트였다. 실제 씬에서 한 번은 돌려봐야 한다 — Canvas 생성, `AdServiceRunner` 펌프, `Awaitable` 완료가 실기에서 동작하는지는 EditMode가 증명하지 못한다.

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/AdService/README.md`
- Modify: `plan.md`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: Task 1~12 전부
- Produces: 없음 (문서와 검증)

- [ ] **Step 1: 설정 에셋을 만들고 씬에 붙인다**

Unity 에디터에서:
1. `Assets/Settings/` (없으면 생성)에 우클릭 → `Create > FoundationDI > Ad Service Settings` → 이름 `AdServiceSettings`
2. `Dummy Provider` 섹션에서 `Load Delay Seconds = 1`, `Failure Rate = 0.5`, `Ad Duration Seconds = 3`, `Banner Height = 100` 으로 설정한다. **실패율을 0.5로 두는 것이 핵심이다** — 재시도와 백오프가 실제로 도는 것을 로그로 확인해야 한다.
3. `Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs`의 `Configure`에 `builder.RegisterAdService(_adServiceSettings);` 를 추가하고 `[SerializeField] private AdServiceSettings _adServiceSettings;` 필드를 만든다.
4. `RootLifetimeScope.prefab`의 인스펙터에 위에서 만든 에셋을 연결한다.

- [ ] **Step 2: 스모크 확인용 임시 컴포넌트를 작성한다**

`Assets/Scripts/AdServiceSmokeTest.cs` (호스트 프로젝트 전용 — 패키지에 넣지 않는다):

```csharp
using DarkNaku.FoundationDI;
using UnityEngine;
using VContainer;

// 스모크 확인용 임시 컴포넌트. 확인이 끝나면 지운다.
public class AdServiceSmokeTest : MonoBehaviour
{
    [Inject] private IAdService _ads;

    private async void Start()
    {
        _ads.Paid += imp => Debug.Log(
            $"[Smoke] 임프레션: platform={imp.AdPlatform} source={imp.NetworkName} " +
            $"format={imp.Format} value={imp.Revenue:F4} {imp.Currency}");
        _ads.Loaded += f => Debug.Log($"[Smoke] 로드됨: {f}");
        _ads.Closed += f => Debug.Log($"[Smoke] 닫힘: {f}");

        var ok = await _ads.InitializeAsync();
        Debug.Log($"[Smoke] 초기화: {ok}");

        _ads.Banner.HeightChanged += h => Debug.Log($"[Smoke] 배너 높이: {h}");
        _ads.Banner.Show();
    }

    private void OnGUI()
    {
        if (GUI.Button(new Rect(20, 20, 260, 60), "전면 표시")) ShowInterstitial();
        if (GUI.Button(new Rect(20, 100, 260, 60), "보상 표시")) ShowRewarded();
        if (GUI.Button(new Rect(20, 180, 260, 60), $"광고제거: {_ads.AdsRemoved}"))
            _ads.AdsRemoved = !_ads.AdsRemoved;
    }

    private async void ShowInterstitial()
    {
        var result = await _ads.Interstitial.ShowAsync("smoke");
        Debug.Log($"[Smoke] 전면 결과: {result.Outcome}");
    }

    private async void ShowRewarded()
    {
        var result = await _ads.Rewarded.ShowAsync("smoke");
        Debug.Log($"[Smoke] 보상 결과: {result.Outcome} amount={result.Reward.Amount}");
    }
}
```

- [ ] **Step 3: 플레이해서 여섯 가지를 눈으로 확인한다**

씬에 빈 GameObject를 만들고 `AdServiceSmokeTest`를 붙인 뒤 플레이한다. 다음을 **하나씩 확인**한다:

1. 배너가 화면 하단에 파란 막대로 뜨고 `[Smoke] 배너 높이: 100` 로그가 찍힌다
2. 실패율 0.5 때문에 `no fill` 실패가 나고, `2초 → 4초 → 8초` 간격으로 재시도 로그가 찍힌다 (`Failure Rate`를 잠시 1로 올리면 확실히 보인다)
3. "전면 표시"를 누르면 검은 패널 + 카운트다운이 뜨고, 3초 후 카운트다운이 끝나면 우상단에 닫기
   버튼(X)이 나타난다 — **자동으로는 안 닫힌다.** 그 버튼을 눌러야 `전면 결과: Shown`이 찍힌다
4. "보상 표시"를 눌러 끝까지 두면 `보상 결과: Rewarded amount=1`, 우상단 X로 즉시 닫으면 `보상 결과: Dismissed`
5. "광고제거"를 켜면 배너가 사라지고 `배너 높이: 0`, 전면은 `전면 결과: Blocked`, **보상은 여전히 정상 표시**된다
6. 광고를 표시할 때마다 `[Smoke] 임프레션: platform=Dummy source=DummyNetwork ... USD` 로그가 찍힌다

하나라도 안 되면 해당 Task로 돌아가 고친다. 특히 3~4번이 멈춘다면 `AdServiceRunner`가 생성되지 않았거나 `Pump`가 안 도는 것이다 — 하이어라키에서 `[AdService] Runner`가 보이는지 확인한다 (`HideFlags.HideAndDontSave`라 안 보이면 `hideFlags`를 잠시 `None`으로 바꿔 확인).

- [ ] **Step 4: 스모크 컴포넌트를 정리한다**

확인이 끝나면 `Assets/Scripts/AdServiceSmokeTest.cs`와 씬의 GameObject를 삭제한다. `RootLifetimeScope`의 `RegisterAdService` 호출과 설정 에셋은 남긴다.

- [ ] **Step 5: README를 작성한다**

`Assets/FoundationDI/Runtime/Services/AdService/README.md`. 다른 서비스 README(`SoundService/README.md`, `ResourceService/README.md`)의 구성을 따른다. 다음을 반드시 담는다:

- **개요**: 세 미디에이션 SDK를 하나의 API로 다루는 서비스라는 것
- **빠른 시작**: `builder.RegisterAdService(settings)` → `await _ads.InitializeAsync()` → `await _ads.Rewarded.ShowAsync("placement")`
- **`AdShowOutcome` 6종의 의미와 `IsRewarded`/`WasShown`을 언제 쓰는지**
- **`AdsRemoved`의 포맷별 동작**: 전면·배너는 차단, 보상은 계속 동작. IAP는 범위 밖이고 게임 코드가 값을 넣는다는 것
- **수익 추적**: `Paid` 이벤트 구독 예시와 Firebase `ad_impression` 파라미터 매핑. **`Currency`를 무시하고 USD로 가정하면 AdMob에서 틀린다**는 경고. 그리고 **`Placement`는 어댑터가 아니라 정책 계층이 채운다** — 어댑터는 `ShowAsync`에 넘어온 배치명을 알 수 없으므로 `FullScreenAdUnit.OnPaid`가 `AdImpression.WithPlacement`로 스탬프한다. 배너는 호출 배치가 없으므로 null이 정상
- **3사 어댑터를 추가하는 방법**: `IAdProvider`/`IFullScreenAdapter`/`IBannerAdapter` 구현 + `AdProviderFactory.IsAvailable`/`Create`에 분기 추가 + 스크립팅 심볼(`FOUNDATIONDI_ADMOB` 등) 정의. spec의 매핑표를 참조하라고 링크
- **알려진 범위 밖**: 3사 실제 어댑터, IAP, 전면 쿨다운 게이트, AppOpen/MREC/Native, 리모트 컨피그

- [ ] **Step 6: CLAUDE.md에 서비스 항목을 추가한다**

`CLAUDE.md`의 "핵심 서비스" 목록에 다른 서비스와 같은 밀도로 `AdService` 항목을 추가한다. 최소한 다음을 담는다: 위치, 3계층 구조, `Awaitable` 기반 `ShowAsync`, `AdsRemoved` 포맷별 게이트, 현재 Dummy provider만 구현됨, 상세는 README 링크.

- [ ] **Step 7: plan.md를 갱신한다**

`plan.md`의 "활성 계획: 없음"을 지우고 완료 항목으로 기록한다. 형식은 기존 완료 섹션(SoundService/UIManager)을 따른다:

```markdown
## 완료: ADService — 광고 네트워크 중립 서비스

세부: `docs/superpowers/specs/2026-08-20-adservice-design.md`

- [x] 재시도 정책이 지수 백오프와 상한을 계산한다
- [x] 로드 실패 시 지수 백오프로 재시도하고 한도를 넘으면 중단한다
- [x] ShowAsync가 광고제거·중복호출·미준비를 구분해 즉시 반환한다
- [x] 보상을 래치하고 닫힘에서 유예 프레임 후 확정한다
- [x] 닫힘이 보상보다 먼저 와도 보상을 잃지 않는다
- [x] 광고가 닫히거나 표시에 실패하면 다음 광고를 자동 로드한다
- [x] 배너가 숨김/파괴/재부착과 높이 중계를 처리한다
- [x] 광고제거 상태가 전면·배너를 차단하고 보상은 통과시키며 영속화된다
- [x] AdService가 어댑터와 provider 전역 임프레션을 하나의 Paid로 합류시킨다
- [x] UnityAdDispatcher가 메인스레드 마샬링·지연·프레임 대기를 제공한다
- [x] Dummy provider가 지연·실패·보상·임프레션을 시뮬레이션한다
- [x] 설정과 스크립팅 심볼로 provider를 고르고 없으면 Dummy로 폴백한다

**후속 예정**: AdMob/LevelPlay/AppLovin 실제 어댑터 (spec의 3사 매핑표 참조)
```

- [ ] **Step 8: 전체 테스트를 돌리고 커밋한다**

Run: `run_tests(mode="EditMode")` (전체)
Expected: 전부 PASS

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/README.md CLAUDE.md plan.md \
        Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs
git commit -m "[BEHAVIORAL] ADService README와 프로젝트 문서 갱신"
```

- [ ] **Step 9: 브랜치를 정리한다**

```bash
git log --oneline master..feature/ad-service
```

커밋이 `[STRUCTURAL]`과 `[BEHAVIORAL]`로 올바르게 분류되어 있고 둘이 섞인 커밋이 없는지 확인한다. 그 뒤 master 병합 여부는 사용자에게 묻는다.

---

## 후속 작업 (이 계획의 범위 밖)

3사 실제 어댑터는 각각 별도 계획으로 진행한다. 각 어댑터가 붙는 순서는 다음과 같다:

1. SDK를 `Packages/manifest.json` 또는 `.unitypackage`로 설치
2. `Player Settings > Scripting Define Symbols`에 `FOUNDATIONDI_ADMOB` 등을 정의
3. `Providers/AdMob/` 아래에 `AdMobProvider`/`AdMobFullScreenAdapter`/`AdMobBannerAdapter` 작성 — **spec의 3사 매핑표와 대조하며** 작성하고, 필드명이 어긋나면 표를 갱신한다
4. `AdProviderFactory.IsAvailable`/`Create`에 분기 추가
5. `Consent/` 에 `UmpAdConsent` 등 provider별 동의 구현 추가

정책 계층은 손대지 않는다. 어댑터를 추가하면서 `FullScreenAdUnit`을 수정해야 한다면, 그건 seam 설계가 잘못됐다는 신호이므로 멈추고 재검토한다.
