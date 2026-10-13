# ConsentService 설계 — 약관 수락 + Google UMP + ATT

- 날짜: 2026-10-14
- 대상 버전: 0.12.0 (공개 API 파괴)
- 참고: [yasirkula/UnitySimpleGDPRConsent](https://github.com/yasirkula/UnitySimpleGDPRConsent), `~/Projects/Decompler/GDPR_ATT_비교_TileExplorer_vs_VitaMahjong.md`

## 1. 목적과 배경

게임이 출시 지역의 개인정보 규정을 지키면서 광고·분석 수익을 잃지 않게 하는 앱 수명 서비스다. 세 가지 동의를 한 흐름으로 묶는다.

1. **약관·개인정보처리방침 수락**: 서비스 이용 계약이다. 수락해야 게임이 진행된다.
2. **GDPR 데이터 처리 동의**: Google UMP가 받는다. 거부해도 게임은 진행된다.
3. **iOS ATT**: OS 권한이다. 거부해도 게임은 진행된다.

### 1.1 SimpleGDPR식 자체 동의창을 쓰지 않는 이유

Google은 2024-01-16부터 EEA·영국, 2024-07-31부터 스위스에서 AdMob·Ad Manager 광고에 **Google 인증 TCF v2.2 CMP**를 요구한다. 없으면 제한 광고만 나간다(https://support.google.com/admob/answer/13554020). LevelPlay 미디에이션에는 Google 수요가 섞이므로, 목적별 동의는 인증 CMP인 UMP가 받아야 한다. SimpleGDPR에서는 약관 수락 창(`TermsOfServiceDialog`)의 개념만 가져온다.

### 1.2 GMA Unity 플러그인 대신 UMP 자체 브리지를 쓰는 이유

UMP의 공식 Unity 바인딩은 Google Mobile Ads(GMA) Unity 플러그인 안에만 있다. 이 플러그인을 쓰면 LevelPlay AdMob 어댑터와 충돌한다(2026-10-13 의존성 파일로 확인).

| | LevelPlay AdMob 어댑터 5.31.0.0 | GMA Unity 플러그인 v11.5.0 |
|---|---|---|
| Android | Next-Gen `ads-mobile-sdk:1.5.0` (어댑터 5.19.0.0부터) | 기존 `play-services-ads:25.4.0` |
| iOS | `Google-Mobile-Ads-SDK = 13.11.0` (정확히 고정) | `Google-Mobile-Ads-SDK ~> 13.9` |

- Android는 Maven 좌표가 달라 EDM4U가 하나로 합칠 수 없다. Google은 기존 SDK를 제외하지 않으면 중복 심볼 에러가 난다고 명시한다.
- iOS는 지금은 우연히 해석되지만, 같은 패턴의 실패 사례가 있다(googleads-mobile-unity #4068).
- 플러그인의 UMP 브리지는 Android·iOS 모두 광고 SDK와 함께 컴파일되므로 UMP 부분만 떼어 쓸 수 없다.

UMP 네이티브 SDK(`com.google.android.ump:user-messaging-platform`, `GoogleUserMessagingPlatform` 포드)는 광고 SDK와 별개 artifact라 충돌하지 않는다. TileExplorer·VitaMahjong도 UMP를 자체 네이티브 코드로 호출한다.

LevelPlay는 SDK 7.7.0부터 UMP의 TCF·Additional Consent 문자열을 스스로 읽는다. 그래서 광고 쪽은 순서만 맞추면 되고 번역은 필요 없다.

## 2. 범위

### 포함
- `IConsentService`와 `ConsentService`. 흐름은 약관 → UMP → ATT.
- 약관 수락 창 기본 UI(uGUI 프리팹)와 두 단계 커스텀(스킨 교체, 완전 교체).
- UMP 네이티브 브리지(Android JNI, iOS `.mm`).
- ATT 구현을 AdService에서 ConsentService로 이관.
- AdService·AnalyticsService 연동(초기화 게이트, 분석 수집 게이트, Adjust DMA 번역).
- 빌드 후처리(AdMob 앱 ID, Firebase 수집 기본값)와 UMP Dependencies.xml 생성 도구.

### 제외
- 목적별 동의 토글 UI. UMP가 대신한다.
- ATT 사전 안내 화면·보상·노출 빈도 정책. 게임 로직이고, 허용 보상은 Apple 지침상 금지다.
- "동의 또는 결제(consent or pay)" 모델.
- 약관 다시 보기 UI. 게임이 `Application.OpenURL`로 처리한다.
- 패키지 차원의 현지화 시스템 의존.

## 3. 사용자에게 보이는 창

| 순서 | 창 | 그리는 쪽 | 커스텀 |
|---|---|---|---|
| ① | 약관·개인정보처리방침 수락 창 | 이 패키지 | 프리팹 교체 / `IPolicyAgreementPrompt` 구현 |
| ② | GDPR 데이터 처리 동의 폼 | Google UMP | AdMob 콘솔 "개인 정보 보호 및 메시지" |
| ③ | (선택) IDFA 설명 메시지 | Google UMP | AdMob 콘솔 |
| ④ | ATT 시스템 팝업 | iOS | `NSUserTrackingUsageDescription` 문구 |

설정 화면에서 다시 여는 "개인정보 옵션"은 ②의 UMP 옵션 폼이다.

## 4. 구성

### 4.1 위치
- 런타임: `Assets/FoundationDI/Runtime/Services/ConsentService/`
- iOS ATT 옵셔널 어셈블리 `FoundationDI.iOSSupport`: `AdService/Consent/iOSSupport/`에서 `ConsentService/iOSSupport/`로 이동한다. 어셈블리 이름과 심볼(`FOUNDATIONDI_IOSSUPPORT`)은 그대로 둔다.
- 에디터: `Assets/FoundationDI/Editor/ConsentService/`
- 수명: 앱 수명이며 루트 컨테이너에 등록한다.

### 4.2 공개 API

```csharp
public interface IConsentService : IDisposable
{
    // 약관 → (prerequisite) → UMP → (옵션) ATT. 진행 중 재호출은 같은 결과에 편승한다.
    // 절대 영원히 매달리지 않는다(전체 타임아웃).
    Awaitable<ConsentResult> RequestAsync();

    bool IsPolicyAccepted { get; }
    void AcceptPolicy();                                // 기존 유저 자동 동의용
    void SetPolicyContent(PolicyAgreementContent content); // 런타임 문구(현지화)

    ConsentResult Current { get; }                      // 현재 상태를 매번 다시 읽는다
    bool IsPrivacyOptionsRequired { get; }              // 설정 화면 버튼 표시 조건
    Awaitable ShowPrivacyOptionsAsync();

    ITrackingAuthorization TrackingAuthorization { get; } // AdService에서 이관

    event Action ConsentChanged;                        // 옵션 폼이 닫힌 뒤, 재판정 뒤
}

public readonly struct ConsentResult
{
    public bool PolicyAccepted { get; }
    public bool CanRequestAds { get; }
    public AnalyticsConsent AnalyticsConsent { get; }
    public TrackingAuthorizationStatus TrackingStatus { get; }
    public TcfSignals Tcf { get; }                      // IABTCF_gdprApplies, IABTCF_PurposeConsents
}

public enum AnalyticsConsent { Unknown, Granted, Denied }
```

DI 등록:

```csharp
builder.RegisterConsentService(settings);                    // 기본 UI
builder.RegisterConsentService(settings, prompt: myPrompt);  // 커스텀 UI
```

### 4.3 내부 seam

| seam | 역할 | 기본 구현 | 대체 |
|---|---|---|---|
| `IPolicyAgreementPrompt` | 약관 수락 창 | `DefaultPolicyAgreementPrompt` | 게임 구현 |
| `IPolicyAgreementStorage` | 수락한 약관 버전(int) 저장 | `PlayerPrefsPolicyAgreementStorage` | 테스트 |
| `IUmpConsent` | UMP 네이티브 호출과 TCF 저장소 읽기 | `AndroidUmpConsent` / `IosUmpConsent` | `UnsupportedUmpConsent`(에디터·미설정) |
| `ITrackingAuthorization` | ATT(기존) | `AttTrackingAuthorization` | `UnsupportedTrackingAuthorization` |
| `IConsentPrerequisite` | UMP 시작 전에 끝나야 하는 작업 | AnalyticsService 등록이 제공 | 테스트 |
| `IMainThreadDispatcher` | 메인 스레드 마샬링·지연·프레임 | `UnityAdDispatcher` | 가짜 시계 |

### 4.4 공용으로 끌어올리는 것 (STRUCTURAL)
- `IMainThreadDispatcher`(Post/Delay/NextFrames)를 `Runtime/Common/`에 새로 두고 `IAdDispatcher : IMainThreadDispatcher`로 만든다. 기존 이름은 바꾸지 않는다 — 호스트에서 컴파일되지 않는 LevelPlay·AppLovin 어댑터가 `IAdDispatcher`를 쓴다. ConsentService가 ATT 재요청 지연과 UMP 콜백 마샬링에 쓴다.
- UMP iOS 브리지는 Active 판정 함수(`FDI_Ump_IsApplicationActive`)를 자체로 가진다. `IAppActivity`는 옮기지 않는다.
- 각 이동은 동작 변경 없이 별도 커밋으로 한다.

## 5. 초기화 흐름

```
RequestAsync()
 ① 약관      저장된 버전 < 설정 버전 → prompt.ShowAsync(content) 대기 → 버전 저장
 ② 선행 작업  등록된 IConsentPrerequisite 전부 await (예: Firebase 초기화)
 ③ UMP       (Use UMP일 때) requestConsentInfoUpdate → loadAndShowConsentFormIfRequired
             → CanRequestAds, PrivacyOptionsRequirementStatus, TCF 재판독
 ④ ATT       (Request Tracking Authorization이 켜져 있고 NotDetermined일 때만)
 → ConsentResult
```

- ①이 UMP보다 먼저인 이유: 약관 수락 전에 데이터 처리 동의를 묻는 것은 순서가 맞지 않는다. 두 참고 게임도 같다.
- ②가 필요한 이유: Google은 consent mode를 쓰려면 "UMP 시작 전에 Firebase를 초기화하라"고 요구한다(https://developers.google.com/admob/android/privacy/consent-mode). 이 순서를 문서가 아니라 구조로 강제한다. prerequisite는 SDK 초기화까지만 기다리고 동의는 기다리지 않으므로 교착이 없다.
- ④: UMP의 IDFA 설명 메시지가 ATT를 이미 띄웠다면 상태가 NotDetermined가 아니므로 건너뛴다. 기존 Active 대기·재요청 로직(`AttTrackingAuthorization`)을 그대로 쓴다. 옵션을 끄면 게임이 원하는 시점에 `TrackingAuthorization.RequestAsync()`를 직접 부른다(기존 `RequestTrackingAuthorization = false`의 이관).
- 직전 결과가 `PolicyAccepted && CanRequestAds && AnalyticsConsent != Unknown`이면 `RequestAsync`는 그 결과를 즉시 돌려준다. 아니면 ③만 다시 수행한다(지역·동의 재판정). 약관은 이미 수락했고 ATT는 한 번만 묻기 때문이다.

### 5.1 약관 단계
- 거절 버튼은 없다. 수락해야 진행한다.
- 모든 신규 유저에게 표시한다(지역 무관).
- 설정의 `Policy Version`을 올리면 약관 변경으로 보고 다시 묻는다.
- 기존 유저 자동 동의는 게임이 판단한다. 예를 들어 세이브가 있으면 `RequestAsync` 전에 `AcceptPolicy()`를 부른다.
- `Policy Version`이 0이면 약관 단계를 쓰지 않는다(항상 수락으로 본다).

### 5.2 실패 처리 — 게임을 막지 않는다
- UMP 업데이트·폼 로드 실패, 예외, 네이티브 클래스나 포드가 없는 경우: 경고 로그를 남기고 진행한다. `CanRequestAds`는 UMP가 캐시한 이전 세션 값을 다시 읽는다.
- iOS UMP 폼은 ATT와 같은 이유로 앱이 Active일 때까지 기다렸다가 띄운다. 첫 씬 Awake가 Inactive 구간이기 때문이다.
- UMP 정보 갱신(네트워크)에만 타임아웃(기본 10초)을 둔다. 폼 표시 중에는 사용자가 읽는 시간이라 타임아웃을 두지 않는다(Android에서는 폼이 떠 있어도 플레이어 루프가 돈다). ATT는 기존 60초 상한을 그대로 쓴다.
- 진행 중 재호출은 waiter 목록에 편승한다(AdService 초기화와 같은 패턴).
- Dispose되면 진행 중인 대기자를 현재 상태로 완료한다.
- 약관 창 표시 중 Dispose되면 창을 정리하고 `PolicyAccepted = false`로 완료한다.
- 기본 UI 프리팹이 비어 있으면 `ShowAsync`가 예외를 던지고 `PolicyAccepted = false`로 완료한다(조용히 수락 처리하지 않는다).

## 6. AdService 연동 (공개 API 파괴)

- 생성자가 `ITrackingAuthorization` 대신 `IConsentService`를 받는다.
- `InitializeAsync`: `await consent.RequestAsync()` → `PolicyAccepted && CanRequestAds`가 아니면 provider를 초기화하지 않고 `false`를 반환한다. 게임이 다시 부르면 UMP 재판정부터 재시도한다.
- `IConsentService`가 등록되지 않았으면 `RegisterAdService`가 경고를 남기고 약관·UMP 없이 ATT만 묻는 `ConsentService.CreateTrackingOnly`로 동작한다(0.11 동작 보존).
- `AdProviderContext.TrackingStatus`에는 `ConsentResult.TrackingStatus`를 싣는다. `LevelPlayAdProvider.ApplyPrivacy`(ATT 거부 → 제한)는 유지한다.
- 제거: `IAdService.Consent`, `IAdService.TrackingAuthorization`, `IAdProvider.Consent`, `IAdConsent`, `NoopAdConsent`, `AdServiceOptions.RequestTrackingAuthorization`, `AdServiceSettings._requestTrackingAuthorization`.
- `TrackingAuthorizationRegistry`와 `UnsupportedTrackingAuthorization`은 ConsentService 쪽으로 옮긴다.

## 7. AnalyticsService 연동

### 7.1 SDK별 동의 처리 (2026-10-14 조사)

| SDK | TCF 자동 인식 | 이 서비스가 할 일 | 순서 제약 |
|---|---|---|---|
| Firebase | ad_* 신호는 TCF에서, `analytics_storage`는 UMP consent mode에서 | 수집 on/off 게이트. `SetConsent`는 호출하지 않는다 | Firebase 초기화 → UMP |
| Adjust | 읽지 않음 | `TrackThirdPartySharing`의 `google_dma`(eea / ad_user_data / ad_personalization)로 번역 | 첫 세션 지연 해제 전 |
| Adjust ATT | 스스로 읽음 | 없음 | 첫 세션 지연이 ATT 대기보다 우선 |

### 7.2 수집 게이트 규칙 (Basic 모드)

| 상황 | 판정 | 분석 수집 | 버퍼 |
|---|---|---|---|
| GDPR 지역, 분석 동의 | `Granted` | 켬 | flush |
| GDPR 지역, 분석 거부 | `Denied` | 끔 (게임 진행) | 폐기 |
| GDPR 지역 아님 (UMP `NotRequired`) | `Granted` | 켬 | flush |
| 미국 주 규정(옵트아웃) | `Granted` | 켬. 판매·공유 제한은 GPP로 광고 쪽에서 처리 | flush |
| 판정 불가(첫 실행 오프라인 등) | `Unknown` | 끔 | 메모리에 유지. 다음 재판정에서 위 규칙 적용 |
| UMP 미사용 | `Granted` | 켬 | flush |

- ATT는 수집 게이트에 영향을 주지 않는다. ATT는 IDFA 연결(추적)만 막고, 자체 분석 전송은 막지 않는다.
- `ConsentChanged`가 오면 같은 규칙으로 다시 적용한다(설정 화면에서 동의를 바꾼 경우).
- 분석 동의 판정: `IABTCF_gdprApplies == 1`이면 `IABTCF_PurposeConsents`의 Purpose 1(기기 저장·접근)으로, 그 외에는 `Granted`로 본다. **실기에서 UMP consent mode 값과 대조해 확정한다(위험 R3).**

### 7.3 초기화 순서

```
AnalyticsService.InitializeAsync()
  provider SDK 초기화            ← ConsentService의 IConsentPrerequisite로도 등록
  await consent.RequestAsync()
  IAnalyticsConsentAware.ApplyConsent(result)  ← Adjust: DMA 번역
  게이트 적용(SetCollectionEnabled)            ← Firebase 수집 on/off
  게이트 규칙에 따라 버퍼 flush 또는 폐기
  IAnalyticsFlushHook.OnBufferedStateFlushed  ← Adjust EndFirstSessionDelay
```

- 번역이 필요한 provider만 선택 seam `IAnalyticsConsentAware.ApplyConsent(ConsentResult)`를 구현한다(Adjust). `IAnalyticsProvider`는 바꾸지 않는다.
- provider SDK 초기화는 `AnalyticsProviderInitializer : IConsentPrerequisite`가 맡아 ConsentService가 UMP 전에 기다린다.
- Adjust의 `Delay First Session`이 꺼져 있으면 DMA 값이 인스톨 레코드에 빠진다. README에 켜기를 권장한다고 적는다.
- TCF → DMA 매핑: `eea` ← `IABTCF_gdprApplies`, `ad_user_data` ← Purpose 1·7, `ad_personalization` ← Purpose 3·4. Google 매핑을 따른 이 서비스의 해석이며 Adjust가 정한 규칙은 아니다.

## 8. 약관 수락 창 UI

### 8.1 계약

```csharp
public interface IPolicyAgreementPrompt
{
    // 사용자가 수락할 때까지 기다린다. 거절 경로는 없다.
    Awaitable ShowAsync(PolicyAgreementContent content);
    void Close();   // 표시 중 Dispose될 때 정리용
}

public readonly struct PolicyAgreementContent
{
    public string Title { get; }
    public string Message { get; }
    public string AcceptLabel { get; }
    public string TermsLabel { get; }
    public string TermsUrl { get; }
    public string PrivacyLabel { get; }
    public string PrivacyUrl { get; }
}
```

수락 저장과 버전 비교는 ConsentService가 맡는다. 구현체는 "보여 주고, 링크를 열고, 동의 버튼이 눌리면 완료"만 한다.

### 8.2 기본 구현
- 프리팹 `PolicyAgreementView`: 제목, 본문, 약관·처리방침 링크 버튼 2개, 동의 버튼. 세로·가로 모두 맞도록 앵커를 잡는다. 텍스트는 TextMeshPro다(런타임이 이미 TMP를 참조한다 — `UITextStateSet`, `VolumeSlider`).
- 프리팹은 `ConsentServiceSettings.PromptPrefab`이 직접 참조한다. 런타임에 Resources를 쓰지 않는다. 설정 생성 도구가 패키지 기본 프리팹으로 채운다.
- UINavigator를 쓰지 않는다. UINavigator는 씬 수명이고, 이 창은 첫 씬 UI 루트보다 먼저 떠야 하는 앱 수명 UI다. `DummyAdCanvas`처럼 자체 캔버스를 연다.
  - `DontDestroyOnLoad`, 최상위 `sortingOrder`, 뒤쪽 입력 차단. 수락 후 파괴한다.
- 씬에 EventSystem이 없으면 표시하는 동안만 임시로 만들고 끝나면 정리한다. 입력 모듈은 Input System이 있으면 `InputSystemUIInputModule`, 없으면 `StandaloneInputModule`을 쓴다.
- 링크는 `IUrlOpener` seam(기본 `Application.OpenURL`)으로 연다.

### 8.3 커스텀
1. **스킨 교체**: 기본 프리팹을 복제해 디자인을 바꾸고 `PromptPrefab`에 지정한다. 루트에 `PolicyAgreementView` 컴포넌트만 있으면 된다.
2. **완전 교체**: `IPolicyAgreementPrompt`를 구현해 `RegisterConsentService(settings, prompt:)`로 넘긴다. 루트 컨테이너에 등록되므로, 씬 수명 UI를 쓰면 생명주기를 게임이 책임진다(README에 명시).

### 8.4 현지화
- 설정의 문구는 단일 언어 기본값이다.
- 다국어는 `SetPolicyContent(content)`를 `RequestAsync` 전에 부르거나, 완전 교체로 처리한다.

## 9. UMP 네이티브 브리지

### 9.1 공통
- `IUmpConsent`:
  - `Awaitable<UmpUpdateResult> UpdateAsync(UmpRequestOptions)`
  - `Awaitable<UmpFormResult> LoadAndShowIfRequiredAsync()`
  - `Awaitable<UmpFormResult> ShowPrivacyOptionsAsync()`
  - `bool CanRequestAds`, `bool IsPrivacyOptionsRequired`, `UmpConsentStatus Status`
  - `int? ReadTcfInt(string key)`, `string ReadTcfString(string key)`
  - `void Reset()` (디버그 전용)
- 모든 콜백은 `IMainThreadDispatcher.Post`로 마샬링한다. UMP 콜백은 메인 스레드 보장이 없다.
- 요청 옵션(설정에서): `Debug Geography`(개발 빌드에서만 적용), `Test Device Hashed Ids`, `Tag For Under Age Of Consent`.

### 9.2 Android — 순수 C#(JNI)
- `AndroidJavaClass("com.google.android.ump.UserMessagingPlatform")`로 직접 호출한다. 리스너는 `AndroidJavaProxy`, UI 스레드 실행은 `activity.Call("runOnUiThread", new AndroidJavaRunnable(...))`를 쓴다.
- Java 소스를 두지 않는다. UMP 클래스를 참조하는 `.java`는 UMP를 끈 프로젝트에서 Gradle 컴파일을 깨뜨린다. JNI 호출은 실행 시점에만 클래스를 찾는다.
- 클래스가 없으면(`AndroidJavaException`) 경고를 남기고 `UnsupportedUmpConsent`로 떨어진다.
- TCF 값은 기본 SharedPreferences(`PreferenceManager.getDefaultSharedPreferences`)에서 읽는다. Unity PlayerPrefs와는 다른 파일이다.

### 9.3 iOS — `FDI_Ump.mm`
- `#if __has_include(<UserMessagingPlatform/UserMessagingPlatform.h>)`로 감싼다. 포드가 없으면 스텁으로 컴파일되고 "미지원"을 보고한다.
- 폼은 `UnityGetGLViewController()`에서 띄운다. 콜백은 요청 ID와 `MonoPInvokeCallback` 정적 함수로 받는다.
- TCF 값은 `NSUserDefaults standardUserDefaults`에서 읽는다.

## 10. 설정·에디터·빌드

### 10.1 `ConsentServiceSettings` (ScriptableObject)
- Policy: `Policy Version`(int), 기본 문구(`PolicyAgreementContent` 필드들), `Prompt Prefab`
- UMP: `Use UMP`, `AdMob App Id (Android/iOS)`, `Debug Geography`, `Test Device Hashed Ids`, `Tag For Under Age Of Consent`
- ATT: `Request Tracking Authorization`(기본 true)
- 타임아웃: `Ump Update Timeout Seconds`(기본 10)
- 분석 게이트는 `AnalyticsServiceSettings.Wait For Consent`(기본 true)에 둔다.

### 10.2 UMP Dependencies.xml 생성
- `Use UMP`를 켜면 `Assets/FoundationDI.Data/Editor/FoundationDIUmpDependencies.xml`을 생성하고, 끄면 삭제한다.
- 내용: `com.google.android.ump:user-messaging-platform` 4.x, `GoogleUserMessagingPlatform` 3.x(범위 지정). 정확한 범위는 계획 단계에서 최신 릴리스로 확정한다.
- 패키지 안에 XML을 고정으로 두지 않는다. EDM4U는 패키지 폴더도 스캔하므로 UMP를 안 쓰는 프로젝트에 네이티브 의존성이 강제로 들어간다.
- EDM4U가 없으면 설정 인스펙터에 경고를 띄운다.
- 스크립팅 심볼은 쓰지 않는다. 브리지에 컴파일 의존성이 없다.

### 10.3 빌드 후처리 `UmpPostProcess`
- iOS: `[PostProcessBuild(1002)]`(기존 999 → 1000 → 1001 다음 번호). Android: Gradle 프로젝트 후처리.
- AdMob 앱 ID(`com.google.android.gms.ads.APPLICATION_ID` / `GADApplicationIdentifier`)를 **키가 없을 때만** 넣는다. LevelPlay AdMob 어댑터가 넣은 값을 덮지 않는다(`IosExportCompliance.ShouldDeclare`와 같은 규칙).
- `Use UMP`가 켜져 있는데 앱 ID가 비어 있고 키도 없으면 빌드 에러로 멈춘다. UMP는 앱 ID 없이 동작하지 않는다.
- `Wait For Consent`가 켜진 `AnalyticsServiceSettings`가 있고 `FOUNDATIONDI_FIREBASE`가 정의돼 있으면 `firebase_analytics_collection_enabled=false` / `FIREBASE_ANALYTICS_COLLECTION_ENABLED=NO`를 같은 규칙으로 넣는다.
- 판정은 순수 함수(`ShouldInject`)로 분리해 EditMode에서 검증한다.

## 11. 테스트

### 11.1 EditMode (NSubstitute)
- ConsentService
  - 순서: 약관 → prerequisite → UMP → ATT
  - 약관: 미수락이면 표시, 수락 후 재표시 안 함, 버전 상승 시 재표시, `AcceptPolicy`로 생략, `SetPolicyContent` 반영
  - UMP 미사용이면 `CanRequestAds = true`, `AnalyticsConsent = Granted`
  - 실패: 업데이트 실패·예외에도 완료, 타임아웃(가짜 시계), 재진입 편승, Dispose 중 완료
  - ATT: 옵션 끔/이미 결정됨이면 건너뜀, UMP가 ATT를 띄운 뒤 건너뜀
  - `ShowPrivacyOptionsAsync` 후 `ConsentChanged`
- 분석 동의 판정과 TCF → DMA 매핑(순수 함수)
- AdService: `CanRequestAds == false`면 provider 미초기화. 기존 `AdTrackingAuthorizationTest`를 ConsentService 쪽으로 이관
- AnalyticsService: 7.2 표의 각 행, `ConsentChanged` 재적용, `ApplyConsent` 호출 순서(flush 전)
- 약관 기본 View: 동의 버튼 → `ShowAsync` 완료, 링크 버튼 → `IUrlOpener` 호출, EventSystem 임시 생성·정리
- 에디터: Dependencies.xml 생성·삭제, 후처리 `ShouldInject`
- 등록: `RegisterConsentService` 기본·커스텀 prompt 배선

### 11.2 실기 체크리스트 (MahjongTile)
- [ ] Android·iOS에서 EEA 디버그 지역 → UMP 폼 표시, 동의/거부 결과 반영
- [ ] 비 EEA → `NotRequired`, 폼 없음, 분석 즉시 켜짐
- [ ] 설정 화면 → 옵션 폼 다시 열기, 변경 반영
- [ ] iOS: 약관 → UMP → ATT 순서, 첫 실행에서 팝업이 버려지지 않음
- [ ] LevelPlay TCF 로그(7.8.0+)에 동의 전달 확인
- [ ] Adjust 대시보드에 DMA 값 확인
- [ ] R8 난독화 빌드에서 JNI 호출 정상
- [ ] LevelPlay AdMob 어댑터와 함께 Gradle·CocoaPods 해석 성공

## 12. 위험

| # | 위험 | 대응 |
|---|---|---|
| R1 | R8가 UMP 클래스 이름을 바꿔 JNI 호출 실패 | UMP aar의 consumer 규칙을 실기로 확인하고, 부족하면 keep 규칙(`proguard-user.txt`)을 생성 |
| R2 | 오프라인 첫 실행에서 EEA 사용자의 분석이 그 세션 동안 꺼짐 | 메모리 버퍼 유지 + 앱 복귀 시 재판정. 세션을 넘는 영속 버퍼는 범위 밖 |
| R3 | Purpose 1 기반 분석 판정이 UMP consent mode의 `analytics_storage`와 다를 수 있음 | 실기에서 대조해 확정. 다르면 판정 함수만 교체 |
| R4 | UMP가 `IABTCF_EnableAdvertiserConsentMode`를 쓰는지 미확인 | Firebase ad_* 신호는 UMP consent mode가 직접 기록하므로 영향이 작음. 실기에서 확인 |
| R5 | UMP 폼 표시 중 씬 전환·Dispose | 대기자를 현재 상태로 완료, 네이티브 콜백은 Dispose 후 무시 |

## 13. 커밋·버전·문서

- 버전 0.12.0.
- STRUCTURAL 먼저:
  1. 디스패처 이름 변경·`Runtime/Common/` 이동
  2. ATT·iOSSupport를 ConsentService로 이동
  3. `IAppActivity` 공용화
- 이후 BEHAVIORAL: `plan.md` 항목 단위로 TDD(RED → GREEN → REFACTOR → 커밋).
- 문서:
  - `ConsentService/README.md` 신규 작성. AdMob 콘솔 설정 가이드(GDPR 메시지, consent mode 토글 2개, Additional Consent에 LevelPlay 네트워크 추가, IDFA 설명 메시지)를 포함한다.
  - AdService·AnalyticsService README와 CLAUDE.md를 갱신한다.
  - 0.11 → 0.12 마이그레이션 안내를 작성한다.
