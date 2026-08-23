# FoundationDI

![Unity](https://img.shields.io/badge/Unity-6000.3%2B-black?logo=unity)
![Version](https://img.shields.io/badge/version-0.5.3-blue)
![License](https://img.shields.io/badge/license-MIT-green)
![Author](https://img.shields.io/badge/author-DarkNaku-orange)

> **0.4.0 BREAKING** — `UIServiceSettings.ReferenceResolution`이 제거되고 루트 캔버스 프리팹 참조(`RootPrefab`)로 대체되었습니다. 업그레이드 절차는 [UIService 마이그레이션](Runtime/Services/UIService/README.md#마이그레이션-030--040)을 참고하세요. 조치하지 않으면 기준 해상도가 코드 기본값(1920x1080)으로 폴백합니다.

DI(의존성 주입) 기반 Unity 게임 개발 파운데이션 패키지입니다. [VContainer](https://github.com/hadashiA/VContainer)를 코어로 Addressables와 Unity `Awaitable`을 조합한 공통 서비스 계층(메시징·리소스·UI·풀·사운드·햅틱·초기화·광고·분석·인앱결제)을 제공합니다. 각 서비스는 인터페이스(`IXxxService`)로 등록되어 생성자 주입으로 소비되며, 외부 의존(Addressables 등)은 seam으로 분리되어 EditMode 단위 테스트가 가능합니다.

## 주요 기능

- **DI 컴포지션** — VContainer `LifetimeScope`에서 서비스를 인터페이스로 등록하고 생성자 주입으로 소비
- **메시징** — 외부 라이브러리 없는 타입 기반 pub-sub. `IDisposable` 구독 토큰(R3를 쓴다면 `AddTo`와도 호환), 발행 스냅샷, 핸들러 예외 격리
- **리소스 로딩** — Addressables 추상화. 키 단위 캐싱 + 참조 카운팅으로 핸들 생명주기를 한 곳에서 관리
- **UI 시스템** — 게임 전역 단일 상주 Canvas(`DontDestroyOnLoad`, 렌더 모드/CanvasScaler는 루트 프리팹이 결정·미지정 시 ScreenSpaceOverlay 폴백) 위에 Page/Popup/Overlay 표시·전환, 모달 입력 차단, `Awaitable` 트랜지션 추상화. Page/Popup에 오버레이를 함께 노출하는 `WithOverlay`(동시 전환·`persistent` 연속 유지 옵션) 제공
- **오브젝트 풀 / 사운드** — 키 기반 GameObject 풀링과 태그 기반 오디오. 사운드는 SFX/음악/플레이리스트/다이내믹 뮤직 빌더, AudioSource 풀링, 페이드·루프·콜백, AudioMixer Output 볼륨 영속화, 3D 오클루전, 전용 에디터 창(Audio Creator/Collection/Output Manager)을 제공
- **햅틱** — iOS/Android 촉각 피드백. 시맨틱 프리셋(`Impact`/`Notification`/`Selection`, 옵트인 쿨다운) + `AnimationCurve` 커브·커스텀 패턴 재생(`Awaitable`, 단일 활성)과 플랫폼 케이퍼빌리티 폴백. 에디터/데스크톱은 Noop
- **부트스트랩 초기화** — 초기화 단위를 SO(`InitializeItem`)로 정의하고 카탈로그 순서대로 순차 실행. 세션 내 중복 실행 방지, 실패 지점부터 재개
- **수익화 3종** — 광고(`IAdService`)·분석(`IAnalyticsService`)·인앱결제(`IIapService`). 세 서비스 모두 SDK를 옵셔널 어셈블리로 격리해 **코어는 어떤 3사 SDK도 참조하지 않으며**, SDK가 없으면 Dummy/Debug provider로 에디터에서 전체 플로우가 돌아갑니다
- **씬 컴포넌트 DI** — 씬에 배치된 MonoBehaviour에 의존성을 주입하는 인프라(`InjectableBehaviour` + `InjectorService`). `SoundButton`/`MusicZone`/`OutputVolumeSlider` 등이 이를 사용

## 설치 방법

Unity Package Manager의 **Add package from git URL**로 설치합니다.

```
https://github.com/DarkNaku/FoundationDI.git?path=/Assets/FoundationDI
```

### 의존성

FoundationDI는 다음 패키지를 전제로 합니다. 먼저 설치되어 있어야 합니다.

| 패키지 | Git URL |
| --- | --- |
| VContainer | `https://github.com/hadashiA/VContainer.git?path=VContainer/Assets/VContainer` |
| Addressables | Unity Package Manager (`com.unity.addressables`) |

비동기는 Unity 6의 `Awaitable`만 씁니다 — R3나 UniTask를 설치할 필요가 없습니다.

### 선택 의존성 (SDK)

광고·분석·인앱결제 어댑터는 스크립팅 심볼이 정의된 **별도 어셈블리**에만 들어 있습니다. 코어는 이 SDK들을 참조하지 않으므로 쓰지 않는 프로젝트는 설치할 필요가 없습니다.

| 서비스 | 패키지 | 스크립팅 심볼 |
| --- | --- | --- |
| IAPService | `com.unity.purchasing` (5.4.2+) | `FOUNDATIONDI_UNITYIAP` |
| AnalyticsService | Firebase Analytics SDK | `FOUNDATIONDI_FIREBASE` |
| AdService | AppLovin MAX SDK | `FOUNDATIONDI_APPLOVIN` |

**심볼은 직접 정의하지 않아도 됩니다.** SDK를 임포트하면 자동으로 켜지고, 지우면 자동으로 꺼집니다(Android/iOS/Standalone 동시 적용). 감지는 SDK 대표 타입의 존재 여부로 하므로 SDK 폴더를 옮겨도, UPM 패키지로 설치해도 동작합니다.

- 끄고 싶으면 `Tools > FoundationDI > SDK Defines > Auto Manage` 체크를 해제합니다.
- 수동으로 한 번만 맞추려면 `Sync Now`를 실행합니다.
- 관리 대상 심볼만 건드리므로 프로젝트의 다른 심볼은 그대로 보존됩니다.

심볼이 없으면 각각 Dummy/Debug provider로 폴백해 에디터에서 전체 플로우를 그대로 확인할 수 있습니다.

## 빠른 시작

VContainer의 루트 `LifetimeScope`에서 서비스를 등록합니다. 등록 순서에 주의합니다 — UIService는 프리팹 로드를 `IResourceService`에 위임하므로 `RegisterUIService` **전에** `IResourceService`가 등록되어야 합니다.

```csharp
using UnityEngine;
using VContainer;
using VContainer.Unity;
using DarkNaku.FoundationDI;

public class RootLifetimeScope : LifetimeScope
{
    [SerializeField] private UIServiceSettings _uiSettings;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.Register<IResourceProvider, ResourcesProvider>(Lifetime.Singleton);
        builder.Register<IResourceService, ResourceService>(Lifetime.Singleton);
        builder.RegisterUIService(_uiSettings);
        builder.RegisterInjector();   // 씬 배치 컴포넌트 주입(SoundButton 등)
        builder.RegisterInitializeService();

        // 필요한 서비스를 같은 방식으로 추가 등록한다.
        // builder.RegisterMessageService();
        // builder.RegisterSoundService(_soundSettings);
        // builder.RegisterHapticService();
        // builder.RegisterAdService(_adSettings);
        // builder.RegisterAnalyticsService(_analyticsSettings);
        // builder.RegisterIapService(_iapSettings);
    }
}
```

소비 측은 인터페이스를 생성자로 주입받습니다.

```csharp
public class TitleFlow
{
    private readonly IUIService _ui;
    public TitleFlow(IUIService ui) => _ui = ui;

    public void Open() => _ui.Page<TitlePresenter>();
}
```

## 구성 요소

각 구성 요소의 개요와, 상세 문서가 있는 경우 해당 README 링크입니다.

| 구성 요소 | 설명 | 상세 문서 |
| --- | --- | --- |
| **UIService** | uGUI 기반 UI 표시/전환 시스템. Presenter 타입으로 Page(단일 교체)/Popup(LIFO·모달)/Overlay(상주 Above/Below) 모드를 고정. **게임 전역 단일 상주 Canvas**(`DontDestroyOnLoad`, 렌더 모드/CanvasScaler는 `UIServiceSettings.RootPrefab`이 결정·미지정 시 ScreenSpaceOverlay/1920x1080 폴백, 씬 전환 시 자식만 clear), Presenter 매 표시 재생성 + **View 풀링**, `Awaitable` 트랜지션, 모달 입력 차단(`CanvasGroup.interactable`). Page/Popup에 `WithOverlay`(오버레이 동시 노출·`persistent` 연속 유지)와 자동-show 빌더 API 제공. 프리팹 로딩은 `IResourceService`(Resources/Addressables)에 위임. | [README](Runtime/Services/UIService/README.md) |
| **ResourceService** | Addressables 추상화. `LoadAsync`/`Load`/`Release`/`Dispose` API로 키 단위 캐싱 + 참조 카운팅. 에셋 로딩이 필요한 모든 서비스의 위임 대상. | [README](Runtime/Services/ResourceService/README.md) |
| **MessageService** | 외부 라이브러리 없는 인-메모리 pub-sub. 타입을 채널로 삼아 `Publish<T>`/`Subscribe<T>`만 제공하며, 구독 토큰은 `IDisposable`(R3를 쓴다면 `AddTo`로 MonoBehaviour 수명에 바인딩 가능). 발행은 스냅샷으로 완주하고 핸들러 예외는 격리한다. 메인 스레드 전제. | [README](Runtime/Services/MessageService/README.md) |
| **PoolService** | 키 기반 GameObject 오브젝트 풀. Resources→Addressables fallback으로 프리팹을 로드하며, 풀 항목 생명주기 콜백과 지연 반환(`Release(delay)`)을 지원. | — |
| **SoundService** | 태그 기반 오디오 시스템. `Sound`/`Music`/`Playlist`/`DynamicMusic` 빌더, AudioSource 풀링, 페이드 인·아웃, 루프/트랙 콜백, id 기반 일괄 제어, AudioMixer Output 볼륨(`PlayerPrefs` 영속, `ISoundVolumeStorage`로 교체 가능), 레이캐스트 3D 오클루전. Audio Creator/Collection/Output Manager/Settings 에디터 창과 `SoundButton`/`MusicZone`/`OutputVolumeSlider`/`VolumeSlider` 컴포넌트 포함. | [README](Runtime/Services/SoundService/README.md) |
| **HapticService** | iOS/Android 통합 햅틱. 시맨틱 프리셋(`Impact`/`Notification`/`Selection`, 옵트인 쿨다운) + `AnimationCurve` 커브·커스텀 패턴 재생(`Play`, `Awaitable`, 단일 활성)·케이퍼빌리티 폴백. 에디터/데스크톱은 Noop, `Enabled`는 `PlayerPrefs`에 영속. | [README](Runtime/Services/HapticService/README.md) |
| **InitializeService** | 게임 부트스트랩 순차 초기화. 초기화 단위를 `InitializeItem`(SO)로 정의하고 `InitializeCatalog`에 묶어 리스트 순서대로 직렬 실행. 세션 내 중복 실행 방지, 예외는 즉시 전파하고 실패 지점부터 재개. | [README](Runtime/Services/InitializeService/README.md) |
| **AdService** | 광고 네트워크 중립 서비스. `IAdService` 하나로 전면·보상·배너를 다루고, 정책 계층(재시도 백오프·보상 래치·자동 재로드·전면 쿨다운·광고제거 게이트)과 SDK 어댑터를 분리. `ShowAsync`는 `Awaitable<AdShowResult>`. 광고제거는 포맷별로 다르게 게이트한다(전면·배너 차단, 보상형은 계속 동작). | [README](Runtime/Services/AdService/README.md) |
| **AnalyticsService** | 다중 분석/MMP 팬아웃. 게임이 `IAnalyticsService` API를 한 번 호출하면 등록된 모든 provider로 브로드캐스트된다. 라우팅 규칙 없음(무엇을 무시할지는 어댑터가 결정), 초기화 전 이벤트는 순서 보존 버퍼링·유저 상태는 latest-wins, provider 예외는 격리. | [README](Runtime/Services/AnalyticsService/README.md) |
| **IAPService** | 모바일 인앱 구매(Google Play/App Store). 소모성·비소모성을 `IIapService` 하나로 구매·복원. **지급을 저장한 뒤에만 확정**하는 규율을 `IIapFulfillment` seam 하나로 접어, 신규 구매·재전달·복원이 전부 같은 메서드로 들어온다. 로컬 영수증 검증(Google Play)·상품 상수 생성기 포함. | [README](Runtime/Services/IAPService/README.md) |
| **InjectorService** | 씬에 배치된 MonoBehaviour에 의존성을 주입하는 인프라. 정적 요청 큐 + EntryPoint로 위치·계층·순서에 무관하게 주입. `InjectableBehaviour` 베이스 상속으로 사용. | [README](Runtime/Services/InjectorService/README.md) |

> 상세 문서가 아직 없는 구성 요소는 소스(`Runtime/Services/<이름>/`)와 인터페이스(`IXxxService`)를 참고하세요.

## 샘플

Package Manager의 **Samples** 탭에서 예제를 import할 수 있습니다.

| 샘플 | 내용 |
| --- | --- |
| 01 Basic Usage | Page/Popup/Overlay 기본 표시와 모달 입력 차단 |
| 02 Page Navigation | 다단계 Page 전환, 파라미터 전달, 라이프사이클 콜백 |
| 03 Popup Modal | 모달 입력 차단과 결과 반환 |
| 04 Overlay | Above/Below 오버레이와 HUD 갱신 |
| 05 Sound | SFX/Music/Playlist/DynamicMusic 재생, 페이드·볼륨 보간, 전체 제어 |

각 샘플은 자체 데이터를 들고 있어 import 후 별도 준비 없이 바로 실행됩니다.

## 라이선스

MIT License
