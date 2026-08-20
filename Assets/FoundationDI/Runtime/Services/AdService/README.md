# AdService

**광고 네트워크 중립 서비스**입니다. AdMob 미디에이션 / Unity LevelPlay / AppLovin MAX 중
어느 SDK를 붙이더라도 게임 코드는 동일한 `IAdService` API 하나로 전면·보상·배너 광고를 다룹니다.
재시도·백오프, 보상 확정, 광고제거 게이트 같은 정책은 provider(SDK 어댑터)가 아니라
서비스 자신이 갖고 있어서, 세 SDK 어댑터마다 같은 로직을 복붙하지 않습니다.

현재 실제로 구현된 provider는 **Dummy 하나**뿐입니다. SDK 없이 로드 지연·실패·보상·임프레션을
흉내 내어 전체 흐름(재시도 백오프, `AdServiceRunner` 펌프, `Awaitable` 완료)을 실기에서
검증할 수 있게 하기 위한 것입니다. AdMob/LevelPlay/AppLovin 실제 어댑터는 각각 별도 계획으로 붙습니다.

---

## 1. 빠른 시작

### 1.1 설정 에셋 만들기

`Assets/Settings/`(또는 원하는 위치)에 우클릭 → `Create > FoundationDI > Ad Service Settings`.

`Dummy Provider` 섹션의 `Load Delay Seconds` / `Failure Rate` / `Ad Duration Seconds` /
`Banner Height`은 Dummy provider가 로드 지연·실패·보상·배너 표시 시간을 흉내 내는 데 쓰는
값입니다. 실기에서 재시도·백오프를 눈으로 확인하려면 `Failure Rate`를 0.3~0.5 정도로 올려 둡니다.

### 1.2 DI 등록

```csharp
using DarkNaku.FoundationDI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public class RootLifetimeScope : LifetimeScope
{
    [SerializeField] private AdServiceSettings _adServiceSettings;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterAdService(_adServiceSettings);
    }
}
```

`RegisterAdService`는 `IAdRemovalStorage`(기본 `PlayerPrefsAdRemovalStorage`),
`IAdDispatcher`(기본 `UnityAdDispatcher`), `IAdProviderFactory`(`AdProviderFactory`),
`IAdService`(`AdService`)를 싱글턴으로 등록합니다. `settings`가 `null`이면 에러 로그만 남기고
서비스를 등록하지 않습니다(등록 자체를 건너뛰므로, 주입받는 쪽에서 VContainer 해석 에러로 드러납니다).

### 1.3 사용

```csharp
public class GameOverScreen
{
    private readonly IAdService _ads;

    public GameOverScreen(IAdService ads) => _ads = ads;

    public async Awaitable ShowAsync()
    {
        if (!_ads.IsInitialized) await _ads.InitializeAsync();

        var result = await _ads.Rewarded.ShowAsync("game_over_revive");

        if (result.IsRewarded)
        {
            // result.Reward.Amount / result.Reward.Label
        }
    }
}
```

`InitializeAsync`는 재진입 안전합니다 — 진행 중에 다시 호출하면 새 초기화를 시작하지 않고
같은 결과에 편승합니다. 이미 초기화됐으면 즉시 `true`를 반환합니다.

`Interstitial`/`Rewarded`/`Banner`는 초기화가 성공하기 전에는 `null`입니다. 게임 코드는
반드시 `InitializeAsync`를 먼저 기다려야 합니다.

---

## 2. `AdShowOutcome`

`ShowAsync`는 `Awaitable<AdShowResult>`를 반환합니다. `AdShowResult.Outcome`은 다음 6가지 중 하나입니다.

| 값 | 의미 | 발생 시점 |
| --- | --- | --- |
| `Shown` | 전면 광고가 정상적으로 노출되고 닫혔다 | `FullScreenAdUnit`(전면)이 보상 없이 닫혔을 때 |
| `Rewarded` | 보상형 광고를 끝까지 보고 보상이 확정됐다 | `FullScreenAdUnit`(보상)이 닫혔고 그 전에 Rewarded 콜백이 왔을 때 |
| `Dismissed` | 보상형 광고를 보상 없이 중간에 닫았다 | `FullScreenAdUnit`(보상)이 닫혔지만 Rewarded 콜백이 없었을 때 |
| `NotReady` | 아직 로드되지 않았다 | `ShowAsync` 호출 시 어댑터가 준비되지 않음(내부적으로 `Load()`를 다시 걸어 다음 기회를 준비) |
| `Failed` | 표시 중 실패했거나 중복 호출됐다 | SDK의 표시 실패 콜백, 이미 표시 중인 상태에서 재호출, 서비스가 `Dispose`된 뒤 대기 중이던 호출 |
| `Blocked` | `AdsRemoved` 등 정책에 의해 차단됐다 | 전면 광고에서 `AdsRemoved == true`일 때 — 로드조차 시도하지 않고 즉시 반환 |

