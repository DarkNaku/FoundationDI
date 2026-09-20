# ATT 추적 동의 전파 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ATT 응답(Denied/Restricted)을 광고 provider까지 전달해 광고 SDK가 비개인화 모드로 초기화되게 하고, 앱 런치 시점 AdMob 자동 수집을 Info.plist로 막는다.

**Architecture:** `AdService`가 `_tracking.Status`를 읽어 `AdProviderContext`에 실어 `IAdProvider.InitializeAsync`로 넘긴다. provider는 그 값을 자기 SDK의 프라이버시 세터로 번역한다 — SDK Init 이전에 불려야 하는 제약이 "context를 받는 순간"으로 자연히 고정된다. `IAdConsent`(CMP 축)는 건드리지 않는다.

**Tech Stack:** Unity 6000.3.17f1, Unity `Awaitable`, NUnit + NSubstitute 5.3.0 (EditMode), LevelPlay(`com.unity.services.levelplay`) 9.5.1, `com.unity.ads.ios-support`

**Spec:** `docs/superpowers/specs/2026-09-21-att-tracking-propagation-design.md`

## Global Constraints

- **테스트 실행은 UnityMCP의 `run_tests`**(EditMode)로 한다. 테스트는 `Assets/FoundationDI/Tests/`의 `FoundationDI.Tests`(EditMode) asmdef에 있다.
- **async 테스트는 `AwaitableTest.Run(async () => {...})`을 쓴다.** EditMode에서 `Awaitable.NextFrameAsync()`/`WaitForSecondsAsync()`는 영원히 완료되지 않으므로 이 헬퍼를 우회하면 테스트가 멈춘다.
- 테스트 이름은 기존 파일들과 같이 **한글 메서드명**으로 쓴다.
- **주석은 "왜"를 쓴다.** "무엇"은 코드가 말한다. 기존 파일들의 주석 밀도와 어조를 맞춘다.
- **`IAdConsent` / `NoopAdConsent`는 수정하지 않는다.** CMP 축은 이번 작업 범위가 아니다(스펙 D4).
- **동의를 긍정하는 호출(`SetGDPRConsent(true)` 등)은 절대 넣지 않는다.** 제한 방향으로만 통보한다(스펙 D3).
- **이 프로젝트에는 LevelPlay·AppLovin·Firebase SDK가 설치돼 있지 않다.** 루트의 `MaxSdk.*.csproj` 등은 예전 상태에서 남은 껍데기다. `FOUNDATIONDI_LEVELPLAY`가 꺼져 있어 Task 2의 파일은 **이 프로젝트에서 컴파일되지 않는다** — 검증 방법은 Task 2에 적어 뒀다.
- 커밋 메시지는 저장소 관례대로 `[BEHAVIORAL]` / `[STRUCTURAL]` 접두어를 쓴다.

---

### Task 1: `AdProviderContext`에 추적 상태를 싣고 `AdService`가 전파한다

리젝의 근본 원인이다. 현재 `AdService`는 ATT 응답을 `Debug.Log`로만 소비하고(`AdService.cs:168`), provider에게 전달할 통로 자체가 없다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/AdService/Providers/IAdProvider.cs:25-41` (`AdProviderContext`)
- Modify: `Assets/FoundationDI/Runtime/Services/AdService/AdService.cs:118-135` (`InitializeAsync`)
- Test: `Assets/FoundationDI/Tests/AdTrackingAuthorizationTest.cs` (기존 파일에 추가)

**Interfaces:**
- Consumes: 없음 (첫 작업)
- Produces:
  - `AdProviderContext.TrackingStatus` → `TrackingAuthorizationStatus`
  - `AdProviderContext.IsTrackingRestricted` → `bool`
  - `AdProviderContext.WithTrackingStatus(TrackingAuthorizationStatus status)` → `AdProviderContext`
  - `AdProviderContext` 생성자 5번째 인자 `TrackingAuthorizationStatus trackingStatus = TrackingAuthorizationStatus.NotDetermined`

- [x] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/AdTrackingAuthorizationTest.cs` 맨 끝 `}` 직전에 아래를 추가한다.
`FakeTrackingAuthorization` / `FakeRemovalStorage` / `NewOptions()`는 같은 파일 위쪽에 이미 있다.

