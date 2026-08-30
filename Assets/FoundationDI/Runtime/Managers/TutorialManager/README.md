# TutorialManager

**조건 기반 튜토리얼 진행 엔진**입니다. "1레벨 시작할 때 조작 안내, 3레벨에서 새 시스템 안내, 5레벨에서 특정 아이템이 나타날 때 안내"처럼 **게임 조건에 따라 나뉘어 발동**하는 튜토리얼을 다룹니다.

- **조건이 지배한다** — 시퀀스들은 순서대로 줄서지 않습니다. 각자 자기 `StartTrigger`가 발동할 때 뜹니다.
- **게임 코드는 튜토리얼을 모른다** — 원래 발행하던 메시지를 그대로 발행하면 `MessageTrigger`가 알아서 반응합니다.
- **진행도는 시퀀스 ID로 저장한다** — 인덱스가 아니라서 시퀀스를 중간에 추가·삭제해도 기존 유저의 진행도가 어긋나지 않습니다.
- **진행 규칙은 순수 C#** — 씬·프리팹 없이 EditMode에서 전부 테스트됩니다. 씬 오써링은 얇은 MonoBehaviour 어댑터가 담당합니다.
- **연출은 열려 있다** — `ITutorialModule` 인터페이스 + 기본 구현 2종(`HighlightModule`, `HandPointerModule`)만 제공합니다.

설계 배경: [`docs/superpowers/specs/2026-08-24-tutorial-manager-design.md`](../../../../../docs/superpowers/specs/2026-08-24-tutorial-manager-design.md)

---

## 구조

```
TutorialManager (순수 C#)          조건부 후보 집합을 들고 하나씩 실행
 └ TutorialSequence               StartTrigger 하나로 발동하는 Step 묶음
    └ TutorialStep                Start/End 트리거 쌍 + 지연 + 모듈
       └ ITutorialModule          연출 (MonoBehaviour 구현체)

씬 오써링 (MonoBehaviour)
 TutorialSequenceBehaviour        자식 Step들을 모아 엔진에 등록
  └ TutorialStepBehaviour         인스펙터 데이터 → TutorialStep
 TutorialTarget                   런타임 생성 UI를 키로 노출
```

---

## 사용법

### 1) DI 등록 (VContainer)

```csharp
using VContainer;
using VContainer.Unity;
using DarkNaku.FoundationDI;

public class RootLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterMessageService();   // MessageTrigger가 쓴다
        builder.RegisterInjector();         // 씬 배치 컴포넌트 주입 경로
        builder.RegisterTutorialManager();  // 저장 키 "default"
    }
}
```

저장 키를 나누고 싶으면 `builder.RegisterTutorialManager("chapter1")`, 진행도를 서버와 동기화하려면 `ITutorialProgressStorage`를 구현해 `builder.RegisterTutorialManager(myStorage)`로 넘깁니다.

> ### ⚠️ `RegisterInjector`와 같은 스코프에 등록하세요
>
> `TutorialSequenceBehaviour`와 `TutorialTarget`은 씬에 배치되는 컴포넌트라 생성자 주입이 안 되고, [`InjectorService`](../../Services/InjectorService/README.md)를 통해 주입받습니다. **`InjectorService`는 정적 컨테이너 참조 하나를 공유하는 단일 컨테이너 모델**이라, 자식(씬) 스코프에 `RegisterTutorialManager`를 두고 루트에 `RegisterInjector`를 두면 루트 리졸버가 `ITutorialManager`를 해결하지 못합니다. 이 경우 주입이 **조용히 실패**해서 시퀀스가 영영 등록되지 않습니다(에러 로그도 없습니다).
>
> 씬 스코프에 두고 싶다면 그 씬 스코프에서 컴포넌트를 직접 등록하세요:
> ```csharp
> builder.RegisterTutorialManager();
> builder.RegisterComponentInHierarchy<TutorialSequenceBehaviour>();
> builder.RegisterComponentInHierarchy<TutorialTarget>();
> ```