- **`IsRewarded`** — `Outcome == Rewarded`. "보상을 줘도 되는가"만 볼 때 씁니다.
- **`WasShown`** — `Shown` / `Rewarded` / `Dismissed` 셋 다 포함합니다. "실제로 화면에 광고가
  떴는가"를 볼 때 씁니다(예: 하루 노출 횟수 카운트). `NotReady`/`Failed`/`Blocked`는 포함되지 않습니다.

`Failed`일 때만 `Reward`가 아니라 `Error`(`AdError` — `Code`/`Message`)가 유효합니다.
`Rewarded`일 때만 `Reward`(`AdReward` — `Label`/`Amount`)가 유효합니다.

보상은 **래치됩니다.** SDK마다 "보상 콜백 → 닫힘" 순서가 다르고(AdMob은 보상 먼저, 일부
미디에이션 네트워크는 닫힘이 먼저 오는 사례가 보고됨) 순서를 보장하지 않으므로, `FullScreenAdUnit`은
보상 콜백을 즉시 완료시키지 않고 보관해 두었다가 닫힘에서 유예 프레임(`Reward Grace Frames`,
기본 1프레임)만큼 기다린 뒤 최종 `Outcome`을 확정합니다. 닫힘이 보상보다 먼저 와도 유예
프레임 안에 보상이 도착하면 `Rewarded`로 확정됩니다.

---

## 3. `AdsRemoved`의 포맷별 동작

```csharp
_ads.AdsRemoved = true;   // 인앱 구매 완료 시 게임 코드가 직접 설정
```

| 포맷 | `AdsRemoved == true`일 때 |
| --- | --- |
| 전면(Interstitial) | `ShowAsync`가 즉시 `Blocked`를 반환한다. 로드도 시도하지 않는다 |
| 배너(Banner) | `Show()`를 불러도 어댑터를 만들지 않는다. 이미 떠 있었다면 즉시 내려가고 `HeightChanged(0)`이 발화한다 |
| 보상(Rewarded) | **차단하지 않는다.** 계속 정상 로드·표시된다 — 유저가 자발적으로 보는 보상형 광고까지 막을 이유가 없기 때문 |

`AdsRemoved`는 세터입니다. **IAP 자체는 이 서비스의 범위 밖입니다** — 구매 검증·복원·상점 연동은
게임 코드(또는 별도 IAP 서비스)가 담당하고, 구매가 확인되면 `AdService.AdsRemoved = true`를
호출해 값만 밀어 넣습니다. 값은 `IAdRemovalStorage`(기본 `PlayerPrefsAdRemovalStorage`)로
영속화되어 다음 실행에 자동 복원됩니다.

`AdsRemovedChanged` 이벤트로 값이 바뀔 때마다 알림을 받을 수 있습니다(예: 배너 자리를 UI
레이아웃에서 접기/펼치기).

---

## 4. 수익 추적

### 4.1 `Paid` 구독

```csharp
_ads.Paid += impression =>
{
    Debug.Log($"{impression.AdPlatform}/{impression.NetworkName} " +
              $"{impression.Format} {impression.Revenue} {impression.Currency}");
};
```

`AdImpression`의 각 필드는 Firebase Analytics `ad_impression` 이벤트 파라미터에 다음과 같이 매핑됩니다.

| `AdImpression` 필드 | `ad_impression` 파라미터 | 비고 |
| --- | --- | --- |
| `AdPlatform` | `ad_platform` | `"AdMob"` / `"LevelPlay"` / `"AppLovin"` / `"Dummy"` |
| `NetworkName` | `ad_source` | 실제로 지면을 채운 네트워크(미디에이션 낙찰사) |
| `AdUnitId` | `ad_unit_name` | |
| `Revenue` | `value` | |
| `Currency` | `currency` | |
| `Format` | `ad_format` | `Banner`/`Interstitial`/`Rewarded` |
| `Placement` | (게임이 직접 매핑, 보통 커스텀 파라미터) | 아래 4.3 참고 |

> **`Currency`를 무시하고 USD로 가정하면 AdMob에서 틀립니다.** AdMob(GMA)의 임프레션 수익은
> 항상 USD로 온다고 보장되지 않습니다. `Revenue`는 반드시 `Currency`와 함께 저장·집계해야 하며,
> 여러 통화가 섞인 채로 `Revenue`만 합산하면 매출 집계가 틀어집니다.