```csharp
    [Test]
    public void 거부와_제한만_추적_제한으로_본다()
    {
        var context = new AdProviderContext("app-key", false, false, new List<string>());

        Assert.IsFalse(context.WithTrackingStatus(TrackingAuthorizationStatus.Authorized).IsTrackingRestricted);
        Assert.IsTrue(context.WithTrackingStatus(TrackingAuthorizationStatus.Denied).IsTrackingRestricted);
        Assert.IsTrue(context.WithTrackingStatus(TrackingAuthorizationStatus.Restricted).IsTrackingRestricted);
    }

    [Test]
    public void 묻지_않은_상태는_제한으로_보지_않는다()
    {
        var context = new AdProviderContext("app-key", false, false, new List<string>());

        // NotDetermined는 "아직 안 물었다"와 "ATT가 없는 플랫폼"을 함께 뜻한다.
        // 제한으로 보면 안드로이드 광고가 통째로 비개인화로 떨어진다.
        Assert.AreEqual(TrackingAuthorizationStatus.NotDetermined, context.TrackingStatus);
        Assert.IsFalse(context.IsTrackingRestricted);
    }

    [Test]
    public void 추적_상태를_바꿔도_나머지_설정은_그대로다()
    {
        var ids = new List<string> { "device-a" };
        var context = new AdProviderContext("app-key", true, true, ids)
            .WithTrackingStatus(TrackingAuthorizationStatus.Denied);

        Assert.AreEqual("app-key", context.AppKey);
        Assert.IsTrue(context.VerboseLogging);
        Assert.IsTrue(context.TestMode);
        Assert.AreSame(ids, context.TestDeviceIds);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 추적을_거부하면_provider가_거부됨으로_받는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var tracking = new FakeTrackingAuthorization();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(),
                                new FakeRemovalStorage(), tracking);

        var initializing = sut.InitializeAsync();

        tracking.Respond(TrackingAuthorizationStatus.Denied);

        await initializing;

        Assert.AreEqual(TrackingAuthorizationStatus.Denied, provider.ReceivedContext.TrackingStatus);
        Assert.IsTrue(provider.ReceivedContext.IsTrackingRestricted,
                      "거부를 provider가 모르면 SDK는 동의받은 것으로 간주하고 계속 추적한다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 추적을_허용하면_provider가_허용됨으로_받는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var tracking = new FakeTrackingAuthorization();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(),
                                new FakeRemovalStorage(), tracking);

        var initializing = sut.InitializeAsync();

        tracking.Respond(TrackingAuthorizationStatus.Authorized);

        await initializing;

        Assert.AreEqual(TrackingAuthorizationStatus.Authorized, provider.ReceivedContext.TrackingStatus);
        Assert.IsFalse(provider.ReceivedContext.IsTrackingRestricted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 권한을_묻지_않기로_했어도_현재_상태는_전달한다() => AwaitableTest.Run(async () =>
    {
        // 게임이 직접 묻는 모드(RequestTrackingAuthorization = false). AdService는 다이얼로그를
        // 띄우지 않지만, 게임이 이미 받아 둔 답이 있으면 그것을 provider에 실어야 한다.
        var provider = new FakeAdProvider();
        var tracking = new FakeTrackingAuthorization
        {
            Status = TrackingAuthorizationStatus.Denied,
        };
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(false),
                                new FakeRemovalStorage(), tracking);

        await sut.InitializeAsync();

        Assert.AreEqual(0, tracking.RequestCount, "묻지 않기로 했는데 다이얼로그를 띄웠다");
        Assert.IsTrue(provider.ReceivedContext.IsTrackingRestricted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 물을_수_없는_플랫폼은_제한으로_보지_않는다() => AwaitableTest.Run(async () =>
    {
        // 안드로이드·에디터 경로. UnsupportedTrackingAuthorization은 항상 NotDetermined다.
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(),
                                new FakeRemovalStorage(), new UnsupportedTrackingAuthorization());

        await sut.InitializeAsync();

        Assert.IsFalse(provider.ReceivedContext.IsTrackingRestricted,
                       "ATT가 없는 플랫폼에서 추적을 끄면 이유 없이 수익이 죽는다");
    });
```

- [x] **Step 2: 테스트가 실패하는지 확인한다**

UnityMCP `run_tests`(EditMode, 필터 `AdTrackingAuthorizationTest`)로 실행한다.
Expected: 컴파일 에러 — `'AdProviderContext' does not contain a definition for 'WithTrackingStatus'`

- [x] **Step 3: `AdProviderContext`를 확장한다**