### 2) 씬 오써링

```
Tutorial Level3          ← TutorialSequenceBehaviour
 ├ Step 1                ← TutorialStepBehaviour
 ├ Step 2                ← TutorialStepBehaviour
 └ Step 3                ← TutorialStepBehaviour
```

`TutorialSequenceBehaviour` 인스펙터:

| 필드 | 뜻 |
|---|---|
| `Sequence Id` | 진행도 저장 키. 비우면 GameObject 이름. **한 번 정하면 바꾸지 않습니다** |
| `Order` | 여러 시퀀스가 동시에 발동하면 낮은 쪽부터 실행 |
| `Resume Mode` | `RestartSequence`(기본) / `ResumeFromStep` |
| `Target Timeout` | 타깃을 기다리는 최대 시간(초). 0이면 무한 |
| `Start Trigger` | 이 시퀀스를 언제 띄울지 |

`TutorialStepBehaviour` 인스펙터에는 `Step Id` · `Start/End Delay` · `Target` · `Start/End Trigger` · `Modules`가 있습니다.

**Step은 자식 계층 순서대로** 실행됩니다. 손자는 모으지 않습니다.

### 3) 트리거

| 트리거 | 언제 쓰나 |
|---|---|
| `AutoTrigger` | 즉시 통과 (지연은 Step의 `Start/End Delay`가 담당) |
| `ManualTrigger` | 게임 코드가 `_tutorial.Complete("id")`를 부를 때 |
| `ButtonClickTrigger` | 타깃 버튼을 눌렀을 때 |
| `MessageTrigger<T>` | `IMessageService`로 메시지가 발행됐을 때 |

`MessageTrigger<T>`를 인스펙터에서 고르려면 **구체 서브클래스를 한 줄** 만듭니다.

```csharp
using System;
using UnityEngine;
using DarkNaku.FoundationDI;

[Serializable]
public sealed class LevelStartedTrigger : MessageTrigger<LevelStartedMessage>
{
    [SerializeField] private int _level = 3;

    protected override bool Match(LevelStartedMessage m) => m.Level == _level;
}
```

이러면 `[SerializeReference]` 드롭다운에 자동으로 뜹니다. 게임 코드는 원래대로 발행만 합니다.

```csharp
_message.Publish(new LevelStartedMessage { Level = 3 });
```

`Match`를 오버라이드하지 않으면 그 타입의 모든 메시지에 발동합니다. 한 번 발동한 뒤에는 `Disarm`까지 다시 발동하지 않습니다.

**Collision / Distance 트리거는 제공하지 않습니다.** 프레임 폴링과 물리 셋업 가정을 엔진에 끌어들이는 데다 게임마다 달라지는 영역이라, 필요하면 `ITutorialTrigger`를 직접 구현하세요.

```csharp
[Serializable]
public sealed class MyTrigger : ITutorialTrigger
{
    public void Arm(TutorialTriggerContext context, Action onFired) { /* 구독 */ }
    public void Disarm() { /* 해제 */ }
}
```

### 4) 타깃 — 씬 오브젝트와 런타임 UI

`TutorialTargetRef`는 둘 중 하나로 채웁니다.

- **씬에 상주하는 오브젝트**: 인스펙터에서 `Direct`에 드래그.
- **런타임에 생성되는 UI**(UINavigator가 띄우는 View 내부 요소): `Key`에 문자열을 적고, 그 UI 프리팹의 오브젝트에 **`TutorialTarget` 컴포넌트**를 붙여 같은 키를 지정.

`TutorialTarget`은 `OnEnable`에 등록하고 `OnDisable`에 해제합니다. UINavigator는 이 컴포넌트의 존재를 모르고, 튜토리얼도 UINavigator에 의존하지 않습니다.

여기서 두 가지가 공짜로 따라옵니다.

