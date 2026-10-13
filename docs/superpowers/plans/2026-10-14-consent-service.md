# ConsentService 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 약관 수락 → Google UMP → ATT를 한 흐름으로 묻는 앱 수명 `IConsentService`를 만들고, AdService·AnalyticsService가 그 결과로 초기화·수집을 게이트하게 한다.

**Architecture:** `ConsentService`(순수 C#)가 seam 다섯 개(`IPolicyAgreementPrompt`, `IPolicyAgreementStorage`, `IUmpConsent`, `ITrackingAuthorization`, `IConsentPrerequisite`)를 조립해 EditMode에서 전부 검증된다. UMP는 GMA 플러그인 없이 네이티브 SDK를 직접 부르는 얇은 브리지(Android JNI, iOS `.mm`)로 붙이고, 그 의존성 XML은 설정 토글로 생성·삭제한다. AdService는 `IConsentService`를 await한 뒤 provider를 초기화하고, AnalyticsService는 provider SDK 초기화(동의 선행 작업)와 동의 반영을 나눠 수집을 게이트한다.

**Tech Stack:** Unity 6000.3, C# (Awaitable), Reflex 14.3.1, uGUI + TextMeshPro, NUnit + NSubstitute, Unity Test Framework (UnityMCP `run_tests`), EDM4U(호스트 프로젝트 측), Google UMP Android 4.x / iOS 3.x.

**Spec:** `docs/superpowers/specs/2026-10-14-consent-service-design.md`

## Global Constraints

- 네임스페이스는 `DarkNaku.FoundationDI` 하나. 런타임 코드는 `FoundationDI` asmdef, 에디터 코드는 `FoundationDI.Editor` asmdef.
- 모든 비동기는 `UnityEngine.Awaitable`. UniTask·R3 금지. `Awaitable`은 한 번만 await한다 — 여러 호출자에게는 호출자마다 `AwaitableCompletionSource`를 만든다.
- 테스트 이름은 한국어 문장을 밑줄로 잇는다(기존 테스트 관례). async 테스트는 `[UnityTest] [Timeout(5000)] public IEnumerator X() => AwaitableTest.Run(async () => {...});`.
- EditMode에서 `Awaitable.NextFrameAsync`/`WaitForSecondsAsync`를 직접 쓰지 않는다. 시간은 `IMainThreadDispatcher`(테스트는 `FakeAdDispatcher.Advance`)로만 잰다.
- 커밋 제목에 `[STRUCTURAL]` 또는 `[BEHAVIORAL]`을 붙이고 둘을 섞지 않는다. 메시지 끝에 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- 커밋 전에는 UnityMCP `read_console`로 컴파일 에러가 없음을 확인하고 `run_tests`(EditMode, 전체)를 돌려 전부 통과해야 한다.
- 브랜치는 `feature/consent-service`(이미 있음). worktree를 쓰지 않는다(UnityMCP가 원본 프로젝트만 본다).
- 패키지 버전은 0.12.0(공개 API 파괴).
- iOS 빌드 후처리 번호는 999 → 1000 → 1001 다음인 **1002**.
- UMP 의존성: Android `com.google.android.ump:user-messaging-platform`, iOS `GoogleUserMessagingPlatform` 포드. **GMA Unity 플러그인(`GoogleMobileAds`)을 참조하거나 의존하지 않는다.**
- 런타임에서 `Resources`를 쓰지 않는다. 기본 프리팹은 설정 에셋이 직접 참조한다.
- 호스트에는 LevelPlay·AppLovin·Adjust·Firebase SDK가 없어 그 어댑터 어셈블리는 컴파일되지 않는다. 그 파일들을 고칠 때는 변경을 최소로 하고, 커밋 메시지에 "호스트 미컴파일 — 실기 빌드로 확인 필요"라고 적는다.

## Review Focus

1. **첫 실행이 오프라인일 때** — UMP 갱신이 실패하거나 시간 초과해도 `RequestAsync`가 끝나고, 분석은 `Unknown`으로 이벤트를 버퍼에 쥔 채 다음 재판정에서 flush/폐기된다. (Task 8 `UMP_갱신이_시간을_넘기면_실패로_보고_진행한다`, Task 11 `판정이_안_났으면_버퍼를_유지하고_재판정에서_flush한다`)
2. **광고 초기화와 분석 초기화를 거의 동시에 부를 때** — UMP 이전에 Firebase가 초기화되고, 동의 흐름은 한 번만 돈다. (Task 6 `약관_다음에_선행_작업을_끝내고_UMP를_시작한다`, Task 8 `진행_중에_다시_부르면_같은_흐름에_편승한다`, Task 12 `분석_등록이_provider_초기화를_동의_선행_작업으로_노출한다`)
3. **약관 창이 떠 있는 동안 씬 전환·Dispose** — 대기자가 영원히 매달리지 않고 `PolicyAccepted = false`로 끝나며 창이 정리된다. (Task 8 `약관_창이_떠_있을_때_Dispose하면_창을_닫고_수락하지_않음으로_완료한다`)
4. **설정 화면에서 동의를 철회/부여** — `ConsentChanged`로 분석 수집이 꺼지고 켜지며, Adjust 첫 세션 훅은 처음 허용될 때 한 번만 불린다. (Task 11 `나중에_동의하면_그때_flush하고_훅을_한_번만_부른다`)
5. **프리팹이 비어 있는 기본 UI** — 조용히 수락 처리하지 않고 에러를 남긴 채 `PolicyAccepted = false`로 끝난다. (Task 14 `프리팹이_없으면_수락하지_않고_예외를_던진다`)

## 파일 구조

```
Assets/FoundationDI/Runtime/
  Common/
    IMainThreadDispatcher.cs                (신규, Task 1)
    MainThreadDispatcherRegistration.cs     (신규, Task 1)
  Services/AdService/Dispatch/IAdDispatcher.cs          (수정, Task 1: IMainThreadDispatcher 상속)
  Services/ConsentService/
    IConsentService.cs, ConsentService.cs, ConsentTypes.cs, ConsentServiceOptions.cs   (Task 3, 5~8)
    AnalyticsConsentJudge.cs, DmaConsentMapper.cs                                        (Task 4, 13)
    ConsentServiceRegistration.cs, ConsentServiceSettings.cs                             (Task 9)
    Policy/ IPolicyAgreementPrompt.cs, IPolicyAgreementStorage.cs,
            PlayerPrefsPolicyAgreementStorage.cs, PolicyAgreementContent.cs              (Task 3)
            PolicyAgreementView.cs, DefaultPolicyAgreementPrompt.cs,
            IUrlOpener.cs, TemporaryEventSystem.cs, PolicyAgreementView.prefab           (Task 14)
    Ump/    IUmpConsent.cs, UmpTypes.cs, UnsupportedUmpConsent.cs                        (Task 3)
            UmpStatusMapper.cs                                                           (Task 15)
            AndroidUmpConsent.cs                                                         (Task 15)
            IosUmpConsent.cs, UmpConsentFactory.cs, Plugins/iOS/FDI_Ump.mm               (Task 16)
    Tracking/  (Task 2에서 AdService/Consent/에서 이동)
            ITrackingAuthorization.cs, TrackingAuthorizationRegistry.cs,
            UnsupportedTrackingAuthorization.cs, iOSSupport/**
    IConsentPrerequisite.cs                                                              (Task 3)
  Services/AnalyticsService/
    AnalyticsProviderInitializer.cs          (신규, Task 12)
    Providers/IAnalyticsConsentAware.cs      (신규, Task 11)
Assets/FoundationDI/Editor/ConsentService/
    UmpDependenciesFile.cs, UmpDependenciesSync.cs, ConsentServiceSettingsEditor.cs     (Task 17)
    UmpBuildRules.cs, AndroidManifestEditor.cs, UmpPostProcess.cs                        (Task 18)
    PolicyAgreementPrefabBuilder.cs                                                      (Task 14)
Assets/FoundationDI/Tests/
    ConsentTestDoubles.cs (Task 3), PolicyAgreementStorageTest.cs (Task 3),
    AnalyticsConsentJudgeTest.cs (Task 4), ConsentServicePolicyTest.cs (Task 5),
    ConsentServiceUmpTest.cs (Task 6), ConsentServiceTrackingTest.cs (Task 7),
    ConsentServiceRobustnessTest.cs (Task 8), ConsentServiceRegistrationTest.cs (Task 9),
    MainThreadDispatcherRegistrationTest.cs (Task 1), TrackingAuthorizationRegistryTest.cs (Task 2),
    AdServiceConsentTest.cs (Task 10), AnalyticsServiceConsentTest.cs (Task 11),
    AnalyticsProviderInitializerTest.cs (Task 12), DmaConsentMapperTest.cs (Task 13),
    PolicyAgreementPromptTest.cs (Task 14), UmpStatusMapperTest.cs (Task 15)
Assets/FoundationDI/Tests/Editor/ConsentService/
    UmpDependenciesFileTest.cs (Task 17), UmpBuildRulesTest.cs, AndroidManifestEditorTest.cs (Task 18)
```

테스트 실행 명령은 모두 UnityMCP다. 이 문서에서 "Run: `ConsentServicePolicyTest`"는 다음을 뜻한다.

```
mcp__UnityMCP__run_tests  mode=EditMode  test_filter=ConsentServicePolicyTest
→ get_test_job 으로 결과 확인
```

"전체 테스트"는 `mode=EditMode` 필터 없이 실행한다. 스크립트를 만들거나 고친 뒤에는 `mcp__UnityMCP__refresh_unity` → `editor_state.isCompiling == false`를 기다린 뒤 `read_console`(types=error)로 컴파일 에러부터 확인한다.

---

### Task 0: plan.md와 스펙에 실행 결정 반영

**Files:**
- Modify: `plan.md` (맨 위에 섹션 추가)
- Modify: `docs/superpowers/specs/2026-10-14-consent-service-design.md`

계획 작성 중 확정한 결정을 스펙에 맞춘다. 코드 변경 없음.

- [ ] **Step 1: 스펙 갱신**

스펙에서 아래를 고친다.
- §4.2 공개 API: `CanRequestAds`/`AnalyticsConsent` 개별 프로퍼티 대신 `ConsentResult Current { get; }`를 둔다. `ConsentResult`에 `TcfSignals Tcf`를 추가한다.
- §4.3: `IAppActivity` 행을 지운다. UMP iOS 브리지는 `FDI_Ump_IsApplicationActive`를 자체로 가진다.
- §4.4: "이름 변경" 대신 "`IMainThreadDispatcher`(Post/Delay/NextFrames)를 `Runtime/Common/`에 새로 두고 `IAdDispatcher : IMainThreadDispatcher`로 만든다. 기존 이름은 바꾸지 않는다(호스트에서 컴파일되지 않는 LevelPlay·AppLovin 어댑터가 `IAdDispatcher`를 쓴다)". `IAppActivity` 공용화 항목은 지운다.
- §5: "한 세션에서 이미 완료된 뒤의 `RequestAsync`"를 "직전 결과가 `CanRequestAds && AnalyticsConsent != Unknown`이면 그 결과를 즉시 돌려주고, 아니면 UMP 단계만 다시 수행한다"로 고친다.
- §5.1 끝에 "`Policy Version`이 0이면 약관 단계를 쓰지 않는다(항상 수락으로 본다)"를 추가한다.
- §5.2: "전체 타임아웃 30초"를 "UMP 정보 갱신(네트워크)에만 타임아웃(기본 10초)을 둔다. 폼 표시 중에는 사용자가 읽는 시간이라 타임아웃을 두지 않는다. ATT는 기존 60초 상한을 그대로 쓴다"로 고친다.
- §5.2: "약관 창 표시 중 Dispose"는 유지한다. 추가로 "기본 UI 프리팹이 비어 있으면 `ShowAsync`가 예외를 던지고 `PolicyAccepted = false`로 완료한다(조용히 수락 처리하지 않는다)".
- §6: "`IConsentService`가 등록되지 않았으면 `RegisterAdService`가 경고를 남기고 약관·UMP 없이 ATT만 묻는 `ConsentService.CreateTrackingOnly`로 동작한다(0.11 동작 보존)"를 추가한다. 게이트 조건은 `PolicyAccepted && CanRequestAds`다.
- §7.3: `IAnalyticsProvider.ApplyConsent` 대신 선택 seam `IAnalyticsConsentAware.ApplyConsent(ConsentResult)`(Adjust만 구현). 동의 선행 작업은 `AnalyticsProviderInitializer : IConsentPrerequisite`가 맡는다.
- §10.1: `Gate Analytics Collection`을 ConsentServiceSettings에서 빼고 `AnalyticsServiceSettings.Wait For Consent`(기본 true)로 옮긴다. `Request Timeout Seconds`는 `Ump Update Timeout Seconds`(기본 10)로 바꾼다.
- §10.3: Firebase 기본값 주입 조건은 "`Wait For Consent`가 켜진 `AnalyticsServiceSettings`가 있고 `FOUNDATIONDI_FIREBASE`가 정의됨".

- [ ] **Step 2: plan.md에 섹션 추가**

`plan.md` 맨 위(`# plan.md` 바로 아래)에 추가한다.

```markdown
## 진행 중: ConsentService (약관 + UMP + ATT) — 0.12.0

상세 계획: `docs/superpowers/plans/2026-10-14-consent-service.md` (Task 0~19).
각 Task의 테스트를 이 목록에 옮겨 적고 `/go` → `/green` → `/refactor` → `/commit`으로 진행한다.

- [ ] Task 1 [STRUCTURAL] IMainThreadDispatcher 추출
- [ ] Task 2 [STRUCTURAL] ATT를 ConsentService로 이동
- [ ] Task 3 타입·저장소
- [ ] Task 4 분석 동의 판정
- [ ] Task 5 약관 단계
- [ ] Task 6 UMP 단계·선행 작업
- [ ] Task 7 ATT 단계
- [ ] Task 8 견고성
- [ ] Task 9 설정·등록
- [ ] Task 10 AdService 연동
- [ ] Task 11 AnalyticsService 동의 게이트
- [ ] Task 12 분석 SDK 선행 초기화
- [ ] Task 13 Adjust DMA 번역
- [ ] Task 14 기본 약관 창 UI
- [ ] Task 15 UMP Android 브리지
- [ ] Task 16 UMP iOS 브리지·팩토리
- [ ] Task 17 UMP 의존성 XML
- [ ] Task 18 빌드 후처리
- [ ] Task 19 문서·버전
```

- [ ] **Step 3: 커밋**

```bash
git add plan.md docs/superpowers/specs/2026-10-14-consent-service-design.md docs/superpowers/plans/2026-10-14-consent-service.md
git commit -m "[STRUCTURAL] ConsentService 구현 계획을 추가하고 스펙에 실행 결정을 반영한다"
```

---

### Task 1: [STRUCTURAL] IMainThreadDispatcher 추출

**Files:**
- Create: `Assets/FoundationDI/Runtime/Common/IMainThreadDispatcher.cs`
- Create: `Assets/FoundationDI/Runtime/Common/MainThreadDispatcherRegistration.cs`
- Modify: `Assets/FoundationDI/Runtime/Services/AdService/Dispatch/IAdDispatcher.cs`
- Modify: `Assets/FoundationDI/Runtime/Services/AdService/AdServiceRegistration.cs:24-25`
- Modify: `Assets/FoundationDI/Runtime/Services/AdService/Consent/TrackingAuthorizationRegistry.cs`
- Modify: `Assets/FoundationDI/Runtime/Services/AdService/Consent/iOSSupport/AttTrackingAuthorization.cs` (`IAdDispatcher` → `IMainThreadDispatcher`, 필드·생성자 2곳)
- Modify: `Assets/FoundationDI/Tests/AdServiceRegistrationTest.cs`
- Test: `Assets/FoundationDI/Tests/MainThreadDispatcherRegistrationTest.cs`

**Interfaces:**
- Produces: `public interface IMainThreadDispatcher { void Post(Action); IDisposable Delay(float, Action); IDisposable NextFrames(int, Action); }`, `public interface IAdDispatcher : IMainThreadDispatcher {}`, `public static ContainerBuilder RegisterMainThreadDispatcher(this ContainerBuilder builder)`.

동작 변경이 없는 구조 변경이다. 테스트는 새 등록 헬퍼의 배선만 확인한다.

- [ ] **Step 1: 공용 인터페이스 작성**

`Runtime/Common/IMainThreadDispatcher.cs`:

```csharp
using System;

namespace DarkNaku.FoundationDI
{
    // 네이티브 콜백을 메인 스레드로 넘기고, 지연·프레임 대기를 가짜 시계로 테스트할 수 있게 하는 seam.
    // 처음에는 AdService 전용(IAdDispatcher)이었으나 ConsentService도 ATT 재요청 지연과
    // UMP 콜백 마샬링에 같은 것이 필요해 위로 올렸다.
    //
    // IAdDispatcher의 이름을 바꾸지 않고 상위 인터페이스를 둔 이유: LevelPlay·AppLovin 어댑터는
    // 게이트된 옵셔널 어셈블리라 이 호스트에서 컴파일되지 않는다. 이름을 바꾸면 검증할 수 없는
    // 파일을 손대야 한다.
    public interface IMainThreadDispatcher
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

- [ ] **Step 2: IAdDispatcher를 상속으로 바꾼다**

`IAdDispatcher.cs` 전체를 교체한다.

```csharp
namespace DarkNaku.FoundationDI
{
    // 두 가지 목적이 있다.
    // 1) 세 광고 SDK 모두 네이티브 스레드에서 콜백이 올라올 수 있어 메인스레드 마샬링이 필요하다.
    // 2) 백오프 지연과 보상 유예 프레임을 가짜 시계로 테스트할 수 있게 한다. 이쪽이 더 큰 이유다.
    //
    // 멤버는 IMainThreadDispatcher에 있다. 이 인터페이스는 광고 어댑터들이 쓰는 이름을 보존하려고 남겨 둔다.
    public interface IAdDispatcher : IMainThreadDispatcher
    {
    }
}
```

- [ ] **Step 3: 실패하는 등록 테스트 작성**

`Tests/MainThreadDispatcherRegistrationTest.cs`:

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;
using Reflex.Core;

public class MainThreadDispatcherRegistrationTest
{
    [Test]
    public void 등록하면_광고용과_공용_계약이_같은_바인딩을_가리킨다()
    {
        var builder = new ContainerBuilder();

        builder.RegisterMainThreadDispatcher();

        Assert.IsTrue(builder.HasBinding(typeof(IMainThreadDispatcher)));
        Assert.IsTrue(builder.HasBinding(typeof(IAdDispatcher)));
    }

    [Test]
    public void 두_번_불러도_바인딩을_하나만_만든다()
    {
        var builder = new ContainerBuilder();

        builder.RegisterMainThreadDispatcher();
        builder.RegisterMainThreadDispatcher();

        // 두 번 등록되면 서비스마다 러너 GameObject가 따로 생겨 펌프가 두 벌이 된다.
        Assert.AreEqual(1, builder.Bindings.Count);
    }
}
```

- [ ] **Step 4: 실패 확인**

Run: `MainThreadDispatcherRegistrationTest`
Expected: 컴파일 에러 — `RegisterMainThreadDispatcher`가 없다.

- [ ] **Step 5: 등록 헬퍼 작성**

`Runtime/Common/MainThreadDispatcherRegistration.cs`:

```csharp
using Reflex.Core;
using Reflex.Enums;
using Resolution = Reflex.Enums.Resolution;

namespace DarkNaku.FoundationDI
{
    public static class MainThreadDispatcherRegistration
    {
        // AdService와 ConsentService가 각자 부른다. 먼저 부른 쪽만 등록하고 나머지는 건너뛴다 —
        // 펌프(러너 GameObject)가 하나여야 지연·프레임 계산이 한 시계로 돈다.
        public static ContainerBuilder RegisterMainThreadDispatcher(this ContainerBuilder builder)
        {
            if (builder.HasBinding(typeof(IMainThreadDispatcher))) return builder;

            return builder.RegisterType(typeof(UnityAdDispatcher),
                                        new[] { typeof(IAdDispatcher), typeof(IMainThreadDispatcher) },
                                        Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
```

- [ ] **Step 6: AdServiceRegistration이 헬퍼를 쓰게 한다**

`AdServiceRegistration.cs`의 다음 두 줄을

```csharp
            builder.RegisterType(typeof(UnityAdDispatcher), new[] { typeof(IAdDispatcher) },
                                 Lifetime.Singleton, Resolution.Lazy);
```

다음으로 바꾼다.

```csharp
            builder.RegisterMainThreadDispatcher();
```

- [ ] **Step 7: ATT 쪽 시그니처를 공용 인터페이스로 바꾼다**

`TrackingAuthorizationRegistry.cs`에서 `Func<IAdDispatcher, ITrackingAuthorization>` 두 곳과 `Create(IAdDispatcher dispatcher)`를 각각 `Func<IMainThreadDispatcher, ITrackingAuthorization>`, `Create(IMainThreadDispatcher dispatcher)`로 바꾼다.
`AttTrackingAuthorization.cs`에서 `private readonly IAdDispatcher _dispatcher;`와 두 생성자의 `IAdDispatcher dispatcher` 파라미터를 `IMainThreadDispatcher`로 바꾼다. `IAdDispatcher`를 넘기던 호출부는 상속 관계라 그대로 컴파일된다.

- [ ] **Step 8: 기존 등록 테스트의 대역 덮어쓰기를 보강한다**

`AdServiceRegistrationTest.cs`의 덮어쓰기 줄을 다음으로 바꾼다(공용 계약으로 해석하는 경로도 가짜를 받게).

```csharp
            builder.RegisterType(typeof(FakeAdDispatcher),
                                 new[] { typeof(IAdDispatcher), typeof(IMainThreadDispatcher) },
                                 Lifetime.Singleton, Resolution.Lazy);
```

`FakeAdDispatcher`는 `IAdDispatcher`를 구현하므로 `IMainThreadDispatcher`도 자동으로 만족한다.

- [ ] **Step 9: 통과 확인**

Run: 전체 테스트
Expected: 전부 PASS(새 테스트 2개 포함). `read_console` 에러 0.

- [ ] **Step 10: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Common Assets/FoundationDI/Runtime/Services/AdService Assets/FoundationDI/Tests/MainThreadDispatcherRegistrationTest.cs* Assets/FoundationDI/Tests/AdServiceRegistrationTest.cs
git commit -m "[STRUCTURAL] 메인 스레드 디스패처를 IMainThreadDispatcher로 추출한다"
```

---

### Task 2: [STRUCTURAL] ATT를 ConsentService로 이동

**Files:**
- Move (`git mv`, `.meta` 함께): `Runtime/Services/AdService/Consent/ITrackingAuthorization.cs`, `TrackingAuthorizationRegistry.cs`, `UnsupportedTrackingAuthorization.cs`, `iOSSupport/` 전체 → `Runtime/Services/ConsentService/Tracking/`
- Create: `Runtime/Services/ConsentService.meta`, `Runtime/Services/ConsentService/Tracking.meta`는 Unity가 생성(폴더를 먼저 Unity에서 만들거나 refresh 후 생성된 meta를 커밋)
- Move: `Tests/AdTrackingAuthorizationTest.cs`의 레지스트리 테스트 2개와 Unsupported 테스트 1개 → `Tests/TrackingAuthorizationRegistryTest.cs`(신규). 원본 파일의 나머지는 Task 10에서 교체하므로 그대로 둔다.
- Modify: `Assets/FoundationDI/Editor/Linker/*`, `Editor/SdkDefines/SdkDefineTable.cs` — 경로를 하드코딩한 곳이 있는지 확인만 한다(어셈블리 이름 기반이면 수정 없음).

**Interfaces:**
- Produces: 파일 위치만 바뀐다. 타입·어셈블리 이름(`FoundationDI.iOSSupport`)·심볼(`FOUNDATIONDI_IOSSUPPORT`) 그대로.

- [ ] **Step 1: 경로 하드코딩 확인**

```bash
grep -rn "AdService/Consent" Assets/FoundationDI --include=*.cs --include=*.asmdef --include=*.md
```
Expected: 문서(README) 외에는 결과 없음. 코드에서 나오면 새 경로로 고친다.

- [ ] **Step 2: 폴더 이동**

```bash
mkdir -p Assets/FoundationDI/Runtime/Services/ConsentService/Tracking
cd Assets/FoundationDI/Runtime/Services
for f in ITrackingAuthorization TrackingAuthorizationRegistry UnsupportedTrackingAuthorization; do
  git mv AdService/Consent/$f.cs ConsentService/Tracking/$f.cs
  git mv AdService/Consent/$f.cs.meta ConsentService/Tracking/$f.cs.meta
done
git mv AdService/Consent/iOSSupport ConsentService/Tracking/iOSSupport
git mv AdService/Consent/iOSSupport.meta ConsentService/Tracking/iOSSupport.meta
```

`AdService/Consent/`에는 `IAdConsent.cs`, `NoopAdConsent.cs`만 남는다(Task 10에서 삭제).

- [ ] **Step 3: Unity가 새 폴더 meta를 만들게 한다**

`mcp__UnityMCP__refresh_unity` → 컴파일 대기 → `read_console`(error) 0건 확인.
생성된 `ConsentService.meta`, `ConsentService/Tracking.meta`가 있는지 `git status`로 확인한다.

- [ ] **Step 4: 레지스트리 테스트를 새 파일로 옮긴다**

`Tests/TrackingAuthorizationRegistryTest.cs`:

```csharp
using System.Collections;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class TrackingAuthorizationRegistryTest
{
    [TearDown]
    public void TearDown() => TrackingAuthorizationRegistry.Reset();

    [Test]
    public void 등록된_구현이_없으면_지원하지_않는_구현을_돌려준다()
    {
        var created = TrackingAuthorizationRegistry.Create(new FakeAdDispatcher());

        Assert.IsNotNull(created, "null을 돌려주면 호출부마다 널 검사를 해야 한다");
        Assert.IsFalse(created.IsSupported);
        Assert.AreEqual(TrackingAuthorizationStatus.NotDetermined, created.Status);
    }

    [Test]
    public void 등록된_구현이_있으면_그것을_돌려준다()
    {
        var registered = new UnsupportedTrackingAuthorization();

        TrackingAuthorizationRegistry.Register(_ => registered);

        Assert.AreSame(registered, TrackingAuthorizationRegistry.Create(new FakeAdDispatcher()));
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 지원하지_않는_구현은_즉시_완료된다() => AwaitableTest.Run(async () =>
    {
        var status = await new UnsupportedTrackingAuthorization().RequestAsync();

        Assert.AreEqual(TrackingAuthorizationStatus.NotDetermined, status);
    });
}
```

`AdTrackingAuthorizationTest.cs`에서 같은 이름의 테스트 3개(`등록된_구현이_없으면…`, `등록된_구현이_있으면…`, `지원하지_않는_구현은_즉시_완료된다`)를 지운다. `[TearDown]`은 남긴다.

- [ ] **Step 5: 전체 테스트**

Run: 전체 테스트(`FoundationDI.iOSSupport.Tests` 포함)
Expected: 전부 PASS, 테스트 수는 이동 전과 같다.

- [ ] **Step 6: 커밋**

```bash
git add -A Assets/FoundationDI/Runtime/Services Assets/FoundationDI/Tests
git commit -m "[STRUCTURAL] ATT 구현을 ConsentService/Tracking으로 옮긴다"
```

---

### Task 3: 동의 타입·seam·약관 저장소

**Files:**
- Create: `Runtime/Services/ConsentService/ConsentTypes.cs`
- Create: `Runtime/Services/ConsentService/ConsentServiceOptions.cs`
- Create: `Runtime/Services/ConsentService/IConsentService.cs`
- Create: `Runtime/Services/ConsentService/IConsentPrerequisite.cs`
- Create: `Runtime/Services/ConsentService/Policy/PolicyAgreementContent.cs`
- Create: `Runtime/Services/ConsentService/Policy/IPolicyAgreementPrompt.cs`
- Create: `Runtime/Services/ConsentService/Policy/IPolicyAgreementStorage.cs`
- Create: `Runtime/Services/ConsentService/Policy/PlayerPrefsPolicyAgreementStorage.cs`
- Create: `Runtime/Services/ConsentService/Ump/UmpTypes.cs`
- Create: `Runtime/Services/ConsentService/Ump/IUmpConsent.cs`
- Create: `Runtime/Services/ConsentService/Ump/UnsupportedUmpConsent.cs`
- Create: `Tests/ConsentTestDoubles.cs`
- Test: `Tests/PolicyAgreementStorageTest.cs`

**Interfaces:**
- Produces (이후 모든 Task가 쓴다):
  - `enum AnalyticsConsent { Unknown, Granted, Denied }`
  - `enum UmpConsentStatus { Unknown, NotRequired, Required, Obtained }`
  - `enum UmpDebugGeography { Disabled, Eea, NotEea, RegulatedUsState, Other }`
  - `readonly struct TcfSignals(bool? gdprApplies, string purposeConsents)` + `static TcfSignals None` + `bool HasPurpose(int purpose)`
  - `readonly struct ConsentResult(bool policyAccepted, bool canRequestAds, AnalyticsConsent analyticsConsent, TrackingAuthorizationStatus trackingStatus, TcfSignals tcf)` : `IEquatable<ConsentResult>`
  - `readonly struct UmpRequestOptions(UmpDebugGeography debugGeography, IReadOnlyList<string> testDeviceHashedIds, bool tagForUnderAgeOfConsent)`
  - `readonly struct ConsentServiceOptions(int policyVersion, PolicyAgreementContent policyContent, bool useUmp, UmpRequestOptions umpRequest, bool requestTrackingAuthorization, float umpUpdateTimeoutSeconds = 10f)`
  - `readonly struct PolicyAgreementContent(string title, string message, string acceptLabel, string termsLabel, string termsUrl, string privacyLabel, string privacyUrl)`
  - `interface IPolicyAgreementPrompt { Awaitable ShowAsync(PolicyAgreementContent content); void Close(); }`
  - `interface IPolicyAgreementStorage { int LoadAcceptedVersion(); void SaveAcceptedVersion(int version); }`
  - `interface IUmpConsent { bool IsSupported; UmpConsentStatus Status; bool CanRequestAds; bool IsPrivacyOptionsRequired; TcfSignals ReadTcf(); Awaitable<bool> UpdateAsync(UmpRequestOptions); Awaitable<bool> LoadAndShowIfRequiredAsync(); Awaitable<bool> ShowPrivacyOptionsAsync(); }`
  - `interface IConsentPrerequisite { Awaitable PrepareAsync(); }`
  - `interface IConsentService` (아래 코드)
  - 테스트 대역: `FakePolicyPrompt`, `FakePolicyStorage`, `FakeUmpConsent`, `FakeTrackingAuthorization`, `FakeConsentPrerequisite`, `FakeConsentService`, `ConsentResults` (`ConsentFixture`는 Task 5)

- [ ] **Step 1: 실패하는 저장소 테스트 작성**

`Tests/PolicyAgreementStorageTest.cs`:

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class PolicyAgreementStorageTest
{
    [SetUp]
    public void SetUp() => PlayerPrefs.DeleteKey(PlayerPrefsPolicyAgreementStorage.Key);

    [TearDown]
    public void TearDown() => PlayerPrefs.DeleteKey(PlayerPrefsPolicyAgreementStorage.Key);

    [Test]
    public void 저장한_적이_없으면_0을_돌려준다()
    {
        Assert.AreEqual(0, new PlayerPrefsPolicyAgreementStorage().LoadAcceptedVersion());
    }

    [Test]
    public void 저장한_버전을_다시_읽는다()
    {
        var storage = new PlayerPrefsPolicyAgreementStorage();

        storage.SaveAcceptedVersion(3);

        Assert.AreEqual(3, new PlayerPrefsPolicyAgreementStorage().LoadAcceptedVersion());
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `PolicyAgreementStorageTest`
Expected: 컴파일 에러 — `PlayerPrefsPolicyAgreementStorage` 없음.

- [ ] **Step 3: 타입 파일 작성**

`ConsentTypes.cs`:

```csharp
using System;

namespace DarkNaku.FoundationDI
{
    // 분석 수집을 켜도 되는가. Unknown은 "아직 판정하지 못했다"(첫 실행 오프라인 등)라
    // 수집을 켜지도 버퍼를 버리지도 않는다.
    public enum AnalyticsConsent
    {
        Unknown,
        Granted,
        Denied,
    }

    // UMP가 IAB TCF 저장소(Android 기본 SharedPreferences / iOS NSUserDefaults)에 쓴 값 중
    // 이 패키지가 읽는 것만 담는다. Unity PlayerPrefs와는 다른 저장소다.
    public readonly struct TcfSignals : IEquatable<TcfSignals>
    {
        // IABTCF_gdprApplies. 1이면 true, 0이면 false, 키가 없으면 null.
        public bool? GdprApplies { get; }

        // IABTCF_PurposeConsents. "1010..." 형태이며 0번 문자가 Purpose 1이다.
        public string PurposeConsents { get; }

        public TcfSignals(bool? gdprApplies, string purposeConsents)
        {
            GdprApplies = gdprApplies;
            PurposeConsents = purposeConsents;
        }

        public static TcfSignals None => new(null, null);

        public bool HasPurpose(int purpose)
        {
            if (string.IsNullOrEmpty(PurposeConsents)) return false;
            if (purpose < 1 || purpose > PurposeConsents.Length) return false;

            return PurposeConsents[purpose - 1] == '1';
        }

        public bool Equals(TcfSignals other) =>
            GdprApplies == other.GdprApplies && PurposeConsents == other.PurposeConsents;

        public override bool Equals(object obj) => obj is TcfSignals other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(GdprApplies, PurposeConsents);
    }

    public readonly struct ConsentResult : IEquatable<ConsentResult>
    {
        // 약관·개인정보처리방침을 수락했는가. false면 광고·분석 모두 시작하지 않는다.
        public bool PolicyAccepted { get; }

        // UMP canRequestAds. UMP를 쓰지 않으면 true.
        public bool CanRequestAds { get; }

        public AnalyticsConsent AnalyticsConsent { get; }

        public TrackingAuthorizationStatus TrackingStatus { get; }

        public TcfSignals Tcf { get; }

        public ConsentResult(bool policyAccepted, bool canRequestAds, AnalyticsConsent analyticsConsent,
                             TrackingAuthorizationStatus trackingStatus, TcfSignals tcf)
        {
            PolicyAccepted = policyAccepted;
            CanRequestAds = canRequestAds;
            AnalyticsConsent = analyticsConsent;
            TrackingStatus = trackingStatus;
            Tcf = tcf;
        }

        public bool Equals(ConsentResult other) =>
            PolicyAccepted == other.PolicyAccepted &&
            CanRequestAds == other.CanRequestAds &&
            AnalyticsConsent == other.AnalyticsConsent &&
            TrackingStatus == other.TrackingStatus &&
            Tcf.Equals(other.Tcf);

        public override bool Equals(object obj) => obj is ConsentResult other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(PolicyAccepted, CanRequestAds, AnalyticsConsent, TrackingStatus, Tcf);

        public override string ToString() =>
            $"Policy={PolicyAccepted} Ads={CanRequestAds} Analytics={AnalyticsConsent} ATT={TrackingStatus}";
    }
}
```

`Ump/UmpTypes.cs`:

```csharp
using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    // 네이티브 enum을 그대로 캐스팅하지 않는다 — Android와 iOS의 정수 값 순서가 다르다
    // (UmpStatusMapper가 플랫폼별로 명시적으로 번역한다).
    public enum UmpConsentStatus
    {
        Unknown,
        NotRequired,
        Required,
        Obtained,
    }

    public enum UmpDebugGeography
    {
        Disabled,
        Eea,
        NotEea,
        RegulatedUsState,
        Other,
    }

    public readonly struct UmpRequestOptions
    {
        // 개발 빌드에서만 의미가 있다. 설정이 릴리스 빌드에서는 Disabled로 만든다.
        public UmpDebugGeography DebugGeography { get; }
        public IReadOnlyList<string> TestDeviceHashedIds { get; }
        public bool TagForUnderAgeOfConsent { get; }

        public UmpRequestOptions(UmpDebugGeography debugGeography, IReadOnlyList<string> testDeviceHashedIds,
                                 bool tagForUnderAgeOfConsent)
        {
            DebugGeography = debugGeography;
            TestDeviceHashedIds = testDeviceHashedIds;
            TagForUnderAgeOfConsent = tagForUnderAgeOfConsent;
        }
    }
}
```

`Ump/IUmpConsent.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // Google UMP 네이티브 SDK를 가리는 seam. GMA Unity 플러그인을 쓰지 않는다 — 그 플러그인은
    // 광고 SDK를 함께 끌고 와 LevelPlay AdMob 어댑터(Next-Gen SDK)와 충돌한다(스펙 §1.2).
    //
    // 계약: 모든 Awaitable은 메인 스레드에서 완료된다(구현이 마샬링한다). 영원히 매달리는 것은
    // 구현의 버그지만, ConsentService는 네트워크 단계(UpdateAsync)에 타임아웃을 따로 건다.
    public interface IUmpConsent
    {
        // 네이티브 SDK가 실제로 있는가. false면 ConsentService는 UMP를 쓰지 않는 것으로 본다.
        bool IsSupported { get; }

        UmpConsentStatus Status { get; }
        bool CanRequestAds { get; }
        bool IsPrivacyOptionsRequired { get; }

        TcfSignals ReadTcf();

        // requestConsentInfoUpdate. 성공이면 true, 실패(네트워크 등)면 false.
        Awaitable<bool> UpdateAsync(UmpRequestOptions options);

        // loadAndShowConsentFormIfRequired. 필요 없으면 폼 없이 즉시 끝난다. 에러가 없으면 true.
        Awaitable<bool> LoadAndShowIfRequiredAsync();

        // showPrivacyOptionsForm. 에러가 없으면 true.
        Awaitable<bool> ShowPrivacyOptionsAsync();
    }
}
```

`Ump/UnsupportedUmpConsent.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 에디터, UMP를 끈 프로젝트, 네이티브 SDK가 빠진 빌드에서 쓴다. "규제 대상 아님"처럼 동작해
    // 광고·분석을 막지 않는다.
    public sealed class UnsupportedUmpConsent : IUmpConsent
    {
        public bool IsSupported => false;
        public UmpConsentStatus Status => UmpConsentStatus.NotRequired;
        public bool CanRequestAds => true;
        public bool IsPrivacyOptionsRequired => false;

        public TcfSignals ReadTcf() => TcfSignals.None;

        public Awaitable<bool> UpdateAsync(UmpRequestOptions options) => Completed(true);
        public Awaitable<bool> LoadAndShowIfRequiredAsync() => Completed(true);
        public Awaitable<bool> ShowPrivacyOptionsAsync() => Completed(true);

        private static Awaitable<bool> Completed(bool value)
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(value);
            return source.Awaitable;
        }
    }
}
```

`Policy/PolicyAgreementContent.cs`:

```csharp
namespace DarkNaku.FoundationDI
{
    // 약관 수락 창에 표시할 문구. 설정 에셋이 기본값을 만들고, 현지화가 필요하면 게임이
    // IConsentService.SetPolicyContent로 런타임에 바꾼다.
    public readonly struct PolicyAgreementContent
    {
        public string Title { get; }
        public string Message { get; }
        public string AcceptLabel { get; }
        public string TermsLabel { get; }
        public string TermsUrl { get; }
        public string PrivacyLabel { get; }
        public string PrivacyUrl { get; }

        public PolicyAgreementContent(string title, string message, string acceptLabel,
                                      string termsLabel, string termsUrl,
                                      string privacyLabel, string privacyUrl)
        {
            Title = title;
            Message = message;
            AcceptLabel = acceptLabel;
            TermsLabel = termsLabel;
            TermsUrl = termsUrl;
            PrivacyLabel = privacyLabel;
            PrivacyUrl = privacyUrl;
        }
    }
}
```

`Policy/IPolicyAgreementPrompt.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 약관 수락 창. 기본 구현(DefaultPolicyAgreementPrompt)을 쓰거나 게임이 직접 구현해
    // RegisterConsentService(settings, prompt:)로 넘긴다.
    //
    // 구현은 "보여 주고, 링크를 열고, 동의 버튼이 눌리면 완료"만 한다. 수락 저장과 버전 비교는
    // ConsentService가 맡는다. 거절 경로는 없다 — 수락해야 게임이 진행된다.
    public interface IPolicyAgreementPrompt
    {
        // 사용자가 동의 버튼을 누르면 완료된다. 표시할 수 없으면 예외를 던진다(조용히 완료하면
        // 사용자가 보지도 않은 약관을 수락한 것으로 저장된다).
        Awaitable ShowAsync(PolicyAgreementContent content);

        // 표시 중이면 창을 정리한다. ShowAsync는 완료시키지 않는다. ConsentService가 Dispose될 때 부른다.
        void Close();
    }
}
```

`Policy/IPolicyAgreementStorage.cs`:

```csharp
namespace DarkNaku.FoundationDI
{
    // 수락한 약관 버전을 저장한다. 버전을 저장하는 이유: 약관이 바뀌면 설정의 Policy Version만
    // 올려 기존 유저에게 다시 물을 수 있다.
    public interface IPolicyAgreementStorage
    {
        // 저장한 적이 없으면 0.
        int LoadAcceptedVersion();

        void SaveAcceptedVersion(int version);
    }
}
```

`Policy/PlayerPrefsPolicyAgreementStorage.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class PlayerPrefsPolicyAgreementStorage : IPolicyAgreementStorage
    {
        internal const string Key = "FoundationDI.Consent.PolicyVersion";

        public int LoadAcceptedVersion() => PlayerPrefs.GetInt(Key, 0);

        public void SaveAcceptedVersion(int version)
        {
            PlayerPrefs.SetInt(Key, version);

            // 수락 직후 앱이 강제 종료되면 다음 실행에 다시 묻게 된다. 저장을 미루지 않는다.
            PlayerPrefs.Save();
        }
    }
}
```

`IConsentPrerequisite.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // UMP를 시작하기 전에 끝나 있어야 하는 작업. Google은 "UMP 시작 전에 Firebase를 초기화하라"고
    // 요구한다(consent mode를 UMP가 Firebase에 기록하기 때문이다). 이 순서를 문서가 아니라
    // 구조로 강제하려고 둔다 — 광고 초기화가 먼저 불려도 UMP 앞에서 이것들을 기다린다.
    //
    // 구현은 SDK 초기화까지만 기다리고 동의 결과를 기다리면 안 된다(교착).
    public interface IConsentPrerequisite
    {
        Awaitable PrepareAsync();
    }
}
```

`ConsentServiceOptions.cs`:

```csharp
namespace DarkNaku.FoundationDI
{
    // ConsentServiceSettings(SO)가 아니라 이 구조체를 받는다. 서비스가 에셋 형식에 묶이지 않고
    // EditMode 테스트가 SO를 만들 필요가 없다(AdServiceOptions와 같은 이유).
    public readonly struct ConsentServiceOptions
    {
        // 0 이하면 약관 단계를 쓰지 않는다(항상 수락으로 본다).
        public int PolicyVersion { get; }
        public PolicyAgreementContent PolicyContent { get; }
        public bool UseUmp { get; }
        public UmpRequestOptions UmpRequest { get; }
        public bool RequestTrackingAuthorization { get; }

        // UMP 정보 갱신(네트워크)에만 건다. 폼 표시 중에는 사용자가 읽는 시간이라 걸지 않는다.
        public float UmpUpdateTimeoutSeconds { get; }

        public ConsentServiceOptions(int policyVersion, PolicyAgreementContent policyContent, bool useUmp,
                                     UmpRequestOptions umpRequest, bool requestTrackingAuthorization,
                                     float umpUpdateTimeoutSeconds = 10f)
        {
            PolicyVersion = policyVersion;
            PolicyContent = policyContent;
            UseUmp = useUmp;
            UmpRequest = umpRequest;
            RequestTrackingAuthorization = requestTrackingAuthorization;
            UmpUpdateTimeoutSeconds = umpUpdateTimeoutSeconds;
        }
    }
}
```

`IConsentService.cs`:

```csharp
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 약관 수락 → (선행 작업) → Google UMP → (옵션) ATT를 한 흐름으로 묻는다.
    // AdService·AnalyticsService가 초기화 앞에서 RequestAsync를 await하므로 게임이 직접 부를
    // 필요는 없다. 로딩 화면처럼 시점을 직접 고르고 싶을 때만 먼저 부른다.
    public interface IConsentService : IDisposable
    {
        // 재진입 안전하다. 진행 중이면 같은 결과에 편승하고, 직전 결과가 확정(광고 가능 + 분석 판정됨)
        // 이면 즉시 그 결과를 돌려준다. 아니면 UMP 단계만 다시 수행한다. 절대 영원히 매달리지 않는다.
        Awaitable<ConsentResult> RequestAsync();

        // 현재 상태를 매번 다시 읽는다.
        ConsentResult Current { get; }

        bool IsPolicyAccepted { get; }

        // 기존 유저 자동 동의용. 세이브가 있으면 RequestAsync 전에 부른다.
        void AcceptPolicy();

        // 현지화된 문구로 바꾼다. RequestAsync 전에 부른다.
        void SetPolicyContent(PolicyAgreementContent content);

        // 설정 화면에 "개인정보 옵션" 버튼을 보여 줄 조건.
        bool IsPrivacyOptionsRequired { get; }

        // UMP 옵션 폼을 다시 연다. 닫힌 뒤 ConsentChanged를 발행한다.
        Awaitable ShowPrivacyOptionsAsync();

        ITrackingAuthorization TrackingAuthorization { get; }

        // 옵션 폼이 닫혔거나 재판정 결과가 바뀌었을 때.
        event Action ConsentChanged;
    }
}
```

- [ ] **Step 4: 테스트 대역 작성**

`Tests/ConsentTestDoubles.cs`:

```csharp
using System;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using UnityEngine;

// 순서 검증이 핵심이라 NSubstitute 대신 호출을 공유 로그에 기록하는 손 대역을 쓴다
// (AnalyticsTestDoubles와 같은 이유).
public class FakePolicyPrompt : IPolicyAgreementPrompt
{
    private readonly List<string> _log;
    private AwaitableCompletionSource _pending;

    public FakePolicyPrompt(List<string> log) { _log = log; }

    public int ShowCount { get; private set; }
    public int CloseCount { get; private set; }
    public PolicyAgreementContent LastContent { get; private set; }
    public bool IsShowing => _pending != null;
    public bool Throws;

    public Awaitable ShowAsync(PolicyAgreementContent content)
    {
        ShowCount++;
        LastContent = content;
        _log.Add("policy");

        if (Throws) throw new InvalidOperationException("프리팹이 없다");

        _pending = new AwaitableCompletionSource();
        return _pending.Awaitable;
    }

    public void Accept()
    {
        var source = _pending;
        _pending = null;
        source?.SetResult();
    }

    public void Close() => CloseCount++;
}

public class FakePolicyStorage : IPolicyAgreementStorage
{
    public int Version;
    public int SaveCount { get; private set; }

    public int LoadAcceptedVersion() => Version;

    public void SaveAcceptedVersion(int version)
    {
        Version = version;
        SaveCount++;
    }
}

public class FakeUmpConsent : IUmpConsent
{
    private readonly List<string> _log;
    private AwaitableCompletionSource<bool> _pendingUpdate;
    private AwaitableCompletionSource<bool> _pendingForm;

    public FakeUmpConsent(List<string> log) { _log = log; }

    public bool IsSupported { get; set; } = true;
    public UmpConsentStatus Status { get; set; } = UmpConsentStatus.NotRequired;
    public bool CanRequestAds { get; set; } = true;
    public bool IsPrivacyOptionsRequired { get; set; }
    public TcfSignals Tcf { get; set; } = TcfSignals.None;

    public bool DeferUpdate;
    public bool DeferForm;
    public bool UpdateResult = true;
    public bool ThrowOnUpdate;
    public bool ThrowOnForm;
    public bool ThrowOnRead;

    // 폼이 닫힐 때 상태를 바꾸는 데 쓴다(사용자가 동의/거부한 결과를 흉내).
    public Action OnForm;
    public Action OnPrivacyOptions;

    public int UpdateCount { get; private set; }
    public int FormCount { get; private set; }
    public int PrivacyOptionsCount { get; private set; }
    public UmpRequestOptions LastOptions { get; private set; }
    public bool IsUpdatePending => _pendingUpdate != null;
    public bool IsFormPending => _pendingForm != null;

    public TcfSignals ReadTcf()
    {
        if (ThrowOnRead) throw new InvalidOperationException("네이티브 읽기 실패");
        return Tcf;
    }

    public Awaitable<bool> UpdateAsync(UmpRequestOptions options)
    {
        UpdateCount++;
        LastOptions = options;
        _log.Add("ump.update");

        if (ThrowOnUpdate) throw new InvalidOperationException("네이티브 예외");

        var source = new AwaitableCompletionSource<bool>();

        if (DeferUpdate) _pendingUpdate = source;
        else source.SetResult(UpdateResult);

        return source.Awaitable;
    }

    public void CompleteUpdate(bool ok)
    {
        var source = _pendingUpdate;
        _pendingUpdate = null;
        source?.SetResult(ok);
    }

    public Awaitable<bool> LoadAndShowIfRequiredAsync()
    {
        FormCount++;
        _log.Add("ump.form");

        if (ThrowOnForm) throw new InvalidOperationException("폼 예외");

        var source = new AwaitableCompletionSource<bool>();

        if (DeferForm)
        {
            _pendingForm = source;
        }
        else
        {
            OnForm?.Invoke();
            source.SetResult(true);
        }

        return source.Awaitable;
    }

    public void CompleteForm()
    {
        var source = _pendingForm;
        _pendingForm = null;
        OnForm?.Invoke();
        source?.SetResult(true);
    }

    public Awaitable<bool> ShowPrivacyOptionsAsync()
    {
        PrivacyOptionsCount++;
        _log.Add("ump.options");
        OnPrivacyOptions?.Invoke();

        var source = new AwaitableCompletionSource<bool>();
        source.SetResult(true);
        return source.Awaitable;
    }
}

// 다이얼로그를 손으로 돌리는 가짜 ATT. RequestAsync는 Respond를 부를 때까지 완료되지 않는다.
public class FakeTrackingAuthorization : ITrackingAuthorization
{
    private readonly List<string> _log;
    private AwaitableCompletionSource<TrackingAuthorizationStatus> _pending;

    public FakeTrackingAuthorization(List<string> log = null) { _log = log ?? new List<string>(); }

    public bool IsSupported { get; set; } = true;
    public TrackingAuthorizationStatus Status { get; set; } = TrackingAuthorizationStatus.NotDetermined;
    public int RequestCount { get; private set; }
    public bool Throws { get; set; }
    public bool IsWaiting => _pending != null;

    public Awaitable<TrackingAuthorizationStatus> RequestAsync()
    {
        RequestCount++;
        _log.Add("att");

        if (Throws) throw new InvalidOperationException("요청이 이미 진행 중이다");

        _pending = new AwaitableCompletionSource<TrackingAuthorizationStatus>();
        return _pending.Awaitable;
    }

    public void Respond(TrackingAuthorizationStatus status)
    {
        var source = _pending;
        _pending = null;
        Status = status;
        source?.SetResult(status);
    }
}

public class FakeConsentPrerequisite : IConsentPrerequisite
{
    private readonly List<string> _log;
    private readonly string _name;
    private AwaitableCompletionSource _pending;

    public FakeConsentPrerequisite(List<string> log, string name = "prereq")
    {
        _log = log;
        _name = name;
    }

    public bool Defer;
    public bool Throws;
    public int PrepareCount { get; private set; }
    public bool IsPending => _pending != null;

    public Awaitable PrepareAsync()
    {
        PrepareCount++;
        _log.Add(_name);

        if (Throws) throw new InvalidOperationException("선행 작업 실패");

        var source = new AwaitableCompletionSource();

        if (Defer) _pending = source;
        else source.SetResult();

        return source.Awaitable;
    }

    public void Complete()
    {
        var source = _pending;
        _pending = null;
        source?.SetResult();
    }
}

// AdService·AnalyticsService 테스트용. 동의 흐름 자체는 ConsentService 테스트가 검증한다.
public class FakeConsentService : IConsentService
{
    private readonly List<AwaitableCompletionSource<ConsentResult>> _pending = new();

    public ConsentResult Result = ConsentResults.Granted();
    public bool Defer;
    public int RequestCount { get; private set; }
    public bool IsDisposed { get; private set; }

    public event Action ConsentChanged;

    public Awaitable<ConsentResult> RequestAsync()
    {
        RequestCount++;

        var source = new AwaitableCompletionSource<ConsentResult>();

        if (Defer) _pending.Add(source);
        else source.SetResult(Result);

        return source.Awaitable;
    }

    public void Complete(ConsentResult result)
    {
        Result = result;

        var pending = new List<AwaitableCompletionSource<ConsentResult>>(_pending);
        _pending.Clear();

        foreach (var source in pending) source.SetResult(result);
    }

    public void RaiseChanged(ConsentResult result)
    {
        Result = result;
        ConsentChanged?.Invoke();
    }

    public ConsentResult Current => Result;
    public bool IsPolicyAccepted => Result.PolicyAccepted;
    public void AcceptPolicy() { }
    public void SetPolicyContent(PolicyAgreementContent content) { }
    public bool IsPrivacyOptionsRequired => false;

    public Awaitable ShowPrivacyOptionsAsync()
    {
        var source = new AwaitableCompletionSource();
        source.SetResult();
        return source.Awaitable;
    }

    public ITrackingAuthorization TrackingAuthorization { get; } = new UnsupportedTrackingAuthorization();

    public void Dispose() => IsDisposed = true;
}

public static class ConsentResults
{
    public static ConsentResult Granted(TrackingAuthorizationStatus att = TrackingAuthorizationStatus.NotDetermined) =>
        new(true, true, AnalyticsConsent.Granted, att, TcfSignals.None);

    public static ConsentResult Denied() =>
        new(true, true, AnalyticsConsent.Denied, TrackingAuthorizationStatus.NotDetermined, new TcfSignals(true, "0000000000"));

    public static ConsentResult Unknown() =>
        new(true, false, AnalyticsConsent.Unknown, TrackingAuthorizationStatus.NotDetermined, TcfSignals.None);

    public static ConsentResult PolicyNotAccepted() =>
        new(false, false, AnalyticsConsent.Unknown, TrackingAuthorizationStatus.NotDetermined, TcfSignals.None);
}
```

`ConsentService`를 조립하는 `ConsentFixture`는 그 타입이 생기는 Task 5에서 추가한다.

- [ ] **Step 5: 통과 확인**

Run: `PolicyAgreementStorageTest`, 이어서 전체 테스트
Expected: 전부 PASS.

- [ ] **Step 6: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ConsentService Assets/FoundationDI/Tests/ConsentTestDoubles.cs* Assets/FoundationDI/Tests/PolicyAgreementStorageTest.cs*
git commit -m "[BEHAVIORAL] 동의 타입과 seam, PlayerPrefs 약관 저장소를 추가한다"
```

---

### Task 4: 분석 동의 판정 (순수 함수)

**Files:**
- Create: `Runtime/Services/ConsentService/AnalyticsConsentJudge.cs`
- Test: `Tests/AnalyticsConsentJudgeTest.cs`

**Interfaces:**
- Produces: `internal static class AnalyticsConsentJudge { public static AnalyticsConsent Judge(UmpConsentStatus status, TcfSignals tcf); }`

규칙(스펙 §7.2): 규제 대상 아님 → Granted / GDPR 적용 + Purpose 1 동의 → Granted / GDPR 적용 + Purpose 1 거부 → Denied / GDPR 적용인데 목적 문자열 없음 → Unknown / GDPR 미적용 → Granted / 판정 정보 없음 → Unknown.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class AnalyticsConsentJudgeTest
{
    [Test]
    public void 규제_대상이_아니면_허용한다()
    {
        Assert.AreEqual(AnalyticsConsent.Granted,
                        AnalyticsConsentJudge.Judge(UmpConsentStatus.NotRequired, TcfSignals.None));
    }

    [Test]
    public void GDPR_지역에서_목적1에_동의하면_허용한다()
    {
        Assert.AreEqual(AnalyticsConsent.Granted,
                        AnalyticsConsentJudge.Judge(UmpConsentStatus.Obtained, new TcfSignals(true, "1000000000")));
    }

    [Test]
    public void GDPR_지역에서_목적1을_거부하면_거부한다()
    {
        Assert.AreEqual(AnalyticsConsent.Denied,
                        AnalyticsConsentJudge.Judge(UmpConsentStatus.Obtained, new TcfSignals(true, "0111111111")));
    }

    [Test]
    public void GDPR_지역인데_목적_문자열이_없으면_아직_모른다()
    {
        // 폼이 아직 안 떴거나 실패한 경우다. 거부로 보면 버퍼를 버리게 되는데 사용자는 거부한 적이 없다.
        Assert.AreEqual(AnalyticsConsent.Unknown,
                        AnalyticsConsentJudge.Judge(UmpConsentStatus.Required, new TcfSignals(true, null)));
    }

    [Test]
    public void GDPR이_적용되지_않으면_허용한다()
    {
        Assert.AreEqual(AnalyticsConsent.Granted,
                        AnalyticsConsentJudge.Judge(UmpConsentStatus.Obtained, new TcfSignals(false, null)));
    }

    [Test]
    public void 판정_정보가_없으면_아직_모른다()
    {
        // 첫 실행 오프라인: 상태 Unknown, TCF 키 없음.
        Assert.AreEqual(AnalyticsConsent.Unknown,
                        AnalyticsConsentJudge.Judge(UmpConsentStatus.Unknown, TcfSignals.None));
    }

    [Test]
    public void 동의를_받았는데_GDPR_키가_없으면_허용한다()
    {
        // 미국 주 규정 메시지만 쓰는 경우 등. 옵트아웃 체계라 분석은 기본 허용이다.
        Assert.AreEqual(AnalyticsConsent.Granted,
                        AnalyticsConsentJudge.Judge(UmpConsentStatus.Obtained, TcfSignals.None));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `AnalyticsConsentJudgeTest`
Expected: 컴파일 에러 — `AnalyticsConsentJudge` 없음.

- [ ] **Step 3: 구현**

```csharp
namespace DarkNaku.FoundationDI
{
    // UMP 결과를 "분석 수집을 켜도 되는가"로 번역한다.
    //
    // GDPR이 적용되면 TCF Purpose 1(기기 저장·접근)을 본다. Firebase의 analytics_storage는 UMP
    // consent mode가 따로 기록하지만 그 값을 읽는 공개 경로가 없어 Purpose 1로 근사한다 —
    // 실기에서 대조해 확정할 항목이다(스펙 위험 R3). 판정 규칙이 바뀌면 이 함수만 고친다.
    internal static class AnalyticsConsentJudge
    {
        public static AnalyticsConsent Judge(UmpConsentStatus status, TcfSignals tcf)
        {
            if (status == UmpConsentStatus.NotRequired) return AnalyticsConsent.Granted;

            if (tcf.GdprApplies == true)
            {
                if (string.IsNullOrEmpty(tcf.PurposeConsents)) return AnalyticsConsent.Unknown;

                return tcf.HasPurpose(1) ? AnalyticsConsent.Granted : AnalyticsConsent.Denied;
            }

            if (tcf.GdprApplies == false) return AnalyticsConsent.Granted;

            return status == UmpConsentStatus.Obtained ? AnalyticsConsent.Granted : AnalyticsConsent.Unknown;
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `AnalyticsConsentJudgeTest` → 7개 PASS. 전체 테스트 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ConsentService/AnalyticsConsentJudge.cs* Assets/FoundationDI/Tests/AnalyticsConsentJudgeTest.cs*
git commit -m "[BEHAVIORAL] UMP 결과를 분석 수집 동의로 판정한다"
```

---

### Task 5: ConsentService — 약관 단계

**Files:**
- Create: `Runtime/Services/ConsentService/ConsentService.cs`
- Modify: `Tests/ConsentTestDoubles.cs` (Task 3에서 미뤄 둔 `ConsentFixture` 추가)
- Test: `Tests/ConsentServicePolicyTest.cs`

**Interfaces:**
- Consumes: Task 3의 모든 타입, `IMainThreadDispatcher`, `ITrackingAuthorization`, `UnsupportedTrackingAuthorization`.
- Produces: `public sealed class ConsentService : IConsentService` with ctor `(ConsentServiceOptions options, IPolicyAgreementPrompt prompt, IPolicyAgreementStorage storage, IUmpConsent ump, ITrackingAuthorization tracking, IMainThreadDispatcher dispatcher, IReadOnlyList<IConsentPrerequisite> prerequisites = null)`.

이 Task에서 `ConsentService`의 전체 골격을 만든다. UMP·ATT·선행 작업 단계는 빈 메서드로 두고 Task 6~7에서 채운다.

- [ ] **Step 1: 픽스처를 대역 파일에 추가**

`ConsentTestDoubles.cs` 끝에 붙인다.

```csharp
// ConsentService를 조립하는 테스트 픽스처. 기본값은 "약관 v1, UMP 사용, ATT 사용"이지만
// ATT 대역은 IsSupported=false로 시작한다 — ATT를 다루지 않는 테스트가 Respond를 기다리며
// 멈추지 않게 하기 위해서다.
public class ConsentFixture
{
    public readonly List<string> Log = new();
    public readonly FakePolicyPrompt Prompt;
    public readonly FakePolicyStorage Storage = new();
    public readonly FakeUmpConsent Ump;
    public readonly FakeTrackingAuthorization Tracking;
    public readonly FakeAdDispatcher Dispatcher = new();
    public readonly List<IConsentPrerequisite> Prerequisites = new();

    public int PolicyVersion = 1;
    public bool UseUmp = true;
    public bool RequestTracking = true;
    public float UpdateTimeout = 10f;
    public UmpRequestOptions UmpRequest = new(UmpDebugGeography.Disabled, new List<string>(), false);
    public PolicyAgreementContent Content = new("약관", "본문", "동의", "이용약관", "https://t", "개인정보처리방침", "https://p");

    public ConsentFixture()
    {
        Prompt = new FakePolicyPrompt(Log);
        Ump = new FakeUmpConsent(Log);
        Tracking = new FakeTrackingAuthorization(Log) { IsSupported = false };
    }

    public ConsentServiceOptions Options =>
        new(PolicyVersion, Content, UseUmp, UmpRequest, RequestTracking, UpdateTimeout);

    public ConsentService Build() =>
        new(Options, Prompt, Storage, Ump, Tracking, Dispatcher, Prerequisites);
}
```

- [ ] **Step 2: 실패하는 테스트 작성**

`Tests/ConsentServicePolicyTest.cs`:

```csharp
using System.Collections;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class ConsentServicePolicyTest
{
    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 수락하지_않았으면_약관_창을_띄우고_수락할_때까지_기다린다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        var sut = f.Build();

        var requesting = sut.RequestAsync();

        Assert.IsTrue(f.Prompt.IsShowing, "약관 창을 띄우지 않았다");
        Assert.AreEqual(0, f.Storage.SaveCount, "수락 전에 저장했다");
        Assert.AreEqual(0, f.Ump.UpdateCount, "약관 수락 전에 UMP를 시작했다");

        f.Prompt.Accept();
        var result = await requesting;

        Assert.IsTrue(result.PolicyAccepted);
        Assert.AreEqual(1, f.Storage.Version, "수락한 버전을 저장하지 않았다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 이미_수락한_버전이면_창을_띄우지_않는다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        f.Storage.Version = 1;

        var result = await f.Build().RequestAsync();

        Assert.AreEqual(0, f.Prompt.ShowCount);
        Assert.IsTrue(result.PolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 약관_버전이_오르면_다시_묻는다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture { PolicyVersion = 2 };
        f.Storage.Version = 1;
        var sut = f.Build();

        var requesting = sut.RequestAsync();

        Assert.IsTrue(f.Prompt.IsShowing, "약관이 바뀌었는데 다시 묻지 않았다");

        f.Prompt.Accept();
        await requesting;

        Assert.AreEqual(2, f.Storage.Version);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 약관_버전이_0이면_약관_단계를_쓰지_않는다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture { PolicyVersion = 0 };

        var result = await f.Build().RequestAsync();

        Assert.AreEqual(0, f.Prompt.ShowCount);
        Assert.IsTrue(result.PolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator AcceptPolicy를_부르면_창_없이_수락으로_본다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        var sut = f.Build();

        sut.AcceptPolicy();
        var result = await sut.RequestAsync();

        Assert.AreEqual(0, f.Prompt.ShowCount, "기존 유저에게 약관 창을 띄웠다");
        Assert.IsTrue(result.PolicyAccepted);
        Assert.IsTrue(sut.IsPolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator SetPolicyContent로_바꾼_문구를_창에_넘긴다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        var sut = f.Build();
        var localized = new PolicyAgreementContent("Terms", "Body", "Agree", "ToS", "https://t", "Privacy", "https://p");

        sut.SetPolicyContent(localized);
        var requesting = sut.RequestAsync();

        Assert.AreEqual("Terms", f.Prompt.LastContent.Title);

        f.Prompt.Accept();
        await requesting;
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 약관_창이_예외를_던지면_수락하지_않음으로_완료한다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        f.Prompt.Throws = true;

        LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("ConsentService"));

        var result = await f.Build().RequestAsync();

        Assert.IsFalse(result.PolicyAccepted, "보지도 않은 약관을 수락한 것으로 처리했다");
        Assert.IsFalse(result.CanRequestAds);
        Assert.AreEqual(AnalyticsConsent.Unknown, result.AnalyticsConsent);
        Assert.AreEqual(0, f.Storage.SaveCount);
    });
}
```

- [ ] **Step 3: 실패 확인**

Run: `ConsentServicePolicyTest`
Expected: 컴파일 에러 — `ConsentService` 없음.

- [ ] **Step 4: ConsentService 골격 구현**

`Runtime/Services/ConsentService/ConsentService.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 약관 수락 → 선행 작업 → Google UMP → ATT를 한 흐름으로 묻는다.
    //
    // 실패해도 게임을 막지 않는다. UMP 실패·예외는 경고만 남기고 캐시된 이전 세션 값으로 진행한다
    // (TileExplorer·VitaMahjong과 같은 정책). 약관만은 예외다 — 수락하지 않으면 PolicyAccepted=false로
    // 끝나고 광고·분석이 시작되지 않는다.
    public sealed class ConsentService : IConsentService
    {
        private readonly ConsentServiceOptions _options;
        private readonly IPolicyAgreementPrompt _prompt;
        private readonly IPolicyAgreementStorage _storage;
        private readonly IUmpConsent _ump;
        private readonly ITrackingAuthorization _tracking;
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly List<IConsentPrerequisite> _prerequisites;

        // 진행 중인 흐름에 편승한 호출자들. Awaitable은 단일 사용이라 호출자마다 완료 소스를 둔다.
        private readonly List<AwaitableCompletionSource<ConsentResult>> _waiters = new();

        private PolicyAgreementContent _content;
        private ConsentResult _last;
        private bool _hasCompleted;
        private bool _running;
        private bool _prerequisitesDone;
        private bool _trackingAsked;
        private bool _disposed;

        public event Action ConsentChanged;

        public ConsentService(ConsentServiceOptions options, IPolicyAgreementPrompt prompt,
                              IPolicyAgreementStorage storage, IUmpConsent ump,
                              ITrackingAuthorization tracking, IMainThreadDispatcher dispatcher,
                              IReadOnlyList<IConsentPrerequisite> prerequisites = null)
        {
            _options = options;
            _prompt = prompt;
            _storage = storage;
            _ump = ump ?? new UnsupportedUmpConsent();
            _tracking = tracking ?? new UnsupportedTrackingAuthorization();
            _dispatcher = dispatcher;
            _prerequisites = prerequisites == null
                ? new List<IConsentPrerequisite>()
                : new List<IConsentPrerequisite>(prerequisites);
            _content = options.PolicyContent;
        }

        public ITrackingAuthorization TrackingAuthorization => _tracking;

        public bool IsPolicyAccepted =>
            _options.PolicyVersion <= 0 ||
            (_storage != null && _storage.LoadAcceptedVersion() >= _options.PolicyVersion);

        public bool IsPrivacyOptionsRequired => UsesUmp && SafeRead(() => _ump.IsPrivacyOptionsRequired, false);

        public ConsentResult Current => Snapshot();

        // UMP를 켰더라도 네이티브가 없으면(에디터·포드 누락) 쓰지 않는 것으로 본다.
        private bool UsesUmp => _options.UseUmp && _ump.IsSupported;

        public void AcceptPolicy()
        {
            if (_disposed || _options.PolicyVersion <= 0) return;

            _storage?.SaveAcceptedVersion(_options.PolicyVersion);
        }

        public void SetPolicyContent(PolicyAgreementContent content) => _content = content;

        public Awaitable<ConsentResult> RequestAsync()
        {
            if (_disposed) return Completed(Snapshot());

            if (_hasCompleted && !_running && IsSettled(_last)) return Completed(_last);

            var waiter = new AwaitableCompletionSource<ConsentResult>();
            _waiters.Add(waiter);

            if (!_running)
            {
                _running = true;
                Run();
            }

            return waiter.Awaitable;
        }

        public async Awaitable ShowPrivacyOptionsAsync()
        {
            if (_disposed || !UsesUmp) return;

            try
            {
                await _ump.ShowPrivacyOptionsAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConsentService] 개인정보 옵션 폼 표시 중 예외가 났다: {e}");
            }

            if (_disposed) return;

            _last = Snapshot();
            ConsentChanged?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            try
            {
                _prompt?.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConsentService] 약관 창 정리 중 예외: {e}");
            }

            CompleteWaiters(Snapshot());
            ConsentChanged = null;
        }

        // 직전 결과로 충분한가. 광고를 요청할 수 있고 분석 판정이 났으면 다시 물을 이유가 없다.
        private static bool IsSettled(ConsentResult result) =>
            result.PolicyAccepted && result.CanRequestAds && result.AnalyticsConsent != AnalyticsConsent.Unknown;

        // async void인 이유: 결과는 _waiters로 전달하고, 예외는 안에서 전부 잡는다.
        private async void Run()
        {
            try
            {
                if (!IsPolicyAccepted)
                {
                    if (_prompt == null)
                    {
                        throw new InvalidOperationException("약관 수락 창(IPolicyAgreementPrompt)이 등록되지 않았다.");
                    }

                    await _prompt.ShowAsync(_content);

                    if (_disposed) return;

                    _storage?.SaveAcceptedVersion(_options.PolicyVersion);
                }

                await RunPrerequisitesAsync();
                if (_disposed) return;

                await RunUmpAsync();
                if (_disposed) return;

                await RunTrackingAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConsentService] 동의 흐름이 중단됐다. 현재 상태로 완료한다: {e}");
            }
            finally
            {
                Finish();
            }
        }

        private Awaitable RunPrerequisitesAsync() => Completed();

        private Awaitable RunUmpAsync() => Completed();

        private Awaitable RunTrackingAsync() => Completed();

        private void Finish()
        {
            _running = false;

            // Dispose가 대기자를 이미 완료했다.
            if (_disposed) return;

            var hadCompleted = _hasCompleted;
            var previous = _last;

            _last = Snapshot();
            _hasCompleted = true;

            CompleteWaiters(_last);

            if (hadCompleted && !previous.Equals(_last)) ConsentChanged?.Invoke();
        }

        private ConsentResult Snapshot()
        {
            var trackingStatus = SafeRead(() => _tracking.Status, TrackingAuthorizationStatus.NotDetermined);

            if (!IsPolicyAccepted)
            {
                return new ConsentResult(false, false, AnalyticsConsent.Unknown, trackingStatus, TcfSignals.None);
            }

            if (!UsesUmp)
            {
                return new ConsentResult(true, true, AnalyticsConsent.Granted, trackingStatus, TcfSignals.None);
            }

            var tcf = SafeRead(() => _ump.ReadTcf(), TcfSignals.None);
            var status = SafeRead(() => _ump.Status, UmpConsentStatus.Unknown);
            var canRequestAds = SafeRead(() => _ump.CanRequestAds, false);

            return new ConsentResult(true, canRequestAds, AnalyticsConsentJudge.Judge(status, tcf),
                                     trackingStatus, tcf);
        }

        // 네이티브 읽기는 JNI·P/Invoke라 던질 수 있다. 상태 조회가 흐름을 깨지 않게 한다.
        private static T SafeRead<T>(Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConsentService] 동의 상태를 읽지 못했다: {e.Message}");
                return fallback;
            }
        }

        private void CompleteWaiters(ConsentResult result)
        {
            if (_waiters.Count == 0) return;

            var waiters = new List<AwaitableCompletionSource<ConsentResult>>(_waiters);
            _waiters.Clear();

            foreach (var waiter in waiters) waiter.TrySetResult(result);
        }

        private static Awaitable<ConsentResult> Completed(ConsentResult value)
        {
            var source = new AwaitableCompletionSource<ConsentResult>();
            source.SetResult(value);
            return source.Awaitable;
        }

        private static Awaitable Completed()
        {
            var source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }
    }
}
```

- [ ] **Step 5: 통과 확인**

Run: `ConsentServicePolicyTest` → 7개 PASS. 전체 PASS.

- [ ] **Step 6: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ConsentService/ConsentService.cs* Assets/FoundationDI/Tests/ConsentTestDoubles.cs Assets/FoundationDI/Tests/ConsentServicePolicyTest.cs*
git commit -m "[BEHAVIORAL] ConsentService가 약관 수락을 받고 버전을 저장한다"
```

---

### Task 6: ConsentService — 선행 작업과 UMP 단계

**Files:**
- Modify: `Runtime/Services/ConsentService/ConsentService.cs` (`RunPrerequisitesAsync`, `RunUmpAsync` 채우기, `UpdateWithTimeoutAsync` 추가)
- Test: `Tests/ConsentServiceUmpTest.cs`

**Interfaces:**
- Consumes: Task 5의 `ConsentService`, `ConsentFixture`, `FakeConsentPrerequisite`.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class ConsentServiceUmpTest
{
    private static ConsentFixture Accepted()
    {
        var f = new ConsentFixture();
        f.Storage.Version = 1;
        return f;
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 약관_다음에_선행_작업을_끝내고_UMP를_시작한다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        var prerequisite = new FakeConsentPrerequisite(f.Log) { Defer = true };
        f.Prerequisites.Add(prerequisite);
        var sut = f.Build();

        var requesting = sut.RequestAsync();
        f.Prompt.Accept();

        Assert.IsTrue(prerequisite.IsPending);
        Assert.AreEqual(0, f.Ump.UpdateCount, "선행 작업(Firebase 초기화)이 끝나기 전에 UMP를 시작했다");

        prerequisite.Complete();
        await requesting;

        CollectionAssert.AreEqual(new[] { "policy", "prereq", "ump.update", "ump.form" }, f.Log);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 요청_옵션을_UMP에_넘긴다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.UmpRequest = new UmpRequestOptions(UmpDebugGeography.Eea, new List<string> { "hash" }, true);

        await f.Build().RequestAsync();

        Assert.AreEqual(UmpDebugGeography.Eea, f.Ump.LastOptions.DebugGeography);
        Assert.AreEqual("hash", f.Ump.LastOptions.TestDeviceHashedIds[0]);
        Assert.IsTrue(f.Ump.LastOptions.TagForUnderAgeOfConsent);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 폼에서_동의한_결과를_돌려준다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.Status = UmpConsentStatus.Required;
        f.Ump.CanRequestAds = false;
        f.Ump.OnForm = () =>
        {
            f.Ump.Status = UmpConsentStatus.Obtained;
            f.Ump.CanRequestAds = true;
            f.Ump.Tcf = new TcfSignals(true, "1111111111");
        };

        var result = await f.Build().RequestAsync();

        Assert.IsTrue(result.CanRequestAds);
        Assert.AreEqual(AnalyticsConsent.Granted, result.AnalyticsConsent);
        Assert.AreEqual(true, result.Tcf.GdprApplies);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 폼에서_거부해도_게임은_진행되고_분석은_거부가_된다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.OnForm = () =>
        {
            f.Ump.Status = UmpConsentStatus.Obtained;
            f.Ump.CanRequestAds = true;   // 거부해도 비개인화 광고는 요청할 수 있다
            f.Ump.Tcf = new TcfSignals(true, "0000000000");
        };

        var result = await f.Build().RequestAsync();

        Assert.IsTrue(result.PolicyAccepted);
        Assert.IsTrue(result.CanRequestAds);
        Assert.AreEqual(AnalyticsConsent.Denied, result.AnalyticsConsent);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP를_끄면_호출하지_않고_허용으로_본다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.UseUmp = false;

        var result = await f.Build().RequestAsync();

        Assert.AreEqual(0, f.Ump.UpdateCount);
        Assert.IsTrue(result.CanRequestAds);
        Assert.AreEqual(AnalyticsConsent.Granted, result.AnalyticsConsent);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP_네이티브가_없으면_허용으로_본다() => AwaitableTest.Run(async () =>
    {
        // 에디터와 포드·aar가 빠진 빌드. 막으면 에디터에서 광고 테스트가 안 된다.
        var f = Accepted();
        f.Ump.IsSupported = false;

        var result = await f.Build().RequestAsync();

        Assert.AreEqual(0, f.Ump.UpdateCount);
        Assert.IsTrue(result.CanRequestAds);
        Assert.AreEqual(AnalyticsConsent.Granted, result.AnalyticsConsent);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 선행_작업은_세션에_한_번만_돈다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        var prerequisite = new FakeConsentPrerequisite(f.Log);
        f.Prerequisites.Add(prerequisite);
        f.Ump.CanRequestAds = false;   // 확정되지 않아 두 번째 요청이 다시 돈다
        var sut = f.Build();

        await sut.RequestAsync();
        await sut.RequestAsync();

        Assert.AreEqual(1, prerequisite.PrepareCount);
        Assert.AreEqual(2, f.Ump.UpdateCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 선행_작업이_실패해도_UMP는_진행한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Prerequisites.Add(new FakeConsentPrerequisite(f.Log) { Throws = true });

        LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("선행 작업"));

        await f.Build().RequestAsync();

        Assert.AreEqual(1, f.Ump.UpdateCount);
    });
}
```

- [ ] **Step 2: 실패 확인**

Run: `ConsentServiceUmpTest`
Expected: FAIL — 순서 로그에 `prereq`/`ump.*`가 없다(빈 단계).

- [ ] **Step 3: 단계 구현**

`ConsentService.cs`에서 빈 `RunPrerequisitesAsync`, `RunUmpAsync`를 교체하고 `UpdateWithTimeoutAsync`를 추가한다.

```csharp
        private async Awaitable RunPrerequisitesAsync()
        {
            if (_prerequisitesDone) return;

            foreach (var prerequisite in _prerequisites)
            {
                try
                {
                    await prerequisite.PrepareAsync();
                }
                catch (Exception e)
                {
                    // Firebase 초기화가 실패했다고 동의 흐름을 멈추면 광고까지 죽는다.
                    Debug.LogWarning($"[ConsentService] 선행 작업이 실패했다. UMP를 계속 진행한다: {e}");
                }

                if (_disposed) return;
            }

            _prerequisitesDone = true;
        }

        private async Awaitable RunUmpAsync()
        {
            if (!UsesUmp) return;

            var updated = await UpdateWithTimeoutAsync();

            if (_disposed || !updated) return;

            try
            {
                await _ump.LoadAndShowIfRequiredAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConsentService] UMP 동의 폼 표시 중 예외가 났다. 계속 진행한다: {e}");
            }
        }

        // 네트워크 단계에만 타임아웃을 건다. 늦게 도착한 결과는 버린다 — 캐시된 값으로 이미 진행했다.
        private async Awaitable<bool> UpdateWithTimeoutAsync()
        {
            var source = new AwaitableCompletionSource<bool>();
            var settled = false;
            IDisposable timer = null;

            void Settle(bool ok)
            {
                if (settled) return;

                settled = true;
                timer?.Dispose();
                source.SetResult(ok);
            }

            async void Forward()
            {
                var ok = false;

                try
                {
                    ok = await _ump.UpdateAsync(_options.UmpRequest);

                    if (!ok) Debug.LogWarning("[ConsentService] UMP 동의 정보 갱신이 실패했다. 이전 값으로 진행한다.");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ConsentService] UMP 동의 정보 갱신 중 예외가 났다. 이전 값으로 진행한다: {e}");
                }

                Settle(ok);
            }

            timer = _dispatcher?.Delay(_options.UmpUpdateTimeoutSeconds, () =>
            {
                if (settled) return;

                Debug.LogWarning($"[ConsentService] UMP 동의 정보 갱신이 {_options.UmpUpdateTimeoutSeconds}초 안에 끝나지 않았다. 이전 값으로 진행한다.");
                Settle(false);
            });

            Forward();

            return await source.Awaitable;
        }
```

- [ ] **Step 4: 통과 확인**

Run: `ConsentServiceUmpTest` → 8개 PASS. 전체 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ConsentService/ConsentService.cs Assets/FoundationDI/Tests/ConsentServiceUmpTest.cs*
git commit -m "[BEHAVIORAL] 약관 다음에 선행 작업을 끝내고 UMP 동의를 받는다"
```

---

### Task 7: ConsentService — ATT 단계 (AdService에서 이관한 규칙)

**Files:**
- Modify: `Runtime/Services/ConsentService/ConsentService.cs` (`RunTrackingAsync`)
- Test: `Tests/ConsentServiceTrackingTest.cs`

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ConsentServiceTrackingTest
{
    private static ConsentFixture WithTracking()
    {
        var f = new ConsentFixture();
        f.Storage.Version = 1;
        f.Tracking.IsSupported = true;
        return f;
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP_다음에_추적_권한을_묻고_응답을_결과에_싣는다() => AwaitableTest.Run(async () =>
    {
        var f = WithTracking();
        var requesting = f.Build().RequestAsync();

        Assert.IsTrue(f.Tracking.IsWaiting, "추적 권한을 묻지 않았다");
        CollectionAssert.AreEqual(new[] { "ump.update", "ump.form", "att" }, f.Log);

        f.Tracking.Respond(TrackingAuthorizationStatus.Denied);
        var result = await requesting;

        Assert.AreEqual(TrackingAuthorizationStatus.Denied, result.TrackingStatus);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 이미_답한_상태면_다시_묻지_않는다() => AwaitableTest.Run(async () =>
    {
        // UMP의 IDFA 설명 메시지가 ATT를 먼저 띄운 경우도 이 경로다.
        var f = WithTracking();
        f.Tracking.Status = TrackingAuthorizationStatus.Authorized;

        var result = await f.Build().RequestAsync();

        Assert.AreEqual(0, f.Tracking.RequestCount);
        Assert.AreEqual(TrackingAuthorizationStatus.Authorized, result.TrackingStatus);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 지원하지_않는_플랫폼에서는_묻지_않는다() => AwaitableTest.Run(async () =>
    {
        var f = WithTracking();
        f.Tracking.IsSupported = false;

        await f.Build().RequestAsync();

        Assert.AreEqual(0, f.Tracking.RequestCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 설정이_꺼져_있으면_묻지_않지만_게임이_직접_물을_수_있다() => AwaitableTest.Run(async () =>
    {
        var f = WithTracking();
        f.RequestTracking = false;
        var sut = f.Build();

        await sut.RequestAsync();

        Assert.AreEqual(0, f.Tracking.RequestCount, "설정을 껐는데도 다이얼로그를 띄웠다");
        Assert.AreSame(f.Tracking, sut.TrackingAuthorization);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 묻지_않기로_했어도_현재_상태는_결과에_싣는다() => AwaitableTest.Run(async () =>
    {
        var f = WithTracking();
        f.RequestTracking = false;
        f.Tracking.Status = TrackingAuthorizationStatus.Denied;

        var result = await f.Build().RequestAsync();

        Assert.AreEqual(TrackingAuthorizationStatus.Denied, result.TrackingStatus);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 권한_요청이_예외를_던져도_완료된다() => AwaitableTest.Run(async () =>
    {
        var f = WithTracking();
        f.Tracking.Throws = true;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ATT 권한 요청"));

        var result = await f.Build().RequestAsync();

        Assert.IsTrue(result.PolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 다시_요청해도_추적_권한은_세션에_한_번만_묻는다() => AwaitableTest.Run(async () =>
    {
        var f = WithTracking();
        f.Ump.CanRequestAds = false;   // 확정되지 않아 재요청이 다시 돈다
        var sut = f.Build();

        var first = sut.RequestAsync();
        f.Tracking.Respond(TrackingAuthorizationStatus.NotDetermined);   // 무시된 경우를 흉내
        await first;
        await sut.RequestAsync();

        Assert.AreEqual(1, f.Tracking.RequestCount);
    });
}
```

- [ ] **Step 2: 실패 확인**

Run: `ConsentServiceTrackingTest`
Expected: FAIL — `att`가 기록되지 않는다.

- [ ] **Step 3: 구현**

`RunTrackingAsync`를 교체한다.

```csharp
        private async Awaitable RunTrackingAsync()
        {
            // 재판정(RequestAsync 재호출)은 UMP만 다시 한다. ATT는 iOS가 한 번만 띄우므로
            // 다시 물어도 왕복만 낭비되고, 무시된 경우의 재요청은 AttTrackingAuthorization이 이미 한다.
            if (_trackingAsked) return;

            _trackingAsked = true;

            if (!_options.RequestTrackingAuthorization) return;
            if (!_tracking.IsSupported) return;

            // UMP의 IDFA 설명 메시지가 ATT를 이미 띄웠다면 여기서 걸러진다.
            if (_tracking.Status != TrackingAuthorizationStatus.NotDetermined) return;

            try
            {
                var status = await _tracking.RequestAsync();

                Debug.Log($"[ConsentService] ATT 추적 권한: {status}");
            }
            catch (Exception e)
            {
                // 권한 요청이 깨져도 흐름은 끝나야 한다. 비타게팅 광고는 여전히 나간다.
                Debug.LogWarning($"[ConsentService] ATT 권한 요청 중 예외가 났다. 계속 진행한다: {e}");
            }
        }
```

- [ ] **Step 4: 통과 확인**

Run: `ConsentServiceTrackingTest` → 7개 PASS. 전체 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ConsentService/ConsentService.cs Assets/FoundationDI/Tests/ConsentServiceTrackingTest.cs*
git commit -m "[BEHAVIORAL] UMP 다음에 ATT를 묻는다"
```

---

### Task 8: ConsentService — 견고성

**Files:**
- Modify: `Runtime/Services/ConsentService/ConsentService.cs` (필요 시)
- Test: `Tests/ConsentServiceRobustnessTest.cs`

Task 5~7의 구현이 이미 대부분을 덮는다. 이 Task는 실패·재진입·Dispose·재판정 규칙을 테스트로 고정하고, 깨지는 것만 고친다.

- [ ] **Step 1: 테스트 작성**

```csharp
using System.Collections;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ConsentServiceRobustnessTest
{
    private static ConsentFixture Accepted()
    {
        var f = new ConsentFixture();
        f.Storage.Version = 1;
        return f;
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP_갱신이_실패해도_이전_값으로_완료한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.UpdateResult = false;
        f.Ump.Status = UmpConsentStatus.Obtained;
        f.Ump.Tcf = new TcfSignals(true, "1111111111");

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("갱신이 실패"));

        var result = await f.Build().RequestAsync();

        Assert.AreEqual(0, f.Ump.FormCount, "갱신 실패 뒤 폼을 띄우려 했다");
        Assert.AreEqual(AnalyticsConsent.Granted, result.AnalyticsConsent, "캐시된 이전 세션 값을 쓰지 않았다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP_갱신이_예외를_던져도_완료한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.ThrowOnUpdate = true;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("갱신 중 예외"));

        var result = await f.Build().RequestAsync();

        Assert.IsTrue(result.PolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP_갱신이_시간을_넘기면_실패로_보고_진행한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.DeferUpdate = true;
        f.Ump.Status = UmpConsentStatus.Unknown;
        var requesting = f.Build().RequestAsync();

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("초 안에 끝나지 않았다"));

        f.Dispatcher.Advance(f.UpdateTimeout);
        var result = await requesting;

        Assert.AreEqual(0, f.Ump.FormCount);
        Assert.AreEqual(AnalyticsConsent.Unknown, result.AnalyticsConsent, "첫 실행 오프라인은 판정 불가여야 한다");

        f.Ump.CompleteUpdate(true);   // 늦게 도착한 결과는 무시된다(예외 없이)
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 폼이_예외를_던져도_완료한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.ThrowOnForm = true;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("폼 표시 중 예외"));

        var result = await f.Build().RequestAsync();

        Assert.IsTrue(result.PolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 네이티브_읽기가_던져도_결과를_만든다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.ThrowOnRead = true;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("읽지 못했다"));

        var result = await f.Build().RequestAsync();

        Assert.IsTrue(result.PolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 진행_중에_다시_부르면_같은_흐름에_편승한다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        var sut = f.Build();

        var first = sut.RequestAsync();
        var second = sut.RequestAsync();

        Assert.AreEqual(1, f.Prompt.ShowCount, "약관 창을 두 번 띄웠다");

        f.Prompt.Accept();

        Assert.IsTrue((await first).PolicyAccepted);
        Assert.IsTrue((await second).PolicyAccepted);
        Assert.AreEqual(1, f.Ump.UpdateCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 확정된_결과가_있으면_다시_묻지_않고_돌려준다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        var sut = f.Build();

        await sut.RequestAsync();
        var again = await sut.RequestAsync();

        Assert.AreEqual(1, f.Ump.UpdateCount, "광고·분석 초기화가 각자 부를 때마다 네트워크 왕복이 생긴다");
        Assert.IsTrue(again.CanRequestAds);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 확정되지_않았으면_다시_부를_때_UMP만_재판정한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.UpdateResult = false;
        f.Ump.Status = UmpConsentStatus.Unknown;
        var sut = f.Build();

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("갱신이 실패"));
        Assert.AreEqual(AnalyticsConsent.Unknown, (await sut.RequestAsync()).AnalyticsConsent);

        f.Ump.UpdateResult = true;
        f.Ump.Status = UmpConsentStatus.NotRequired;
        var second = await sut.RequestAsync();

        Assert.AreEqual(2, f.Ump.UpdateCount);
        Assert.AreEqual(0, f.Prompt.ShowCount, "재판정에서 약관을 다시 물었다");
        Assert.AreEqual(AnalyticsConsent.Granted, second.AnalyticsConsent);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 재판정_결과가_바뀌면_ConsentChanged를_발행한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.UpdateResult = false;
        f.Ump.Status = UmpConsentStatus.Unknown;
        var sut = f.Build();
        var changed = 0;
        sut.ConsentChanged += () => changed++;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("갱신이 실패"));
        await sut.RequestAsync();

        Assert.AreEqual(0, changed, "첫 완료는 변경이 아니다");

        f.Ump.UpdateResult = true;
        f.Ump.Status = UmpConsentStatus.NotRequired;
        await sut.RequestAsync();

        Assert.AreEqual(1, changed);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 개인정보_옵션_폼이_닫히면_ConsentChanged를_발행한다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.IsPrivacyOptionsRequired = true;
        f.Ump.OnPrivacyOptions = () => f.Ump.Tcf = new TcfSignals(true, "0000000000");
        f.Ump.Status = UmpConsentStatus.Obtained;
        var sut = f.Build();
        var changed = 0;
        sut.ConsentChanged += () => changed++;

        Assert.IsTrue(sut.IsPrivacyOptionsRequired);

        await sut.ShowPrivacyOptionsAsync();

        Assert.AreEqual(1, f.Ump.PrivacyOptionsCount);
        Assert.AreEqual(1, changed);
        Assert.AreEqual(AnalyticsConsent.Denied, sut.Current.AnalyticsConsent);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 약관_창이_떠_있을_때_Dispose하면_창을_닫고_수락하지_않음으로_완료한다() => AwaitableTest.Run(async () =>
    {
        var f = new ConsentFixture();
        var sut = f.Build();

        var requesting = sut.RequestAsync();
        sut.Dispose();
        var result = await requesting;

        Assert.AreEqual(1, f.Prompt.CloseCount, "창을 정리하지 않았다");
        Assert.IsFalse(result.PolicyAccepted);
        Assert.AreEqual(0, f.Storage.SaveCount);

        f.Prompt.Accept();   // Dispose 뒤 늦게 눌려도 저장하지 않는다
        Assert.AreEqual(0, f.Storage.SaveCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose_뒤의_요청은_즉시_현재_상태로_끝난다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        var sut = f.Build();

        sut.Dispose();
        var result = await sut.RequestAsync();

        Assert.AreEqual(0, f.Ump.UpdateCount);
        Assert.IsTrue(result.PolicyAccepted);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator UMP_폼이_떠_있을_때_Dispose해도_대기자는_완료된다() => AwaitableTest.Run(async () =>
    {
        var f = Accepted();
        f.Ump.DeferForm = true;
        var sut = f.Build();

        var requesting = sut.RequestAsync();

        Assert.IsTrue(f.Ump.IsFormPending);

        sut.Dispose();
        await requesting;

        f.Ump.CompleteForm();   // 늦은 콜백이 예외를 내지 않는다
    });
}
```

- [ ] **Step 2: 실행**

Run: `ConsentServiceRobustnessTest`
Expected: 대부분 PASS. 실패가 있으면 아래 Step 3을 적용한다.

- [ ] **Step 3: 깨지는 경우의 수정**

예상되는 실패와 수정:
- `약관_창이_떠_있을_때_Dispose…`에서 늦은 `Accept()`가 `SaveAcceptedVersion`을 부르면: `Run()`의 `await _prompt.ShowAsync(_content);` 뒤 `if (_disposed) return;`이 있는지 확인한다(Task 5 코드에 있다).
- `UMP_갱신이_시간을_넘기면…`에서 늦은 `CompleteUpdate`가 예외를 내면: `Settle`의 `if (settled) return;`이 첫 줄인지 확인한다.

- [ ] **Step 4: 통과 확인**

Run: `ConsentServiceRobustnessTest` 전부 PASS, 전체 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ConsentService/ConsentService.cs Assets/FoundationDI/Tests/ConsentServiceRobustnessTest.cs*
git commit -m "[BEHAVIORAL] 동의 흐름의 실패·재진입·재판정·Dispose 규칙을 고정한다"
```

---

### Task 9: ConsentServiceSettings와 RegisterConsentService

**Files:**
- Create: `Runtime/Services/ConsentService/ConsentServiceSettings.cs`
- Create: `Runtime/Services/ConsentService/ConsentServiceRegistration.cs`
- Modify: `Runtime/Services/ConsentService/ConsentService.cs` (`CreateTrackingOnly` 추가)
- Test: `Tests/ConsentServiceRegistrationTest.cs`

**Interfaces:**
- Consumes: `RegisterMainThreadDispatcher`(Task 1), `TrackingAuthorizationRegistry.Create(IMainThreadDispatcher)`, `UmpConsentFactory.Create`(Task 16 — 이 Task에서는 아직 없으므로 `new UnsupportedUmpConsent()`를 쓰고 Task 16에서 바꾼다), `DefaultPolicyAgreementPrompt`(Task 14 — 마찬가지로 Task 14에서 연결).
- Produces:
  - `public static ContainerBuilder RegisterConsentService(this ContainerBuilder builder, ConsentServiceSettings settings, IPolicyAgreementPrompt prompt = null)`
  - `public static ConsentService ConsentService.CreateTrackingOnly(IMainThreadDispatcher dispatcher, ITrackingAuthorization tracking)`
  - `ConsentServiceSettings` 프로퍼티: `bool UseUmp`, `string AdMobAppIdAndroid`, `string AdMobAppIdIos`, `PolicyAgreementView PromptPrefab`(Task 14에서 추가), `ConsentServiceOptions ToOptions(bool isDebugBuild)`

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections;
using System.Linq;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
using UnityEngine.TestTools;
using Resolution = Reflex.Enums.Resolution;

public class ConsentServiceRegistrationTest
{
    private ConsentServiceSettings _settings;

    [SetUp]
    public void SetUp() => _settings = ScriptableObject.CreateInstance<ConsentServiceSettings>();

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_settings);
        TrackingAuthorizationRegistry.Reset();
    }

    private Container Build(IPolicyAgreementPrompt prompt = null, params IConsentPrerequisite[] prerequisites)
    {
        var builder = new ContainerBuilder();
        builder.RegisterConsentService(_settings, prompt);

        // 진짜 디스패처는 러너 GameObject에 DontDestroyOnLoad를 부른다(EditMode에서 예외).
        builder.RegisterType(typeof(FakeAdDispatcher), new[] { typeof(IAdDispatcher), typeof(IMainThreadDispatcher) },
                             Lifetime.Singleton, Resolution.Lazy);

        // 기본 저장소는 PlayerPrefs를 건드리므로 테스트는 메모리 대역으로 덮는다.
        builder.RegisterValue(new FakePolicyStorage { Version = 0 }, new[] { typeof(IPolicyAgreementStorage) });

        foreach (var prerequisite in prerequisites)
        {
            builder.RegisterValue(prerequisite, new[] { typeof(IConsentPrerequisite) });
        }

        return builder.Build();
    }

    [Test]
    public void 등록하면_IConsentService를_해석한다()
    {
        var container = Build(new FakePolicyPrompt(new System.Collections.Generic.List<string>()));

        Assert.IsInstanceOf<ConsentService>(container.Resolve<IConsentService>());
        Assert.DoesNotThrow(() => container.Dispose());
    }

    [Test]
    public void 설정이_null이면_등록하지_않는다()
    {
        var builder = new ContainerBuilder();

        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("ConsentServiceSettings"));

        builder.RegisterConsentService(null);

        Assert.IsFalse(builder.HasBinding(typeof(IConsentService)));
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 넘긴_약관_창을_쓰고_등록된_선행_작업을_기다린다() => AwaitableTest.Run(async () =>
    {
        var log = new System.Collections.Generic.List<string>();
        var prompt = new FakePolicyPrompt(log);
        var prerequisite = new FakeConsentPrerequisite(log);
        var consent = Build(prompt, prerequisite).Resolve<IConsentService>();

        var requesting = consent.RequestAsync();
        prompt.Accept();
        await requesting;

        CollectionAssert.AreEqual(new[] { "policy", "prereq" }, log);
    });

    [Test]
    public void 릴리스_빌드에서는_디버그_지역을_끈다()
    {
        Assert.AreEqual(UmpDebugGeography.Disabled, _settings.ToOptions(isDebugBuild: false).UmpRequest.DebugGeography);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 추적만_묻는_동의는_약관과_UMP_없이_ATT만_묻는다() => AwaitableTest.Run(async () =>
    {
        var tracking = new FakeTrackingAuthorization();
        var sut = ConsentService.CreateTrackingOnly(new FakeAdDispatcher(), tracking);

        var requesting = sut.RequestAsync();

        Assert.IsTrue(tracking.IsWaiting);

        tracking.Respond(TrackingAuthorizationStatus.Authorized);
        var result = await requesting;

        Assert.IsTrue(result.PolicyAccepted);
        Assert.IsTrue(result.CanRequestAds);
        Assert.AreEqual(AnalyticsConsent.Granted, result.AnalyticsConsent);
        Assert.AreEqual(TrackingAuthorizationStatus.Authorized, result.TrackingStatus);
    });
}
```

- [ ] **Step 2: 실패 확인**

Run: `ConsentServiceRegistrationTest`
Expected: 컴파일 에러 — `ConsentServiceSettings`, `RegisterConsentService`, `CreateTrackingOnly` 없음.

- [ ] **Step 3: 설정 에셋 작성**

`ConsentServiceSettings.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "ConsentServiceSettings", menuName = "FoundationDI/Consent Service Settings")]
    public class ConsentServiceSettings : ScriptableObject
    {
        [Header("Policy (약관·개인정보처리방침)")]
        [Tooltip("약관을 바꾸면 올린다. 저장된 수락 버전보다 크면 다시 묻는다. 0이면 약관 단계를 쓰지 않는다.")]
        [SerializeField, Min(0)] private int _policyVersion = 1;

        [SerializeField] private string _title = "이용약관 및 개인정보처리방침";
        [SerializeField, TextArea(3, 8)] private string _message =
            "게임을 시작하려면 이용약관과 개인정보처리방침에 동의해 주세요.";
        [SerializeField] private string _acceptLabel = "동의하고 시작하기";
        [SerializeField] private string _termsLabel = "이용약관";
        [SerializeField] private string _termsUrl = "";
        [SerializeField] private string _privacyLabel = "개인정보처리방침";
        [SerializeField] private string _privacyUrl = "";

        [Header("Google UMP")]
        [Tooltip("켜면 에디터가 UMP 네이티브 의존성 XML을 생성하고 런타임이 UMP 동의를 받는다. " +
                 "AdMob 콘솔의 '개인 정보 보호 및 메시지'에서 GDPR 메시지를 만들어 둬야 한다.")]
        [SerializeField] private bool _useUmp;

        [Tooltip("ca-app-pub-XXXX~YYYY. LevelPlay AdMob 어댑터가 이미 넣었다면 비워 둬도 된다.")]
        [SerializeField] private string _adMobAppIdAndroid = "";
        [SerializeField] private string _adMobAppIdIos = "";

        [Tooltip("개발 빌드에서만 적용된다.")]
        [SerializeField] private UmpDebugGeography _debugGeography = UmpDebugGeography.Disabled;
        [SerializeField] private List<string> _testDeviceHashedIds = new();
        [SerializeField] private bool _tagForUnderAgeOfConsent;

        [Tooltip("UMP 동의 정보 갱신(네트워크)을 기다리는 최대 시간(초).")]
        [SerializeField, Min(1f)] private float _umpUpdateTimeoutSeconds = 10f;

        [Header("iOS Tracking (ATT)")]
        [Tooltip("UMP 다음에 ATT 다이얼로그를 띄운다. 사전 안내 화면을 먼저 보여 주는 등 시점을 직접 " +
                 "고르려면 끄고 IConsentService.TrackingAuthorization.RequestAsync()를 부른다.")]
        [SerializeField] private bool _requestTrackingAuthorization = true;

        public bool UseUmp => _useUmp;
        public string AdMobAppIdAndroid => _adMobAppIdAndroid;
        public string AdMobAppIdIos => _adMobAppIdIos;

        public ConsentServiceOptions ToOptions(bool isDebugBuild)
        {
            var content = new PolicyAgreementContent(_title, _message, _acceptLabel,
                                                     _termsLabel, _termsUrl, _privacyLabel, _privacyUrl);

            var geography = isDebugBuild ? _debugGeography : UmpDebugGeography.Disabled;
            var umpRequest = new UmpRequestOptions(geography, new List<string>(_testDeviceHashedIds),
                                                   _tagForUnderAgeOfConsent);

            return new ConsentServiceOptions(Mathf.Max(0, _policyVersion), content, _useUmp, umpRequest,
                                             _requestTrackingAuthorization,
                                             Mathf.Max(1f, _umpUpdateTimeoutSeconds));
        }
    }
}
```

- [ ] **Step 4: CreateTrackingOnly 추가**

`ConsentService.cs`의 생성자 아래에 추가한다.

```csharp
        // ConsentService를 등록하지 않은 프로젝트의 AdService 기본값. 0.11까지의 동작(광고 초기화 앞에서
        // ATT만 묻는다)을 그대로 유지한다 — 약관 단계(버전 0)와 UMP를 쓰지 않는다.
        public static ConsentService CreateTrackingOnly(IMainThreadDispatcher dispatcher, ITrackingAuthorization tracking)
        {
            var options = new ConsentServiceOptions(0, default, false, default, true);

            return new ConsentService(options, null, null, new UnsupportedUmpConsent(), tracking, dispatcher);
        }
```

- [ ] **Step 5: 등록 확장 작성**

`ConsentServiceRegistration.cs`:

```csharp
using System.Collections.Generic;
using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
using Resolution = Reflex.Enums.Resolution;

namespace DarkNaku.FoundationDI
{
    public static class ConsentServiceRegistration
    {
        // 루트 IInstaller의 InstallBindings에서 호출한다. AdService·AnalyticsService보다 앞이든 뒤든 상관없다.
        //   builder.RegisterConsentService(_consentSettings);                 // 기본 약관 창
        //   builder.RegisterConsentService(_consentSettings, prompt: myPrompt); // 커스텀 약관 창
        public static ContainerBuilder RegisterConsentService(this ContainerBuilder builder,
                                                              ConsentServiceSettings settings,
                                                              IPolicyAgreementPrompt prompt = null)
        {
            if (settings == null)
            {
                Debug.LogError("[ConsentService] ConsentServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterValue(settings);
            builder.RegisterMainThreadDispatcher();
            builder.RegisterType(typeof(PlayerPrefsPolicyAgreementStorage), new[] { typeof(IPolicyAgreementStorage) },
                                 Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterFactory<IConsentService>(container =>
            {
                var dispatcher = container.Resolve<IMainThreadDispatcher>();
                var storage = container.Resolve<IPolicyAgreementStorage>();

                // 선행 작업(분석 SDK 초기화 등)은 해석 시점에 모은다. 등록 순서와 무관하게 잡힌다.
                var prerequisites = new List<IConsentPrerequisite>(container.All<IConsentPrerequisite>());

                var ump = settings.UseUmp ? (IUmpConsent)new UnsupportedUmpConsent() : new UnsupportedUmpConsent();
                var tracking = TrackingAuthorizationRegistry.Create(dispatcher);

                return new ConsentService(settings.ToOptions(Debug.isDebugBuild), prompt, storage, ump,
                                          tracking, dispatcher, prerequisites);
            }, Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
```

`ump` 줄은 Task 16에서 `UmpConsentFactory.Create(dispatcher)`로, `prompt`의 기본값은 Task 14에서 `DefaultPolicyAgreementPrompt`로 바뀐다. 지금은 두 분기가 같아 보이지만 그 자리를 표시하려고 남겨 둔다.

- [ ] **Step 6: 통과 확인**

Run: `ConsentServiceRegistrationTest` → 5개 PASS. 전체 PASS.

- [ ] **Step 7: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ConsentService Assets/FoundationDI/Tests/ConsentServiceRegistrationTest.cs*
git commit -m "[BEHAVIORAL] ConsentServiceSettings와 RegisterConsentService를 추가한다"
```

---

### Task 10: AdService가 동의를 기다린다 (공개 API 파괴)

**Files:**
- Modify: `Runtime/Services/AdService/AdService.cs` (생성자, `InitializeAsync`, ATT 메서드 삭제, `Consent`/`TrackingAuthorization` 삭제, `AdServiceOptions.RequestTrackingAuthorization` 삭제)
- Modify: `Runtime/Services/AdService/IAdService.cs` (`Consent`, `TrackingAuthorization` 삭제)
- Modify: `Runtime/Services/AdService/AdServiceRegistration.cs`
- Modify: `Runtime/Services/AdService/Settings/AdServiceSettings.cs` (`_requestTrackingAuthorization` 필드·Header 삭제, `ToOptions` 인자 삭제)
- Modify: `Runtime/Services/AdService/Providers/IAdProvider.cs` (`IAdConsent Consent { get; }` 삭제, `AdProviderContext` 주석의 `AdServiceOptions.RequestTrackingAuthorization` 언급을 `ConsentServiceSettings.Request Tracking Authorization`으로)
- Modify: `Providers/Dummy/DummyAdProvider.cs:19`, `Providers/LevelPlay/LevelPlayAdProvider.cs:55-65`, `Providers/AppLovin/AppLovinAdProvider.cs:32` (`Consent` 프로퍼티와 관련 주석 삭제 — LevelPlay·AppLovin은 호스트 미컴파일)
- Delete: `Runtime/Services/AdService/Consent/IAdConsent.cs`, `NoopAdConsent.cs`, 폴더 `Consent/`(+ meta)
- Modify: `Tests/AdTestDoubles.cs:147` (`Consent` 삭제)
- Delete: `Tests/AdTrackingAuthorizationTest.cs`(+meta) — 남은 테스트는 아래 새 파일과 Task 7로 옮겨졌다.
- Test: `Tests/AdServiceConsentTest.cs`
- Modify: 그 외 `new AdService(...)`나 `new AdServiceOptions(...)`를 쓰는 테스트(`AdServiceTest.cs` 등)의 마지막 인자 정리

**Interfaces:**
- Consumes: `IConsentService`, `ConsentResult`, `ConsentService.CreateTrackingOnly`, `FakeConsentService`, `ConsentResults`.
- Produces: `public AdService(IAdProvider provider, IAdDispatcher dispatcher, AdServiceOptions options, IAdRemovalStorage removalStorage, IConsentService consent = null)`. `AdServiceOptions` 생성자에서 `requestTrackingAuthorization` 파라미터 제거.

- [ ] **Step 1: 사용처 목록 확보**

```bash
grep -rn "requestTrackingAuthorization\|RequestTrackingAuthorization\|TrackingAuthorization\b\|\.Consent\b\|IAdConsent\|NoopAdConsent" Assets/FoundationDI --include=*.cs | grep -v ConsentService/
```

- [ ] **Step 2: 실패하는 테스트 작성**

`Tests/AdServiceConsentTest.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AdServiceConsentTest
{
    private class FakeRemovalStorage : IAdRemovalStorage
    {
        public bool Load() => false;
        public void Save(bool removed) { }
    }

    private static AdServiceOptions NewOptions()
    {
        return new AdServiceOptions(
            banner: new AdUnitId("banner-a", "banner-i"),
            interstitial: new AdUnitId("inter-a", "inter-i"),
            rewarded: new AdUnitId("reward-a", "reward-i"),
            bannerOptions: new BannerOptions(BannerPosition.Bottom, BannerSize.Adaptive, true),
            providerContext: new AdProviderContext("app-key", false, false, new List<string>()),
            retryPolicy: new AdRetryPolicy(3, 2f, 64f),
            rewardGraceFrames: 1,
            interstitialCooldownSeconds: 0f,
            autoLoadOnInitialize: true);
    }

    private static AdService Create(FakeAdProvider provider, IConsentService consent) =>
        new(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage(), consent);

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의를_먼저_받고_provider를_초기화한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Defer = true };
        var sut = Create(provider, consent);

        var initializing = sut.InitializeAsync();

        Assert.AreEqual(1, consent.RequestCount);
        Assert.AreEqual(0, provider.InitializeCallCount, "동의 전에 광고 SDK를 초기화했다");

        consent.Complete(ConsentResults.Granted());

        Assert.IsTrue(await initializing);
        Assert.AreEqual(1, provider.InitializeCallCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 광고를_요청할_수_없으면_provider를_초기화하지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Unknown() };

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("광고를 요청할 수 없다"));

        Assert.IsFalse(await Create(provider, consent).InitializeAsync());
        Assert.AreEqual(0, provider.InitializeCallCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 약관을_수락하지_않았으면_초기화하지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Result = ConsentResults.PolicyNotAccepted() };

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("광고를 요청할 수 없다"));

        Assert.IsFalse(await Create(provider, consent).InitializeAsync());
        Assert.AreEqual(0, provider.InitializeCallCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 거부된_뒤_다시_부르면_동의부터_재시도한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Unknown() };
        var sut = Create(provider, consent);

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("광고를 요청할 수 없다"));
        await sut.InitializeAsync();

        consent.Result = ConsentResults.Granted();

        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(2, consent.RequestCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의_결과의_추적_거부를_provider가_받는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Granted(TrackingAuthorizationStatus.Denied) };

        await Create(provider, consent).InitializeAsync();

        Assert.AreEqual(TrackingAuthorizationStatus.Denied, provider.ReceivedContext.TrackingStatus);
        Assert.IsTrue(provider.ReceivedContext.IsTrackingRestricted,
                      "거부를 provider가 모르면 SDK는 동의받은 것으로 간주하고 계속 추적한다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 물을_수_없는_플랫폼은_제한으로_보지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();

        await Create(provider, new FakeConsentService()).InitializeAsync();

        Assert.IsFalse(provider.ReceivedContext.IsTrackingRestricted,
                       "ATT가 없는 플랫폼에서 추적을 끄면 이유 없이 수익이 죽는다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의를_넘기지_않으면_추적만_묻는_기본_동의로_동작한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAdProvider();
        var sut = new AdService(provider, new FakeAdDispatcher(), NewOptions(), new FakeRemovalStorage());

        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(1, provider.InitializeCallCount);
    });

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
}
```

- [ ] **Step 3: 실패 확인**

Run: `AdServiceConsentTest`
Expected: 컴파일 에러 — `AdService` 생성자에 `IConsentService`를 받을 수 없음.

- [ ] **Step 4: AdService 수정**

`AdServiceOptions`에서 `RequestTrackingAuthorization` 프로퍼티·주석·생성자 파라미터(`bool requestTrackingAuthorization = true`)·대입을 지운다.

`AdService` 클래스에서:
- 필드 `private readonly ITrackingAuthorization _tracking;`을 `private readonly IConsentService _consent;`로 바꾼다.
- 생성자를 다음으로 교체한다.

```csharp
        // consent는 선택 인자다. 넘기지 않으면 약관·UMP 없이 ATT만 묻는(실기 iOS가 아니면 아무것도
        // 하지 않는) 기본 동의로 동작한다 — 0.11까지의 호출부와 테스트를 깨지 않기 위해서다.
        public AdService(IAdProvider provider, IAdDispatcher dispatcher,
                         AdServiceOptions options, IAdRemovalStorage removalStorage,
                         IConsentService consent = null)
        {
            _provider = provider;
            _dispatcher = dispatcher;
            _options = options;
            _removalStorage = removalStorage;
            _consent = consent ?? ConsentService.CreateTrackingOnly(dispatcher, new UnsupportedTrackingAuthorization());
            _adsRemoved = removalStorage?.Load() ?? false;
        }
```

- `public IAdConsent Consent => _provider.Consent;`와 `public ITrackingAuthorization TrackingAuthorization => _tracking;`을 지운다.
- `InitializeAsync`의 `try` 블록 앞부분을 다음으로 교체한다(`await RequestTrackingAuthorizationAsync();`부터 `var context = …;`까지).

```csharp
                // provider보다 먼저 동의를 받는다. 광고 SDK는 초기화 시점의 TCF·ATT 상태로
                // 개인화 여부를 정하므로, 뒤에 받으면 그 세션의 첫 요청들이 잘못된 상태로 나간다.
                var consent = await _consent.RequestAsync();

                // 다이얼로그가 떠 있는 동안 씬이 언로드돼 서비스가 정리됐을 수 있다.
                if (_isDisposed) return false;

                if (!consent.PolicyAccepted || !consent.CanRequestAds)
                {
                    // Google 지침: canRequestAds가 false면 광고를 요청하지 않는다. 게임이 다시
                    // InitializeAsync를 부르면 동의 재판정부터 다시 시도한다.
                    Debug.LogWarning($"[AdService] 광고를 요청할 수 없다({consent}). provider를 초기화하지 않는다.");
                    return false;
                }

                var context = _options.ProviderContext.WithTrackingStatus(consent.TrackingStatus);
```

- `RequestTrackingAuthorizationAsync` 메서드 전체를 지운다.

- [ ] **Step 5: 인터페이스·설정·provider 정리**

`IAdService.cs`에서 `IAdConsent Consent { get; }`와 `TrackingAuthorization` 프로퍼티(주석 포함)를 지운다.

`AdServiceSettings.cs`에서 `[Header("iOS Tracking (ATT)")]` 블록(Tooltip 포함 `_requestTrackingAuthorization` 필드)을 지우고, `ToOptions`의 마지막 인자 `requestTrackingAuthorization: _requestTrackingAuthorization`과 앞 줄의 쉼표를 정리한다.

`IAdProvider.cs`에서 `IAdConsent Consent { get; }` 줄을 지운다. `AdProviderContext` 주석의 "`AdServiceOptions.RequestTrackingAuthorization = false`뿐이고"를 "`ConsentServiceSettings`의 Request Tracking Authorization을 끈 경우뿐이고"로 바꾼다.

`DummyAdProvider.cs`, `AppLovinAdProvider.cs`에서 `public IAdConsent Consent { get; } = new NoopAdConsent();`를 지운다. `LevelPlayAdProvider.cs`에서 `Consent` 프로퍼티와 그 위 주석(55~64행 "IAdConsent는 여전히 no-op이다…")을 지우고, 217행 주석의 "GDPR 동의는 UMP/CMP가 받아 IAdConsent 축으로 흘린다"를 "GDPR 동의는 ConsentService(UMP)가 받고, LevelPlay가 TCF·AC 문자열을 직접 읽는다"로 바꾼다.

`Tests/AdTestDoubles.cs`에서 `public IAdConsent Consent { get; } = new NoopAdConsent();`를 지운다.

```bash
git rm -r Assets/FoundationDI/Runtime/Services/AdService/Consent Assets/FoundationDI/Runtime/Services/AdService/Consent.meta
git rm Assets/FoundationDI/Tests/AdTrackingAuthorizationTest.cs Assets/FoundationDI/Tests/AdTrackingAuthorizationTest.cs.meta
```

- [ ] **Step 6: 등록 수정**

`AdServiceRegistration.cs`의 팩토리 본문을 교체한다.

```csharp
            return builder.RegisterFactory<IAdService>(container =>
            {
                var factory = container.Resolve<IAdProviderFactory>();
                var dispatcher = container.Resolve<IAdDispatcher>();
                var storage = container.Resolve<IAdRemovalStorage>();

                var forceDummy = settings.ForceDummyInEditor && Application.isEditor;
                var provider = factory.Create(settings.Provider, settings.DummyOptions, forceDummy);

                return new AdService(provider, dispatcher, settings.ToOptions(), storage, ResolveConsent(container, dispatcher));
            }, Lifetime.Singleton, Resolution.Lazy);
        }

        // ConsentService를 등록하지 않은 프로젝트도 0.11처럼 ATT는 묻게 한다. 다만 약관·UMP 없이
        // EEA에 Google 수요를 내보내면 제한 광고만 나가므로 경고를 남긴다.
        private static IConsentService ResolveConsent(Container container, IAdDispatcher dispatcher)
        {
            if (container.HasBinding<IConsentService>()) return container.Resolve<IConsentService>();

            Debug.LogWarning("[AdService] IConsentService가 등록되지 않았다. 약관·UMP 없이 ATT만 묻는다. " +
                             "EEA·영국에서 광고를 내보내려면 RegisterConsentService로 UMP를 켠다.");

            return ConsentService.CreateTrackingOnly(dispatcher, TrackingAuthorizationRegistry.Create(dispatcher));
        }
```

파일 상단 `using`에 `Reflex.Core`가 이미 있다(`Container` 타입).

`AdServiceRegistrationTest`는 `IConsentService`를 등록하지 않으므로 경고가 하나 나온다. 해석 줄 앞에 다음을 추가한다.

```csharp
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("IConsentService가 등록되지 않았다"));
```

(`using UnityEngine.TestTools;` 추가)

- [ ] **Step 7: 나머지 컴파일 에러 정리**

`refresh_unity` → `read_console`(error). `AdServiceTest.cs` 등에서 `requestTrackingAuthorization:` 인자나 `tracking` 인자를 넘기는 곳이 나오면 그 인자를 지운다. 테스트 의미는 바뀌지 않는다.

- [ ] **Step 8: 통과 확인**

Run: `AdServiceConsentTest` → 10개 PASS. 전체 PASS.

- [ ] **Step 9: 커밋**

```bash
git add -A Assets/FoundationDI/Runtime/Services/AdService Assets/FoundationDI/Tests
git commit -m "[BEHAVIORAL] AdService가 ConsentService의 동의를 받은 뒤 광고를 초기화한다

IAdService.Consent/TrackingAuthorization, IAdProvider.Consent, IAdConsent,
AdServiceOptions.RequestTrackingAuthorization을 제거한다(0.12.0 공개 API 파괴).
LevelPlay·AppLovin 어댑터 수정은 호스트 미컴파일 — 실기 빌드로 확인 필요."
```

---

### Task 11: AnalyticsService 동의 게이트

**Files:**
- Create: `Runtime/Services/AnalyticsService/Providers/IAnalyticsConsentAware.cs`
- Modify: `Runtime/Services/AnalyticsService/AnalyticsService.cs`
- Modify: `Tests/AnalyticsTestDoubles.cs` (`FakeConsentAwareAnalyticsProvider` 추가)
- Test: `Tests/AnalyticsServiceConsentTest.cs`

**Interfaces:**
- Consumes: `IConsentService`, `ConsentResult`, `FakeConsentService`, `ConsentResults`.
- Produces: `public interface IAnalyticsConsentAware { void ApplyConsent(ConsentResult result); }`, `AnalyticsService(IReadOnlyList<IAnalyticsProvider> providers, AnalyticsServiceOptions options, IConsentService consent = null)`.

게이트 규칙(스펙 §7.2): 동의가 없으면(`consent == null`) 지금과 똑같다. 있으면 provider 초기화 → `await consent.RequestAsync()` → `IAnalyticsConsentAware.ApplyConsent` → `Granted`면 flush / `Denied`면 수집 끄고 버퍼 폐기 / `Unknown`이면 수집 끄고 버퍼 유지. `ConsentChanged`가 오면 다시 적용. flush 훅(`IAnalyticsFlushHook`)은 처음 `Granted`일 때 한 번만.

- [ ] **Step 1: 대역 추가**

`AnalyticsTestDoubles.cs` 끝에 추가한다.

```csharp
// 동의를 SDK 신호로 번역하는 provider(Adjust 같은). 번역이 flush 훅보다 먼저 와야 한다 —
// Adjust는 DMA 값을 첫 세션 지연을 풀기 전에 받아야 인스톨 레코드에 실린다.
public class FakeConsentAwareAnalyticsProvider : FakeFlushHookAnalyticsProvider, IAnalyticsConsentAware
{
    public FakeConsentAwareAnalyticsProvider(string name = "ConsentAware") : base(name)
    {
    }

    public readonly List<ConsentResult> AppliedConsents = new();

    public void ApplyConsent(ConsentResult result)
    {
        AppliedConsents.Add(result);
        Calls.Add($"ApplyConsent:{result.AnalyticsConsent}");
    }
}
```

- [ ] **Step 2: 실패하는 테스트 작성**

`Tests/AnalyticsServiceConsentTest.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class AnalyticsServiceConsentTest
{
    private static AnalyticsService Create(IAnalyticsProvider provider, IConsentService consent) =>
        new(new List<IAnalyticsProvider> { provider }, new AnalyticsServiceOptions(true), consent);

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의가_올_때까지_이벤트를_버퍼에_쥐고_있는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var consent = new FakeConsentService { Defer = true };
        var sut = Create(provider, consent);

        sut.LogEvent("before_init");
        var initializing = sut.InitializeAsync();
        sut.LogEvent("waiting_consent");

        Assert.AreEqual(1, provider.InitializeCount, "동의보다 먼저 SDK를 초기화해야 한다(UMP 앞 Firebase)");
        Assert.AreEqual(0, provider.Events.Count, "동의 전에 이벤트를 보냈다");

        consent.Complete(ConsentResults.Granted());
        await initializing;

        CollectionAssert.AreEqual(new[] { "before_init", "waiting_consent" }, provider.Events.ConvertAll(e => e.Name));
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 분석을_거부하면_수집을_끄고_버퍼를_버린다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var sut = Create(provider, new FakeConsentService { Result = ConsentResults.Denied() });

        sut.LogEvent("before");
        await sut.InitializeAsync();
        sut.LogEvent("after");

        Assert.AreEqual(0, provider.Events.Count);
        Assert.AreEqual(false, provider.CollectionFlags[provider.CollectionFlags.Count - 1]);
        Assert.IsTrue(sut.IsInitialized, "거부해도 게임은 진행된다 — 초기화 자체는 성공이다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 판정이_안_났으면_버퍼를_유지하고_재판정에서_flush한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Unknown() };
        var sut = Create(provider, consent);

        sut.LogEvent("offline_first_launch");
        await sut.InitializeAsync();
        sut.LogEvent("still_unknown");

        Assert.AreEqual(0, provider.Events.Count);

        consent.RaiseChanged(ConsentResults.Granted());

        CollectionAssert.AreEqual(new[] { "offline_first_launch", "still_unknown" },
                                  provider.Events.ConvertAll(e => e.Name));
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 판정이_안_났다가_거부로_확정되면_버퍼를_버린다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Unknown() };
        var sut = Create(provider, consent);

        sut.LogEvent("buffered");
        await sut.InitializeAsync();
        consent.RaiseChanged(ConsentResults.Denied());
        consent.RaiseChanged(ConsentResults.Granted());

        Assert.AreEqual(0, provider.Events.Count, "거부로 확정된 시점의 버퍼가 나중에 새어 나갔다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 설정에서_동의를_철회하면_수집을_끈다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var consent = new FakeConsentService();
        var sut = Create(provider, consent);

        await sut.InitializeAsync();
        consent.RaiseChanged(ConsentResults.Denied());
        sut.LogEvent("after_withdraw");

        Assert.AreEqual(0, provider.Events.Count);
        Assert.AreEqual(false, provider.CollectionFlags[provider.CollectionFlags.Count - 1]);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의_번역을_flush_훅보다_먼저_부른다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeConsentAwareAnalyticsProvider();
        var sut = Create(provider, new FakeConsentService());

        await sut.InitializeAsync();

        var apply = provider.Calls.IndexOf("ApplyConsent:Granted");
        var hook = provider.Calls.IndexOf("OnBufferedStateFlushed");

        Assert.GreaterOrEqual(apply, 0);
        Assert.Less(apply, hook, "DMA 값이 첫 세션 지연 해제 뒤에 나가 인스톨 레코드에서 빠진다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 나중에_동의하면_그때_flush하고_훅을_한_번만_부른다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeConsentAwareAnalyticsProvider();
        var consent = new FakeConsentService { Result = ConsentResults.Denied() };
        var sut = Create(provider, consent);

        await sut.InitializeAsync();

        Assert.AreEqual(0, provider.FlushHookCount);

        consent.RaiseChanged(ConsentResults.Granted());
        consent.RaiseChanged(ConsentResults.Denied());
        consent.RaiseChanged(ConsentResults.Granted());

        Assert.AreEqual(1, provider.FlushHookCount);
        Assert.AreEqual(4, provider.AppliedConsents.Count, "변경마다 번역을 다시 해야 한다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 게임이_수집을_꺼_두면_동의해도_켜지_않는다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var sut = Create(provider, new FakeConsentService());

        sut.CollectionEnabled = false;
        await sut.InitializeAsync();

        Assert.AreEqual(false, provider.CollectionFlags[provider.CollectionFlags.Count - 1]);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose하면_동의_변경_구독을_푼다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var consent = new FakeConsentService();
        var sut = Create(provider, consent);

        await sut.InitializeAsync();
        sut.Dispose();

        Assert.DoesNotThrow(() => consent.RaiseChanged(ConsentResults.Denied()));
    });
}
```

- [ ] **Step 3: 실패 확인**

Run: `AnalyticsServiceConsentTest`
Expected: 컴파일 에러 — 3번째 생성자 인자 없음.

- [ ] **Step 4: 선택 seam 작성**

`Providers/IAnalyticsConsentAware.cs`:

```csharp
namespace DarkNaku.FoundationDI
{
    // 동의 결과를 자기 SDK의 신호로 번역해야 하는 provider만 구현한다.
    //
    // IAnalyticsProvider 본체에 넣지 않은 이유: Firebase는 UMP가 쓴 TCF·consent mode를 스스로 읽어
    // 할 일이 없고, 번역이 필요한 SDK는 지금 Adjust 하나다(IAnalyticsFlushHook과 같은 이유).
    //
    // 호출 시점: 초기화 직후 첫 판정, 그리고 ConsentChanged마다. 항상 flush 훅보다 먼저다.
    public interface IAnalyticsConsentAware
    {
        void ApplyConsent(ConsentResult result);
    }
}
```

- [ ] **Step 5: AnalyticsService 수정**

필드를 추가한다.

```csharp
        // null이면 0.11과 같다(동의를 기다리지 않는다).
        private readonly IConsentService _consent;

        // 동의 게이트. 동의 서비스가 없으면 처음부터 Granted다.
        private AnalyticsConsent _gate;

        // IAnalyticsFlushHook은 처음 허용될 때 한 번만 부른다(Adjust 첫 세션 지연 해제).
        private bool _flushHookFired;
```

생성자를 교체한다.

```csharp
        public AnalyticsService(IReadOnlyList<IAnalyticsProvider> providers, AnalyticsServiceOptions options,
                                IConsentService consent = null)
        {
            _providers = providers == null
                ? new List<IAnalyticsProvider>()
                : new List<IAnalyticsProvider>(providers);

            _collectionEnabled = options.CollectionEnabledByDefault;
            _consent = consent;
            _gate = consent == null ? AnalyticsConsent.Granted : AnalyticsConsent.Unknown;
        }
```

수집 가능 여부를 둘로 나눈다. 기존 `CanCollect`를 다음으로 교체한다.

```csharp
        // 게임이 끈 수집과 동의 거부는 버퍼에도 넣지 않는다. 동의 전에 쌓아 뒀다가 거부 시점에
        // 소급 전송하는 것은 게이트를 두는 의미 자체를 없앤다.
        private bool CanCollect => !_disposed && _collectionEnabled && _gate != AnalyticsConsent.Denied;

        // 버퍼에 쌓아야 하는가: 초기화 전이거나 동의가 아직 판정되지 않았다.
        private bool ShouldBuffer => !IsInitialized || _gate == AnalyticsConsent.Unknown;
```

`CollectionEnabled` 세터의 `Fanout(p => p.SetCollectionEnabled(value));`을 `Fanout(p => p.SetCollectionEnabled(EffectiveCollection));`로 바꾸고 프로퍼티를 추가한다.

```csharp
        private bool EffectiveCollection => _collectionEnabled && _gate == AnalyticsConsent.Granted;
```

`SetUserId`, `SetUserProperty`, `Dispatch`의 `if (!IsInitialized)`를 `if (ShouldBuffer)`로 바꾼다.

`RunInitializeAsync`의 성공 분기를 교체한다.

```csharp
            var succeeded = _active.Count > 0;

            if (succeeded)
            {
                if (_consent == null)
                {
                    IsInitialized = true;
                    Flush();
                }
                else
                {
                    var result = await _consent.RequestAsync();

                    if (!_disposed)
                    {
                        IsInitialized = true;
                        _consent.ConsentChanged += OnConsentChanged;
                        ApplyConsent(result);
                    }
                }
            }
```

메서드를 추가한다.

```csharp
        private void OnConsentChanged()
        {
            if (_disposed || !IsInitialized) return;

            ApplyConsent(_consent.Current);
        }

        // 번역(IAnalyticsConsentAware)을 먼저 하고 게이트를 적용한다. Adjust는 DMA 값을 첫 세션
        // 지연을 풀기 전(flush 훅 전)에 받아야 한다.
        private void ApplyConsent(ConsentResult result)
        {
            Fanout(p => (p as IAnalyticsConsentAware)?.ApplyConsent(result));

            _gate = result.AnalyticsConsent;

            switch (_gate)
            {
                case AnalyticsConsent.Granted:
                    Flush();
                    break;

                case AnalyticsConsent.Denied:
                    Fanout(p => p.SetCollectionEnabled(false));
                    ClearPending();
                    break;

                default:
                    // 판정 전: SDK 자동 수집은 막아 두고 버퍼는 쥐고 있는다.
                    Fanout(p => p.SetCollectionEnabled(false));
                    break;
            }
        }
```

`Flush()`의 첫 두 줄을 `Fanout(p => p.SetCollectionEnabled(EffectiveCollection));`로 바꾸고, 마지막 훅 호출을 한 번만 하게 바꾼다.

```csharp
            if (_flushHookFired) return;

            _flushHookFired = true;

            // 반드시 마지막이다. Adjust 어댑터가 여기서 첫 세션 지연을 푸는데(IAnalyticsFlushHook),
            // 이 줄이 위로 올라가면 아직 전달되지 않은 파라미터가 첫 세션에서 빠진다.
            Fanout(p => (p as IAnalyticsFlushHook)?.OnBufferedStateFlushed());
```

`Dispose()` 맨 앞(`_disposed = true;` 뒤)에 추가한다.

```csharp
            if (_consent != null) _consent.ConsentChanged -= OnConsentChanged;
```

- [ ] **Step 6: 통과 확인**

Run: `AnalyticsServiceConsentTest` → 9개 PASS. 기존 `AnalyticsServiceTest` 포함 전체 PASS(동의 서비스 없이 만드는 기존 테스트는 동작이 같다).

- [ ] **Step 7: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AnalyticsService Assets/FoundationDI/Tests/AnalyticsTestDoubles.cs Assets/FoundationDI/Tests/AnalyticsServiceConsentTest.cs*
git commit -m "[BEHAVIORAL] AnalyticsService가 동의 판정에 따라 수집과 버퍼를 게이트한다"
```

---

### Task 12: 분석 SDK를 UMP보다 먼저 초기화한다

**Files:**
- Create: `Runtime/Services/AnalyticsService/AnalyticsProviderInitializer.cs`
- Modify: `Runtime/Services/AnalyticsService/AnalyticsService.cs` (provider 초기화 루프를 initializer로 위임)
- Modify: `Runtime/Services/AnalyticsService/AnalyticsServiceRegistration.cs`
- Modify: `Runtime/Services/AnalyticsService/Settings/AnalyticsServiceSettings.cs` (`_waitForConsent` 추가)
- Test: `Tests/AnalyticsProviderInitializerTest.cs`

**Interfaces:**
- Produces:
  - `public sealed class AnalyticsProviderInitializer : IConsentPrerequisite { ctor(IReadOnlyList<IAnalyticsProvider> providers); IReadOnlyList<IAnalyticsProvider> Providers; IReadOnlyList<IAnalyticsProvider> Active; Awaitable<bool> InitializeAsync(); Awaitable PrepareAsync(); }`
  - `AnalyticsService(AnalyticsProviderInitializer initializer, AnalyticsServiceOptions options, IConsentService consent)` (추가 생성자)
  - `AnalyticsServiceSettings.WaitForConsent`

이 Task는 구조 변경(루프 이동)과 동작 추가(선행 작업 등록)가 섞이므로 커밋을 둘로 나눈다.

- [ ] **Step 1: [STRUCTURAL] initializer 추출 — 테스트 먼저**

`Tests/AnalyticsProviderInitializerTest.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AnalyticsProviderInitializerTest
{
    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 성공한_provider만_활성으로_남긴다() => AwaitableTest.Run(async () =>
    {
        var ok = new FakeAnalyticsProvider("ok");
        var failed = new FakeAnalyticsProvider("failed") { InitializeResult = false };
        var sut = new AnalyticsProviderInitializer(new List<IAnalyticsProvider> { ok, failed });

        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("failed 초기화에 실패"));

        Assert.IsTrue(await sut.InitializeAsync());
        CollectionAssert.AreEqual(new[] { ok }, sut.Active);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동시에_불러도_SDK를_한_번만_초기화한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider { DeferInitialize = true };
        var sut = new AnalyticsProviderInitializer(new List<IAnalyticsProvider> { provider });

        var first = sut.InitializeAsync();
        var second = sut.PrepareAsync();

        provider.CompleteInitialize(true);

        Assert.IsTrue(await first);
        await second;
        Assert.AreEqual(1, provider.InitializeCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 성공한_뒤에는_즉시_완료한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider();
        var sut = new AnalyticsProviderInitializer(new List<IAnalyticsProvider> { provider });

        await sut.InitializeAsync();

        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(1, provider.InitializeCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 전부_실패하면_다음_호출에서_재시도한다() => AwaitableTest.Run(async () =>
    {
        var provider = new FakeAnalyticsProvider { InitializeResult = false };
        var sut = new AnalyticsProviderInitializer(new List<IAnalyticsProvider> { provider });

        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("초기화에 실패"));
        Assert.IsFalse(await sut.InitializeAsync());

        provider.InitializeResult = true;

        Assert.IsTrue(await sut.InitializeAsync());
        Assert.AreEqual(2, provider.InitializeCount);
    });
}
```

`FakeAnalyticsProvider.CompleteInitialize(bool)`는 이미 있다(`DeferInitialize`로 보류한 초기화를 한꺼번에 완료한다).

- [ ] **Step 2: 실패 확인**

Run: `AnalyticsProviderInitializerTest` → 컴파일 에러.

- [ ] **Step 3: initializer 구현**

`AnalyticsProviderInitializer.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 분석 provider SDK 초기화만 맡는다. AnalyticsService에서 떼어 낸 이유는 ConsentService가
    // UMP를 시작하기 전에 이것을 기다려야 하기 때문이다 — Google은 "UMP 전에 Firebase를
    // 초기화하라"고 요구한다. 동의를 기다리지 않으므로 ConsentService가 기다려도 교착이 없다.
    public sealed class AnalyticsProviderInitializer : IConsentPrerequisite
    {
        private readonly List<IAnalyticsProvider> _providers;
        private readonly List<IAnalyticsProvider> _active = new();
        private readonly List<AwaitableCompletionSource<bool>> _waiters = new();

        private bool _running;
        private bool _succeeded;

        public AnalyticsProviderInitializer(IReadOnlyList<IAnalyticsProvider> providers)
        {
            _providers = providers == null
                ? new List<IAnalyticsProvider>()
                : new List<IAnalyticsProvider>(providers);
        }

        // 생성 시점에 받은 전부. 실패한 provider도 남는다 — 재시도와 Dispose의 대상이다.
        public IReadOnlyList<IAnalyticsProvider> Providers => _providers;

        // 초기화에 성공해 팬아웃을 받는 provider들.
        public IReadOnlyList<IAnalyticsProvider> Active => _active;

        // 재진입 안전하다. 진행 중이면 편승하고, 성공한 뒤에는 즉시 true다.
        // provider 중 하나라도 살아나면 성공이다. 전부 실패하면 다음 호출에서 다시 시도한다.
        public Awaitable<bool> InitializeAsync()
        {
            if (_succeeded) return Completed(true);

            var waiter = new AwaitableCompletionSource<bool>();
            _waiters.Add(waiter);

            if (!_running)
            {
                _running = true;
                Run();
            }

            return waiter.Awaitable;
        }

        public async Awaitable PrepareAsync()
        {
            await InitializeAsync();
        }

        private async void Run()
        {
            _active.Clear();

            foreach (var provider in _providers)
            {
                var ok = false;

                try
                {
                    ok = await provider.InitializeAsync();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[AnalyticsService] {provider.Name} 초기화 중 예외: {e}");
                }

                if (ok)
                {
                    _active.Add(provider);
                }
                else
                {
                    Debug.LogError($"[AnalyticsService] {provider.Name} 초기화에 실패했다. 팬아웃에서 제외한다.");
                }
            }

            _succeeded = _active.Count > 0;
            _running = false;

            var waiters = new List<AwaitableCompletionSource<bool>>(_waiters);
            _waiters.Clear();

            foreach (var waiter in waiters) waiter.SetResult(_succeeded);
        }

        private static Awaitable<bool> Completed(bool value)
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(value);
            return source.Awaitable;
        }
    }
}
```

- [ ] **Step 4: AnalyticsService가 initializer를 쓰게 한다**

`AnalyticsService`에서:
- `_providers`, `_active` 필드를 지우고 `private readonly AnalyticsProviderInitializer _initializer;`를 둔다.
- 생성자를 둘로 만든다.

```csharp
        public AnalyticsService(IReadOnlyList<IAnalyticsProvider> providers, AnalyticsServiceOptions options,
                                IConsentService consent = null)
            : this(new AnalyticsProviderInitializer(providers), options, consent)
        {
        }

        public AnalyticsService(AnalyticsProviderInitializer initializer, AnalyticsServiceOptions options,
                                IConsentService consent)
        {
            _initializer = initializer;
            _collectionEnabled = options.CollectionEnabledByDefault;
            _consent = consent;
            _gate = consent == null ? AnalyticsConsent.Granted : AnalyticsConsent.Unknown;
        }
```

- `RunInitializeAsync`의 provider 루프(`_active.Clear();`부터 `var succeeded = _active.Count > 0;`까지)를 `var succeeded = await _initializer.InitializeAsync();`로 바꾼다.
- `Fanout`의 `_active`를 `_initializer.Active`로 바꾼다.
- `Dispose`의 `foreach (var provider in _providers)`를 `_initializer.Providers`로 바꾸고 `_providers.Clear(); _active.Clear();`를 지운다. Dispose 뒤에 `Fanout`이 돌지 않는 것은 `_disposed` 가드가 이미 막는다.

- [ ] **Step 5: 구조 변경 통과 확인 후 커밋**

Run: 전체 테스트 PASS(동작 변경 없음).

```bash
git add Assets/FoundationDI/Runtime/Services/AnalyticsService Assets/FoundationDI/Tests/AnalyticsProviderInitializerTest.cs* Assets/FoundationDI/Tests/AnalyticsTestDoubles.cs
git commit -m "[STRUCTURAL] 분석 provider 초기화를 AnalyticsProviderInitializer로 떼어 낸다"
```

- [ ] **Step 6: [BEHAVIORAL] 선행 작업 등록 — 실패하는 테스트 작성**

UMP 앞에서 선행 작업을 기다리는 순서 자체는 Task 6(`약관_다음에_선행_작업을_끝내고_UMP를_시작한다`)이 이미 고정했다. 여기서는 분석 등록이 provider 초기화를 그 선행 작업으로 **노출하는지**만 확인한다. 컨테이너를 빌드·해석하지 않고 빌더의 바인딩만 본다 — 해석하면 provider 팩토리가 EditMode에서 creator 없음 에러를 남길 수 있다.

`Tests/AnalyticsProviderInitializerTest.cs`에 추가한다.

```csharp
    [Test]
    public void 분석_등록이_provider_초기화를_동의_선행_작업으로_노출한다()
    {
        var settings = ScriptableObject.CreateInstance<AnalyticsServiceSettings>();

        try
        {
            var builder = new Reflex.Core.ContainerBuilder();

            builder.RegisterAnalyticsService(settings);

            // 광고가 먼저 동의를 요청해도 ConsentService가 이 바인딩으로 분석 SDK 초기화를 먼저 기다린다.
            Assert.IsTrue(builder.HasBinding(typeof(IConsentPrerequisite)));
            Assert.IsTrue(builder.HasBinding(typeof(AnalyticsProviderInitializer)));
        }
        finally
        {
            Object.DestroyImmediate(settings);
        }
    }
```

- [ ] **Step 7: 실패 확인**

Run: `AnalyticsProviderInitializerTest`
Expected: 새 테스트 FAIL — `IConsentPrerequisite` 바인딩이 없다.

- [ ] **Step 8: 등록과 설정 수정**

`AnalyticsServiceSettings.cs`에 필드와 프로퍼티를 추가한다(기존 Header 관례를 따른다).

```csharp
        [Header("Consent")]
        [Tooltip("ConsentService의 동의를 기다린 뒤 수집을 시작한다. GDPR 지역에서 분석을 거부하면 수집하지 " +
                 "않는다. 켜 두면 빌드 후처리가 Firebase 자동 수집을 기본으로 꺼 둔다(동의 후 켠다).")]
        [SerializeField] private bool _waitForConsent = true;

        public bool WaitForConsent => _waitForConsent;
```

`AnalyticsServiceRegistration.cs`의 팩토리를 교체한다.

```csharp
            builder.RegisterFactory<AnalyticsProviderInitializer>(container =>
            {
                var factory = container.Resolve<IAnalyticsProviderFactory>();
                var options = settings.ToOptions();
                var types = settings.ResolveProviders(Application.isEditor);
                var providers = factory.CreateAll(types, options, settings.ProviderSettings);

                return new AnalyticsProviderInitializer(providers);
            }, new[] { typeof(AnalyticsProviderInitializer), typeof(IConsentPrerequisite) },
               Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterFactory<IAnalyticsService>(container =>
            {
                var initializer = container.Resolve<AnalyticsProviderInitializer>();

                return new AnalyticsService(initializer, settings.ToOptions(), ResolveConsent(container, settings));
            }, Lifetime.Singleton, Resolution.Lazy);
        }

        private static IConsentService ResolveConsent(Container container, AnalyticsServiceSettings settings)
        {
            if (!settings.WaitForConsent) return null;

            if (container.HasBinding<IConsentService>()) return container.Resolve<IConsentService>();

            Debug.LogWarning("[AnalyticsService] Wait For Consent가 켜져 있지만 IConsentService가 등록되지 않았다. " +
                             "동의를 기다리지 않고 수집한다.");
            return null;
        }
```

파일 상단에 `using Reflex.Core;`가 있는지 확인한다(`Container`).

기존 `AnalyticsServiceRegistrationTest`가 경고를 받게 되면(기본 `WaitForConsent = true`인데 동의 미등록) `LogAssert.Expect(LogType.Warning, …"IConsentService가 등록되지 않았다")`를 추가한다.

- [ ] **Step 9: 통과 확인**

Run: `AnalyticsProviderInitializerTest`, `AnalyticsServiceRegistrationTest`, 전체 PASS.

- [ ] **Step 10: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/AnalyticsService Assets/FoundationDI/Tests
git commit -m "[BEHAVIORAL] 분석 provider 초기화를 UMP 선행 작업으로 등록한다"
```

---

### Task 13: Adjust DMA 번역

**Files:**
- Create: `Runtime/Services/ConsentService/DmaConsentMapper.cs`
- Modify: `Runtime/Services/AnalyticsService/Providers/Adjust/AdjustAnalyticsProvider.cs` (호스트 미컴파일)
- Test: `Tests/DmaConsentMapperTest.cs`

**Interfaces:**
- Produces: `public readonly struct DmaConsent { bool? Eea; bool AdUserData; bool AdPersonalization; }`, `public static class DmaConsentMapper { public static bool TryMap(TcfSignals tcf, out DmaConsent consent); }`

규칙(스펙 §7.3): GDPR 판정이 없으면(UMP 미사용) 보내지 않는다 — `eea`를 빼면 Google이 EEA로 간주하므로 허용값을 잘못 보내는 것보다 안 보내는 쪽이 안전하다. `gdprApplies == false` → eea=0, 둘 다 1. `true` → eea=1, ad_user_data = P1 && P7, ad_personalization = P3 && P4.

- [ ] **Step 1: 실패하는 테스트**

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class DmaConsentMapperTest
{
    [Test]
    public void GDPR_판정이_없으면_보내지_않는다()
    {
        Assert.IsFalse(DmaConsentMapper.TryMap(TcfSignals.None, out _));
    }

    [Test]
    public void GDPR_지역이_아니면_EEA_아님과_허용을_보낸다()
    {
        Assert.IsTrue(DmaConsentMapper.TryMap(new TcfSignals(false, null), out var dma));
        Assert.AreEqual(false, dma.Eea);
        Assert.IsTrue(dma.AdUserData);
        Assert.IsTrue(dma.AdPersonalization);
    }

    [Test]
    public void GDPR_지역에서_모두_동의하면_모두_허용한다()
    {
        Assert.IsTrue(DmaConsentMapper.TryMap(new TcfSignals(true, "1111111111"), out var dma));
        Assert.AreEqual(true, dma.Eea);
        Assert.IsTrue(dma.AdUserData);
        Assert.IsTrue(dma.AdPersonalization);
    }

    [Test]
    public void 목적7을_거부하면_광고_사용자_데이터를_거부한다()
    {
        //                                               P1 P2 P3 P4 P5 P6 P7
        Assert.IsTrue(DmaConsentMapper.TryMap(new TcfSignals(true, "1111110111"), out var dma));
        Assert.IsFalse(dma.AdUserData);
        Assert.IsTrue(dma.AdPersonalization);
    }

    [Test]
    public void 목적4를_거부하면_광고_개인화를_거부한다()
    {
        Assert.IsTrue(DmaConsentMapper.TryMap(new TcfSignals(true, "1110111111"), out var dma));
        Assert.IsTrue(dma.AdUserData);
        Assert.IsFalse(dma.AdPersonalization);
    }

    [Test]
    public void GDPR_지역인데_목적_문자열이_없으면_모두_거부한다()
    {
        Assert.IsTrue(DmaConsentMapper.TryMap(new TcfSignals(true, null), out var dma));
        Assert.AreEqual(true, dma.Eea);
        Assert.IsFalse(dma.AdUserData);
        Assert.IsFalse(dma.AdPersonalization);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `DmaConsentMapperTest` → 컴파일 에러.

- [ ] **Step 3: 구현**

```csharp
namespace DarkNaku.FoundationDI
{
    public readonly struct DmaConsent
    {
        public bool? Eea { get; }
        public bool AdUserData { get; }
        public bool AdPersonalization { get; }

        public DmaConsent(bool? eea, bool adUserData, bool adPersonalization)
        {
            Eea = eea;
            AdUserData = adUserData;
            AdPersonalization = adPersonalization;
        }
    }

    // TCF 동의를 Google DMA 신호(eea / ad_user_data / ad_personalization)로 번역한다.
    // Adjust처럼 TCF를 스스로 읽지 않는 MMP에 넘길 때 쓴다.
    //
    // 매핑은 Google의 TCF → consent mode 매핑을 따른 이 패키지의 해석이다(P1·P7 → ad_user_data,
    // P3·P4 → ad_personalization). Adjust가 정한 규칙은 아니다.
    public static class DmaConsentMapper
    {
        // GDPR 판정이 없으면(UMP 미사용) false. eea를 빼고 보내면 Google이 EEA로 간주하므로,
        // 모르는 상태에서 허용값을 보내는 것보다 아무것도 안 보내는 쪽이 안전하다.
        public static bool TryMap(TcfSignals tcf, out DmaConsent consent)
        {
            if (!tcf.GdprApplies.HasValue)
            {
                consent = default;
                return false;
            }

            if (tcf.GdprApplies == false)
            {
                consent = new DmaConsent(false, true, true);
                return true;
            }

            consent = new DmaConsent(true,
                                     tcf.HasPurpose(1) && tcf.HasPurpose(7),
                                     tcf.HasPurpose(3) && tcf.HasPurpose(4));
            return true;
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `DmaConsentMapperTest` → 6개 PASS.

- [ ] **Step 5: Adjust 어댑터에 번역 연결 (호스트 미컴파일)**

`AdjustAnalyticsProvider.cs`의 클래스 선언을 `public sealed class AdjustAnalyticsProvider : IAnalyticsProvider, IAnalyticsFlushHook, IAnalyticsConsentAware`로 바꾸고 메서드를 추가한다.

```csharp
        // Adjust는 TCF를 읽지 않는다. EEA 유저에게 Google Ads를 쓰면 DMA 신호가 필수다.
        // 코어가 이 메서드를 flush 훅(EndFirstSessionDelay)보다 먼저 부르므로 첫 세션(인스톨)에 실린다.
        public void ApplyConsent(ConsentResult result)
        {
            if (!DmaConsentMapper.TryMap(result.Tcf, out var dma)) return;

            var sharing = new AdjustThirdPartySharing(null);

            sharing.AddGranularOption("google_dma", "eea", dma.Eea == true ? "1" : "0");
            sharing.AddGranularOption("google_dma", "ad_personalization", dma.AdPersonalization ? "1" : "0");
            sharing.AddGranularOption("google_dma", "ad_user_data", dma.AdUserData ? "1" : "0");

            Adjust.TrackThirdPartySharing(sharing);
        }
```

이 파일은 호스트에서 컴파일되지 않는다. Adjust Unity SDK v5의 시그니처(`AdjustThirdPartySharing(bool? isEnabled)`, `AddGranularOption(string partnerName, string key, string value)`, `Adjust.TrackThirdPartySharing`)를 https://dev.adjust.com/en/sdk/unity/features/privacy 에서 다시 확인한다.

- [ ] **Step 6: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ConsentService/DmaConsentMapper.cs* Assets/FoundationDI/Tests/DmaConsentMapperTest.cs* Assets/FoundationDI/Runtime/Services/AnalyticsService/Providers/Adjust/AdjustAnalyticsProvider.cs
git commit -m "[BEHAVIORAL] TCF 동의를 Adjust DMA 신호로 번역해 보낸다

Adjust 어댑터 수정은 호스트 미컴파일 — 실기 빌드로 확인 필요."
```

---

### Task 14: 기본 약관 창 UI

**Files:**
- Create: `Runtime/Services/ConsentService/Policy/IUrlOpener.cs`
- Create: `Runtime/Services/ConsentService/Policy/PolicyAgreementView.cs`
- Create: `Runtime/Services/ConsentService/Policy/TemporaryEventSystem.cs`
- Create: `Runtime/Services/ConsentService/Policy/DefaultPolicyAgreementPrompt.cs`
- Create: `Editor/ConsentService/PolicyAgreementPrefabBuilder.cs`
- Create (도구 실행 결과): `Runtime/Services/ConsentService/Policy/PolicyAgreementView.prefab`
- Modify: `Runtime/Services/ConsentService/ConsentServiceSettings.cs` (`_promptPrefab` 추가)
- Modify: `Runtime/Services/ConsentService/ConsentServiceRegistration.cs` (기본 prompt 연결)
- Test: `Tests/PolicyAgreementPromptTest.cs`

**Interfaces:**
- Produces: `public interface IUrlOpener { void Open(string url); }`, `public sealed class UnityUrlOpener : IUrlOpener`, `public class PolicyAgreementView : MonoBehaviour { void Bind(PolicyAgreementContent); event Action Accepted; event Action<string> LinkRequested; }`, `public sealed class DefaultPolicyAgreementPrompt : IPolicyAgreementPrompt { ctor(PolicyAgreementView prefab, IUrlOpener opener); }`, `ConsentServiceSettings.PromptPrefab`.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class PolicyAgreementPromptTest
{
    private class RecordingOpener : IUrlOpener
    {
        public readonly List<string> Opened = new();
        public void Open(string url) => Opened.Add(url);
    }

    private readonly List<Object> _created = new();

    [TearDown]
    public void TearDown()
    {
        foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
        _created.Clear();

        foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None)) Object.DestroyImmediate(es.gameObject);
    }

    // 프리팹 대신 같은 모양의 계층을 코드로 만든다(테스트가 에셋에 의존하지 않게).
    private PolicyAgreementView CreateViewTemplate()
    {
        var root = new GameObject("PolicyAgreementView", typeof(RectTransform), typeof(Canvas));
        _created.Add(root);

        TMP_Text Text(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(root.transform, false);
            return go.GetComponent<TMP_Text>();
        }

        Button Button(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(root.transform, false);
            return go.GetComponent<Button>();
        }

        var view = root.AddComponent<PolicyAgreementView>();
        view.SetReferences(Text("Title"), Text("Message"), Button("Accept"), Text("AcceptLabel"),
                           Button("Terms"), Text("TermsLabel"), Button("Privacy"), Text("PrivacyLabel"));
        return view;
    }

    private static PolicyAgreementContent Content(string termsUrl = "https://t", string privacyUrl = "https://p") =>
        new("제목", "본문", "동의", "이용약관", termsUrl, "개인정보처리방침", privacyUrl);

    [Test]
    public void 문구를_바인딩한다()
    {
        var view = CreateViewTemplate();

        view.Bind(Content());

        Assert.AreEqual("제목", view.TitleText.text);
        Assert.AreEqual("동의", view.AcceptLabelText.text);
    }

    [Test]
    public void 링크가_비어_있으면_그_버튼을_숨긴다()
    {
        var view = CreateViewTemplate();

        view.Bind(Content(termsUrl: ""));

        Assert.IsFalse(view.TermsButton.gameObject.activeSelf);
        Assert.IsTrue(view.PrivacyButton.gameObject.activeSelf);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 동의_버튼을_누르면_완료하고_창을_정리한다() => AwaitableTest.Run(async () =>
    {
        var template = CreateViewTemplate();
        var sut = new DefaultPolicyAgreementPrompt(template, new RecordingOpener());

        var showing = sut.ShowAsync(Content());
        var instance = sut.CurrentView;

        Assert.IsNotNull(instance, "창을 만들지 않았다");

        instance.AcceptButton.onClick.Invoke();
        await showing;

        Assert.IsTrue(instance == null, "수락 뒤 창이 남았다");
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 링크_버튼은_URL을_연다() => AwaitableTest.Run(async () =>
    {
        var opener = new RecordingOpener();
        var sut = new DefaultPolicyAgreementPrompt(CreateViewTemplate(), opener);

        var showing = sut.ShowAsync(Content());
        sut.CurrentView.TermsButton.onClick.Invoke();
        sut.CurrentView.PrivacyButton.onClick.Invoke();
        sut.CurrentView.AcceptButton.onClick.Invoke();
        await showing;

        CollectionAssert.AreEqual(new[] { "https://t", "https://p" }, opener.Opened);
    });

    [Test]
    public void 프리팹이_없으면_수락하지_않고_예외를_던진다()
    {
        var sut = new DefaultPolicyAgreementPrompt(null, new RecordingOpener());

        Assert.Throws<System.InvalidOperationException>(() => sut.ShowAsync(Content()));
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator EventSystem이_없으면_표시하는_동안만_만든다() => AwaitableTest.Run(async () =>
    {
        var sut = new DefaultPolicyAgreementPrompt(CreateViewTemplate(), new RecordingOpener());

        var showing = sut.ShowAsync(Content());

        Assert.IsNotNull(Object.FindFirstObjectByType<EventSystem>(), "첫 씬보다 먼저 뜨면 버튼이 눌리지 않는다");

        sut.CurrentView.AcceptButton.onClick.Invoke();
        await showing;

        Assert.IsNull(Object.FindFirstObjectByType<EventSystem>(), "임시 EventSystem을 남겼다");
    });

    [Test]
    public void 이미_EventSystem이_있으면_만들지_않는다()
    {
        var existing = new GameObject("ES", typeof(EventSystem));
        _created.Add(existing);
        var sut = new DefaultPolicyAgreementPrompt(CreateViewTemplate(), new RecordingOpener());

        sut.ShowAsync(Content());

        Assert.AreEqual(1, Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length);
        sut.Close();
    }

    [Test]
    public void Close하면_창과_임시_EventSystem을_정리한다()
    {
        var sut = new DefaultPolicyAgreementPrompt(CreateViewTemplate(), new RecordingOpener());

        sut.ShowAsync(Content());
        var view = sut.CurrentView;
        sut.Close();

        Assert.IsTrue(view == null);
        Assert.IsNull(Object.FindFirstObjectByType<EventSystem>());
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `PolicyAgreementPromptTest` → 컴파일 에러.

- [ ] **Step 3: URL 오프너**

`IUrlOpener.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 링크 열기 seam. 테스트가 실제 브라우저를 띄우지 않게 한다.
    public interface IUrlOpener
    {
        void Open(string url);
    }

    public sealed class UnityUrlOpener : IUrlOpener
    {
        public void Open(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            Application.OpenURL(url);
        }
    }
}
```

- [ ] **Step 4: View**

`PolicyAgreementView.cs`:

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    // 기본 약관 수락 창의 View. 스킨을 바꾸려면 프리팹을 복제해 디자인만 고치고
    // ConsentServiceSettings.PromptPrefab에 지정한다 — 루트에 이 컴포넌트만 있으면 된다.
    //
    // 리스너를 Awake가 아니라 Bind에서 건다. EditMode에서는 AddComponent/Instantiate에 Awake가
    // 불리지 않아 테스트가 버튼을 눌러 볼 수 없다.
    public class PolicyAgreementView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _message;
        [SerializeField] private Button _acceptButton;
        [SerializeField] private TMP_Text _acceptLabel;
        [SerializeField] private Button _termsButton;
        [SerializeField] private TMP_Text _termsLabel;
        [SerializeField] private Button _privacyButton;
        [SerializeField] private TMP_Text _privacyLabel;

        private PolicyAgreementContent _content;

        public event Action Accepted;
        public event Action<string> LinkRequested;

        internal TMP_Text TitleText => _title;
        internal TMP_Text AcceptLabelText => _acceptLabel;
        internal Button AcceptButton => _acceptButton;
        internal Button TermsButton => _termsButton;
        internal Button PrivacyButton => _privacyButton;

        public void Bind(PolicyAgreementContent content)
        {
            _content = content;

            SetText(_title, content.Title);
            SetText(_message, content.Message);
            SetText(_acceptLabel, content.AcceptLabel);
            SetText(_termsLabel, content.TermsLabel);
            SetText(_privacyLabel, content.PrivacyLabel);

            // URL이 없는 링크는 눌러도 아무 일이 없으므로 숨긴다.
            SetActive(_termsButton, !string.IsNullOrEmpty(content.TermsUrl));
            SetActive(_privacyButton, !string.IsNullOrEmpty(content.PrivacyUrl));

            Rewire(_acceptButton, OnAccept);
            Rewire(_termsButton, OnTerms);
            Rewire(_privacyButton, OnPrivacy);
        }

        // 프리팹 빌더와 테스트가 참조를 채우는 데 쓴다.
        internal void SetReferences(TMP_Text title, TMP_Text message, Button acceptButton, TMP_Text acceptLabel,
                                    Button termsButton, TMP_Text termsLabel, Button privacyButton, TMP_Text privacyLabel)
        {
            _title = title;
            _message = message;
            _acceptButton = acceptButton;
            _acceptLabel = acceptLabel;
            _termsButton = termsButton;
            _termsLabel = termsLabel;
            _privacyButton = privacyButton;
            _privacyLabel = privacyLabel;
        }

        private void OnAccept() => Accepted?.Invoke();
        private void OnTerms() => LinkRequested?.Invoke(_content.TermsUrl);
        private void OnPrivacy() => LinkRequested?.Invoke(_content.PrivacyUrl);

        private static void Rewire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value ?? string.Empty;
        }

        private static void SetActive(Component component, bool active)
        {
            if (component != null) component.gameObject.SetActive(active);
        }
    }
}
```

- [ ] **Step 5: 임시 EventSystem**

`TemporaryEventSystem.cs`:

```csharp
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    // 약관 창은 첫 씬의 UI보다 먼저 뜰 수 있다. 그때 씬에 EventSystem이 없으면 버튼이 눌리지 않아
    // 게임이 시작되지 않는다. 없을 때만 만들고, 창이 닫히면 지운다(씬의 것과 겹치지 않게).
    internal sealed class TemporaryEventSystem
    {
        private const string InputSystemModuleType =
            "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem";

        private GameObject _created;

        public void Acquire()
        {
            if (_created != null) return;
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;

            _created = new GameObject("[ConsentService] EventSystem", typeof(EventSystem));

            // Input System 전용 프로젝트에서 StandaloneInputModule은 매 프레임 예외를 낸다. 패키지가
            // Input System을 참조하지 않도록 리플렉션으로 찾는다.
            var inputSystemModule = Type.GetType(InputSystemModuleType);

            if (inputSystemModule != null) _created.AddComponent(inputSystemModule);
            else _created.AddComponent<StandaloneInputModule>();

            if (Application.isPlaying) Object.DontDestroyOnLoad(_created);
        }

        public void Release()
        {
            if (_created == null) return;

            if (Application.isPlaying) Object.Destroy(_created);
            else Object.DestroyImmediate(_created);

            _created = null;
        }
    }
}
```

- [ ] **Step 6: 기본 prompt**

`DefaultPolicyAgreementPrompt.cs`:

```csharp
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    // 앱 수명 UI라 UINavigator(씬 수명)를 쓰지 않고 자체 캔버스를 연다(DummyAdCanvas와 같은 방식).
    public sealed class DefaultPolicyAgreementPrompt : IPolicyAgreementPrompt
    {
        // 게임 UI와 광고 더미 캔버스보다 위.
        private const int SortingOrder = 32000;

        private readonly PolicyAgreementView _prefab;
        private readonly IUrlOpener _opener;
        private readonly TemporaryEventSystem _eventSystem = new();

        private AwaitableCompletionSource _pending;

        public DefaultPolicyAgreementPrompt(PolicyAgreementView prefab, IUrlOpener opener)
        {
            _prefab = prefab;
            _opener = opener ?? new UnityUrlOpener();
        }

        internal PolicyAgreementView CurrentView { get; private set; }

        public Awaitable ShowAsync(PolicyAgreementContent content)
        {
            // 조용히 완료하면 사용자가 보지도 않은 약관을 수락한 것으로 저장된다.
            if (_prefab == null)
            {
                throw new InvalidOperationException(
                    "[ConsentService] 약관 창 프리팹이 없다. ConsentServiceSettings의 Prompt Prefab을 지정한다.");
            }

            Close();

            CurrentView = Object.Instantiate(_prefab);
            CurrentView.name = "[ConsentService] PolicyAgreement";

            var canvas = CurrentView.GetComponentInChildren<Canvas>(true);

            if (canvas != null)
            {
                canvas.overrideSorting = true;
                canvas.sortingOrder = SortingOrder;
            }

            if (Application.isPlaying) Object.DontDestroyOnLoad(CurrentView.gameObject);

            _eventSystem.Acquire();

            CurrentView.Accepted += OnAccepted;
            CurrentView.LinkRequested += _opener.Open;
            CurrentView.Bind(content);

            _pending = new AwaitableCompletionSource();
            return _pending.Awaitable;
        }

        public void Close()
        {
            if (CurrentView != null)
            {
                CurrentView.Accepted -= OnAccepted;
                CurrentView.LinkRequested -= _opener.Open;

                if (Application.isPlaying) Object.Destroy(CurrentView.gameObject);
                else Object.DestroyImmediate(CurrentView.gameObject);
            }

            CurrentView = null;
            _eventSystem.Release();
        }

        private void OnAccepted()
        {
            var pending = _pending;
            _pending = null;

            Close();
            pending?.SetResult();
        }
    }
}
```

플레이 모드의 `Object.Destroy`는 프레임 끝에 파괴하므로, 테스트의 "수락 뒤 창이 남았다" 단언은 EditMode(`DestroyImmediate`)에서만 즉시 참이다. 이 테스트는 EditMode 어셈블리에 있으니 그대로 둔다.

- [ ] **Step 7: 통과 확인**

Run: `PolicyAgreementPromptTest` → 8개 PASS.

- [ ] **Step 8: 프리팹 빌더와 프리팹 생성**

`Editor/ConsentService/PolicyAgreementPrefabBuilder.cs`:

```csharp
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI.Editor
{
    // 기본 약관 창 프리팹을 코드로 만든다. 디자인을 바꾸려면 만들어진 프리팹을 복제해 고친다.
    internal static class PolicyAgreementPrefabBuilder
    {
        internal const string PrefabPath =
            "Assets/FoundationDI/Runtime/Services/ConsentService/Policy/PolicyAgreementView.prefab";

        [MenuItem("Tools/FoundationDI/Consent/Rebuild Default Policy Prompt Prefab")]
        private static void Rebuild()
        {
            var root = Build();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();
            Debug.Log($"[ConsentService] 기본 약관 창 프리팹을 만들었다: {PrefabPath}");
        }

        internal static GameObject Build()
        {
            var root = new GameObject("PolicyAgreementView", typeof(RectTransform), typeof(Canvas),
                                      typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            // 뒤쪽 입력을 막는 반투명 배경.
            var dim = CreateImage("Dim", root.transform, new Color(0f, 0f, 0f, 0.6f));
            Stretch(dim.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var panel = CreateImage("Panel", root.transform, Color.white);
            Stretch(panel.rectTransform, new Vector2(0.08f, 0.25f), new Vector2(0.92f, 0.75f), Vector2.zero, Vector2.zero);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(48, 48, 48, 48);
            layout.spacing = 24;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var title = CreateText("Title", panel.transform, 56, FontStyles.Bold);
            var message = CreateText("Message", panel.transform, 40, FontStyles.Normal);
            message.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;

            var links = new GameObject("Links", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            links.transform.SetParent(panel.transform, false);
            var linksLayout = links.GetComponent<HorizontalLayoutGroup>();
            linksLayout.spacing = 24;
            linksLayout.childAlignment = TextAnchor.MiddleCenter;
            linksLayout.childControlWidth = true;
            linksLayout.childControlHeight = true;

            var (termsButton, termsLabel) = CreateButton("Terms", links.transform, new Color(0.92f, 0.92f, 0.92f), 36);
            var (privacyButton, privacyLabel) = CreateButton("Privacy", links.transform, new Color(0.92f, 0.92f, 0.92f), 36);
            var (acceptButton, acceptLabel) = CreateButton("Accept", panel.transform, new Color(0.18f, 0.48f, 0.95f), 44);
            acceptLabel.color = Color.white;
            acceptButton.gameObject.AddComponent<LayoutElement>().minHeight = 140;

            var view = root.AddComponent<PolicyAgreementView>();
            view.SetReferences(title, message, acceptButton, acceptLabel, termsButton, termsLabel, privacyButton, privacyLabel);

            return root;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static TMP_Text CreateText(string name, Transform parent, float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.fontStyle = style;
            text.color = new Color(0.1f, 0.1f, 0.1f);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static (Button, TMP_Text) CreateButton(string name, Transform parent, Color color, float fontSize)
        {
            var image = CreateImage(name, parent, color);
            var button = image.gameObject.AddComponent<Button>();
            image.gameObject.AddComponent<LayoutElement>().minHeight = 110;

            var label = CreateText("Label", image.transform, fontSize, FontStyles.Normal);
            Stretch(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            return (button, label);
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }
}
```

`PolicyAgreementView.SetReferences`가 `internal`이므로 `Runtime/AssemblyInfo.cs`에 이미 `InternalsVisibleTo("FoundationDI.Editor")`가 있는지 확인한다(있다).

UnityMCP `execute_menu_item`으로 `Tools/FoundationDI/Consent/Rebuild Default Policy Prompt Prefab`을 실행한다. `PolicyAgreementView.prefab`과 `.meta`가 생겼는지 확인한다.

- [ ] **Step 9: 설정과 등록에 연결**

`ConsentServiceSettings.cs`의 Policy 섹션 끝에 추가한다.

```csharp
        [Tooltip("약관 수락 창 프리팹. 루트에 PolicyAgreementView가 있어야 한다. 비우면 기본 프리팹을 쓴다.")]
        [SerializeField] private PolicyAgreementView _promptPrefab;

        public PolicyAgreementView PromptPrefab => _promptPrefab;

#if UNITY_EDITOR
        // 새 설정 에셋을 만들면 패키지 기본 프리팹으로 채운다. 런타임에서 Resources로 찾지 않기 위해서다.
        private void Reset()
        {
            const string defaultPrefabPath =
                "Packages/com.darknaku.foundationdi/Runtime/Services/ConsentService/Policy/PolicyAgreementView.prefab";
            const string hostPrefabPath =
                "Assets/FoundationDI/Runtime/Services/ConsentService/Policy/PolicyAgreementView.prefab";

            _promptPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<PolicyAgreementView>(defaultPrefabPath)
                            ?? UnityEditor.AssetDatabase.LoadAssetAtPath<PolicyAgreementView>(hostPrefabPath);
        }
#endif
```

`ConsentServiceRegistration.cs`의 팩토리에서 `prompt` 인자를 다음으로 바꿔 넘긴다.

```csharp
                var resolvedPrompt = prompt ?? new DefaultPolicyAgreementPrompt(settings.PromptPrefab, new UnityUrlOpener());
```

그리고 `new ConsentService(…, prompt, …)`의 `prompt`를 `resolvedPrompt`로 바꾼다.

- [ ] **Step 10: 전체 테스트 후 커밋**

Run: 전체 PASS.

```bash
git add Assets/FoundationDI/Runtime/Services/ConsentService Assets/FoundationDI/Editor/ConsentService Assets/FoundationDI/Editor/ConsentService.meta Assets/FoundationDI/Tests/PolicyAgreementPromptTest.cs*
git commit -m "[BEHAVIORAL] 기본 약관 수락 창(uGUI 프리팹)을 제공한다"
```

---

### Task 15: UMP Android 브리지

**Files:**
- Create: `Runtime/Services/ConsentService/Ump/UmpStatusMapper.cs`
- Create: `Runtime/Services/ConsentService/Ump/AndroidUmpConsent.cs`
- Test: `Tests/UmpStatusMapperTest.cs`

**Interfaces:**
- Produces: `internal static class UmpStatusMapper { FromAndroid(int), FromIos(int), ToAndroidGeography(UmpDebugGeography), ToIosGeography(UmpDebugGeography) }`, `internal sealed class AndroidUmpConsent : IUmpConsent { static AndroidUmpConsent TryCreate(IMainThreadDispatcher dispatcher); }` (`UNITY_ANDROID && !UNITY_EDITOR`에서만 컴파일).

네이티브 상수(UMP 문서 기준):
- Android `ConsentInformation.ConsentStatus`: UNKNOWN=0, NOT_REQUIRED=1, REQUIRED=2, OBTAINED=3
- iOS `UMPConsentStatus`: Unknown=0, Required=1, NotRequired=2, Obtained=3
- Android `ConsentDebugSettings.DebugGeography` / iOS `UMPDebugGeography`: DISABLED=0, EEA=1, NOT_EEA=2, REGULATED_US_STATE=3, OTHER=4

**구현 전에 위 값을 확인한다**: https://developers.google.com/admob/android/privacy/api/reference/com/google/android/ump/ConsentInformation.ConsentStatus 와 iOS `UMPConsentInformation.h`. 다르면 아래 테스트와 매퍼를 문서 값으로 고친다.

- [ ] **Step 1: 실패하는 테스트**

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class UmpStatusMapperTest
{
    [TestCase(0, UmpConsentStatus.Unknown)]
    [TestCase(1, UmpConsentStatus.NotRequired)]
    [TestCase(2, UmpConsentStatus.Required)]
    [TestCase(3, UmpConsentStatus.Obtained)]
    [TestCase(99, UmpConsentStatus.Unknown)]
    public void 안드로이드_상태를_번역한다(int native, UmpConsentStatus expected)
    {
        Assert.AreEqual(expected, UmpStatusMapper.FromAndroid(native));
    }

    [TestCase(0, UmpConsentStatus.Unknown)]
    [TestCase(1, UmpConsentStatus.Required)]
    [TestCase(2, UmpConsentStatus.NotRequired)]
    [TestCase(3, UmpConsentStatus.Obtained)]
    [TestCase(99, UmpConsentStatus.Unknown)]
    public void iOS_상태를_번역한다(int native, UmpConsentStatus expected)
    {
        // iOS는 Required와 NotRequired의 순서가 안드로이드와 반대다. 캐스팅하면 조용히 뒤집힌다.
        Assert.AreEqual(expected, UmpStatusMapper.FromIos(native));
    }

    [TestCase(UmpDebugGeography.Disabled, 0)]
    [TestCase(UmpDebugGeography.Eea, 1)]
    [TestCase(UmpDebugGeography.NotEea, 2)]
    [TestCase(UmpDebugGeography.RegulatedUsState, 3)]
    [TestCase(UmpDebugGeography.Other, 4)]
    public void 디버그_지역을_네이티브_값으로_번역한다(UmpDebugGeography geography, int expected)
    {
        Assert.AreEqual(expected, UmpStatusMapper.ToAndroidGeography(geography));
        Assert.AreEqual(expected, UmpStatusMapper.ToIosGeography(geography));
    }
}
```

- [ ] **Step 2: 실패 확인 → 매퍼 구현**

`UmpStatusMapper.cs`:

```csharp
namespace DarkNaku.FoundationDI
{
    // 네이티브 enum 값을 캐스팅하지 않고 명시적으로 번역한다. Android와 iOS의 ConsentStatus
    // 순서가 다르고(REQUIRED/NOT_REQUIRED), SDK가 값을 바꾸면 여기서 테스트가 깨져야 한다.
    internal static class UmpStatusMapper
    {
        public static UmpConsentStatus FromAndroid(int value)
        {
            switch (value)
            {
                case 1: return UmpConsentStatus.NotRequired;
                case 2: return UmpConsentStatus.Required;
                case 3: return UmpConsentStatus.Obtained;
                default: return UmpConsentStatus.Unknown;
            }
        }

        public static UmpConsentStatus FromIos(int value)
        {
            switch (value)
            {
                case 1: return UmpConsentStatus.Required;
                case 2: return UmpConsentStatus.NotRequired;
                case 3: return UmpConsentStatus.Obtained;
                default: return UmpConsentStatus.Unknown;
            }
        }

        public static int ToAndroidGeography(UmpDebugGeography geography) => ToNativeGeography(geography);

        public static int ToIosGeography(UmpDebugGeography geography) => ToNativeGeography(geography);

        private static int ToNativeGeography(UmpDebugGeography geography)
        {
            switch (geography)
            {
                case UmpDebugGeography.Eea: return 1;
                case UmpDebugGeography.NotEea: return 2;
                case UmpDebugGeography.RegulatedUsState: return 3;
                case UmpDebugGeography.Other: return 4;
                default: return 0;
            }
        }
    }
}
```

Run: `UmpStatusMapperTest` → 전부 PASS.

- [ ] **Step 3: Android 브리지 작성**

`AndroidUmpConsent.cs`:

```csharp
#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // UMP Android SDK(com.google.android.ump)를 JNI로 직접 부른다.
    //
    // Java 소스를 두지 않는 이유: UMP 클래스를 참조하는 .java는 UMP를 끈 프로젝트에서 Gradle
    // 컴파일을 깨뜨린다. JNI 호출은 실행 시점에만 클래스를 찾으므로 의존성이 없어도 빌드된다.
    //
    // UMP 콜백은 메인 스레드 보장이 없다(UI 스레드에서 온다). 전부 디스패처로 넘긴다.
    internal sealed class AndroidUmpConsent : IUmpConsent
    {
        private const string UmpClass = "com.google.android.ump.UserMessagingPlatform";
        private const string Package = "com.google.android.ump.";

        private readonly IMainThreadDispatcher _dispatcher;
        private readonly AndroidJavaObject _activity;
        private readonly AndroidJavaObject _info;

        private AndroidUmpConsent(IMainThreadDispatcher dispatcher, AndroidJavaObject activity, AndroidJavaObject info)
        {
            _dispatcher = dispatcher;
            _activity = activity;
            _info = info;
        }

        // UMP aar가 빌드에 없으면(의존성 XML 미생성, EDM4U 미해석) null.
        public static AndroidUmpConsent TryCreate(IMainThreadDispatcher dispatcher)
        {
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");

                using var ump = new AndroidJavaClass(UmpClass);
                var info = ump.CallStatic<AndroidJavaObject>("getConsentInformation", activity);

                return new AndroidUmpConsent(dispatcher, activity, info);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ConsentService] UMP Android SDK를 찾지 못했다. UMP 없이 진행한다. " +
                                 $"Use UMP를 켰다면 EDM4U로 Android 의존성을 해석했는지 확인한다: {e.Message}");
                return null;
            }
        }

        public bool IsSupported => true;

        public UmpConsentStatus Status => UmpStatusMapper.FromAndroid(_info.Call<int>("getConsentStatus"));

        public bool CanRequestAds => _info.Call<bool>("canRequestAds");

        public bool IsPrivacyOptionsRequired
        {
            get
            {
                using var status = _info.Call<AndroidJavaObject>("getPrivacyOptionsRequirementStatus");
                return status.Call<string>("name") == "REQUIRED";
            }
        }

        // UMP는 IAB 키를 앱 기본 SharedPreferences(<패키지명>_preferences)에 쓴다.
        public TcfSignals ReadTcf()
        {
            var name = _activity.Call<string>("getPackageName") + "_preferences";

            using var prefs = _activity.Call<AndroidJavaObject>("getSharedPreferences", name, 0);

            bool? gdprApplies = null;

            if (prefs.Call<bool>("contains", "IABTCF_gdprApplies"))
            {
                gdprApplies = prefs.Call<int>("getInt", "IABTCF_gdprApplies", 0) == 1;
            }

            var purposes = prefs.Call<string>("getString", "IABTCF_PurposeConsents", null);

            return new TcfSignals(gdprApplies, purposes);
        }

        public Awaitable<bool> UpdateAsync(UmpRequestOptions options)
        {
            var source = new AwaitableCompletionSource<bool>();

            RunOnUiThread(() =>
            {
                try
                {
                    using var parameters = BuildParameters(options);

                    _info.Call("requestConsentInfoUpdate", _activity, parameters,
                               new UpdateSuccessListener(() => _dispatcher.Post(() => source.TrySetResult(true))),
                               new UpdateFailureListener(message => _dispatcher.Post(() =>
                               {
                                   Debug.LogWarning($"[ConsentService] UMP 갱신 실패: {message}");
                                   source.TrySetResult(false);
                               })));
                }
                catch (Exception e)
                {
                    _dispatcher.Post(() =>
                    {
                        Debug.LogWarning($"[ConsentService] UMP 갱신 호출 실패: {e.Message}");
                        source.TrySetResult(false);
                    });
                }
            });

            return source.Awaitable;
        }

        public Awaitable<bool> LoadAndShowIfRequiredAsync() => ShowForm("loadAndShowConsentFormIfRequired");

        public Awaitable<bool> ShowPrivacyOptionsAsync() => ShowForm("showPrivacyOptionsForm");

        private Awaitable<bool> ShowForm(string method)
        {
            var source = new AwaitableCompletionSource<bool>();

            RunOnUiThread(() =>
            {
                try
                {
                    using var ump = new AndroidJavaClass(UmpClass);

                    ump.CallStatic(method, _activity, new FormDismissedListener(message => _dispatcher.Post(() =>
                    {
                        if (message != null) Debug.LogWarning($"[ConsentService] UMP 폼 에러({method}): {message}");
                        source.TrySetResult(message == null);
                    })));
                }
                catch (Exception e)
                {
                    _dispatcher.Post(() =>
                    {
                        Debug.LogWarning($"[ConsentService] UMP 폼 호출 실패({method}): {e.Message}");
                        source.TrySetResult(false);
                    });
                }
            });

            return source.Awaitable;
        }

        private AndroidJavaObject BuildParameters(UmpRequestOptions options)
        {
            using var builder = new AndroidJavaObject(Package + "ConsentRequestParameters$Builder");

            builder.Call<AndroidJavaObject>("setTagForUnderAgeOfConsent", options.TagForUnderAgeOfConsent).Dispose();

            var hasIds = options.TestDeviceHashedIds != null && options.TestDeviceHashedIds.Count > 0;

            if (options.DebugGeography != UmpDebugGeography.Disabled || hasIds)
            {
                using var debug = new AndroidJavaObject(Package + "ConsentDebugSettings$Builder", _activity);

                debug.Call<AndroidJavaObject>("setDebugGeography",
                                              UmpStatusMapper.ToAndroidGeography(options.DebugGeography)).Dispose();

                if (hasIds)
                {
                    foreach (var id in options.TestDeviceHashedIds)
                    {
                        debug.Call<AndroidJavaObject>("addTestDeviceHashedId", id).Dispose();
                    }
                }

                using var debugSettings = debug.Call<AndroidJavaObject>("build");
                builder.Call<AndroidJavaObject>("setConsentDebugSettings", debugSettings).Dispose();
            }

            return builder.Call<AndroidJavaObject>("build");
        }

        private void RunOnUiThread(Action action) =>
            _activity.Call("runOnUiThread", new AndroidJavaRunnable(action));

        private sealed class UpdateSuccessListener : AndroidJavaProxy
        {
            private readonly Action _onSuccess;

            public UpdateSuccessListener(Action onSuccess)
                : base(Package + "ConsentInformation$OnConsentInfoUpdateSuccessListener")
            {
                _onSuccess = onSuccess;
            }

            // JNI가 이름으로 부른다. 이름을 바꾸지 않는다.
            public void onConsentInfoUpdateSuccess() => _onSuccess();
        }

        private sealed class UpdateFailureListener : AndroidJavaProxy
        {
            private readonly Action<string> _onFailure;

            public UpdateFailureListener(Action<string> onFailure)
                : base(Package + "ConsentInformation$OnConsentInfoUpdateFailureListener")
            {
                _onFailure = onFailure;
            }

            public void onConsentInfoUpdateFailure(AndroidJavaObject formError) =>
                _onFailure(formError?.Call<string>("getMessage") ?? "unknown");
        }

        private sealed class FormDismissedListener : AndroidJavaProxy
        {
            private readonly Action<string> _onDismissed;

            public FormDismissedListener(Action<string> onDismissed)
                : base(Package + "ConsentForm$OnConsentFormDismissedListener")
            {
                _onDismissed = onDismissed;
            }

            // 에러가 없으면 formError가 null이다.
            public void onConsentFormDismissed(AndroidJavaObject formError) =>
                _onDismissed(formError?.Call<string>("getMessage"));
        }
    }
}
#endif
```

`AwaitableCompletionSource<T>.TrySetResult`는 기존 코드(`AdService.CompleteWaiters`)가 쓰고 있다.

- [ ] **Step 4: Android 빌드 타깃 컴파일 확인**

이 파일은 `UNITY_ANDROID && !UNITY_EDITOR`라 에디터에서는 컴파일되지 않는다. UnityMCP `execute_code`로 플레이어 스크립트 컴파일을 돌려 확인한다.

```csharp
var settings = new UnityEditor.Build.Player.ScriptCompilationSettings
{
    target = UnityEditor.BuildTarget.Android,
    group = UnityEditor.BuildTargetGroup.Android,
};
var result = UnityEditor.Build.Player.PlayerBuildInterface.CompilePlayerScripts(settings, "Temp/FdiAndroidCompile");
return result.assemblies.Count;
```

Expected: 예외 없이 어셈블리 수가 반환되고 `read_console`에 `AndroidUmpConsent` 관련 에러가 없다.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ConsentService/Ump Assets/FoundationDI/Tests/UmpStatusMapperTest.cs*
git commit -m "[BEHAVIORAL] UMP Android SDK를 JNI로 부르는 브리지를 추가한다"
```

---

### Task 16: UMP iOS 브리지와 팩토리

**Files:**
- Create: `Runtime/Services/ConsentService/Ump/Plugins/iOS/FDI_Ump.mm` (+ meta, iOS 전용)
- Create: `Runtime/Services/ConsentService/Ump/IosUmpConsent.cs`
- Create: `Runtime/Services/ConsentService/Ump/UmpConsentFactory.cs`
- Modify: `Runtime/Services/ConsentService/ConsentServiceRegistration.cs` (`ump` 줄)

**Interfaces:**
- Produces: `internal static class UmpConsentFactory { public static IUmpConsent Create(IMainThreadDispatcher dispatcher); }`

- [ ] **Step 1: 네이티브 헬퍼 작성**

`FDI_Ump.mm`:

```objc
// FoundationDI Google UMP 네이티브 브리지.
//
// GoogleUserMessagingPlatform 포드가 없으면(Use UMP를 끈 프로젝트) 스텁으로 컴파일되고
// FDI_Ump_IsAvailable이 false를 돌려준다. C# 쪽은 그때 UMP 없이 진행한다.
#import <UIKit/UIKit.h>

#if __has_include(<UserMessagingPlatform/UserMessagingPlatform.h>)
#import <UserMessagingPlatform/UserMessagingPlatform.h>
#define FDI_UMP_AVAILABLE 1
#else
#define FDI_UMP_AVAILABLE 0
#endif

extern UIViewController* UnityGetGLViewController();

typedef void (*FDI_UmpCallback)(int requestId, bool success, const char* message);

static char* FDI_Ump_CopyString(NSString* value)
{
    if (value == nil) return NULL;

    const char* utf8 = [value UTF8String];
    char* copy = (char*)malloc(strlen(utf8) + 1);
    strcpy(copy, utf8);
    return copy;
}

extern "C"
{
    bool FDI_Ump_IsAvailable(void)
    {
        return FDI_UMP_AVAILABLE == 1;
    }

    // iOS 15+는 Inactive일 때 띄운 모달을 버린다(ATT와 같은 문제). C#이 Active를 기다린 뒤 폼을 띄운다.
    bool FDI_Ump_IsApplicationActive(void)
    {
        return UIApplication.sharedApplication.applicationState == UIApplicationStateActive;
    }

    // IABTCF_gdprApplies. 키가 없으면 -1.
    int FDI_Ump_GdprApplies(void)
    {
        id value = [[NSUserDefaults standardUserDefaults] objectForKey:@"IABTCF_gdprApplies"];
        if (value == nil || ![value respondsToSelector:@selector(intValue)]) return -1;
        return [value intValue];
    }

    // 호출자(IL2CPP 마샬러)가 free한다.
    char* FDI_Ump_PurposeConsents(void)
    {
        id value = [[NSUserDefaults standardUserDefaults] objectForKey:@"IABTCF_PurposeConsents"];
        if (![value isKindOfClass:[NSString class]]) return NULL;
        return FDI_Ump_CopyString((NSString*)value);
    }

#if FDI_UMP_AVAILABLE
    int FDI_Ump_ConsentStatus(void)
    {
        return (int)UMPConsentInformation.sharedInstance.consentStatus;
    }

    bool FDI_Ump_CanRequestAds(void)
    {
        return UMPConsentInformation.sharedInstance.canRequestAds;
    }

    bool FDI_Ump_IsPrivacyOptionsRequired(void)
    {
        return UMPConsentInformation.sharedInstance.privacyOptionsRequirementStatus ==
               UMPPrivacyOptionsRequirementStatusRequired;
    }

    void FDI_Ump_RequestUpdate(int requestId, int debugGeography, const char* testDeviceIdsCsv,
                               bool tagForUnderAgeOfConsent, FDI_UmpCallback callback)
    {
        UMPRequestParameters* parameters = [[UMPRequestParameters alloc] init];
        parameters.tagForUnderAgeOfConsent = tagForUnderAgeOfConsent;

        bool hasIds = testDeviceIdsCsv != NULL && strlen(testDeviceIdsCsv) > 0;

        if (debugGeography != 0 || hasIds)
        {
            UMPDebugSettings* debug = [[UMPDebugSettings alloc] init];
            debug.geography = (UMPDebugGeography)debugGeography;

            if (hasIds)
            {
                NSString* csv = [NSString stringWithUTF8String:testDeviceIdsCsv];
                debug.testDeviceIdentifiers = [csv componentsSeparatedByString:@","];
            }

            parameters.debugSettings = debug;
        }

        [UMPConsentInformation.sharedInstance
            requestConsentInfoUpdateWithParameters:parameters
                                 completionHandler:^(NSError* error) {
                                     dispatch_async(dispatch_get_main_queue(), ^{
                                         callback(requestId, error == nil,
                                                  error ? [error.localizedDescription UTF8String] : NULL);
                                     });
                                 }];
    }

    void FDI_Ump_LoadAndShowIfRequired(int requestId, FDI_UmpCallback callback)
    {
        [UMPConsentForm loadAndPresentIfRequiredFromViewController:UnityGetGLViewController()
                                                  completionHandler:^(NSError* error) {
                                                      dispatch_async(dispatch_get_main_queue(), ^{
                                                          callback(requestId, error == nil,
                                                                   error ? [error.localizedDescription UTF8String] : NULL);
                                                      });
                                                  }];
    }

    void FDI_Ump_ShowPrivacyOptions(int requestId, FDI_UmpCallback callback)
    {
        [UMPConsentForm presentPrivacyOptionsFormFromViewController:UnityGetGLViewController()
                                                   completionHandler:^(NSError* error) {
                                                       dispatch_async(dispatch_get_main_queue(), ^{
                                                           callback(requestId, error == nil,
                                                                    error ? [error.localizedDescription UTF8String] : NULL);
                                                       });
                                                   }];
    }
#else
    int FDI_Ump_ConsentStatus(void) { return 0; }
    bool FDI_Ump_CanRequestAds(void) { return true; }
    bool FDI_Ump_IsPrivacyOptionsRequired(void) { return false; }

    void FDI_Ump_RequestUpdate(int requestId, int debugGeography, const char* testDeviceIdsCsv,
                               bool tagForUnderAgeOfConsent, FDI_UmpCallback callback)
    {
        callback(requestId, false, "UMP SDK not linked");
    }

    void FDI_Ump_LoadAndShowIfRequired(int requestId, FDI_UmpCallback callback)
    {
        callback(requestId, false, "UMP SDK not linked");
    }

    void FDI_Ump_ShowPrivacyOptions(int requestId, FDI_UmpCallback callback)
    {
        callback(requestId, false, "UMP SDK not linked");
    }
#endif
}
```

ObjC 선택자 이름을 UMP iOS 3.x 헤더(`UMPConsentInformation.h`, `UMPConsentForm.h`)와 대조한다: `requestConsentInfoUpdateWithParameters:completionHandler:`, `loadAndPresentIfRequiredFromViewController:completionHandler:`, `presentPrivacyOptionsFormFromViewController:completionHandler:`, `privacyOptionsRequirementStatus`, `UMPPrivacyOptionsRequirementStatusRequired`.

- [ ] **Step 2: 플러그인 임포트 설정**

`refresh_unity` 후 UnityMCP `execute_code`:

```csharp
const string path = "Assets/FoundationDI/Runtime/Services/ConsentService/Ump/Plugins/iOS/FDI_Ump.mm";
var importer = (UnityEditor.PluginImporter)UnityEditor.AssetImporter.GetAtPath(path);
importer.SetCompatibleWithAnyPlatform(false);
importer.SetCompatibleWithEditor(false);
importer.SetCompatibleWithPlatform(UnityEditor.BuildTarget.iOS, true);
importer.SaveAndReimport();
return "ok";
```

`.meta`의 `iOS: enabled: 1`, 나머지 0, `defineConstraints`가 비어 있는지 확인한다(`FDI_Att.mm.meta`와 달리 심볼 게이트가 없다 — 스텁이 있어 언제 들어가도 링크된다).

- [ ] **Step 3: C# 브리지**

`IosUmpConsent.cs`:

```csharp
#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    internal sealed class IosUmpConsent : IUmpConsent
    {
        // Inactive 구간을 지난 직후의 장면 전환에 폼이 버려지지 않도록 잠깐 기다린다(ATT와 같은 이유).
        private const float SettleSeconds = 0.5f;

        private delegate void UmpCallback(int requestId, bool success, string message);

        [DllImport("__Internal")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool FDI_Ump_IsAvailable();
        [DllImport("__Internal")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool FDI_Ump_IsApplicationActive();
        [DllImport("__Internal")] private static extern int FDI_Ump_GdprApplies();
        [DllImport("__Internal")] private static extern string FDI_Ump_PurposeConsents();
        [DllImport("__Internal")] private static extern int FDI_Ump_ConsentStatus();
        [DllImport("__Internal")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool FDI_Ump_CanRequestAds();
        [DllImport("__Internal")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool FDI_Ump_IsPrivacyOptionsRequired();
        [DllImport("__Internal")] private static extern void FDI_Ump_RequestUpdate(int requestId, int debugGeography, string testDeviceIdsCsv, [MarshalAs(UnmanagedType.I1)] bool tagForUnderAgeOfConsent, UmpCallback callback);
        [DllImport("__Internal")] private static extern void FDI_Ump_LoadAndShowIfRequired(int requestId, UmpCallback callback);
        [DllImport("__Internal")] private static extern void FDI_Ump_ShowPrivacyOptions(int requestId, UmpCallback callback);

        // 네이티브 콜백은 정적 함수만 받을 수 있다(IL2CPP). 요청 ID로 완료 소스를 찾는다.
        private static readonly Dictionary<int, Action<bool, string>> Pending = new();
        private static int _nextRequestId;

        private readonly IMainThreadDispatcher _dispatcher;

        public IosUmpConsent(IMainThreadDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public static bool IsAvailable => FDI_Ump_IsAvailable();

        public bool IsSupported => true;
        public UmpConsentStatus Status => UmpStatusMapper.FromIos(FDI_Ump_ConsentStatus());
        public bool CanRequestAds => FDI_Ump_CanRequestAds();
        public bool IsPrivacyOptionsRequired => FDI_Ump_IsPrivacyOptionsRequired();

        public TcfSignals ReadTcf()
        {
            var gdpr = FDI_Ump_GdprApplies();
            bool? gdprApplies = gdpr < 0 ? (bool?)null : gdpr == 1;

            return new TcfSignals(gdprApplies, FDI_Ump_PurposeConsents());
        }

        public Awaitable<bool> UpdateAsync(UmpRequestOptions options)
        {
            var ids = options.TestDeviceHashedIds == null ? string.Empty : string.Join(",", options.TestDeviceHashedIds);

            return Call(id => FDI_Ump_RequestUpdate(id, UmpStatusMapper.ToIosGeography(options.DebugGeography), ids,
                                                    options.TagForUnderAgeOfConsent, OnNativeCallback));
        }

        public Awaitable<bool> LoadAndShowIfRequiredAsync() =>
            WhenActive(() => Call(id => FDI_Ump_LoadAndShowIfRequired(id, OnNativeCallback)));

        public Awaitable<bool> ShowPrivacyOptionsAsync() =>
            WhenActive(() => Call(id => FDI_Ump_ShowPrivacyOptions(id, OnNativeCallback)));

        private Awaitable<bool> Call(Action<int> invoke)
        {
            var source = new AwaitableCompletionSource<bool>();
            var requestId = ++_nextRequestId;

            Pending[requestId] = (success, message) =>
            {
                if (!success) Debug.LogWarning($"[ConsentService] UMP iOS 호출 실패: {message}");
                source.TrySetResult(success);
            };

            invoke(requestId);
            return source.Awaitable;
        }

        // 앱이 Active가 되고 안정화될 때까지 기다린 뒤 실행한다.
        private Awaitable<bool> WhenActive(Func<Awaitable<bool>> run)
        {
            var source = new AwaitableCompletionSource<bool>();

            async void Forward()
            {
                source.TrySetResult(await run());
            }

            void Check()
            {
                if (!FDI_Ump_IsApplicationActive())
                {
                    _dispatcher.NextFrames(1, Check);
                    return;
                }

                _dispatcher.Delay(SettleSeconds, () =>
                {
                    if (!FDI_Ump_IsApplicationActive())
                    {
                        _dispatcher.NextFrames(1, Check);
                        return;
                    }

                    Forward();
                });
            }

            Check();
            return source.Awaitable;
        }

        [MonoPInvokeCallback(typeof(UmpCallback))]
        private static void OnNativeCallback(int requestId, bool success, string message)
        {
            // 네이티브가 dispatch_get_main_queue로 넘기므로 Unity 메인 스레드다.
            if (!Pending.TryGetValue(requestId, out var complete)) return;

            Pending.Remove(requestId);
            complete(success, message);
        }
    }
}
#endif
```

- [ ] **Step 4: 팩토리**

`UmpConsentFactory.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    internal static class UmpConsentFactory
    {
        // 실기가 아니거나 네이티브 SDK가 빠졌으면 UnsupportedUmpConsent로 떨어진다 — 에디터에서도
        // 광고·분석 흐름이 막히지 않게 한다.
        public static IUmpConsent Create(IMainThreadDispatcher dispatcher)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return (IUmpConsent)AndroidUmpConsent.TryCreate(dispatcher) ?? new UnsupportedUmpConsent();
#elif UNITY_IOS && !UNITY_EDITOR
            if (IosUmpConsent.IsAvailable) return new IosUmpConsent(dispatcher);

            Debug.LogWarning("[ConsentService] GoogleUserMessagingPlatform 포드가 링크되지 않았다. UMP 없이 진행한다. " +
                             "Use UMP를 켰다면 pod install이 됐는지 확인한다.");
            return new UnsupportedUmpConsent();
#else
            return new UnsupportedUmpConsent();
#endif
        }
    }
}
```

`ConsentServiceRegistration.cs`의 `ump` 줄을 교체한다.

```csharp
                var ump = settings.UseUmp ? UmpConsentFactory.Create(dispatcher) : new UnsupportedUmpConsent();
```

- [ ] **Step 5: iOS·Android 플레이어 컴파일 확인**

Task 15 Step 4의 `execute_code`를 `BuildTarget.iOS` / `BuildTargetGroup.iOS`로 한 번 더 실행한다. iOS 빌드 지원 모듈이 설치돼 있지 않으면 "iOS 컴파일 미확인"을 커밋 메시지에 적는다.

Run: 전체 EditMode 테스트 PASS.

- [ ] **Step 6: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ConsentService
git commit -m "[BEHAVIORAL] UMP iOS 네이티브 브리지와 플랫폼 팩토리를 추가한다"
```

---

### Task 17: UMP 의존성 XML 동기화 (에디터)

**Files:**
- Create: `Editor/ConsentService/UmpDependenciesFile.cs`
- Create: `Editor/ConsentService/UmpDependenciesSync.cs`
- Create: `Editor/ConsentService/ConsentServiceSettingsEditor.cs`
- Create: `Editor/ConsentService/ConsentServiceSettingsLocator.cs`
- Test: `Tests/Editor/ConsentService/UmpDependenciesFileTest.cs`

**Interfaces:**
- Produces: `internal static class UmpDependenciesFile { const string AssetPath; const string AndroidPackage; const string IosPod; const string IosPodVersion; static string BuildXml(); static bool ShouldExist(IEnumerable<bool> useUmpFlags); static bool Sync(bool shouldExist, string projectRoot); }`, `internal static class ConsentServiceSettingsLocator { static List<ConsentServiceSettings> FindAll(); }`

- [ ] **Step 1: 최신 UMP 버전 확인**

```bash
curl -s https://dl.google.com/android/maven2/com/google/android/ump/user-messaging-platform/maven-metadata.xml | grep -o "<release>[^<]*"
curl -s https://cdn.cocoapods.org/all_pods_versions_d_2_e.txt | grep "^GoogleUserMessagingPlatform/" | tr '/' '\n' | tail -3
```

`maven-metadata.xml`이 없으면 `https://dl.google.com/android/maven2/com/google/android/ump/group-index.xml`에서 `user-messaging-platform` 버전 목록을 본다. 아래 상수의 `4.0.0`/`3.1`을 확인한 최신으로 바꾼다(iOS는 `~> 메이저.마이너`).

- [ ] **Step 2: 실패하는 테스트**

```csharp
using System.IO;
using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;

public class UmpDependenciesFileTest
{
    private string _root;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "FdiUmpDeps_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public void XML은_UMP_의존성만_선언한다()
    {
        var xml = UmpDependenciesFile.BuildXml();

        StringAssert.Contains($"<androidPackage spec=\"{UmpDependenciesFile.AndroidPackage}\"", xml);
        StringAssert.Contains($"<iosPod name=\"{UmpDependenciesFile.IosPod}\" version=\"{UmpDependenciesFile.IosPodVersion}\"", xml);

        // 광고 SDK를 끌고 오면 LevelPlay AdMob 어댑터(Next-Gen)와 충돌한다.
        StringAssert.DoesNotContain("play-services-ads", xml);
        StringAssert.DoesNotContain("Google-Mobile-Ads-SDK", xml);
    }

    [Test]
    public void 하나라도_UMP를_켜면_파일이_있어야_한다()
    {
        Assert.IsTrue(UmpDependenciesFile.ShouldExist(new[] { false, true }));
        Assert.IsFalse(UmpDependenciesFile.ShouldExist(new[] { false, false }));
        Assert.IsFalse(UmpDependenciesFile.ShouldExist(new bool[0]));
    }

    [Test]
    public void 켜면_파일을_만들고_끄면_지운다()
    {
        var path = Path.Combine(_root, UmpDependenciesFile.AssetPath);

        Assert.IsTrue(UmpDependenciesFile.Sync(true, _root));
        Assert.IsTrue(File.Exists(path));

        Assert.IsFalse(UmpDependenciesFile.Sync(true, _root), "내용이 같으면 다시 쓰지 않는다(재임포트 루프 방지)");

        Assert.IsTrue(UmpDependenciesFile.Sync(false, _root));
        Assert.IsFalse(File.Exists(path));
    }
}
```

- [ ] **Step 3: 실패 확인 → 구현**

`UmpDependenciesFile.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DarkNaku.FoundationDI.Editor
{
    // EDM4U가 읽는 UMP 의존성 선언 파일.
    //
    // 패키지 안에 고정으로 두지 않는다. EDM4U는 패키지 폴더도 스캔하므로, 그러면 UMP를 쓰지 않는
    // 프로젝트에도 네이티브 의존성이 강제로 들어간다. 설정의 Use UMP를 켠 프로젝트에만 Assets 아래에 만든다.
    internal static class UmpDependenciesFile
    {
        internal const string AssetPath = "Assets/FoundationDI.Data/Editor/FoundationDIUmpDependencies.xml";
        internal const string AndroidPackage = "com.google.android.ump:user-messaging-platform:4.0.0";
        internal const string IosPod = "GoogleUserMessagingPlatform";
        internal const string IosPodVersion = "~> 3.1";

        internal static string BuildXml()
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                   "<!-- FoundationDI ConsentService가 생성한다. ConsentServiceSettings의 Use UMP로 켜고 끈다. 손으로 고치지 않는다. -->\n" +
                   "<dependencies>\n" +
                   "  <androidPackages>\n" +
                   $"    <androidPackage spec=\"{AndroidPackage}\">\n" +
                   "      <repositories>\n" +
                   "        <repository>https://maven.google.com/</repository>\n" +
                   "      </repositories>\n" +
                   "    </androidPackage>\n" +
                   "  </androidPackages>\n" +
                   "  <iosPods>\n" +
                   $"    <iosPod name=\"{IosPod}\" version=\"{IosPodVersion}\" addToAllTargets=\"false\" />\n" +
                   "  </iosPods>\n" +
                   "</dependencies>\n";
        }

        internal static bool ShouldExist(IEnumerable<bool> useUmpFlags) => useUmpFlags.Any(flag => flag);

        // 값이 실제로 바뀔 때만 쓴다. 매번 쓰면 임포트 → 동기화 → 임포트 루프가 생긴다.
        internal static bool Sync(bool shouldExist, string projectRoot)
        {
            var path = Path.Combine(projectRoot, AssetPath);

            if (!shouldExist)
            {
                if (!File.Exists(path)) return false;

                File.Delete(path);
                if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
                return true;
            }

            var xml = BuildXml();

            if (File.Exists(path) && File.ReadAllText(path) == xml) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, xml);
            return true;
        }
    }
}
```

`ConsentServiceSettingsLocator.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;

namespace DarkNaku.FoundationDI.Editor
{
    internal static class ConsentServiceSettingsLocator
    {
        internal static List<ConsentServiceSettings> FindAll()
        {
            var result = new List<ConsentServiceSettings>();

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(ConsentServiceSettings)))
            {
                var settings = AssetDatabase.LoadAssetAtPath<ConsentServiceSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (settings != null) result.Add(settings);
            }

            return result;
        }
    }
}
```

`UmpDependenciesSync.cs`:

```csharp
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    // 설정의 Use UMP와 의존성 XML을 맞춘다. 인스펙터 토글, 메뉴, 빌드 직전 세 곳에서 부른다.
    internal sealed class UmpDependenciesSync : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Run();

        [MenuItem("Tools/FoundationDI/Consent/Sync UMP Dependencies")]
        internal static void Run()
        {
            var useUmp = ConsentServiceSettingsLocator.FindAll().Select(s => s.UseUmp);
            var root = Directory.GetParent(Application.dataPath).FullName;

            if (!UmpDependenciesFile.Sync(UmpDependenciesFile.ShouldExist(useUmp), root)) return;

            AssetDatabase.Refresh();
            Debug.Log("[ConsentService] UMP 의존성 XML을 갱신했다. EDM4U의 Android/iOS Resolver가 다음 해석에서 반영한다.");
        }

        // EDM4U(External Dependency Manager)가 없으면 XML을 만들어도 아무도 읽지 않는다.
        internal static bool IsEdm4uInstalled() =>
            System.AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name.StartsWith("Google.VersionHandler"));
    }
}
```

`ConsentServiceSettingsEditor.cs`:

```csharp
using UnityEditor;

namespace DarkNaku.FoundationDI.Editor
{
    [CustomEditor(typeof(ConsentServiceSettings))]
    internal sealed class ConsentServiceSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            var changed = EditorGUI.EndChangeCheck();

            var settings = (ConsentServiceSettings)target;

            if (settings.UseUmp && !UmpDependenciesSync.IsEdm4uInstalled())
            {
                EditorGUILayout.HelpBox("External Dependency Manager(EDM4U)가 없다. UMP 네이티브 의존성이 빌드에 들어가지 " +
                                        "않는다. LevelPlay를 설치했다면 함께 들어 있다.", MessageType.Warning);
            }

            if (settings.UseUmp && string.IsNullOrEmpty(settings.AdMobAppIdAndroid) && string.IsNullOrEmpty(settings.AdMobAppIdIos))
            {
                EditorGUILayout.HelpBox("AdMob 앱 ID가 비어 있다. LevelPlay AdMob 어댑터가 넣지 않았다면 빌드가 멈춘다.",
                                        MessageType.Info);
            }

            if (settings.PromptPrefab == null)
            {
                EditorGUILayout.HelpBox("Prompt Prefab이 비어 있다. 약관 창을 띄울 수 없어 약관 수락이 되지 않는다.",
                                        MessageType.Error);
            }

            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(settings);
                UmpDependenciesSync.Run();
            }
        }
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `UmpDependenciesFileTest` → 3개 PASS. 전체 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Editor/ConsentService Assets/FoundationDI/Tests/Editor/ConsentService Assets/FoundationDI/Tests/Editor/ConsentService.meta
git commit -m "[BEHAVIORAL] Use UMP 설정에 맞춰 UMP 의존성 XML을 생성·삭제한다"
```

---

### Task 18: 빌드 후처리 (AdMob 앱 ID, Firebase 수집 기본값)

**Files:**
- Create: `Editor/ConsentService/UmpBuildRules.cs`
- Create: `Editor/ConsentService/AndroidManifestEditor.cs`
- Create: `Editor/ConsentService/UmpPostProcess.cs`
- Test: `Tests/Editor/ConsentService/UmpBuildRulesTest.cs`, `Tests/Editor/ConsentService/AndroidManifestEditorTest.cs`

**Interfaces:**
- Produces: `internal enum AppIdAction { Skip, Inject, Fail }`, `internal static class UmpBuildRules { AppIdAction DecideAppId(bool useUmp, string configuredId, bool alreadyPresent); bool ShouldDisableFirebaseCollection(bool waitForConsent, bool firebaseActive, bool alreadyPresent); }`, `internal static class AndroidManifestEditor { bool HasMetaData(string xml, string name); string AddMetaData(string xml, string name, string value); }`

- [ ] **Step 1: 실패하는 테스트**

`UmpBuildRulesTest.cs`:

```csharp
using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;

public class UmpBuildRulesTest
{
    [Test]
    public void UMP를_끄면_앱_ID를_건드리지_않는다()
    {
        Assert.AreEqual(AppIdAction.Skip, UmpBuildRules.DecideAppId(false, "", false));
    }

    [Test]
    public void 이미_있으면_덮지_않는다()
    {
        // LevelPlay AdMob 어댑터가 넣은 값이다. 덮으면 매니페스트 병합 충돌이 나거나 다른 ID로 바뀐다.
        Assert.AreEqual(AppIdAction.Skip, UmpBuildRules.DecideAppId(true, "ca-app-pub-1~2", true));
    }

    [Test]
    public void 없고_설정에_있으면_넣는다()
    {
        Assert.AreEqual(AppIdAction.Inject, UmpBuildRules.DecideAppId(true, "ca-app-pub-1~2", false));
    }

    [Test]
    public void 없고_설정에도_없으면_빌드를_멈춘다()
    {
        // UMP는 앱 ID 없이 실행 시점에 크래시한다. 빌드에서 잡는 편이 낫다.
        Assert.AreEqual(AppIdAction.Fail, UmpBuildRules.DecideAppId(true, "", false));
    }

    [Test]
    public void Firebase_수집_기본값은_동의를_기다리고_Firebase가_있고_아직_없을_때만_넣는다()
    {
        Assert.IsTrue(UmpBuildRules.ShouldDisableFirebaseCollection(true, true, false));
        Assert.IsFalse(UmpBuildRules.ShouldDisableFirebaseCollection(false, true, false));
        Assert.IsFalse(UmpBuildRules.ShouldDisableFirebaseCollection(true, false, false));
        Assert.IsFalse(UmpBuildRules.ShouldDisableFirebaseCollection(true, true, true));
    }
}
```

`AndroidManifestEditorTest.cs`:

```csharp
using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;

public class AndroidManifestEditorTest
{
    private const string Manifest =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"com.unity3d.player\">\n" +
        "  <application>\n" +
        "    <activity android:name=\"com.unity3d.player.UnityPlayerActivity\" />\n" +
        "  </application>\n" +
        "</manifest>\n";

    [Test]
    public void meta_data를_application_안에_넣는다()
    {
        var result = AndroidManifestEditor.AddMetaData(Manifest, "com.google.android.gms.ads.APPLICATION_ID", "ca-app-pub-1~2");

        Assert.IsTrue(AndroidManifestEditor.HasMetaData(result, "com.google.android.gms.ads.APPLICATION_ID"));
        StringAssert.Contains("android:value=\"ca-app-pub-1~2\"", result);
    }

    [Test]
    public void 없으면_없다고_답한다()
    {
        Assert.IsFalse(AndroidManifestEditor.HasMetaData(Manifest, "com.google.android.gms.ads.APPLICATION_ID"));
    }
}
```

- [ ] **Step 2: 실패 확인 → 구현**

`UmpBuildRules.cs`:

```csharp
namespace DarkNaku.FoundationDI.Editor
{
    internal enum AppIdAction
    {
        Skip,
        Inject,
        Fail,
    }

    // 빌드 후처리의 판정만 모은 순수 함수. 파일 I/O와 분리해 EditMode에서 검증한다
    // (IosExportCompliance.ShouldDeclare와 같은 방식).
    internal static class UmpBuildRules
    {
        internal const string AndroidAppIdKey = "com.google.android.gms.ads.APPLICATION_ID";
        internal const string IosAppIdKey = "GADApplicationIdentifier";
        internal const string AndroidFirebaseCollectionKey = "firebase_analytics_collection_enabled";
        internal const string IosFirebaseCollectionKey = "FIREBASE_ANALYTICS_COLLECTION_ENABLED";

        internal static AppIdAction DecideAppId(bool useUmp, string configuredId, bool alreadyPresent)
        {
            if (!useUmp || alreadyPresent) return AppIdAction.Skip;

            return string.IsNullOrEmpty(configuredId) ? AppIdAction.Fail : AppIdAction.Inject;
        }

        // Basic 모드: 동의 전에는 Firebase 자동 이벤트(first_open 등)도 나가면 안 된다.
        // AnalyticsService가 동의 후 SetCollectionEnabled(true)로 켠다.
        internal static bool ShouldDisableFirebaseCollection(bool waitForConsent, bool firebaseActive, bool alreadyPresent)
        {
            return waitForConsent && firebaseActive && !alreadyPresent;
        }
    }
}
```

`AndroidManifestEditor.cs`:

```csharp
using System.Xml;

namespace DarkNaku.FoundationDI.Editor
{
    internal static class AndroidManifestEditor
    {
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";

        internal static bool HasMetaData(string xml, string name)
        {
            var doc = Load(xml);
            return Find(doc, name) != null;
        }

        internal static string AddMetaData(string xml, string name, string value)
        {
            var doc = Load(xml);

            if (Find(doc, name) != null) return xml;

            var application = doc.SelectSingleNode("/manifest/application") as XmlElement;

            if (application == null)
            {
                application = doc.CreateElement("application");
                doc.DocumentElement.AppendChild(application);
            }

            var meta = doc.CreateElement("meta-data");
            meta.SetAttribute("name", AndroidNs, name);
            meta.SetAttribute("value", AndroidNs, value);
            application.AppendChild(meta);

            using var writer = new System.IO.StringWriter();
            doc.Save(writer);
            return writer.ToString();
        }

        private static XmlDocument Load(string xml)
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            return doc;
        }

        private static XmlNode Find(XmlDocument doc, string name)
        {
            foreach (XmlElement meta in doc.GetElementsByTagName("meta-data"))
            {
                if (meta.GetAttribute("name", AndroidNs) == name) return meta;
            }

            return null;
        }
    }
}
```

`doc.Save(StringWriter)`는 인코딩 선언을 `utf-16`으로 쓴다. 파일에 쓸 때는 `UmpPostProcess`가 `XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }`로 다시 저장한다(아래).

Run: `UmpBuildRulesTest`, `AndroidManifestEditorTest` → PASS.

- [ ] **Step 3: 후처리 연결**

`UmpPostProcess.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
#endif

namespace DarkNaku.FoundationDI.Editor
{
    // UMP가 요구하는 AdMob 앱 ID와 Basic 모드의 Firebase 수집 기본값을 넣는다.
    // 이미 있는 키는 덮지 않는다(LevelPlay AdMob 어댑터·Firebase 설정이 넣었을 수 있다).
    internal sealed class UmpPostProcess : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1002;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            var settings = ConsentServiceSettingsLocator.FindAll().FirstOrDefault();
            var projectRoot = Directory.GetParent(unityLibraryPath).FullName;
            var manifests = Directory.GetFiles(projectRoot, "AndroidManifest.xml", SearchOption.AllDirectories);
            var target = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");

            if (!File.Exists(target)) return;

            var xml = File.ReadAllText(target);
            var dirty = false;

            bool AnyHas(string key) => manifests.Any(m => AndroidManifestEditor.HasMetaData(File.ReadAllText(m), key));

            if (settings != null)
            {
                switch (UmpBuildRules.DecideAppId(settings.UseUmp, settings.AdMobAppIdAndroid,
                                                  AnyHas(UmpBuildRules.AndroidAppIdKey)))
                {
                    case AppIdAction.Inject:
                        xml = AndroidManifestEditor.AddMetaData(xml, UmpBuildRules.AndroidAppIdKey, settings.AdMobAppIdAndroid);
                        dirty = true;
                        break;

                    case AppIdAction.Fail:
                        throw new BuildFailedException("[ConsentService] Use UMP가 켜져 있지만 AdMob 앱 ID(Android)가 없다. " +
                                                       "ConsentServiceSettings에 넣거나 LevelPlay AdMob 어댑터 설정에 넣는다.");
                }
            }

            if (UmpBuildRules.ShouldDisableFirebaseCollection(AnyAnalyticsWaitsForConsent(), IsFirebaseActive(),
                                                              AnyHas(UmpBuildRules.AndroidFirebaseCollectionKey)))
            {
                xml = AndroidManifestEditor.AddMetaData(xml, UmpBuildRules.AndroidFirebaseCollectionKey, "false");
                dirty = true;
            }

            if (dirty) WriteUtf8(target, xml);
        }

#if UNITY_IOS
        // FDI_HapticiOSPostProcess(999) → AttIosPostProcess(1000) → IosExportCompliance(1001) 다음.
        [PostProcessBuild(1002)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;

            var plistPath = Path.Combine(path, "Info.plist");
            if (!File.Exists(plistPath)) return;

            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            var settings = ConsentServiceSettingsLocator.FindAll().FirstOrDefault();
            var dirty = false;

            if (settings != null)
            {
                switch (UmpBuildRules.DecideAppId(settings.UseUmp, settings.AdMobAppIdIos,
                                                  plist.root.values.ContainsKey(UmpBuildRules.IosAppIdKey)))
                {
                    case AppIdAction.Inject:
                        plist.root.SetString(UmpBuildRules.IosAppIdKey, settings.AdMobAppIdIos);
                        dirty = true;
                        break;

                    case AppIdAction.Fail:
                        throw new BuildFailedException("[ConsentService] Use UMP가 켜져 있지만 AdMob 앱 ID(iOS)가 없다. " +
                                                       "ConsentServiceSettings에 넣거나 LevelPlay AdMob 어댑터 설정에 넣는다.");
                }
            }

            if (UmpBuildRules.ShouldDisableFirebaseCollection(AnyAnalyticsWaitsForConsent(), IsFirebaseActive(),
                                                              plist.root.values.ContainsKey(UmpBuildRules.IosFirebaseCollectionKey)))
            {
                plist.root.SetBoolean(UmpBuildRules.IosFirebaseCollectionKey, false);
                dirty = true;
            }

            if (dirty) plist.WriteToFile(plistPath);
        }
#endif

        private static bool AnyAnalyticsWaitsForConsent()
        {
            return AssetDatabase.FindAssets("t:" + nameof(AnalyticsServiceSettings))
                                .Select(g => AssetDatabase.LoadAssetAtPath<AnalyticsServiceSettings>(AssetDatabase.GUIDToAssetPath(g)))
                                .Any(s => s != null && s.WaitForConsent);
        }

        private static bool IsFirebaseActive()
        {
#if FOUNDATIONDI_FIREBASE
            return true;
#else
            return false;
#endif
        }

        private static void WriteUtf8(string path, string xml)
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);

            var settings = new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };

            using var writer = System.Xml.XmlWriter.Create(path, settings);
            doc.Save(writer);
        }
    }
}
```

`IPostGenerateGradleAndroidProject`(`UnityEditor.Android`)는 Android Build Support 모듈이 설치된 에디터에서만 존재한다. 호스트에는 설치돼 있다. 작성 전에 `grep -rn "IPostGenerateGradleAndroidProject" Assets/FoundationDI/Editor`로 기존 Android 후처리가 있는지 보고, 있으면 그 파일의 가드 방식을 그대로 따른다.

- [ ] **Step 4: 통과 확인**

Run: 전체 EditMode PASS, `read_console` 에러 0.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Editor/ConsentService Assets/FoundationDI/Tests/Editor/ConsentService
git commit -m "[BEHAVIORAL] UMP용 AdMob 앱 ID와 Firebase 수집 기본값을 빌드 후처리로 넣는다"
```

---

### Task 19: 문서·버전·호스트 배선

**Files:**
- Create: `Runtime/Services/ConsentService/README.md`
- Modify: `Runtime/Services/AdService/README.md` (ATT 6장 → ConsentService로 이동 안내, `IAdConsent` 언급 제거)
- Modify: `Runtime/Services/AnalyticsService/README.md` ("동의 판단은 범위 밖" → Wait For Consent·게이트 표)
- Modify: `CLAUDE.md` (핵심 서비스에 ConsentService 항목, AdService ATT 문단 정리, iOS 빌드 후처리 목록에 1002)
- Modify: `Assets/FoundationDI/package.json` (`"version": "0.12.0"`)
- Modify: `Assets/Scripts/Installers/RootInstaller.cs` (호스트: `RegisterConsentService` 호출 + 설정 필드)
- Modify: `plan.md` (완료 체크)

- [ ] **Step 1: ConsentService README 작성**

다음 장으로 쓴다. 각 장은 스펙의 해당 절을 사용자 관점으로 옮긴다.

1. 개요 — 약관 → UMP → ATT, 보이는 창 표(스펙 §3)
2. 설치 — `ConsentServiceSettings` 만들기, `RegisterConsentService`, EDM4U, Use UMP 토글이 XML을 만든다는 것
3. **AdMob 콘솔 설정** — 개인 정보 보호 및 메시지에서 GDPR 메시지 생성·게시, 미국 주 규정 메시지(선택), consent mode 토글 2개(광고·분석) 켜기, Additional Consent에 LevelPlay 미디에이션 네트워크 추가, IDFA 설명 메시지(선택), 앱 ID 복사
4. 게임 코드 — 기존 유저 `AcceptPolicy`, 현지화 `SetPolicyContent`, 설정 화면 버튼(`IsPrivacyOptionsRequired`/`ShowPrivacyOptionsAsync`), ATT를 직접 묻기
5. 커스텀 UI — 스킨 교체 / `IPolicyAgreementPrompt` 구현 예제(UINavigator 팝업, 수명 주의)
6. 광고·분석 연동 — AdService 게이트(`PolicyAccepted && CanRequestAds`), 분석 게이트 표(스펙 §7.2), Adjust `Delay First Session` 권장
7. 실패 처리 — 게임을 막지 않는 규칙, 타임아웃, 오프라인 첫 실행
8. 빌드 — 후처리(앱 ID, Firebase 기본값), GMA Unity 플러그인을 설치하지 말 것(충돌 표, 스펙 §1.2)
9. 실기 체크리스트 — 스펙 §11.2 그대로
10. 0.11 → 0.12 마이그레이션 — 제거된 API 표와 대체: `IAdService.TrackingAuthorization` → `IConsentService.TrackingAuthorization`, `AdServiceSettings.Request Tracking Authorization` → `ConsentServiceSettings.Request Tracking Authorization`, `IAdService.Consent` → `IConsentService`, 등록 순서

- [ ] **Step 2: 다른 문서와 버전 갱신**

`package.json`의 `"version"`을 `"0.12.0"`으로 바꾼다. CLAUDE.md의 AdService 항목에서 "iOS ATT 동의는 `InitializeAsync`가 provider 초기화 앞에서 한 번 요청한다" 문단을 ConsentService 항목으로 옮기고 "AdService는 `IConsentService.RequestAsync`를 await한 뒤 `PolicyAccepted && CanRequestAds`일 때만 provider를 초기화한다"로 바꾼다. AnalyticsService 항목의 "동의 판단은 범위 밖" 문장을 "`Wait For Consent`(기본 켜짐)면 ConsentService 판정으로 수집을 게이트한다(GDPR 지역 거부 → 수집 끔·버퍼 폐기, 판정 불가 → 버퍼 유지)"로 바꾼다. iOS 빌드 후처리 목록에 `UmpPostProcess`(1002)를 추가한다.

- [ ] **Step 3: 호스트 배선**

`Assets/Scripts/Installers/RootInstaller.cs`를 읽고, 다른 설정 필드 옆에 `[SerializeField] private ConsentServiceSettings _consentServiceSettings;`를 추가하고 `InstallBindings`의 AdService·AnalyticsService 등록 앞에 `builder.RegisterConsentService(_consentServiceSettings);`를 넣는다. `Assets/FoundationDI.Data/`에 `ConsentServiceSettings.asset`을 만들고(UnityMCP `manage_scriptable_object` 또는 메뉴) `RootScope.prefab`의 `RootInstaller`에 연결한다. `Use UMP`는 호스트에서 끈 채로 둔다(호스트에 EDM4U가 없다).

- [ ] **Step 4: 플레이 모드 스모크**

UnityMCP로 호스트의 시작 씬을 플레이한다. 약관 창이 뜨고 동의 버튼을 누르면 사라지는지, `read_console`에 `[AdService]`/`[AnalyticsService]` 초기화 로그가 그 뒤에 찍히는지 확인한다. 확인한 뒤 PlayerPrefs의 `FoundationDI.Consent.PolicyVersion`을 지운다.

- [ ] **Step 5: 전체 테스트 후 커밋**

Run: 전체 PASS.

```bash
git add Assets/FoundationDI Assets/Scripts Assets/FoundationDI.Data Assets/Prefabs CLAUDE.md plan.md
git commit -m "[STRUCTURAL] ConsentService 문서를 쓰고 버전을 0.12.0으로 올린다"
```

---

## 실기 확인 (구현 후, MahjongTile)

이 계획의 코드로는 EditMode에서 검증할 수 없는 항목이다. 스펙 §11.2 체크리스트를 MahjongTile Android·iOS 실기 빌드로 확인한다. 특히:
- `Use UMP` + LevelPlay AdMob 어댑터 조합에서 Gradle·CocoaPods 해석 성공
- R8 난독화 빌드에서 `AndroidUmpConsent` JNI 호출 정상(실패하면 `-keep class com.google.android.ump.** { *; }`를 `proguard-user.txt`에 넣는 단계를 Task 17 도구에 추가)
- 호스트에서 컴파일되지 않은 LevelPlay·AppLovin·Adjust 어댑터 수정분 컴파일
