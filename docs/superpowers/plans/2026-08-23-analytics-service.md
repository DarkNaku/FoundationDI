# AnalyticsService Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Firebase Analytics를 기본으로 하되 MMP(AppsFlyer/Adjust/Singular/Airbridge)를 추가해도 게임 코드는 `IAnalyticsService` API를 한 번만 호출하면 등록된 모든 provider로 브로드캐스트되는 서비스를 만든다.

**Architecture:** 3계층 — `Providers/`(SDK seam, `IAnalyticsProvider`) → `AnalyticsService`(팬아웃·버퍼·예외격리·수집게이트 정책) → `IAnalyticsService`(공개 계약). `AdService`가 확립한 옵셔널 asmdef + `[RuntimeInitializeOnLoadMethod]` Registry 패턴을 그대로 따르되, provider가 하나가 아니라 `[Flags]`로 동시에 여럿이라는 점만 다르다.

**Tech Stack:** Unity 6000.3.17f1, VContainer, Unity `Awaitable`, NUnit + NSubstitute 5.3.0 (EditMode), Firebase Analytics Unity SDK 13.15.0

**Spec:** `docs/superpowers/specs/2026-08-23-analyticsservice-design.md`

## Global Constraints

- 네임스페이스는 `DarkNaku.FoundationDI` 단일.
- 런타임 코드는 전부 `Assets/FoundationDI/Runtime/Services/AnalyticsService/` 아래. `FoundationDI` asmdef 소속(Firebase 폴더만 예외).
- 신규 프로덕션 async는 `UniTask`가 아니라 **`Awaitable`**로 작성한다.
- 테스트 함수 이름은 **한국어 의도의 `should~` 형식**, `Assets/FoundationDI/Tests/`의 `FoundationDI.Tests`(EditMode) asmdef.
- **STRUCTURAL 커밋과 BEHAVIORAL 커밋을 절대 섞지 않는다.** 제목에 `[STRUCTURAL]` / `[BEHAVIORAL]` 접두어.
- 한 번에 테스트 하나. 매번 전체 EditMode 테스트를 돌린다.
- 컴파일·테스트는 UnityMCP(`read_console` → `run_tests`)로만 수행한다. Unity Editor가 떠 있어야 한다.
- 메인 스레드 단독 접근 전제. 잠금 없음. SDK 콜백 마샬링은 어댑터 책임.
- **Unity6 `Awaitable`은 단일 사용**이다 — `await` 이후 `.IsCompleted`에 접근하지 않는다. 테스트는 `await` 전에 단언한다.
- 테스트 파일은 부분 수정이 아니라 **Write로 통째로** 쓴다(UnityMCP 편집 제약).

---

## File Structure

| 경로 | 책임 |
| --- | --- |
| `AnalyticsService/AnalyticsTypes.cs` | `AnalyticsParamValue`(union) / `AnalyticsParams`(컬렉션 초기화) / `PurchaseInfo` |
| `AnalyticsService/IAnalyticsService.cs` | 공개 계약 |
| `AnalyticsService/AnalyticsService.cs` | 팬아웃 + 버퍼 + 상태 슬롯 + 예외 격리 + 수집 게이트 |
| `AnalyticsService/AnalyticsServiceRegistration.cs` | `builder.RegisterAnalyticsService(settings)` |
| `AnalyticsService/Providers/IAnalyticsProvider.cs` | SDK seam |
| `AnalyticsService/Providers/AnalyticsProviderRegistry.cs` | 옵셔널 어셈블리 자기 등록 진입점 + `AnalyticsProviderCreationContext` |
| `AnalyticsService/Providers/IAnalyticsProviderFactory.cs` | 팩토리 seam |
| `AnalyticsService/Providers/AnalyticsProviderFactory.cs` | 플래그 → creator 조회 → 생성. 없으면 그것만 스킵 |
| `AnalyticsService/Providers/Debug/DebugAnalyticsProvider.cs` | 콘솔 provider |
| `AnalyticsService/Providers/Firebase/FoundationDI.Firebase.asmdef` | 옵셔널 어셈블리 |
| `AnalyticsService/Providers/Firebase/FirebaseAnalyticsProvider.cs` | Firebase 어댑터 |
| `AnalyticsService/Providers/Firebase/FirebaseParamConverter.cs` | `AnalyticsParams` → `Firebase.Analytics.Parameter[]` + 이름 검증 |
| `AnalyticsService/Providers/Firebase/FirebaseInstaller.cs` | `[RuntimeInitializeOnLoadMethod]` 자기 등록 |
| `AnalyticsService/Settings/AnalyticsProviderType.cs` | `[Flags]` enum |
| `AnalyticsService/Settings/AnalyticsServiceSettings.cs` | ScriptableObject + `ToOptions()` |
| `Tests/AnalyticsServiceTest.cs` | EditMode 단위 테스트 전부 |