- **타깃이 아직 없으면 Step이 기다립니다.** "팝업이 열리면 그때 하이라이트하라"를 별도 기능 없이 얻습니다.
- **팝업을 닫았다 다시 열어도 이어집니다.** 타깃이 사라지면 모듈은 숨고 `ButtonClickTrigger`는 해제되며, 다시 나타나면 둘 다 복귀합니다.

같은 키가 여러 번 등록되면(풀에서 나온 View + 새 View) **마지막 등록이 이깁니다.**

### 5) 연출 모듈 만들기

`TutorialModuleBehaviour`를 상속합니다. 프레임 추적은 기반 클래스의 `LateUpdate`가 처리하고, 여러분은 좌표만 받습니다.

```csharp
public sealed class MyArrowModule : TutorialModuleBehaviour
{
    [SerializeField] private RectTransform _arrow;

    protected override void OnTrack(Rect screenRect)
    {
        _arrow.position = new Vector3(screenRect.center.x, screenRect.yMax + 40f, 0f);
    }

    protected override void OnTargetLost() => _arrow.gameObject.SetActive(false);
}
```

**모듈은 타깃을 자식으로 삼지 않습니다.** 스크린 rect만 읽으므로 타깃이 `UIRoot`(씬 수명 캔버스) 안에 있든, 씬 캔버스에 있든, 3D 월드에 있든 똑같이 동작합니다. `TutorialScreenRect`가 `RectTransform`이면 코너 4점을, 일반 `Transform`이면 `Renderer`/`Collider` 바운즈를 스크린으로 투영합니다.

### 정렬 — 기존 UI를 가리거나 가려지지 않나?

**가려지지 않습니다.** UINavigator의 `UIRoot`는 `sortingOrder = 0`짜리 캔버스 **하나**이고, Page/Popup/Overlay 레이어들은 자기 `Canvas` 없이 그 안에서 하이어라키 순서로만 정렬됩니다. 튜토리얼 모듈은 자기 root `Canvas`를 `ScreenSpaceOverlay` + 높은 `sortingOrder`로 들고 있어서 팝업이든 오버레이든 전부 위에 그려집니다(`HighlightModule` 32000, `HandPointerModule` 32001). `ScreenSpaceOverlay` 캔버스는 하이어라키가 아니라 `sortingOrder`로 전역 정렬되기 때문입니다.

`UINavigatorSettings.RootPrefab`으로 `UIRoot`를 `ScreenSpaceCamera`로 바꿔도 안전합니다 — Overlay 캔버스는 Camera 캔버스보다 항상 위에 그려집니다.

> `overrideSorting`은 **중첩 캔버스에서만** 의미가 있습니다. 모듈 프리팹을 루트에 두면 Unity가 이 값을 무시하고 `sortingOrder` 하나로 정렬합니다. 모듈이 코드에서 켜두는 건 프리팹을 다른 `Canvas` 밑에 넣었을 때를 대비한 것입니다.

**입력 차단은 `GraphicRaycaster`가 있어야 동작합니다.** 딤 패널의 `raycastTarget`만으로는 아무것도 막지 못합니다 — 그 캔버스에 레이캐스터가 있어야 그래픽이 레이캐스트 대상이 됩니다. `HighlightModule`은 `[RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]`로 이를 강제하고, `Block Outside Click`을 끄면 레이캐스터도 함께 꺼집니다.

반대로 **하이라이트 구멍과 손가락은 클릭을 먹지 않습니다.** 둘 다 `Awake`에서 자기 그래픽의 `raycastTarget`을 끕니다 — 안 그러면 "이 버튼을 누르세요"라고 가리켜 놓고 정작 그 버튼을 막게 됩니다.

기본 제공:

- **`HighlightModule`** — 타깃만 남기고 화면을 덮고 바깥 클릭을 막습니다. 셰이더 없이 구멍 이미지 1장 + 딤 패널 4장(위/아래/왼/오른 순서로 인스펙터에 넣습니다).
- **`HandPointerModule`** — 타깃 위에 손가락 + 탭 루프 애니메이션(`AnimationCurve`). 레이캐스터를 붙이지 않습니다.

