# ConsentService

약관·개인정보처리방침 수락 → Google UMP(GDPR 동의) → iOS ATT를 한 흐름으로 묻는 앱 수명 서비스입니다.
AdService와 AnalyticsService가 초기화 앞에서 이 결과를 기다리므로, 게임 코드는 보통 아무것도 하지 않아도 됩니다.

| 순서 | 창 | 그리는 쪽 | 바꾸는 방법 |
|---|---|---|---|
| ① | 약관·개인정보처리방침 수락 창 | 이 패키지 | 프리팹 교체 / `IPolicyAgreementPrompt` 구현 (5장) |
| ② | GDPR 데이터 처리 동의 폼 | Google UMP | AdMob 콘솔 "개인 정보 보호 및 메시지" (3장) |
| ③ | (선택) IDFA 설명 메시지 | Google UMP | AdMob 콘솔 |
| ④ | ATT 시스템 팝업 | iOS | `NSUserTrackingUsageDescription` 문구 (7.2) |

- **약관은 수락해야 게임이 진행됩니다.** 거절 버튼이 없습니다.
- **UMP 동의와 ATT는 거부해도 게임이 진행됩니다.** 거부의 효과는 비개인화·제한 광고와 분석 수집 중단까지입니다.
  GDPR은 서비스 이용을 데이터 처리 동의의 조건으로 거는 것을 유효한 동의로 보지 않고, Apple은 추적 허용을
  기능의 조건으로 걸거나 허용에 보상을 주는 것을 금지합니다.

---

## 1. 왜 UMP를 직접 부르는가

Google은 EEA·영국(2024-01-16)과 스위스(2024-07-31)에서 AdMob 계열 광고에 **Google 인증 TCF v2.2 CMP**를
요구합니다. 없으면 제한 광고만 나갑니다. LevelPlay 미디에이션에도 Google 수요가 섞이므로 목적별 동의는
인증 CMP인 UMP가 받습니다. LevelPlay(SDK 7.7.0+)와 미디에이션 네트워크는 UMP가 쓴 TCF·Additional Consent
문자열을 **스스로 읽습니다** — 이 패키지가 네트워크마다 번역하지 않습니다.

UMP의 공식 Unity 바인딩은 Google Mobile Ads(GMA) Unity 플러그인 안에만 있습니다. **GMA 플러그인을 설치하지
마세요.** 광고 SDK를 함께 끌고 와 LevelPlay AdMob 어댑터와 충돌합니다(2026-10 기준).

| | LevelPlay AdMob 어댑터 5.31.0.0 | GMA Unity 플러그인 v11.5.0 |
|---|---|---|
| Android | Next-Gen `ads-mobile-sdk:1.5.0` | 기존 `play-services-ads:25.4.0` → 중복 클래스 |
| iOS | `Google-Mobile-Ads-SDK = 13.11.0` (정확히 고정) | `~> 13.9` → 버전이 어긋나면 CocoaPods 해석 실패 |

이 패키지는 UMP 네이티브 SDK(`com.google.android.ump:user-messaging-platform`, `GoogleUserMessagingPlatform`
포드)만 얇게 부릅니다. 광고 SDK와 별개 artifact라 어댑터 버전과 상관없이 충돌하지 않습니다.

## 2. 설치

1. `Create > FoundationDI > Consent Service Settings`로 설정 에셋을 만듭니다. **Prompt Prefab**은 패키지 기본
   프리팹으로 자동으로 채워집니다.
2. 루트 `IInstaller`에서 등록합니다. AdService·AnalyticsService보다 앞이든 뒤든 상관없습니다.

   ```csharp
   builder.RegisterConsentService(_consentServiceSettings);
   builder.RegisterAdService(_adServiceSettings);
   builder.RegisterAnalyticsService(_analyticsServiceSettings);
   ```