## 확정 시그니처 (모든 태스크가 이것에 맞춘다)

```csharp
public enum AnalyticsParamKind { String, Long, Double }

public readonly struct AnalyticsParamValue
{
    public AnalyticsParamKind Kind { get; }
    public string StringValue { get; }
    public long LongValue { get; }
    public double DoubleValue { get; }
    public static AnalyticsParamValue Of(string value);
    public static AnalyticsParamValue Of(long value);
    public static AnalyticsParamValue Of(double value);
}

public sealed class AnalyticsParams : IEnumerable<KeyValuePair<string, AnalyticsParamValue>>
{
    public int Count { get; }
    public void Add(string key, string value);
    public void Add(string key, long value);
    public void Add(string key, double value);
    public IEnumerator<KeyValuePair<string, AnalyticsParamValue>> GetEnumerator();
}

public readonly struct PurchaseInfo
{
    public PurchaseInfo(string productId, double price, string currency,
                        int quantity = 1, string transactionId = null, AnalyticsParams extra = null);
    public string ProductId { get; }
    public double Price { get; }
    public string Currency { get; }
    public int Quantity { get; }
    public string TransactionId { get; }
    public AnalyticsParams Extra { get; }
    public double Revenue => Price * Quantity;
}

public readonly struct AnalyticsServiceOptions
{
    public AnalyticsServiceOptions(bool collectionEnabledByDefault);
    public bool CollectionEnabledByDefault { get; }
}

public interface IAnalyticsService : IDisposable
{
    bool IsInitialized { get; }
    Awaitable<bool> InitializeAsync();
    bool CollectionEnabled { get; set; }
    void LogEvent(string name);
    void LogEvent(string name, AnalyticsParams parameters);
    void LogPurchase(in PurchaseInfo purchase);
    void LogAdImpression(in AdImpression impression);
    void SetUserId(string userId);
    void SetUserProperty(string name, string value);
}

public interface IAnalyticsProvider : IDisposable
{
    string Name { get; }
    Awaitable<bool> InitializeAsync();
    void SetCollectionEnabled(bool enabled);
    void LogEvent(string name, AnalyticsParams parameters);
    void LogPurchase(in PurchaseInfo purchase);
    void LogAdImpression(in AdImpression impression);
    void SetUserId(string userId);
    void SetUserProperty(string name, string value);
}

[Flags]
public enum AnalyticsProviderType
{
    None = 0, Debug = 1, Firebase = 2, AppsFlyer = 4, Adjust = 8, Singular = 16, Airbridge = 32,
}

public readonly struct AnalyticsProviderCreationContext
{
    public AnalyticsProviderCreationContext(AnalyticsServiceOptions options);
    public AnalyticsServiceOptions Options { get; }
}

public static class AnalyticsProviderRegistry
{
    public static void Register(AnalyticsProviderType type,
                                Func<AnalyticsProviderCreationContext, IAnalyticsProvider> creator);
    internal static bool TryResolve(AnalyticsProviderType type,
                                    out Func<AnalyticsProviderCreationContext, IAnalyticsProvider> creator);
    internal static void Reset();   // 테스트 전용
}

public interface IAnalyticsProviderFactory
{
    IReadOnlyList<IAnalyticsProvider> CreateAll(AnalyticsProviderType types,
                                                in AnalyticsServiceOptions options);
}

public sealed class AnalyticsService : IAnalyticsService
{
    public AnalyticsService(IReadOnlyList<IAnalyticsProvider> providers, in AnalyticsServiceOptions options);
}
```