### 6) 게임 코드가 쓰는 표면

```csharp
public sealed class SettingsPopupPresenter : UIPopupPresenter<SettingsView>
{
    private readonly ITutorialManager _tutorial;

    public SettingsPopupPresenter(ITutorialManager tutorial) => _tutorial = tutorial;

    public void OnSkipTutorialButton() => _tutorial.SkipAll();

    public bool CanOpenShop() => _tutorial.IsCompleted("intro");
}
```

| 멤버 | 뜻 |
|---|---|
| `IsCompleted(id)` | 그 시퀀스를 봤나 (`SkipAll` 이후면 항상 참) |
| `IsRunning` | 지금 튜토리얼이 떠 있나 |
| `Skip()` | 실행 중인 시퀀스만 완료 처리 |
| `SkipAll()` | 전역 스킵 (씬에 없는 시퀀스까지 덮음) |
| `Complete(stepId)` | `ManualTrigger` 발동 |
| `SequenceStarted` / `SequenceCompleted` | 시퀀스 ID를 실어 발행 |

`Play()`가 없습니다. 재생은 `TutorialSequenceBehaviour`가 씬에서 스스로 하고, 게임 코드는 묻거나 스킵시키는 쪽만 씁니다.

---

## 재개 정책

시퀀스 중간에 앱이 죽으면 상태가 `Running`으로 남습니다. 기본은 **시퀀스 처음부터 재시작**(`ResumeMode.RestartSequence`)입니다.

Step 중간 재개는 앞선 Step들의 부작용이 이미 반영돼 있다는 걸 전제하는데 그걸 보장할 방법이 없습니다. 튜토리얼 시퀀스는 짧다는 전제에서 처음부터 다시 하는 비용이 잘못 재개하는 위험보다 쌉니다. 긴 시퀀스는 `ResumeFromStep`으로 옵트인하세요.

**재개도 `StartTrigger`를 기다립니다.** 3레벨 튜토리얼 도중 종료한 유저가 5레벨에 들어갔을 때 엉뚱하게 튀어나오지 않게 하기 위해서입니다.

---

## 실패 모드

핵심 원칙은 **튜토리얼이 게임을 영구 정지시키지 않는다**입니다.

| 상황 | 동작 |
|---|---|
| 타깃이 영영 안 나타남 | `Target Timeout` 초과 시 시퀀스 중단, 상태를 `NotStarted`로 되돌려 다음 기회에 재시도 |
| 모듈이 예외를 던짐 | 모듈별로 격리 — 한 연출이 터져도 Step은 진행 |
| 트리거가 예외를 던짐 | 격리 + `Disarm`은 항상 실행 |
| 씬 언로드 | `Dispose()`가 진행을 취소하고 모든 트리거를 해제 |
| 중복 `Sequence Id` | 에러 로그 후 뒤엣것 무시 |
| `Complete(id)` 미매칭 | 경고 로그 후 무시 |

한 번에 **하나의 시퀀스만** 실행됩니다. 실행 중 다른 조건이 맞으면 `Order` 오름차순 대기열에 들어갑니다.

---

## 범위 밖

- **씬 경계를 가로지르는 시퀀스** — 한 시퀀스의 Step들은 한 씬 안에서 시작해 끝납니다.
- **Collision / Distance 트리거** — `ITutorialTrigger`를 직접 구현하세요.
- **Arrow / PopUp / QuestMarker / Text3D / VideoPanel 모듈** — `TutorialModuleBehaviour`를 상속해 만드세요.
- **분기 튜토리얼** — Step은 선형입니다. 분기가 필요하면 시퀀스를 나누고 각자의 `StartTrigger`로 표현하세요.
- **진행도 서버 동기화** — `ITutorialProgressStorage`를 구현해 붙이세요.
- **에디터 오써링 마법사** — 현재는 인스펙터 기본 드로어 + `[SerializeReference]` 드롭다운으로 씁니다.
