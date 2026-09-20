# ATT 추적 동의 전파 설계

**작성일:** 2026-09-21
**계기:** App Store 리젝 — Submission ID `81f08a9a-74a2-4ef6-94cf-27078e19eed4`, InfiniteSteps 1.1.7 (10), 심사일 2026-09-18, 심사 기기 iPad Air 11-inch (M3)

---

## 1. 리젝 사유

> **Guideline 5.1.1(iv) - Legal - Privacy - Data Collection and Storage**
>
> You collect data to track after the user selects "Ask App Not to Track" on the App Tracking
> Transparency permission request.
>
> Specifically, we noticed the app accesses web content you own and collects cookies for tracking
> after the user asked you not to track them.

같은 메일의 Guideline 1.5(Support URL) 지적은 App Store Connect 설정 문제이며 이 문서 범위 밖이다.

---

## 2. 근본 원인

ATT 다이얼로그는 정상적으로 뜨고 정상적으로 응답을 받는다. **받은 응답을 아무도 쓰지 않는 것**이 문제다.

### 2.1 AdService가 ATT 상태를 로그로만 소비한다

`Runtime/Services/AdService/AdService.cs:155-175`

```csharp
private async Awaitable RequestTrackingAuthorizationAsync()
{
    ...
    var status = await _tracking.RequestAsync();
    Debug.Log($"[AdService] ATT 추적 권한: {status}");   // ← 여기서 끝난다
    ...
}
```

`InitializeAsync`(`AdService.cs:123`)는 이 메서드를 `await`한 뒤 곧바로
`_provider.InitializeAsync(_options.ProviderContext)`를 부른다. `status`는 어디로도 흘러가지 않으며,
Denied든 Authorized든 뒤 흐름이 바이트 단위로 동일하다.

### 2.2 전달할 통로 자체가 없다

`Runtime/Services/AdService/Providers/IAdProvider.cs:25-52` — `AdProviderContext`는
`AppKey` / `VerboseLogging` / `TestMode` / `TestDeviceIds` 네 개뿐이고, `IAdProvider`에도 추적 상태를
받는 멤버가 없다. 즉 2.1은 게으름이 아니라 **구조적 누락**이다.

### 2.3 LevelPlay provider가 프라이버시 API를 의도적으로 비워 뒀다

`Runtime/Services/AdService/Providers/LevelPlay/LevelPlayAdProvider.cs:55-61`

```csharp
// LevelPlay의 동의 플로우(LevelPlayPrivacySettings.SetGDPRConsent/SetCCPA/SetCOPPA)는
// 이번 범위 밖이다 — IAdConsent가 요구하는 것은 "동의 폼을 띄우고 CanRequestAds를
// 갱신하는" CMP 수준의 계약인데, LevelPlay가 제공하는 것은 이미 받은 동의 값을 SDK에
// 통보하는 세터뿐이라 폼을 띄우는 주체(UMP/CMP)가 따로 필요하다. ...
public IAdConsent Consent { get; } = new NoopAdConsent();
```

**이 판단은 `IAdConsent`에 대해서는 옳다.** `IAdConsent`는 폼을 띄우는 CMP 계약이고, LevelPlay가 주는
것은 세터뿐이니 억지 매핑은 거짓말이 된다.

틀린 것은 결론의 적용 범위다. **ATT는 폼을 OS가 대신 띄워 준다.** 그래서 "폼을 띄울 주체가 없다"는
이유가 ATT에는 해당하지 않고, 세터만 있으면 충분하다. 이 코드는 `IAdConsent`에 안 맞는다는 이유로
ATT 경로까지 함께 버렸다.

`AppLovinAdProvider.cs:32`, `DummyAdProvider.cs:19`도 같은 `NoopAdConsent`다.

### 2.4 결과

"Ask App Not to Track"을 눌러도 ironSource와 미디에이션되는 AdMob / Vungle / UnityAds가 전부
**동의받은 상태로 초기화**된다. OS가 IDFA를 막으므로 SDK들은 그 공백을 자기 웹뷰의 쿠키와
핑거프린팅으로 메운다 — 그것이 심사가 캡처한 "collects cookies for tracking"이다.

### 2.5 AnalyticsService README의 잘못된 전제

`Runtime/Services/AnalyticsService/README.md` §5 "ATT와 GDPR형 동의는 다르다":

> **ATT(iOS 추적 동의)** — 거부하면 **OS가 IDFA를 막습니다.** 앱이 아무것도 하지 않아도 그 정보는
> 얻어지지 않습니다. SDK는 그대로 돌고 이벤트도 그대로 나가며, 광고 식별자만 비어 옵니다.
> 이 서비스가 할 일이 없습니다.