`Assets/FoundationDI/Runtime/Services/AdService/Providers/IAdProvider.cs`의 `AdProviderContext`를
아래로 바꾼다. 기존 4인자 생성자 호출 5곳(`AdServiceSettings.cs:90`, `AdTrackingAuthorizationTest.cs:56`,
`AdServiceTest.cs:24,313,338`)이 그대로 컴파일되도록 **기본값 있는 마지막 인자**로 붙인다.

```csharp
    // provider가 초기화에 필요로 하는 것만 담는다. AdServiceSettings 전체를 넘기지 않는 이유는
    // 재시도·유예 프레임 같은 정책값이 상위 계층 소관이라 provider가 볼 이유가 없기 때문이다.
    public readonly struct AdProviderContext
    {
        public string AppKey { get; }       // LevelPlay appKey / MAX sdkKey. AdMob은 불필요(null)
        public bool VerboseLogging { get; }
        public bool TestMode { get; }
        public IReadOnlyList<string> TestDeviceIds { get; }

        // ATT 응답. provider가 이것을 자기 SDK의 프라이버시 세터로 번역한다.
        //
        // IAdProvider의 별도 메서드가 아니라 context에 실은 이유는 순서 때문이다. LevelPlay·MAX·
        // AdMob 모두 프라이버시 세터를 SDK Init **이전에** 불러야 효력이 있는데, 별도 메서드로
        // 두면 그 규약이 문서로만 존재해 언제든 깨진다. 초기화 인자로 두면 provider가 이 값을
        // 받는 시점이 곧 Init 직전이라 순서를 틀릴 수가 없다.
        public TrackingAuthorizationStatus TrackingStatus { get; }

        // 추적이 명시적으로 막혔는가. provider는 이 값 하나만 보고 분기한다.
        //
        // NotDetermined가 false인 것이 핵심이다. 그 값은 "iOS인데 아직 안 물었다"와 "ATT가
        // 없는 플랫폼(안드로이드·에디터)"을 함께 뜻하는데, 후자가 압도적으로 흔하다
        // (UnsupportedTrackingAuthorization.Status는 항상 NotDetermined다). 제한으로 해석하면
        // 안드로이드 광고가 통째로 비개인화로 떨어져 수익이 이유 없이 죽는다.
        //
        // iOS에서 "아직 안 물었다"가 provider까지 도달하는 경로는
        // AdServiceOptions.RequestTrackingAuthorization = false뿐이고, 그건 게임이 직접 묻겠다고
        // 선언한 경우라 판단 주체가 게임이다.
        public bool IsTrackingRestricted =>
            TrackingStatus == TrackingAuthorizationStatus.Denied ||
            TrackingStatus == TrackingAuthorizationStatus.Restricted;

        public AdProviderContext(string appKey, bool verboseLogging, bool testMode,
                                 IReadOnlyList<string> testDeviceIds,
                                 TrackingAuthorizationStatus trackingStatus =
                                     TrackingAuthorizationStatus.NotDetermined)
        {
            AppKey = appKey;
            VerboseLogging = verboseLogging;
            TestMode = testMode;
            TestDeviceIds = testDeviceIds;
            TrackingStatus = trackingStatus;
        }

        // 설정에서 만들어진 context는 ATT를 묻기 전에 조립되므로 추적 상태를 모른다.
        // AdService가 응답을 받은 뒤 이 메서드로 채워 provider에 넘긴다.
        public AdProviderContext WithTrackingStatus(TrackingAuthorizationStatus status) =>
            new(AppKey, VerboseLogging, TestMode, TestDeviceIds, status);
    }
```

- [x] **Step 4: `AdService.InitializeAsync`가 상태를 실어 넘기게 한다**

`Assets/FoundationDI/Runtime/Services/AdService/AdService.cs`의 `InitializeAsync` 안,
`await RequestTrackingAuthorizationAsync();` 이후 블록을 아래로 바꾼다.

```csharp
                // provider보다 먼저 묻는다. 광고 SDK는 초기화 시점의 ATT 상태를 보고
                // IDFA 사용 여부와 타게팅 가능 여부를 결정하므로, 뒤에 물으면 그 세션의
                // 첫 요청들이 동의를 받고도 비타게팅으로 나간다.
                await RequestTrackingAuthorizationAsync();

                // 다이얼로그가 떠 있는 동안 씬이 언로드돼 서비스가 정리됐을 수 있다.
                if (_isDisposed) return false;

                // RequestAsync의 반환값이 아니라 Status를 다시 읽는다. 요청을 건너뛴 경로
                // (RequestTrackingAuthorization = false, 이미 답한 상태, 물을 수 없는 플랫폼)
                // 에서도 현재 상태를 provider에 실어야 하기 때문이다 — 반환값을 쓰면 그 세
                // 경로가 전부 NotDetermined로 뭉개진다.
                var context = _options.ProviderContext.WithTrackingStatus(_tracking.Status);

                ok = await _provider.InitializeAsync(context);
```