3. UMP를 쓰려면 **Use UMP**를 켜고 AdMob 앱 ID(Android/iOS)를 넣습니다.
   - 켜면 에디터가 `Assets/FoundationDI.Data/Editor/FoundationDIUmpDependencies.xml`을 만들고, 끄면 지웁니다.
     **External Dependency Manager(EDM4U)**가 이 파일을 읽어 네이티브 의존성을 넣습니다. LevelPlay를 쓰면 이미
     설치돼 있습니다. 없으면 설정 인스펙터가 경고합니다.
   - 앱 ID는 LevelPlay AdMob 어댑터 설정에 이미 넣었다면 비워 둬도 됩니다. 빌드 후처리가 **키가 없을 때만** 넣습니다.
     둘 다 비어 있으면 빌드가 멈춥니다(UMP는 앱 ID 없이 실행 시점에 크래시합니다).

| 설정 | 기본값 | 설명 |
|---|---|---|
| Policy Version | 1 | 약관을 바꾸면 올립니다. 저장된 수락 버전보다 크면 다시 묻습니다. **0이면 약관 단계를 쓰지 않습니다.** |
| 문구 (Title 등) | 영어 | 기본 프리팹의 TMP 기본 폰트에 CJK 글리프가 없어 영어가 기본입니다(5장). |
| Terms/Privacy Url | 비어 있음 | 비면 그 링크 버튼이 숨겨집니다. |
| Use UMP | 끔 | 켜면 의존성 XML을 만들고 런타임이 UMP 동의를 받습니다. |
| AdMob App Id | 비어 있음 | `ca-app-pub-XXXX~YYYY`. |
| Debug Geography / Test Device Hashed Ids | Disabled / 없음 | **개발 빌드에서만** 적용됩니다. |
| Tag For Under Age Of Consent | 끔 | 아동 대상 앱이면 켭니다. |
| Ump Update Timeout Seconds | 10 | UMP 정보 갱신(네트워크)을 기다리는 최대 시간입니다. |
| Request Tracking Authorization | 켬 | UMP 다음에 ATT를 묻습니다(7장). |

## 3. AdMob 콘솔 설정

UMP 폼의 내용은 코드가 아니라 AdMob 콘솔 **개인 정보 보호 및 메시지**에서 정합니다.

1. **GDPR 메시지**를 만들고 앱에 연결해 게시합니다. 게시하지 않으면 EEA에서도 폼이 뜨지 않습니다.
2. **광고 파트너(Additional Consent)**에 LevelPlay에서 쓰는 미디에이션 네트워크(ironSource, Unity Ads,
   AppLovin, Mintegral 등)를 추가합니다. 빠진 네트워크에는 동의가 전달되지 않습니다.
3. **Consent mode** 설정에서 광고 목적과 분석 목적 토글을 둘 다 켭니다. Firebase가 UMP 결과를
   `ad_storage`·`analytics_storage` 등으로 받습니다(분석 토글은 광고 토글을 켜야 나타납니다).
4. (선택) **미국 주 규정 메시지**를 만들면 UMP가 GPP 문자열을 씁니다.
5. (선택, iOS) **IDFA 설명 메시지**를 만들면 UMP가 사전 안내 후 ATT를 직접 띄웁니다. 그 경우 7장의 ATT
   단계는 이미 답한 상태라 건너뜁니다.

## 4. 게임 코드

보통은 할 일이 없습니다. `IAdService.InitializeAsync()`/`IAnalyticsService.InitializeAsync()`가 동의를 먼저
기다립니다. 로딩 화면처럼 시점을 직접 고르고 싶으면 먼저 부릅니다.

```csharp
[Inject] private IConsentService _consent;

// 기존 유저(이전 버전에서 업데이트)는 약관을 다시 묻지 않으려면 판단해서 먼저 수락 처리한다.
if (_save.HasAnyProgress) _consent.AcceptPolicy();

// 현지화 문구(RequestAsync 전에).
_consent.SetPolicyContent(new PolicyAgreementContent(
    title: L("policy.title"), message: L("policy.body"), acceptLabel: L("policy.accept"),
    termsLabel: L("policy.terms"), termsUrl: "https://example.com/terms",
    privacyLabel: L("policy.privacy"), privacyUrl: "https://example.com/privacy"));

var result = await _consent.RequestAsync();
```

