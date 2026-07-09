# FoundationDI

![Unity](https://img.shields.io/badge/Unity-6000.3%2B-black?logo=unity)
![Version](https://img.shields.io/badge/version-0.1.12-blue)
![License](https://img.shields.io/badge/license-MIT-green)
![Author](https://img.shields.io/badge/author-DarkNaku-orange)

DI(의존성 주입) 기반 Unity 게임 개발 파운데이션 패키지입니다. [VContainer](https://github.com/hadashiA/VContainer)를 코어로 MessagePipe·R3·UniTask·Addressables를 조합한 공통 서비스 계층(메시징·리소스·UI·풀·사운드)을 제공합니다. 각 서비스는 인터페이스(`IXxxService`)로 등록되어 생성자 주입으로 소비되며, 외부 의존(Addressables 등)은 seam으로 분리되어 EditMode 단위 테스트가 가능합니다.

## 주요 기능

- **DI 컴포지션** — VContainer `LifetimeScope`에서 서비스를 인터페이스로 등록하고 생성자 주입으로 소비
- **메시징** — MessagePipe 래퍼로 동기/비동기(UniTask) pub-sub, `IPublisher`/`ISubscriber` 지연 해석·캐싱
- **리소스 로딩** — Addressables 추상화. 키 단위 캐싱 + 참조 카운팅으로 핸들 생명주기를 한 곳에서 관리
- **UI 시스템** — uGUI 기반 Page/Popup/Overlay 표시·전환, 모달 입력 차단, 트랜지션 추상화
- **오브젝트 풀 / 사운드** — 키 기반 GameObject 풀링, SFX/BGM 재생. 사운드는 카탈로그(문자열키)·비동기 프리로드·볼륨/활성화 영속화를 제공하고 클립 로딩을 `IResourceService`에 위임
- **씬 컴포넌트 DI** — 씬에 배치된 MonoBehaviour에 의존성을 주입하는 인프라(`InjectableBehaviour` + `InjectorService`). 버튼 클릭 사운드용 `SoundButton` 제공

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
| MessagePipe | `https://github.com/Cysharp/MessagePipe.git?path=src/MessagePipe.Unity/Assets/Plugins/MessagePipe` |
| MessagePipe.VContainer | `https://github.com/Cysharp/MessagePipe.git?path=src/MessagePipe.Unity/Assets/Plugins/MessagePipe.VContainer` |
| R3 | `https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity` |
| UniTask | `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask` |
| Addressables | Unity Package Manager (`com.unity.addressables`) |

## 빠른 시작

VContainer의 루트 `LifetimeScope`에서 서비스를 등록합니다. 등록 순서에 주의합니다 — UIManager는 프리팹 로드를 `IResourceService`에 위임하므로 `RegisterUIManager` **전에** `IResourceService`가 등록되어야 합니다.

```csharp
using UnityEngine;
using VContainer;
using VContainer.Unity;
using DarkNaku.FoundationDI;

public class RootLifetimeScope : LifetimeScope
{
    [SerializeField] private UIManagerSettings _uiSettings;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.Register<IResourceService, AddressableResourceService>(Lifetime.Singleton);
        builder.RegisterUIManager(_uiSettings);
        builder.RegisterInjector();   // 씬 배치 컴포넌트 주입(SoundButton 등)
        // 필요한 서비스를 같은 방식으로 추가 등록 (예: builder.RegisterSoundService(_soundCatalog))
    }
}
```

소비 측은 인터페이스를 생성자로 주입받습니다.

```csharp
public class TitleFlow
{
    private readonly IUIManager _ui;
    public TitleFlow(IUIManager ui) => _ui = ui;

    public void Open() => _ui.Page<TitlePresenter>();
}
```

## 구성 요소

각 구성 요소의 개요와, 상세 문서가 있는 경우 해당 README 링크입니다.

| 구성 요소 | 설명 | 상세 문서 |
| --- | --- | --- |
| **UIManager** | uGUI 기반 UI 표시/전환 시스템. Presenter 타입으로 Page(단일 교체)/Popup(LIFO·모달)/Overlay(상주) 모드를 고정하고, 자동-show 빌더 API·모달 입력 차단(`CanvasGroup.interactable`)·트랜지션 추상화를 제공. 프리팹 로딩은 `IResourceService`에 위임. | [README](Runtime/Managers/UIManager/README.md) |
| **ResourceService** | Addressables 추상화. `LoadAsync`/`Load`/`Release`/`Dispose` API로 키 단위 캐싱 + 참조 카운팅. 에셋 로딩이 필요한 모든 서비스의 위임 대상. | [README](Runtime/Services/ResourceService/README.md) |
| **MessageService** | MessagePipe 래퍼. `IObjectResolver`로 `IPublisher<T>`/`ISubscriber<T>`를 지연 해석해 캐싱하고, 동기/비동기(UniTask) pub-sub을 제공. | — |
| **PoolService** | 키 기반 GameObject 오브젝트 풀. Resources→Addressables fallback으로 프리팹을 로드하며, 풀 항목 생명주기 콜백과 지연 반환(`Release(delay)`)을 지원. | — |
| **SoundService** | SFX/BGM 재생. 사운드 카탈로그(문자열키→리소스키)·엄격 모드·비동기 프리로드(`PreloadAsync`)를 제공하고 클립 로딩을 `IResourceService`에 위임. 볼륨/활성화는 `PlayerPrefs`에 영속. 버튼용 `SoundButton` 포함. | [README](Runtime/Services/SoundService/README.md) |
| **InjectorService** | 씬에 배치된 MonoBehaviour에 의존성을 주입하는 인프라. 정적 요청 큐 + EntryPoint로 위치·계층·순서에 무관하게 주입. `InjectableBehaviour` 베이스 상속으로 사용. | [README](Runtime/Services/InjectorService/README.md) |

> 상세 문서가 아직 없는 구성 요소는 소스(`Runtime/Services/<이름>/`)와 인터페이스(`IXxxService`)를 참고하세요.

## 샘플

Package Manager의 **Samples** 탭에서 UIManager 예제를 import할 수 있습니다.

| 샘플 | 내용 |
| --- | --- |
| 01 Basic Usage | Page/Popup/Overlay 기본 표시와 모달 입력 차단 |
| 02 Page Navigation | 다단계 Page 전환, 파라미터 전달, 라이프사이클 콜백 |
| 03 Popup Modal | 모달 입력 차단과 결과 반환 |
| 04 Overlay | Above/Below 오버레이와 HUD 갱신 |

## 라이선스

MIT License