- [x] **Step 5: 테스트가 통과하는지 확인한다**

UnityMCP `run_tests`(EditMode, 필터 `AdTrackingAuthorizationTest`).
Expected: 새 테스트 7개 PASS, 기존 테스트 전부 PASS.

- [x] **Step 6: AdService 전체 회귀를 확인한다**

UnityMCP `run_tests`(EditMode, 필터 `Ad`)로 `AdServiceTest` / `AdProviderFactoryTest` /
`DummyAdProviderTest` / `AdTestDoublesTest`까지 함께 돌린다.
Expected: 전부 PASS. `DummyAdProvider`는 `IAdProvider` 구현이 바뀌지 않았으므로(생성자 인자만
늘었다) 수정할 것이 없다.

- [x] **Step 7: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Providers/IAdProvider.cs \
        Assets/FoundationDI/Runtime/Services/AdService/AdService.cs \
        Assets/FoundationDI/Tests/AdTrackingAuthorizationTest.cs
git commit -m "[BEHAVIORAL] ATT 응답을 AdProviderContext에 실어 provider로 넘긴다"
```

---

### Task 2: `LevelPlayAdProvider`가 추적 제한을 LevelPlay 프라이버시 API로 번역한다

Task 1이 만든 통로에 실제 소비자를 붙인다. **여기가 리젝을 직접 고치는 지점이다.**

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/AdService/Providers/LevelPlay/LevelPlayAdProvider.cs:55-61` (주석), `:168-196` (`ApplyContext`)
- Test: 없음 — 이유는 Step 1에 적었다

**Interfaces:**
- Consumes: `AdProviderContext.IsTrackingRestricted` (Task 1)
- Produces: 없음 (SDK 경계에서 끝난다)

- [x] **Step 1: 왜 테스트가 없는지 확인하고 넘어간다**

`LevelPlayAdProvider`는 `LevelPlay` / `LevelPlayPrivacySettings` **정적 클래스**를 직접 부른다. 주입할
seam이 없어 단위 테스트로 호출을 관찰할 수 없고, `FoundationDI.Tests` asmdef는 `FoundationDI`만
참조하므로 `FoundationDI.LevelPlay`의 타입을 보지도 못한다. 게다가 이 프로젝트에는 LevelPlay SDK가
설치돼 있지 않아 `FOUNDATIONDI_LEVELPLAY`가 꺼져 있고, asmdef 자체가 통째로 건너뛰어진다.

**검증은 Step 5의 InfinityStep 컴파일 + 스펙 §5의 실기 프록시 캡처로 한다.** 정적 SDK 호출을 테스트
가능하게 만들자고 래퍼 인터페이스를 새로 파는 것은 이 작업의 범위를 넘고, 다른 세 provider와도
패턴이 어긋난다.

- [x] **Step 2: 낡은 주석을 정정한다**

`LevelPlayAdProvider.cs:55-61`의 주석을 아래로 바꾼다. 기존 주석은 "LevelPlay의 동의 플로우는 이번
범위 밖"이라고 선언하는데, 그 판단이 ATT까지 함께 버린 것이 리젝의 원인이다.

```csharp
        // IAdConsent는 여전히 no-op이다. 이 인터페이스가 요구하는 것은 "동의 폼을 띄우고
        // CanRequestAds를 갱신하는" CMP 수준의 계약인데, LevelPlay가 제공하는 것은 이미 받은
        // 동의 값을 SDK에 통보하는 세터뿐이라 폼을 띄우는 주체(UMP/CMP)가 따로 필요하다.
        // 억지로 매핑하면 "RequestAsync를 불렀는데 아무 폼도 안 뜨고 true가 온다"가 된다.
        //
        // **ATT는 이 축이 아니다.** 폼을 OS가 대신 띄워 주므로 세터만 있으면 충분하고,
        // 실제로 ApplyPrivacy가 LevelPlayPrivacySettings로 통보한다. 예전에는 이 주석이
        // "LevelPlay의 동의 플로우는 범위 밖"이라며 두 축을 함께 버렸고, 그 결과 ATT를
        // 거부해도 미디에이션 네트워크들이 동의받은 상태로 돌아 App Store 5.1.1(iv)로
        // 리젝됐다(2026-09-18, InfiniteSteps 1.1.7).
        public IAdConsent Consent { get; } = new NoopAdConsent();
```