**설정 화면의 "개인정보 옵션" 버튼** — GDPR 지역 사용자에게만 보여 줍니다. 사용자가 언제든 동의를 바꿀 수
있어야 합니다.

```csharp
_privacyButton.gameObject.SetActive(_consent.IsPrivacyOptionsRequired);
_privacyButton.onClick.AddListener(async () => await _consent.ShowPrivacyOptionsAsync());
```

폼이 닫히면 `ConsentChanged`가 발행되고 AnalyticsService가 수집 여부를 다시 적용합니다.

| API | 설명 |
|---|---|
| `Awaitable<ConsentResult> RequestAsync()` | 약관 → 선행 작업 → UMP → ATT. 진행 중이면 편승하고, 직전 결과가 확정(`PolicyAccepted && CanRequestAds && AnalyticsConsent != Unknown`)이면 즉시 그 결과를 돌려줍니다. 아니면 UMP 단계만 다시 합니다. |
| `ConsentResult Current` | 현재 상태를 매번 다시 읽습니다. |
| `bool IsPolicyAccepted` / `void AcceptPolicy()` | 약관 수락 여부 / 창 없이 수락 처리. |
| `void SetPolicyContent(PolicyAgreementContent)` | 약관 창 문구를 바꿉니다. |
| `bool IsPrivacyOptionsRequired` / `Awaitable ShowPrivacyOptionsAsync()` | 설정 화면 버튼. |
| `ITrackingAuthorization TrackingAuthorization` | ATT를 직접 물을 때(7.3). |
| `event Action ConsentChanged` | 옵션 폼이 닫혔거나 재판정 결과가 바뀌었을 때. |

`ConsentResult`는 `PolicyAccepted`, `CanRequestAds`, `AnalyticsConsent`(`Unknown`/`Granted`/`Denied`),
`TrackingStatus`, `Tcf`(`GdprApplies`, `PurposeConsents`)를 담습니다.

## 5. 약관 창 바꾸기

**스킨 교체 (코드 없음)** — `ConsentService/Policy/PolicyAgreementView.prefab`을 프로젝트로 복제해 디자인을
고치고 설정의 **Prompt Prefab**에 지정합니다. 루트에 `PolicyAgreementView` 컴포넌트만 있으면 되고, 버튼·텍스트
참조는 인스펙터로 연결합니다. **한국어·일본어 등은 이 프리팹에 해당 글리프가 있는 TMP 폰트를 넣어야 합니다.**
기본 프리팹은 TMP 기본 폰트(LiberationSans)라 CJK가 네모로 깨집니다.

**완전 교체** — `IPolicyAgreementPrompt`를 구현해 넘깁니다.

```csharp
public sealed class MyPolicyPrompt : IPolicyAgreementPrompt
{
    public Awaitable ShowAsync(PolicyAgreementContent content) { /* 띄우고 동의 버튼에서 완료 */ }
    public void Close() { /* 표시 중이면 정리(ConsentService Dispose 때 불린다) */ }
}

builder.RegisterConsentService(_consentServiceSettings, prompt: new MyPolicyPrompt());
```

- 구현은 "보여 주고, 링크를 열고, 동의 버튼이 눌리면 완료"만 합니다. 수락 저장과 버전 비교는 ConsentService가
  합니다.
- **표시할 수 없으면 예외를 던지세요.** 조용히 완료하면 사용자가 보지도 않은 약관을 수락한 것으로 저장됩니다.
  예외가 나면 `PolicyAccepted = false`로 끝나고 광고·분석이 시작되지 않습니다.