**사실과 다르다.** Apple 기준으로 ATT 거부는 IDFA 차단이 아니라 **쿠키·핑거프린팅을 포함한 모든
크로스-컴퍼니 추적 금지**다. "SDK는 그대로 돌고"가 바로 리젝 사유다. 이 문장이 2.3의 판단을 뒷받침하는
근거로 쓰였을 가능성이 높으므로 함께 정정한다.

---

## 3. 설계 결정

### D1. seam은 `IAdProvider` 메서드가 아니라 `AdProviderContext` 필드로 둔다

**대안:** `IAdProvider.ApplyTrackingAuthorization(TrackingAuthorizationStatus)` 추가.

**선택 이유:** LevelPlay·AdMob·MAX 모두 프라이버시 세터를 **SDK Init 이전에** 불러야 효력이 있다.
별도 메서드로 두면 "Init보다 먼저 부른다"가 호출 규약(문서)으로만 존재해 언제든 깨진다.
context에 실으면 provider가 그 값을 **초기화의 일부로** 받으므로 순서를 틀릴 수가 없다.
`IAdProvider` 표면도 늘어나지 않는다.

**호환:** 생성자에 기본값 있는 마지막 인자로 추가한다. 기존 호출 5곳
(`AdServiceSettings.cs:90`, `AdTrackingAuthorizationTest.cs:56`, `AdServiceTest.cs:24,313,338`)이
그대로 컴파일된다.

### D2. provider가 읽는 값은 `IsTrackingRestricted` 하나다

`TrackingAuthorizationStatus` 원본도 싣되, provider가 분기하는 기준은 파생 프로퍼티로 고정한다.

| Status | IsTrackingRestricted | 근거 |
|---|---|---|
| `Denied` | `true` | 사용자가 거부했다 |
| `Restricted` | `true` | 기기 정책상 허용될 수 없다 |
| `Authorized` | `false` | 허용했다 |
| `NotDetermined` | `false` | **애매한 값이라 안전한 쪽이 `false`다** |

`NotDetermined`는 두 가지를 동시에 뜻한다 — "iOS인데 아직 안 물었다"와 "ATT가 없는 플랫폼
(안드로이드·에디터)". 안드로이드에서 `UnsupportedTrackingAuthorization.Status`는 항상
`NotDetermined`이므로(`UnsupportedTrackingAuthorization.cs`), 이걸 제한으로 해석하면 **안드로이드
전체의 광고 수익이 이유 없이 죽는다.** 그리고 iOS에서는 `AdService`가 provider 초기화 전에 반드시
먼저 묻기 때문에 "아직 안 물었다"로 provider에 도달하는 경로는
`AdServiceOptions.RequestTrackingAuthorization = false`(게임이 직접 묻겠다고 선언한 경우)뿐이다.
그 경우 판단 주체는 게임이다.

### D3. 제한 방향으로만 통보한다 — 동의를 긍정하지 않는다

`IsTrackingRestricted == false`일 때 `SetGDPRConsent(true)`를 부르지 **않는다.**

ATT 허용은 "Apple의 크로스-앱 추적 동의"이지 "GDPR 개인정보 처리 동의"가 아니다. 둘은 법적으로 다른
동의이며, ATT 허용을 GDPR 동의로 번역하면 받지 않은 동의를 SDK에 긍정하는 것이 된다. GDPR 동의는
UMP/CMP가 받아야 하고, 그것은 `IAdConsent` 축의 일이다(D4).