- [x] **Step 3: `ApplyPrivacy`를 추가하고 `ApplyContext`에서 부른다**

`ApplyContext`의 첫 줄에 호출을 넣는다. `ApplyContext`는 `LevelPlay.Init` 이전에 모든 진입 경로에서
도는 유일한 지점이다(`InitializeAsync:107`).

```csharp
        private static void ApplyContext(AdProviderContext context)
        {
            // 프라이버시 먼저다. 아래 로그 설정과 달리 이건 Init 이후에 부르면 효력이 없다.
            ApplyPrivacy(context);

            // LevelPlay 자신의 상세 로그는 컴파일 심볼
```

그리고 `ApplyContext` 메서드 바로 아래에 다음을 추가한다.

```csharp
        // ATT 거부를 LevelPlay와 미디에이션 어댑터들에게 통보한다.
        //
        // ATT 거부는 IDFA만 막는 것이 아니다. Apple 기준으로는 쿠키·핑거프린팅을 포함한 모든
        // 크로스-컴퍼니 추적이 금지된다. 통보하지 않으면 네트워크들(AdMob/Vungle/UnityAds)은
        // 동의받은 것으로 간주하고 IDFA의 공백을 자기 웹뷰의 쿠키로 메운다 — 그것이
        // 2026-09-18 App Store 5.1.1(iv) 리젝에서 심사가 캡처한 트래픽이다.
        //
        // 세 호출 모두 LevelPlay.Init 이전이어야 한다. 호출부(ApplyContext)가 그것을 보장한다.
        private static void ApplyPrivacy(AdProviderContext context)
        {
            // 제한 방향으로만 통보한다. 허용일 때 SetGDPRConsent(true)를 부르지 않는 이유는,
            // ATT 허용이 "Apple의 크로스-앱 추적 동의"이지 "GDPR 개인정보 처리 동의"가
            // 아니기 때문이다. 둘은 법적으로 다른 동의이고, 받지 않은 동의를 SDK에 긍정하면
            // 그쪽이 더 큰 문제가 된다. GDPR 동의는 UMP/CMP가 받아 IAdConsent 축으로 흘린다.
            if (!context.IsTrackingRestricted) return;

            // GDPR 동의 거부. 유럽 밖에서도 대부분의 어댑터가 이 플래그를 개인화 스위치로 읽는다.
            LevelPlayPrivacySettings.SetGDPRConsent(false);

            // CCPA는 의미가 뒤집힌 플래그다 — true가 "개인정보 판매를 거부했다"는 뜻이다.
            LevelPlayPrivacySettings.SetCCPA(true);

            // do_not_sell은 CCPA를 지원하는 어댑터로 전파되는 메타데이터다. SetCCPA와 겹쳐
            // 보이지만 어댑터마다 읽는 쪽이 달라 둘 다 넣는다 — 한쪽만 넣었다가 특정
            // 네트워크에서만 추적이 남으면 프록시 캡처 전에는 알 방법이 없다.
            LevelPlay.SetMetaData("do_not_sell", "true");
        }
```

- [x] **Step 4: 파일 안에서 심볼이 해석되는지 눈으로 확인한다**

`LevelPlayAdProvider.cs` 상단 `using`에 `Unity.Services.LevelPlay`가 이미 있다(`:3`).
`LevelPlayPrivacySettings`는 같은 네임스페이스의 `public static class`이므로 추가 using이 필요 없다
(`com.unity.services.levelplay@9.5.1`의 `Runtime/Api/LevelPlayPrivacySettings.cs`).

시그니처 확인:
- `LevelPlayPrivacySettings.SetGDPRConsent(bool consent)`
- `LevelPlayPrivacySettings.SetCCPA(bool value)`
- `LevelPlay.SetMetaData(string key, string value)`

`LevelPlay.SetConsent(bool)`은 `[Obsolete]`다 — `LevelPlayPrivacySettings.SetGDPRConsent`를 쓴다.

- [ ] **Step 5: InfinityStep에서 컴파일을 검증한다**

이 프로젝트에서는 파일이 컴파일되지 않으므로(Step 1) LevelPlay SDK가 설치된 게임 프로젝트에서
확인한다.