- 이 구현은 루트(앱 수명) 컨테이너에 등록됩니다. UINavigator 같은 씬 수명 UI를 쓰면 표시 시점에 그 씬이
  살아 있는지 게임이 책임져야 합니다. 기본 구현이 UINavigator를 쓰지 않는 이유가 이것입니다 — 첫 씬의 UI
  루트보다 먼저 떠야 할 수 있습니다.
- 기본 구현은 씬에 EventSystem이 없으면 표시하는 동안만 임시로 만듭니다(Input System이 있으면
  `InputSystemUIInputModule`).

## 6. 광고·분석 연동

### 6.1 AdService

`InitializeAsync`가 `RequestAsync()`를 먼저 기다리고, **`PolicyAccepted && CanRequestAds`일 때만** provider를
초기화합니다(Google 지침: `canRequestAds`가 false면 광고를 요청하지 않는다). 아니면 경고를 남기고 `false`를
돌려줍니다. 다시 부르면 동의 재판정부터 재시도합니다. ATT 결과는 `AdProviderContext.TrackingStatus`로 provider까지
갑니다.

`IConsentService`를 등록하지 않으면 경고와 함께 약관·UMP 없이 ATT만 묻습니다(0.11 동작).

### 6.2 AnalyticsService

`AnalyticsServiceSettings`의 **Wait For Consent**(기본 켜짐)가 켜져 있으면 동의 판정으로 수집을 게이트합니다.

| 상황 | 판정 | 수집 | 초기화 전·판정 전 이벤트 |
|---|---|---|---|
| GDPR 지역, 분석 동의(TCF Purpose 1) | `Granted` | 켬 | 전송 |
| GDPR 지역, 분석 거부 | `Denied` | 끔 (게임은 진행) | 폐기 |
| GDPR 지역 아님 (UMP `NotRequired`) | `Granted` | 켬 | 전송 |
| 판정 불가 (첫 실행 오프라인 등) | `Unknown` | 끔 | 메모리에 유지, 재판정에서 전송/폐기 |
| UMP 미사용 | `Granted` | 켬 | 전송 |

- **ATT는 분석 수집과 무관합니다.** ATT는 IDFA 연결(추적)만 막습니다.
- **Firebase는 UMP보다 먼저 초기화됩니다.** Google 요구 사항이라 ConsentService가 UMP 전에 분석 provider
  초기화(`AnalyticsProviderInitializer`)를 기다립니다. 광고 초기화가 먼저 불려도 순서는 같습니다.
- 빌드 후처리가 Firebase 자동 수집을 기본으로 꺼 둡니다(`firebase_analytics_collection_enabled=false`).
  동의 후 AnalyticsService가 켭니다. 이게 없으면 약관 수락 전에도 `first_open` 등이 나갑니다.
- **Adjust는 TCF를 읽지 않습니다.** 어댑터가 동의를 Google DMA 신호(`google_dma` eea / ad_user_data /
  ad_personalization)로 번역해 보냅니다. 어댑터는 첫 세션 지연을 **항상** 켜므로(0.12) 약관 창이 떠 있거나
  판정 전인 동안 인스톨 세션이 나가지 않고, 코어가 번역을 첫 세션 지연 해제보다 먼저 부르므로 DMA 값이
  인스톨 레코드에 실립니다. 매핑(P1·P7 → ad_user_data, P3·P4 →
  ad_personalization)은 Google의 TCF 매핑을 따른 이 패키지의 해석입니다.

## 7. iOS 추적 권한(ATT)

UMP 다음에 묻습니다. 광고 SDK는 **초기화 시점의 ATT 상태**로 IDFA 사용 여부를 정하므로 provider 초기화보다
먼저 끝나 있어야 합니다.

> **다른 권한 팝업(알림·위치 등)은 `await _ads.InitializeAsync()` 이후에 요청하세요.** iOS는 시스템 권한
> 요청을 동시에 받으면 하나만 남기고 버립니다.