이 결정은 `LevelPlayAdProvider.ApplyContext`의 기존 원칙("끄는 것도 가능해야 한다 — 한 번 켜면 다시
끌 수 없는 API로 두지 않는다")과 충돌하는 것처럼 보이지만 아니다. 그 원칙은 로그·테스트 플래그처럼
**우리가 값의 주인인 설정**에 대한 것이고, 여기서는 우리가 주인이 아니다.

### D4. `IAdConsent`는 손대지 않는다

`IAdConsent`(CMP 폼 축)와 `ITrackingAuthorization`(OS 권한 축)은 서로 다른 축이라는 기존 설계는
유지한다(`ITrackingAuthorization.cs`의 주석이 이미 그렇게 선언한다). 이번 작업은 **ATT 축만**
provider까지 연결한다. `NoopAdConsent`는 그대로 남는다.

### D5. 앱 런치 시점의 SDK 자동 수집은 빌드 후처리로 막는다

런타임 코드가 손댈 수 없는 구간이 있다. AdMob(GMA SDK)은 `didFinishLaunchingWithOptions`에서 App
Measurement를 자동 초기화하고, Firebase Analytics도 마찬가지다. 둘 다 `AdService.InitializeAsync`가
돌기 전, ATT 다이얼로그가 화면에 떠 있는 동안이다.

이건 Info.plist 키로만 막힌다. 이미 `Editor/AdService/AttIosPostProcess.cs`가
`NSUserTrackingUsageDescription`을 쓰고 있으므로 같은 후처리에 키를 하나 더 얹는다.

| 키 | 값 | 이유 |
|---|---|---|
| `GADDelayAppMeasurementInit` | `true` | AdMob App Measurement 자동 초기화를 앱이 `GADMobileAds.start`를 부를 때까지 미룬다 |

**Firebase 쪽 같은 문제(`FIREBASE_ANALYTICS_COLLECTION_ENABLED = NO`)는 패키지가 맡지 않는다.**
검토했으나 자동화할 안전한 방법이 없다 — `AnalyticsServiceSettings`는 `Resources`나 고정 경로가 아니라
DI로 주입되는 에셋이라(`AnalyticsServiceRegistration.cs:13`) 에디터 후처리가 "이 프로젝트의 설정"을
특정할 수 없다. `AssetDatabase.FindAssets("t:AnalyticsServiceSettings")`로 추측하면 에셋이 둘 이상인
프로젝트에서 엉뚱한 값을 따르고, 그 실패는 조용하다.

그리고 이 키를 무조건 쓸 수도 없다. 수집을 켜고 출시하기로 한 프로젝트에서 SDK만 꺼 버리면
`AnalyticsService.Flush()`가 `SetCollectionEnabled(true)`를 부를 때까지의 세션 시작 이벤트가 사라진다.

따라서 이 키는 **게임 프로젝트의 빌드 후처리 책임**으로 남기고, 패키지는 `AnalyticsService` README에
필요성만 문서화한다(§2.5 정정과 함께).

---

## 4. 범위 밖

- **App Store Connect Support URL**(Guideline 1.5) — 코드와 무관하다.
- **UMP/CMP 도입** — `IAdConsent`의 실제 구현. 별도 과제다.
- **`AppLovinAdProvider` 대응** — 같은 구멍이 있지만(`AppLovinAdProvider.cs:32`) MAX SDK가 두
  프로젝트 어디에도 설치돼 있지 않다(루트의 `MaxSdk.*.csproj`는 예전 상태에서 남은 껍데기이고
  `Assets/MaxSdk/`는 없다). `FOUNDATIONDI_APPLOVIN`이 꺼져 있어 asmdef가 통째로 건너뛰어지므로
  컴파일조차 검증할 수 없다. **MAX SDK를 쓰는 프로젝트가 생길 때 같은 패턴으로 처리한다**
  (`MaxSdk.SetHasUserConsent` / `MaxSdk.SetDoNotSell` — 이름은 설치 후 확인할 것).
- **네트워크별 메타데이터 키**(`Vungle_ccpa` 등) — ironSource가 `do_not_sell`을 지원 어댑터로
  전파하므로 공통 경로로 충분하다고 본다. 프록시 캡처(§5)에서 특정 네트워크만 남으면 그때 추가한다.
- **InfinityStep(게임) 쪽 변경** — `Firebase/Analytics` → `FirebaseAnalyticsWithoutAdIdSupport` 팟
  교체, `AnalyticsServiceSettings.asset`의 `_collectionEnabledByDefault` 값, `GameManager`의 초기화
  순서. 별도 세션에서 다룬다.

---

## 5. 완료 판정

단위 테스트만으로는 부족하다. **실기 프록시 캡처가 최종 판정 기준이다.**

1. iOS 실기에 빌드해서 설치한다(기존 설치본 삭제 — ATT 상태가 남는다).
2. Charles / Proxyman으로 SSL 프록시를 건다.
3. 앱을 켜고 **"Ask App Not to Track"** 을 누른다.
4. 다음 도메인의 요청에 `Set-Cookie` 응답 헤더와 쿠키를 실은 요청이 없어야 한다:
   - `googleads.g.doubleclick.net`
   - `pagead2.googlesyndication.com`
   - `app.adjust.com` / Vungle(`api.vungle.com`, `ads.api.vungle.com`)
   - `init.supersonicads.com` / `outcome-ssp.supersonicads.com` (ironSource)
5. 같은 절차를 **"Allow"** 로 반복해 광고가 정상 노출되는지 확인한다 — 과잉 차단 회귀 검사다.

재심사 답신에 이 캡처를 첨부한다.