```bash
cd ~/Projects/FoundationDI && git rev-parse HEAD
```

`~/Projects/InfinityStep/Packages/packages-lock.json`의 `com.darknaku.foundationdi` 해시를 그 값으로
바꾸고 InfinityStep을 연 뒤 콘솔에 컴파일 에러가 없는지 본다. 커밋 전이라면 InfinityStep의
`Packages/manifest.json`에서 `com.darknaku.foundationdi`를
`file:/Users/chakyounghoon/Projects/FoundationDI/Assets/FoundationDI`로 잠시 바꿔 확인해도 된다
(**확인 후 반드시 되돌린다**).

Expected: 컴파일 에러 없음.

- [x] **Step 6: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AdService/Providers/LevelPlay/LevelPlayAdProvider.cs
git commit -m "[BEHAVIORAL] ATT를 거부하면 LevelPlay에 비개인화를 통보한다"
```

---

### Task 3: `AttIosPostProcess`가 `GADDelayAppMeasurementInit`을 넣는다

런타임 코드가 닿지 않는 구간을 막는다. AdMob(GMA SDK)은 `didFinishLaunchingWithOptions`에서 App
Measurement를 자동 초기화한다 — `AdService.InitializeAsync`가 돌기 전, ATT 다이얼로그가 화면에
떠 있는 동안이다.

**Files:**
- Modify: `Assets/FoundationDI/Editor/AdService/AttIosPostProcess.cs`
- Test: `Assets/FoundationDI/Tests/Editor/AdService/AttIosPostProcessTest.cs` (기존 파일에 추가)

**Interfaces:**
- Consumes: 없음 (Task 1·2와 독립이다 — 순서를 바꿔 진행해도 된다)
- Produces: `AttIosPostProcess.DelayAppMeasurementKey` → `const string` ("GADDelayAppMeasurementInit")

- [x] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/Editor/AdService/AttIosPostProcessTest.cs` 맨 끝 `}` 직전에 추가한다.

```csharp
    [Test]
    public void AdMob_자동_측정_지연_키_이름은_구글이_요구하는_그대로다()
    {
        // 오타가 나도 plist는 조용히 받아들이고 SDK는 그냥 예전처럼 런치 때 초기화한다.
        // 실패가 심사 리젝으로만 드러나는 자리라 문자열을 테스트로 못 박는다.
        Assert.AreEqual("GADDelayAppMeasurementInit", AttIosPostProcess.DelayAppMeasurementKey);
    }
```

- [x] **Step 2: 테스트가 실패하는지 확인한다**

UnityMCP `run_tests`(EditMode, 필터 `AttIosPostProcessTest`).
Expected: 컴파일 에러 — `'AttIosPostProcess' does not contain a definition for 'DelayAppMeasurementKey'`

- [x] **Step 3: 키 상수를 추가한다**

`AttIosPostProcess.cs`의 `UsageDescription` 상수 아래에 넣는다.

```csharp
        // AdMob(GMA SDK)은 이 키가 없으면 didFinishLaunchingWithOptions에서 App Measurement를
        // 자동 초기화한다 — AdService가 ATT를 묻기도 전, 다이얼로그가 떠 있는 동안이다.
        // 런타임 코드가 닿을 수 없는 구간이라 plist로만 막힌다. true면 앱이
        // GADMobileAds.start를 부를 때까지 미뤄지고, 그 시점은 ATT 응답 이후다.
        internal const string DelayAppMeasurementKey = "GADDelayAppMeasurementInit";
```

- [x] **Step 4: `WriteUsageDescription`에서 키를 쓴다**

`plist.WriteToFile(plistPath);` 앞에 한 줄을 넣는다.

```csharp
            plist.root.SetString(UsageDescriptionKey, Resolve(existing));

            // 값이 이미 있어도 덮는다. UsageDescription과 달리 이건 프로젝트가 고를 수 있는
            // 문구가 아니라 켜져 있어야만 하는 스위치다 — false로 남아 있으면 리젝 사유가 된다.
            plist.root.SetBoolean(DelayAppMeasurementKey, true);

            plist.WriteToFile(plistPath);
```

- [x] **Step 5: 테스트가 통과하는지 확인한다**

UnityMCP `run_tests`(EditMode, 필터 `AttIosPostProcessTest`).
Expected: 기존 4개 + 새 1개 전부 PASS.

- [ ] **Step 6: 실제 plist에 들어가는지 확인한다**