> `AdImpression`은 **새로 만들지 않는다.** `AdService/AdTypes.cs`의 것을 그대로 쓴다.

---

## Task 1: 값 타입

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/AnalyticsService/AnalyticsTypes.cs`
- Test: `Assets/FoundationDI/Tests/AnalyticsServiceTest.cs`

**Interfaces:**
- Consumes: 없음
- Produces: `AnalyticsParamKind`, `AnalyticsParamValue`, `AnalyticsParams`, `PurchaseInfo` (위 시그니처 그대로)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

```csharp
[Test]
public void 컬렉션_초기화가_파라미터의_순서와_타입을_보존해야_한다()
{
    var parameters = new AnalyticsParams
    {
        { "level", 12L },
        { "clear_time", 34.5 },
        { "difficulty", "hard" },
    };

    var items = parameters.ToList();

    Assert.That(parameters.Count, Is.EqualTo(3));
    Assert.That(items[0].Key, Is.EqualTo("level"));
    Assert.That(items[0].Value.Kind, Is.EqualTo(AnalyticsParamKind.Long));
    Assert.That(items[0].Value.LongValue, Is.EqualTo(12L));
    Assert.That(items[1].Value.Kind, Is.EqualTo(AnalyticsParamKind.Double));
    Assert.That(items[1].Value.DoubleValue, Is.EqualTo(34.5));
    Assert.That(items[2].Value.Kind, Is.EqualTo(AnalyticsParamKind.String));
    Assert.That(items[2].Value.StringValue, Is.EqualTo("hard"));
}
```

- [ ] **Step 2: 컴파일 에러(타입 없음)로 실패하는 것을 확인한다** — `read_console`로 확인 후 `run_tests`
- [ ] **Step 3: `AnalyticsTypes.cs`에 최소 구현을 쓴다** — 위 확정 시그니처대로. `AnalyticsParams` 내부는 `List<KeyValuePair<string, AnalyticsParamValue>>` 하나. `Add`가 `null`/빈 키를 받으면 경고 로그 후 무시한다(분석 파라미터의 키 없는 값은 어느 SDK에서도 의미가 없다).
- [ ] **Step 4: 테스트 통과 확인** — `run_tests` EditMode
- [ ] **Step 5: 커밋** — `[BEHAVIORAL] AnalyticsParams 컬렉션 초기화 지원`

---

## Task 2: 팬아웃 + 예외 격리

**Files:**
- Create: `AnalyticsService/IAnalyticsService.cs`, `AnalyticsService/Providers/IAnalyticsProvider.cs`, `AnalyticsService/AnalyticsService.cs`
- Modify: `Tests/AnalyticsServiceTest.cs`

**Interfaces:**
- Consumes: Task 1의 `AnalyticsParams`, `PurchaseInfo`
- Produces: `IAnalyticsService`, `IAnalyticsProvider`, `AnalyticsService(providers, options)`

이 태스크에서는 **버퍼를 아직 만들지 않는다.** 테스트는 `InitializeAsync`를 먼저 await한 뒤 발행한다. 버퍼는 Task 3이다.

- [ ] **Step 1: 테스트 두 개를 쓴다**

```csharp
[Test]
public async Task 이벤트를_발행하면_모든_provider가_각각_한_번씩_받아야_한다()
{
    var a = CreateProvider("A");
    var b = CreateProvider("B");
    var service = new AnalyticsService(new[] { a, b }, new AnalyticsServiceOptions(true));

    await service.InitializeAsync();
    service.LogEvent("boss_defeated");

    a.Received(1).LogEvent("boss_defeated", Arg.Any<AnalyticsParams>());
    b.Received(1).LogEvent("boss_defeated", Arg.Any<AnalyticsParams>());
}