### 7.1 설치

바인딩은 유니티 공식 패키지 **iOS 14 Advertising Support**(`com.unity.ads.ios-support`)의
`ATTrackingStatusBinding`입니다. Package Manager에서 설치하면 그걸로 끝입니다 —
스크립팅 심볼 `FOUNDATIONDI_IOSSUPPORT`는 `SdkDefineSynchronizer`가 패키지의 존재를 보고
자동으로 켜고, 패키지를 지우면 다시 끕니다(손으로 정의하지 마세요).

**버전은 아무거나 괜찮습니다.** 이 패키지는 버전 번호가 시간순과 어긋나 있습니다 —
1.2.0이 2021-11에, 1.0.1이 2025-10에 게시됐고 레지스트리가 `latest`로 가리키는 쪽은
**1.0.1**입니다(그래서 Package Manager에서 그냥 설치하면 1.0.1이 들어옵니다). 두 버전은
API도 다릅니다: 완료 콜백을 받는
`RequestAuthorizationTracking(RequestAuthorizationTrackingCompleteHandler)`는 1.2.0에만
있습니다. asmdef 이름은 같아서 어느 쪽이든 심볼은 켜지므로, 어댑터가 그 오버로드를 쓰면
1.0.1 사용자는 컴파일조차 되지 않습니다. 그래서 `AttTrackingAuthorization`은 모든 버전에
있는 파라미터 없는 요청 + 상태 폴링만 씁니다.

**패키지를 설치하지 않으면 아무 일도 일어나지 않습니다.** 코어는 이 패키지를 참조하지 않고,
`TrackingAuthorizationRegistry`가 `UnsupportedTrackingAuthorization`으로 떨어져 초기화가
그대로 진행됩니다. 안드로이드·에디터에서도 같습니다.

### 7.2 동의 문구

`NSUserTrackingUsageDescription`은 `AttIosPostProcess`가 Xcode 빌드 후처리로 Info.plist에
넣습니다. 유니티 플레이어 설정에는 이 키를 넣을 자리가 없고, **문구가 없으면 iOS는
다이얼로그를 아예 띄우지 않고** 심사에서도 리젝됩니다.

현재 문구:

> We use your activity data to provide personalized ads and measure ad performance.

바꾸려면 `Editor/AdService/AttIosPostProcess.cs`의 `UsageDescription` 상수를 고칩니다.
다국어가 필요하면 `InfoPlist.strings`를 쓰거나 별도 후처리로 값을 먼저 채우세요 —
**이미 값이 있으면 이 후처리는 덮지 않습니다.**

### 7.3 시점을 직접 고르고 싶을 때

사전 안내 화면을 먼저 보여주는 식으로 시점을 조절하려면 `ConsentServiceSettings`의
**Request Tracking Authorization**을 끄고 직접 부릅니다.

```csharp
var status = await _consent.TrackingAuthorization.RequestAsync();
```

`IConsentService.TrackingAuthorization`은 설정을 꺼도, 패키지가 없어도 항상 null이 아닙니다
(`IsSupported`가 false일 뿐입니다).

### 7.4 요청 시점: Active를 기다렸다가 묻는다

**iOS 15부터 `requestTrackingAuthorization`은 앱이 `UIApplicationStateActive`일 때만 팝업을
띄웁니다.** 그 외 상태에서 요청하면 팝업도 오류도 없이 `notDetermined`로 끝납니다. 심사에서는
"ATT 권한 요청을 찾을 수 없다"(Guideline 2.1)로 리젝됩니다.

문제는 **Unity 6.3 iOS 트램펄린이 첫 씬을 Inactive 구간에서 로드한다**는 점입니다. UIScene
라이프사이클의 시작 순서가 이렇습니다.