`PlistDocument` 쓰기 경로는 `UNITY_IOS && FOUNDATIONDI_IOSSUPPORT` 안이라 테스트가 닿지 않는다.
InfinityStep에서 iOS로 빌드한 뒤 확인한다.

```bash
/usr/libexec/PlistBuddy -c "Print :GADDelayAppMeasurementInit" <빌드경로>/Info.plist
```

Expected: `true`

- [x] **Step 7: 커밋**

```bash
git add Assets/FoundationDI/Editor/AdService/AttIosPostProcess.cs \
        Assets/FoundationDI/Tests/Editor/AdService/AttIosPostProcessTest.cs
git commit -m "[BEHAVIORAL] AdMob 자동 측정을 ATT 응답 뒤로 미룬다"
```

---

### Task 4: ATT에 대한 잘못된 서술을 정정한다

`AnalyticsService` README §5가 "ATT 거부는 OS가 IDFA를 막는 것뿐이니 이 서비스가 할 일이 없다"고
단언한다. 이 문장이 Task 2가 고친 판단(ATT를 `IAdConsent`와 함께 버린 것)을 뒷받침하는 근거로
쓰였을 가능성이 높다. 코드만 고치고 문서를 두면 다음 사람이 같은 결론에 다시 도달한다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/AnalyticsService/README.md` (§5 "ATT와 GDPR형 동의는 다르다")
- Modify: `CLAUDE.md` (AdService 항목의 iOS ATT 단락)

**Interfaces:**
- Consumes: Task 1·2의 결과 (문서가 그 동작을 서술한다)
- Produces: 없음

- [x] **Step 1: README §5의 ATT 항목을 바꾼다**

`Runtime/Services/AnalyticsService/README.md`에서 아래 문단을 찾는다.

```markdown
- **ATT(iOS 추적 동의)** — 거부하면 **OS가 IDFA를 막습니다.** 앱이 아무것도 하지 않아도 그 정보는
  얻어지지 않습니다. SDK는 그대로 돌고 이벤트도 그대로 나가며, 광고 식별자만 비어 옵니다.
  이 서비스가 할 일이 없습니다.
```

아래로 바꾼다.

```markdown
- **ATT(iOS 추적 동의)** — 거부하면 OS가 IDFA를 막습니다. **하지만 그것으로 끝이 아닙니다.**
  Apple 기준에서 ATT 거부는 IDFA 차단이 아니라 **쿠키·핑거프린팅을 포함한 모든 크로스-컴퍼니
  추적 금지**입니다. "SDK는 그대로 돌아도 된다"고 읽으면 리젝됩니다 — 실제로 그렇게 읽고
  광고 SDK에 아무것도 통보하지 않았다가 App Store 5.1.1(iv)로 리젝된 적이 있습니다
  (2026-09-18, InfiniteSteps 1.1.7). 광고 쪽은 `AdService`가 `AdProviderContext.TrackingStatus`로
  provider까지 전달해 SDK에 비개인화를 통보합니다.

  **분석 쪽은 게임이 판단합니다.** `IAnalyticsService`는 ATT를 보지 않습니다 — 수집을 ATT에
  묶을지는 사용하는 SDK와 수집 항목에 달린 법적 판단이고, 이 서비스가 대신 내릴 수 없습니다.
  묶기로 했다면 게임이 ATT 응답을 받은 뒤 `CollectionEnabled`를 세팅하면 됩니다.

  > **iOS에서 하나 더**: Firebase Analytics는 `AnalyticsService.InitializeAsync`가 돌기 전,
  > 앱 런치 시점에 이미 수집을 시작합니다. `CollectionEnabledByDefault = false`로 출시한다면
  > Info.plist에 `FIREBASE_ANALYTICS_COLLECTION_ENABLED = NO`도 함께 넣어야 그 구간이 막힙니다.
  > 이 키는 패키지가 넣지 않습니다 — `AnalyticsServiceSettings`가 DI로 주입되는 에셋이라
  > 에디터 후처리가 "이 프로젝트의 설정"을 특정할 수 없기 때문입니다. 게임의 빌드 후처리에서
  > 넣으세요.
```

- [x] **Step 2: `CLAUDE.md`의 ATT 단락을 갱신한다**

`CLAUDE.md`에서 `**iOS ATT 동의는 \`InitializeAsync\`가 provider 초기화 앞에서 한 번 요청한다.**`로
시작하는 단락을 찾는다. 그 단락 **끝에** 아래 두 문장을 덧붙인다(기존 내용은 그대로 둔다).