### 4.2 임프레션이 합류하는 경로

`AdService.Paid`는 두 경로가 합쳐진 하나의 이벤트입니다.

1. **어댑터별 `Paid`** — `IFullScreenAdapter`/`IBannerAdapter` 각각에 달린 임프레션 이벤트.
   AdMob, AppLovin MAX처럼 광고 객체 단위로 수익 콜백이 오는 SDK가 이 경로를 씁니다.
2. **provider 전역 `IAdProvider.ImpressionPaid`** — LevelPlay처럼 임프레션 데이터가 광고 객체가
   아니라 **SDK 전역 이벤트 하나**로 오는 SDK를 위한 경로입니다. 어댑터별 `Paid`만 있으면 특히
   **배너 자동 갱신 임프레션**이 어떤 어댑터에도 매칭되지 않아 조용히 누락됩니다.

`AdService.BuildAdUnits()`가 두 경로를 모두 `OnPaid`로 구독해 하나의 공개 `Paid`로 합류시킵니다.
**Dummy provider는 `ImpressionPaid`를 절대 발화하지 않습니다** — `DummyAdProvider`의 더미
임프레션은 전부 어댑터별 `Paid`로만 옵니다(no-op 이벤트로 인터페이스 계약만 지킵니다). 만약
Dummy provider가 `ImpressionPaid`도 함께 발화했다면, 어댑터 경로와 provider 경로 양쪽에서
같은 임프레션이 올라와 `Paid`가 두 번 발화되고 수익이 이중 집계됩니다. 실제 SDK 어댑터를
작성할 때는 **한 임프레션이 두 경로 중 정확히 하나로만 나가도록** 해야 합니다(AdMob/MAX는
어댑터 경로, LevelPlay는 provider 경로).

### 4.3 `Placement`는 정책 계층이 채운다

```csharp
public string Placement { get; }  // 게임이 ShowAsync에 넘긴 배치명
```

`AdImpression.Placement`는 **어댑터가 채우지 않습니다.** 어댑터는 `ShowAsync(placement)`에
넘어온 배치명을 알 방법이 없습니다 — 어댑터가 아는 것은 SDK 콜백이 준 데이터뿐이고, 배치명은
게임 코드가 `ShowAsync` 호출 시점에만 알려주는 값이기 때문입니다.

대신 `FullScreenAdUnit`이 `ShowAsync(placement)` 호출 시 `_activePlacement`에 배치명을
보관해 두었다가, 어댑터의 `Paid`가 올라오면 `AdImpression.WithPlacement(_activePlacement)`로
**스탬프한 사본**을 재발행합니다(`AdImpression`은 `readonly struct`라 원본은 불변입니다).

**배너는 `Placement`가 항상 `null`입니다.** 배너에는 `ShowAsync(placement)` 같은 호출
배치명 자체가 없습니다(`BannerAdUnit.Show()`는 인자를 받지 않습니다) — 배너는 화면에 계속
띄워 두는 것이 전제이므로 "이 배치명으로 보여 달라"는 개념이 없습니다. 배너 임프레션의
`Placement`가 `null`인 것은 결함이 아니라 정상입니다.

---

## 5. 3사 어댑터를 추가하는 방법

정책 계층(`FullScreenAdUnit`/`BannerAdUnit`/`AdService`)은 건드리지 않습니다. 새 SDK는
`Providers/<SDK>/` 아래에 seam 구현만 추가합니다.

1. SDK를 `Packages/manifest.json` 또는 `.unitypackage`로 설치한다.
2. `Player Settings > Scripting Define Symbols`에 `FOUNDATIONDI_ADMOB` 같은 심볼을 정의한다
   (`AdProviderFactory.IsAvailable`이 이 심볼로 SDK 존재 여부를 판단합니다).
3. `Providers/AdMob/`(예시) 아래에 `IAdProvider`/`IFullScreenAdapter`/`IBannerAdapter`를
   구현하는 `AdMobProvider`/`AdMobFullScreenAdapter`/`AdMobBannerAdapter`를 작성한다.
   구현 시 `docs/superpowers/specs/2026-08-20-adservice-design.md`의 **"3사 매핑표"**(6절)를
   대조하며 작성하고, 실제 SDK의 필드명이 표와 어긋나면 표를 갱신한다.
4. `AdProviderFactory.Create`(내부적으로 `Build`)의 `switch`에 새 `case`를 추가한다.
5. 필요하면 `Consent/`에 provider별 동의 구현(`UmpAdConsent` 등)을 추가해 `IAdProvider.Consent`로 노출한다.