```
sceneWillEnterForeground → initUnityWithScene → startUnity
  → UnityLoadApplication   ← 첫 씬 로드: Awake/Start가 여기서 돈다 (아직 Inactive)
  → UnitySetPlayerFocus(1)
  → ... → sceneDidBecomeActive   ← 여기서야 Active
```

첫 씬 `Awake`에서 `InitializeAsync()`(→ `RequestAsync()`)를 부르면 첫 `await` 전에 동기로 요청이 나가므로, 그대로
두면 요청이 Inactive에서 버려집니다. 그래서 `AttTrackingAuthorization`은 이렇게 동작합니다.

1. **Active 대기** — 매 프레임 네이티브 헬퍼 `FDI_Att_IsApplicationActive()`로
   `UIApplication.applicationState`를 확인하고, Active가 되면 **0.5초 안정화** 후 요청합니다.
   안정화 중 다시 Inactive가 되면 1단계로 돌아갑니다.
2. **요청 → 폴링** — 상태가 `NotDetermined`를 벗어나면 완료합니다.
3. **무시 판정과 재요청** — 요청 직전의 `FDI_Att_ResignActiveCount()`(앱이 Inactive로 내려간
   횟수)를 기억해 두고,
   - 1.5초 동안 카운트가 그대로(= 팝업이 뜨지 않음)이고 `NotDetermined`면 무시된 것으로 봅니다.
   - 카운트가 올랐는데(= 다른 팝업 등으로 Inactive를 거침) Active로 돌아온 뒤에도
     `NotDetermined`면 역시 무시된 것으로 봅니다.

   어느 쪽이든 **1초 → 2초 → 4초** 뒤 1단계부터 다시 묻고, **최대 3회**까지 재요청합니다.
   iOS는 이미 답한 뒤의 요청에는 팝업을 띄우지 않으므로 재요청은 안전합니다. 상한을 넘기면
   타임아웃을 기다리지 않고 현재 상태로 끝냅니다.
4. **전체 60초 타임아웃** — Active 대기와 재요청을 모두 포함합니다. 어떤 경우에도
   `InitializeAsync`가 영원히 멈추지 않습니다.

시간은 메인 스레드 디스패처 펌프(`Time.unscaledDeltaTime`)로 셉니다. 트램펄린은
`applicationWillResignActive`에서 플레이어 루프를 멈추므로(`UnityPause(1)`), **팝업이 떠 있는
동안에는 1.5초 판정도 60초 타임아웃도 흐르지 않습니다** — 사용자가 팝업을 읽는 중에 무시로
오판하거나 타임아웃이 끼어들지 않습니다.

**`Application.isFocused`나 `OnApplicationFocus`를 쓰지 않는 이유:** 위 순서에서 보듯
`UnitySetPlayerFocus(1)`이 `sceneDidBecomeActive`보다 먼저 불려, 포커스가 true인데 앱은 아직
Inactive인 구간이 있습니다. 첫 씬이 바로 그 구간입니다. `OnApplicationFocus`는 일부 기기에서
누락 보고도 있습니다. 그래서 `UIApplication` 상태와 `UIApplicationWillResignActiveNotification`
을 네이티브에서 직접 읽습니다(`ConsentService/Tracking/iOSSupport/Plugins/iOS/FDI_Att.mm`, AppTrackingTransparency
는 import하지 않음 — 요청과 상태 조회는 계속 ios-support 바인딩이 합니다).

**실기기에서 확인하는 방법:** Xcode 콘솔에서 단계별 로그를 트램펄린 로그와 대조합니다.

```
-> sceneDidBecomeActive()
[AdService] ATT Active 대기 종료 — 0.5초 안정화 후 요청한다
[AdService] ATT 1번째 요청
-> sceneWillResignActive()          ← 팝업이 떴다
-> sceneDidBecomeActive()
[AdService] ATT 최종 상태: Authorized (요청 1회)
```