```markdown
**응답은 `AdProviderContext.TrackingStatus`/`IsTrackingRestricted`로 provider까지 흘러간다** — `AdService`가 `_tracking.Status`를 읽어 `WithTrackingStatus`로 실어 넘기고, `LevelPlayAdProvider.ApplyPrivacy`가 `SetGDPRConsent(false)`/`SetCCPA(true)`/`SetMetaData("do_not_sell","true")`로 번역한다(`LevelPlay.Init` 이전이어야 해서 `ApplyContext` 첫 줄에 있다). 제한 방향으로만 통보하고 허용일 때 동의를 긍정하지는 않는다 — ATT 허용은 GDPR 동의가 아니다. `NotDetermined`를 제한으로 보지 않는 것이 중요하다(안드로이드가 항상 그 값이다). **이걸 하지 않으면 ATT를 거부해도 미디에이션 네트워크들이 쿠키로 추적을 이어가 App Store 5.1.1(iv)로 리젝된다** — 실제로 겪었다(2026-09-18). `AttIosPostProcess`가 `GADDelayAppMeasurementInit = true`도 함께 넣는다. 없으면 AdMob이 앱 런치 시점(ATT 다이얼로그가 떠 있는 동안)에 App Measurement를 자동 초기화한다.
```

- [x] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AnalyticsService/README.md CLAUDE.md
git commit -m "[STRUCTURAL] ATT 거부의 의미를 문서에서 정정한다"
```

---

## 완료 후

> **진행 상태 (2026-09-21):** Task 1~4의 코드·문서 변경은 전부 적용됐고 EditMode 627개가 통과한다
> (브랜치 `feature/att-tracking-propagation`, 패키지 0.10.9). **남은 것은 이 프로젝트에서 할 수 없는
> 검증 두 가지뿐이다** — Task 2 Step 5(InfinityStep 컴파일)와 Task 3 Step 6(빌드 plist 확인), 그리고
> 아래 실기 프록시 캡처다. LevelPlay API 시그니처는 `com.unity.services.levelplay@9.5.1` 소스로
> 확인했으나(`SetGDPRConsent(bool)` / `SetCCPA(bool)` / `SetMetaData(string,string)`,
> 네임스페이스 `Unity.Services.LevelPlay`), `FOUNDATIONDI_LEVELPLAY`가 꺼져 있어 실제 컴파일은
> 거치지 않았다.


### 실기 검증 (필수)

단위 테스트는 "통로가 뚫렸는지"만 본다. **리젝이 실제로 고쳐졌는지는 프록시 캡처로만 확인된다.**
스펙 `docs/superpowers/specs/2026-09-21-att-tracking-propagation-design.md` §5의 절차를 따른다.
요약하면: 기존 설치본 삭제 → 실기 설치 → Proxyman/Charles → "Ask App Not to Track" →
`googleads.g.doubleclick.net` / `pagead2.googlesyndication.com` / Vungle / ironSource 엔드포인트에
쿠키가 없는지 확인 → "Allow"로 반복해 광고가 정상 노출되는지(과잉 차단 회귀) 확인.

### 패키지 버전

`Assets/FoundationDI/package.json`의 버전과 `README.md` 배지를 올린다. 저장소 관례상 별도
`[STRUCTURAL]` 커밋이다(예: `de38595`, `a0b3691`).

### 후속 과제 (이 계획서 범위 밖)

- **`AppLovinAdProvider` 대응** — 같은 구멍이 있지만(`AppLovinAdProvider.cs:32`) MAX SDK가 두
  프로젝트 어디에도 설치돼 있지 않아 컴파일 검증이 불가능하다. MAX를 쓰는 프로젝트가 생기면
  Task 2와 같은 패턴으로 처리한다.
- **UMP/CMP 도입** — `IAdConsent`의 실제 구현. ATT와는 다른 축이다.
- **InfinityStep(게임) 쪽** — `Firebase/Analytics` → `FirebaseAnalyticsWithoutAdIdSupport` 팟 교체,
  `AnalyticsServiceSettings.asset`의 `_collectionEnabledByDefault`, `GameManager.cs:31,35`의 초기화
  순서, Info.plist `FIREBASE_ANALYTICS_COLLECTION_ENABLED`.
- **App Store Connect Support URL**(Guideline 1.5) — 같은 리젝 메일의 두 번째 지적. 코드와 무관하다.