`FullScreenAdUnit`을 수정해야만 어댑터가 붙는다면 seam 설계가 잘못됐다는 신호입니다 — 멈추고
재검토합니다.

---

## 6. 구조

```
AdService/
├── IAdService.cs                 공개 API
├── AdService.cs                  조립 + 초기화 재진입 가드 + 임프레션 합류
├── AdTypes.cs                    AdFormat/AdShowOutcome/AdShowResult/AdImpression/AdRetryPolicy 등 값 타입
├── AdUnitId.cs                   Android/iOS 광고 단위 ID 쌍(인스펙터 직렬화)
├── AdServiceRegistration.cs      builder.RegisterAdService(settings) 확장 메서드
├── Ads/
│   ├── FullScreenAdUnit.cs       전면·보상 정책 계층 — 재시도, 보상 래치, 자동 재로드
│   ├── BannerAdUnit.cs           배너 정책 계층 — 재시도 없음(SDK가 자체 갱신)
│   ├── IFullScreenAd.cs          IInterstitialAd / IRewardedAd
│   └── IBannerAd.cs
├── Providers/
│   ├── IAdProvider.cs            SDK seam. BannerOptions/AdProviderContext도 여기 있다
│   ├── IFullScreenAdapter.cs / IBannerAdapter.cs   광고 단위 하나를 나타내는 어댑터 seam
│   ├── IAdProviderFactory.cs / AdProviderFactory.cs  심볼 기반 provider 선택 + Dummy 폴백
│   └── Dummy/                    SDK 없이 흐름을 검증하는 provider 구현
├── Dispatch/
│   ├── IAdDispatcher.cs          메인스레드 마샬링 + 지연 + 프레임 대기 seam
│   ├── UnityAdDispatcher.cs      실제 구현. `HideAndDontSave` GameObject를 코드로 만들어 펌프한다(프리팹 아님)
│   └── AdServiceRunner.cs        Update에서 큐를 퍼내는 MonoBehaviour(직접 배치하지 않는다)
├── Consent/                      IAdConsent seam + NoopAdConsent(Dummy용)
├── Storage/                      IAdRemovalStorage seam + PlayerPrefsAdRemovalStorage
└── Settings/
    ├── AdServiceSettings.cs      ScriptableObject. 인스펙터에서 편집하고 ToOptions()로 값 타입 변환
    └── AdProviderType.cs
```

- **`BannerOptions`는 `Settings/`가 아니라 `Providers/IAdProvider.cs`에 있습니다.** provider가
  배너를 만들 때(`CreateBanner(adUnitId, options)`) 필요로 하는 값이라 provider seam과 같은
  파일에 둔 것이고, `AdServiceSettings.ToOptions()`가 인스펙터 값으로 조립해 넘깁니다.
- `AdService`는 `AdServiceSettings`(ScriptableObject)를 직접 참조하지 않고 `AdServiceOptions`
  (`readonly struct`)를 받습니다. EditMode 테스트가 SO 없이도 서비스를 조립할 수 있게 하기 위함입니다.
- `UnityAdDispatcher`는 `[AdService] Runner`라는 이름의 `GameObject`를 `HideFlags.HideAndDontSave`로
  만들어 그 위에 `AdServiceRunner`를 붙입니다. 하이어라키에 보이지 않는 것이 정상 동작입니다.

---

## 7. 알려진 범위 밖

- **AdMob/LevelPlay/AppLovin 실제 어댑터** — seam과 매핑표만 준비돼 있고, SDK 설치 후 별도
  계획으로 진행합니다.
- **IAP(인앱 구매)** — `AdsRemoved`는 세터만 제공합니다. 구매 검증·복원·상점 연동은 이 서비스의
  책임이 아닙니다.
- **전면 쿨다운 게이트**(마지막 노출 후 N초 재표시 금지 같은 정책) — 현재 구현되지 않았습니다.
- **AppOpen / MREC / Native 광고 포맷** — `AdFormat`은 `Banner`/`Interstitial`/`Rewarded` 셋뿐입니다.
- **리모트 컨피그 연동**(광고 단위 ID·재시도 정책을 서버에서 갱신) — `AdServiceSettings`는
  에디터에서 편집하는 정적 값입니다.
- **`AdService`의 스레드 안전성** — `IAdDispatcher`로 콜백을 메인 스레드로 마샬링하지만, `AdService`
  자신의 상태(`_initializing`, `_adsRemoved` 등)는 메인 스레드 단독 접근을 전제합니다.