`ATT Active 대기 시작`이 `sceneDidBecomeActive`보다 앞서고, `1번째 요청`이 그 뒤에 나오면
정상입니다. `무시 판정`이 찍히면 다른 권한 팝업과 겹치지 않았는지 먼저 확인하세요.

### 7.5 동작 규칙

- **iOS 실기에서만 묻습니다.** 에디터·안드로이드에서는 `IsSupported`가 false라 건너뜁니다.
- **이미 답을 받았으면 다시 묻지 않습니다.** iOS는 두 번째 요청에 다이얼로그를 띄우지 않고
  기존 상태를 그대로 돌려줍니다.
- **요청이 실패해도 동의 흐름은 끝납니다.** 비타게팅 광고는 여전히 나가고, 여기서
  던지면 그마저 잃습니다.
- **완료 콜백 대신 상태를 폴링합니다.** 콜백 오버로드가 1.2.0에만 있기 때문입니다(위 7.1
  참고). 폴링은 디스패처 펌프 위에서만 돌아 메인 스레드 마샬링이 필요 없고, 바인딩이
  진행 중인 요청에 던지는 `InvalidOperationException`도 지나치지 않습니다.
- **60초 타임아웃이 있습니다.** Active가 끝내 오지 않거나 요청이 계속 버려져도 초기화가
  영원히 끝나지 않는 일은 없습니다(7.4 참고). 타임아웃이나 재요청 상한으로 끝나면 경고 로그를
  남기고 현재 상태로 초기화를 계속합니다.
- **GDPR 동의와는 다른 축입니다.** UMP는 데이터 처리 동의(TCF)이고 ATT는 OS의 추적 권한입니다. ATT를 허용해도 GDPR 동의가 되지 않고, ATT를 거부해도 분석 수집은 막히지 않습니다(6.2).

## 8. 실패 처리 — 게임을 막지 않는다

- UMP 갱신·폼 실패, 예외, 네이티브 SDK 누락: 경고를 남기고 진행합니다. `CanRequestAds`는 UMP가 캐시한 이전
  세션 값을 다시 읽습니다.
- UMP 정보 갱신에만 타임아웃(기본 10초)이 있습니다. 폼이 떠 있는 동안은 사용자가 읽는 시간이라 타임아웃이
  없습니다(Android는 폼이 떠 있어도 게임 루프가 돕니다).
- iOS UMP 폼은 ATT와 같은 이유로 앱이 Active가 될 때까지 기다렸다가 띄웁니다.
- 진행 중 재호출은 같은 흐름에 편승합니다. Dispose되면 대기자를 현재 상태로 완료합니다. 약관 창이 떠 있을
  때 Dispose되면 창을 닫고 `PolicyAccepted = false`로 끝납니다.
- 에디터에서는 UMP 네이티브가 없어 "규제 대상 아님"처럼 동작합니다. 약관 창은 에디터에서도 뜹니다.

## 9. 빌드

| 후처리 | 내용 |
|---|---|
| `UmpPostProcess` (iOS `PostProcessBuild(1002)`, Android Gradle 후처리) | AdMob 앱 ID를 **없을 때만** 넣습니다. Wait For Consent + Firebase면 Firebase 자동 수집 기본값을 끕니다. |
| `UmpDependenciesSync` (빌드 전) | Use UMP와 의존성 XML을 맞춥니다. |

iOS 후처리 순서는 `FDI_HapticiOSPostProcess`(999) → `AttIosPostProcess`(1000) → `IosExportCompliance`(1001)
→ `UmpPostProcess`(1002)입니다.

## 10. 실기 확인 체크리스트

EditMode로 검증할 수 없는 항목입니다. 체크된 항목은 2026-10-14 Android 실기(Galaxy A13, LevelPlay 9.5.1 +
AdMob 어댑터 5.31.0.0, UMP 4.0.0, Google 샘플 AdMob 앱 ID)로 확인했습니다.