[Test]
public async Task 한_provider가_예외를_던져도_나머지_provider는_호출되어야_한다()
{
    var broken = CreateProvider("Broken");
    broken.When(p => p.LogEvent(Arg.Any<string>(), Arg.Any<AnalyticsParams>()))
          .Do(_ => throw new InvalidOperationException("boom"));
    var healthy = CreateProvider("Healthy");
    var service = new AnalyticsService(new[] { broken, healthy }, new AnalyticsServiceOptions(true));

    await service.InitializeAsync();
    LogAssert.ignoreFailingMessages = true;
    service.LogEvent("boss_defeated");
    LogAssert.ignoreFailingMessages = false;

    healthy.Received(1).LogEvent("boss_defeated", Arg.Any<AnalyticsParams>());
}
```

`CreateProvider(name)` 헬퍼는 `Substitute.For<IAnalyticsProvider>()`를 만들고 `Name`을 세팅한 뒤 `InitializeAsync()`가 완료된 `Awaitable<bool>(true)`를 반환하도록 스텁한다. **`Awaitable<bool>`은 생성자가 없으므로** `AwaitableCompletionSource<bool>`로 만들어 `SetResult(true)` 한 것을 돌려준다.

- [ ] **Step 2: 실패 확인**
- [ ] **Step 3: 최소 구현** — `AnalyticsService`가 provider 목록을 들고 `Fanout(Action<IAnalyticsProvider>)` 하나로 순회하며 provider별 `try/catch`. catch에서 `Debug.LogError($"[AnalyticsService] {p.Name} 에서 예외: {e}")`. `InitializeAsync`는 지금은 전 provider의 `InitializeAsync`를 await하고 `IsInitialized = true`만 한다.
- [ ] **Step 4: 전체 테스트 통과 확인**
- [ ] **Step 5: 커밋** — `[BEHAVIORAL] AnalyticsService 팬아웃과 provider 예외 격리`

---

## Task 3: 초기화 + 버퍼 + 상태 슬롯

**Files:**
- Modify: `AnalyticsService/AnalyticsService.cs`, `Tests/AnalyticsServiceTest.cs`

**Interfaces:**
- Consumes: Task 2의 `AnalyticsService`
- Produces: 없음 (동작 추가)

한 번에 하나씩, 아래 순서로 각각 RED → GREEN → 전체 테스트 → 커밋.

- [ ] **3-1: 초기화 전 이벤트는 버퍼링됐다가 초기화 후 순서대로 전달된다**
      `Queue<Action<IAnalyticsProvider>> _pendingEvents`. `LogEvent`/`LogPurchase`/`LogAdImpression`이 `IsInitialized == false`면 큐에 넣고 반환. 상한 없음.
      검증: `Received.InOrder(() => { p.LogEvent("first", ...); p.LogEvent("second", ...); })`
- [ ] **3-2: 초기화 전 SetUserProperty는 같은 키의 마지막 값만 전달된다**
      `Dictionary<string,string> _pendingProperties`, `string _pendingUserId` + `bool _hasPendingUserId`. latest-wins.
      검증: `p.Received(1).SetUserProperty("player_level", "37")` + `p.DidNotReceive().SetUserProperty("player_level", "12")`
- [ ] **3-3: 초기화 시 유저 상태가 버퍼된 이벤트보다 먼저 전달된다**
      `Flush()`는 **UserId → Properties → Events** 순서.
- [ ] **3-4: provider 하나가 초기화에 실패해도 초기화는 성공하고 실패한 provider에는 전달되지 않는다**
      각 provider의 `InitializeAsync` 결과를 모아 `true`인 것만 `_providers`로 남긴다. 실패한 provider는 `Debug.LogError` 후 제외. 하나라도 남으면 `IsInitialized = true`, 반환 `true`.
      **`InitializeAsync`가 예외를 던지는 provider도 실패로 취급한다**(try/catch).
- [ ] **3-5: 모든 provider가 초기화에 실패하면 false를 반환하고 버퍼는 유지된다**
      `IsInitialized`는 `false`로 남고 `_pendingEvents`를 비우지 않는다. 다시 `InitializeAsync`를 부르면 재시도한다.
- [ ] **3-6: InitializeAsync는 재진입해도 초기화를 두 번 시작하지 않는다**
      `AwaitableCompletionSource<bool> _initializing`. 진행 중이면 그 결과에 편승. 이미 초기화됐으면 즉시 `true`.
      검증: provider의 `InitializeAsync`가 `Received(1)`.
      **주의**: `Awaitable`은 단일 사용이므로 편승 경로는 `_initializing.Awaitable`을 여러 번 await 할 수 없다 — 결과 `bool`을 보관하고 두 번째 호출자는 완료 여부를 확인해 값만 돌려주는 방식으로 구현하거나, 대기 중인 호출자마다 별도 `AwaitableCompletionSource`를 리스트로 들고 완료 시 전부 `SetResult` 한다. **후자를 택한다.**

각 항목마다 커밋: `[BEHAVIORAL] <항목 요지>`

---

## Task 4: CollectionEnabled 게이트 + Dispose

**Files:**
- Modify: `AnalyticsService/AnalyticsService.cs`, `Tests/AnalyticsServiceTest.cs`

- [ ] **4-1: CollectionEnabled가 false면 어떤 provider에도 전달되지 않는다**
      `LogEvent`/`LogPurchase`/`LogAdImpression`/`SetUserId`/`SetUserProperty` 진입부에서 `if (!_collectionEnabled) return;` — **버퍼에도 넣지 않는다.**
      초기값은 `options.CollectionEnabledByDefault`.
- [ ] **4-2: CollectionEnabled를 바꾸면 모든 provider에 전파되고 같은 값 재설정은 전파되지 않는다**
      setter에서 `if (_collectionEnabled == value) return;` 후 `Fanout(p => p.SetCollectionEnabled(value))`.
      **초기화 전에는 전파하지 않는다** — provider가 아직 없거나 SDK가 준비되지 않았다. `Flush()`가 상태보다 먼저 `SetCollectionEnabled(_collectionEnabled)`를 한 번 밀어 넣는다.
- [ ] **4-3: Dispose하면 모든 provider가 Dispose되고 이후 호출은 무시된다**
      `_disposed` 플래그. 전 provider `Dispose()`(각각 try/catch), 큐·슬롯 clear. 이후 로깅 호출은 조용히 반환하고, `InitializeAsync`는 `false`를 반환한다. 중복 `Dispose`는 안전.

각 항목마다 커밋.

---

## Task 5: Registry + Factory + Settings + DI 등록

**Files:**
- Create: `Providers/AnalyticsProviderRegistry.cs`, `Providers/IAnalyticsProviderFactory.cs`, `Providers/AnalyticsProviderFactory.cs`, `Settings/AnalyticsProviderType.cs`, `Settings/AnalyticsServiceSettings.cs`, `AnalyticsServiceRegistration.cs`
- Modify: `Tests/AnalyticsServiceTest.cs`

- [ ] **5-1: AnalyticsProviderFactory는 creator가 없는 provider만 건너뛰고 나머지를 생성한다**
      `CreateAll(Debug | Firebase, options)` 상태에서 `Debug`만 Registry에 등록해 두면 결과 길이 1, `Firebase`에 대해 `Debug.LogError`.
      `[TearDown]`에서 **반드시 `AnalyticsProviderRegistry.Reset()`** — 정적 상태가 다음 테스트로 새면 안 된다.
- [ ] **5-2: RegisterAnalyticsService로 IAnalyticsService가 싱글턴 등록된다**
      `AdServiceRegistration`과 동형. `settings == null`이면 `Debug.LogError` 후 등록 건너뜀.
      `AnalyticsServiceSettings`는 `Providers`(Flags) / `ForceDebugOnlyInEditor` / `CollectionEnabledByDefault` + `ToOptions()`.
      `[CreateAssetMenu(menuName = "FoundationDI/Analytics Service Settings")]`.
      검증: `ContainerBuilder`로 빌드해 `Resolve<IAnalyticsService>()`를 두 번 호출했을 때 같은 인스턴스.

각 항목마다 커밋. Settings SO와 enum 파일 생성은 STRUCTURAL이 아니라 이 기능의 일부이므로 같은 BEHAVIORAL 커밋에 포함한다.

---

## Task 6: Debug provider

**Files:**
- Create: `Providers/Debug/DebugAnalyticsProvider.cs`

- [ ] **Step 1: 구현** — `Name => "Debug"`. `InitializeAsync`는 완료된 `Awaitable<bool>(true)`. 각 메서드가 `Debug.Log($"[Analytics/Debug] ...")`로 찍는다. 파라미터는 `key=value` 나열로 포맷.
- [ ] **Step 2: `AnalyticsProviderRegistry.Register(AnalyticsProviderType.Debug, ...)`를 `AnalyticsProviderFactory` 정적 생성자가 아니라 `FoundationDI` 어셈블리의 `[RuntimeInitializeOnLoadMethod]`에서 한다** — Firebase와 동일한 경로를 쓰게 해서 "Debug만 특별대우"가 생기지 않도록. 파일은 `Providers/Debug/DebugAnalyticsInstaller.cs`.
- [ ] **Step 3: 전체 테스트 통과 확인** (Registry 자기 등록이 테스트의 `Reset()`과 충돌하지 않는지 — 충돌하면 테스트는 자기가 필요한 creator를 명시 등록하므로 문제없다)
- [ ] **Step 4: 커밋** — `[BEHAVIORAL] Debug analytics provider 추가`

---

## Task 7: Firebase 어댑터

**Files:**
- Create: `Providers/Firebase/FoundationDI.Firebase.asmdef`, `FirebaseAnalyticsProvider.cs`, `FirebaseParamConverter.cs`, `FirebaseInstaller.cs`
- Modify: `ProjectSettings/ProjectSettings.asset` (스크립팅 심볼 `FOUNDATIONDI_FIREBASE`)

**asmdef 내용** — AppLovin과 달리 Firebase는 asmdef가 아니라 **precompiled DLL**이므로 `overrideReferences` + `precompiledReferences`를 쓴다:

```json
{
    "name": "FoundationDI.Firebase",
    "rootNamespace": "",
    "references": ["FoundationDI"],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "Firebase.App.dll",
        "Firebase.Analytics.dll",
        "Firebase.TaskExtension.dll"
    ],
    "autoReferenced": true,
    "defineConstraints": ["FOUNDATIONDI_FIREBASE"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 1: asmdef를 만들고 `FOUNDATIONDI_FIREBASE` 심볼을 정의한 뒤 컴파일이 통과하는지 확인한다** (`read_console`). 이 단계에서 `overrideReferences: true`가 엔진 어셈블리 참조까지 끊지 않는지 반드시 확인 — 끊긴다면 `precompiledReferences`에 필요한 DLL을 추가한다.
- [ ] **Step 2: `FirebaseParamConverter`** — `AnalyticsParams` → `Firebase.Analytics.Parameter[]`. `Kind` 3분기. `null`/빈 params는 빈 배열.
      이름 검증: 이벤트명 40자 이내, `[A-Za-z][A-Za-z0-9_]*`, `firebase_`/`google_`/`ga_` 접두어 금지, 파라미터 25개 이내. 어긋나면 `Debug.LogWarning` — **버리지는 않는다**(SDK가 판단하게 두고 개발자에게만 알린다).
- [ ] **Step 3: `FirebaseAnalyticsProvider`**
      - `Name => "Firebase"`
      - `InitializeAsync`: `FirebaseApp.CheckAndFixDependenciesAsync()` → `ContinueWithOnMainThread`로 `DependencyStatus.Available` 확인 → `AwaitableCompletionSource<bool>`로 브리지. 실패 시 `LogError` 후 `false`.
      - `LogEvent` → `FirebaseAnalytics.LogEvent(name, Parameter[])`
      - `LogPurchase` → `FirebaseAnalytics.EventPurchase` + `ParameterValue`(=`Revenue`) / `ParameterCurrency` / `ParameterTransactionID` / `ParameterQuantity` / `ParameterItemID` + `Extra` 병합
      - `LogAdImpression` → `FirebaseAnalytics.EventAdImpression` + `ad_platform`/`ad_source`/`ad_unit_name`/`ad_format`/`value`/`currency`
      - `SetUserId`/`SetUserProperty`/`SetCollectionEnabled` → 동명 API
      - `Dispose`: no-op (정적 API라 해제할 것이 없다)
- [ ] **Step 4: `FirebaseInstaller`** — `[RuntimeInitializeOnLoadMethod]`에서 `AnalyticsProviderRegistry.Register(AnalyticsProviderType.Firebase, ctx => new FirebaseAnalyticsProvider())`
- [ ] **Step 5: 컴파일 + 전체 EditMode 테스트 통과 확인**
- [ ] **Step 6: 커밋** — `[BEHAVIORAL] Firebase analytics 어댑터 추가`

> **런타임 검증 제약**: `google-services.json` / `GoogleService-Info.plist`가 프로젝트에 없어서 `CheckAndFixDependenciesAsync`가 `Available`을 반환하지 않는다. 어댑터의 컴파일·구조는 검증되지만 **실제 이벤트 전송은 설정 파일을 넣기 전까지 검증 불가**다. 이 사실을 README와 최종 보고에 명시한다.

---

## Task 8: 문서 + 스모크 테스트 + 최종 검증

**Files:**
- Create: `AnalyticsService/README.md`, `Assets/Scripts/AnalyticsServiceSmokeTest.cs`
- Modify: `CLAUDE.md`, `plan.md`

- [ ] **Step 1: README** — AdService README 형식. 빠른 시작 / 공개 API / `AnalyticsParams` / 팬아웃과 예외 격리 / 버퍼와 상태 슬롯 / `CollectionEnabled`와 동의의 경계(ATT vs GDPR) / provider 추가 방법 / 구조 / 알려진 범위 밖(설정 파일 없음 포함).
- [ ] **Step 2: 스모크 테스트** — 호스트 프로젝트 스크립트. `AdServiceSmokeTest.cs`와 같은 모양으로 버튼을 눌러 이벤트를 쏘고 Debug provider 출력을 눈으로 확인.
- [ ] **Step 3: `CLAUDE.md`의 핵심 서비스 목록에 AnalyticsService 항목 추가** — STRUCTURAL 커밋으로 분리.
- [ ] **Step 4: `plan.md`의 AnalyticsService 계획을 완료로 이동** — STRUCTURAL 커밋.
- [ ] **Step 5: 전체 EditMode 테스트 실행 후 결과를 그대로 보고한다.** 실패가 있으면 실패로 보고한다.

---

## Self-Review

**스펙 커버리지**

| 스펙 항목 | 태스크 |
| --- | --- |
| 값 타입 (`AnalyticsParams`/`PurchaseInfo`) | Task 1 |
| 공개 계약 `IAnalyticsService` | Task 2 |
| Provider seam | Task 2 |
| 팬아웃 / 예외 격리 | Task 2 |
| 이벤트 버퍼 (상한 없음) | Task 3-1 |
| 상태 슬롯 latest-wins / flush 순서 | Task 3-2, 3-3 |
| 부분 초기화 성공 / 전체 실패 후 재시도 / 재진입 | Task 3-4, 3-5, 3-6 |
| `CollectionEnabled` 게이트·전파 | Task 4-1, 4-2 |
| `Dispose` | Task 4-3 |
| `[Flags]` provider 선택 / Registry / Factory | Task 5-1 |
| Settings SO / DI 등록 | Task 5-2 |
| Debug provider | Task 6 |
| Firebase 어댑터 + asmdef + 이름 검증 | Task 7 |
| `AdImpression` 재사용 / 수동 배선 | Task 2(시그니처), Task 8(README에 문서화) |
| 범위 밖 항목 명시 | Task 8 |

**타입 일관성** — `CreateAll(AnalyticsProviderType, in AnalyticsServiceOptions)`, `Fanout`, `Flush`, `AnalyticsProviderRegistry.Reset()` 이름이 전 태스크에서 동일하게 쓰였는지 확인함. `AnalyticsServiceOptions`는 Task 2에서 처음 쓰이므로 **Task 1에서 함께 만든다**(위 확정 시그니처 블록에 포함).

**미해결로 남긴 위험** — `Awaitable`의 단일 사용 제약이 `InitializeAsync` 재진입(Task 3-6)과 Firebase `Task` 브리지(Task 7-3) 두 곳에서 걸린다. 두 곳 모두 `AwaitableCompletionSource`를 **호출자마다 하나씩** 만드는 방식으로 회피하도록 계획에 명시했다. 구현 중 이 방식이 통하지 않으면 멈추고 재설계한다.