- [x] Android: Debug Geography = EEA → UMP 폼 표시, 동의/거부 결과 반영 (`PurposeConsents` 읽기 포함)
- [ ] iOS: Debug Geography = EEA → UMP 폼 표시, 동의/거부 결과 반영
- [x] 비 EEA → UMP가 `gdprApplies=0` 기록, 폼 없음, 분석 즉시 켜짐
- [x] 설정 화면 → 옵션 폼 다시 열기, `ConsentChanged`로 변경 반영
- [x] 오프라인 첫 실행 → `Unknown`·광고 보류 → 네트워크 복귀 후 자동 재판정 → 동의 시 광고 자동 초기화
- [ ] iOS: 약관 → UMP → ATT 순서, 첫 실행에서 팝업이 버려지지 않음
- [ ] LevelPlay TCF 로그(7.8.0+)에 동의 전달 확인 (실제 LevelPlay 앱 키 필요)
- [ ] Adjust 대시보드에 DMA 값 확인
- [ ] R8 난독화 빌드에서 UMP JNI 호출 정상 (실패하면 `-keep class com.google.android.ump.** { *; }`)
- [x] Android: LevelPlay AdMob 어댑터(Next-Gen `ads-mobile-sdk:1.5.0`)와 함께 Gradle 해석·빌드 성공 (`play-services-ads` 미포함)
- [ ] iOS: LevelPlay AdMob 어댑터와 함께 CocoaPods 해석 성공

**테스트 기기 등록**: Debug Geography는 테스트 기기에서만 적용됩니다. 첫 실행 logcat의
`UserMessagingPlatform: Use new ConsentDebugSettings.Builder().addTestDeviceHashedId("…")` 값을 설정의
**Test Device Hashed Ids**에 넣으세요. Google 샘플 앱 ID(`ca-app-pub-3940256099942544~3347511713`)로도
EEA 폼이 뜹니다.

## 11. 0.11 → 0.12 마이그레이션

| 0.11 | 0.12 |
|---|---|
| `IAdService.TrackingAuthorization` | `IConsentService.TrackingAuthorization` |
| `AdServiceSettings` > Request Tracking Authorization | `ConsentServiceSettings` > Request Tracking Authorization |
| `IAdService.Consent` / `IAdProvider.Consent` / `IAdConsent` / `NoopAdConsent` | 제거. `IConsentService` |
| `AdServiceOptions(..., requestTrackingAuthorization)` | 파라미터 제거 |
| `new AdService(provider, dispatcher, options, storage, tracking)` | 마지막 인자가 `IConsentService` |
| `TrackingAuthorizationRegistry.Create(IAdDispatcher)` | `Create(IMainThreadDispatcher)` (`IAdDispatcher`도 그대로 넘어갑니다) |

등록에 `builder.RegisterConsentService(settings)` 한 줄을 추가합니다. 추가하지 않아도 0.11처럼 ATT는 묻지만
경고가 남고, EEA에서는 제한 광고만 나갑니다.

## 12. 구조

```
ConsentService/
├── IConsentService.cs / ConsentService.cs     흐름(약관 → 선행 작업 → UMP → ATT)과 실패 처리
├── ConsentTypes.cs                             ConsentResult, TcfSignals, AnalyticsConsent
├── ConsentServiceOptions.cs / ConsentServiceSettings.cs / ConsentServiceRegistration.cs
├── IConsentPrerequisite.cs                     UMP 전에 끝나야 하는 작업(분석 SDK 초기화)
├── AnalyticsConsentJudge.cs                    UMP 결과 → 분석 수집 동의
├── DmaConsentMapper.cs                         TCF → Google DMA 신호(Adjust)
├── Policy/                                     약관 창 seam, 기본 uGUI 구현, 저장소
├── Ump/                                        IUmpConsent, Android(JNI)/iOS(FDI_Ump.mm) 브리지, 팩토리
└── Tracking/                                   ATT seam, 레지스트리, iOSSupport 옵셔널 어셈블리
```
