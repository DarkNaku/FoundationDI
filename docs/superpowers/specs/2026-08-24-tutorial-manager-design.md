# TutorialManager 설계 — 조건 기반 튜토리얼 진행 엔진

- 상태: 설계 확정
- 작성일: 2026-08-24
- 레퍼런스: NINESOFT Tutorial System v1.3.0 (`Assets/NINESOFT_ASSETS/TutorialSystem/`) — **참고만 하고 의존하지 않는다**

---

## 배경 / 목표

튜토리얼은 "레벨 1 시작 시 조작 안내, 레벨 3에서 새 시스템 안내, 레벨 5에서 특정 아이템이 등장할 때 안내"처럼 **게임 조건에 따라 나뉘어 발동**한다. 각 발동 단위는 짧고(3~5 Step), 한 씬 안에서 시작해 끝난다.

FoundationDI가 재사용 가치를 갖는 부분은 **연출이 아니라 진행 엔진**이다. 하이라이트·손가락·화살표 같은 연출은 게임 아트에 맞춰 매번 새로 만들게 되지만, "조건이 맞으면 시퀀스를 띄우고, Step을 순서대로 넘기고, 완료를 영속화해 다시 뜨지 않게 한다"는 규칙은 게임이 바뀌어도 같다.

목표:

- 게임 코드가 튜토리얼을 **모르는 채로** 있을 것 — 원래 발행하던 메시지를 그대로 발행하면 튜토리얼이 알아서 반응한다.
- 진행 규칙이 **EditMode에서 단위 테스트 가능**할 것 — 씬·프리팹·플레이 모드 없이.
- UIService가 런타임에 생성하는 UI도 하이라이트 타깃이 될 것.
- 튜토리얼이 게임을 **영구 정지시키지 않을 것**.

## 확정된 설계 결정

브레인스토밍에서 다음이 확정됐다.

1. **`Manager`다 (`Service` 아님).** 씬 수명이므로 `Runtime/Managers/`에 들어가고 씬 `LifetimeScope`에 등록한다. PoolManager와 같은 자리.
2. **연출은 인터페이스만 개방.** `ITutorialModule` seam + 기본 구현 2종(`HighlightModule`, `HandPointerModule`)만. 레퍼런스의 나머지 5종(Arrow / PopUp / QuestMarker / Text3D / VideoPanel)은 넣지 않는다.
3. **하이브리드 오써링.** 진행 규칙은 순수 C#, 씬 오써링은 얇은 MonoBehaviour 어댑터.
4. **기본 트리거 4종** — `Auto` / `Manual` / `ButtonClick` / `Message`. Collision·Distance는 제외한다(프레임 펌프와 물리 셋업을 엔진에 끌어들이고, 게임마다 어차피 다시 쓴다).
5. **모듈은 씬에 배치**하고, **타깃만 추상화**한다(`TutorialTargetRef`). 모듈은 타깃을 리페어런팅하지 않고 스크린 좌표만 추적한다.
6. **시퀀스는 순차 리스트가 아니라 조건부 후보 집합**이고, 진행도는 **인덱스가 아니라 시퀀스 ID**로 저장한다.
7. **시퀀스가 씬 경계를 가로지르는 경우는 범위 밖.**

## 레퍼런스에서 가져온 것과 버린 것

| 레퍼런스 | 이 설계 |
|---|---|
| `TutorialManager`(MonoBehaviour 싱글톤) → `Tutorial` → `TutorialStage` → `TutorialModule` 4계층 | `TutorialManager`(순수 C#) → `TutorialSequence` → `TutorialStep` → `ITutorialModule`. 계층은 유지, 상위 3개를 MonoBehaviour에서 뺀다 |
| `currentTutorialIndex` 하나로 전체 순차 진행 | 시퀀스마다 `StartTrigger`. 순서가 아니라 조건이 지배 |
| `PlayerPrefs`에 인덱스 저장 (`ns_savedTutorialIndex_{key}`) | 시퀀스 **ID** 단위 상태 저장. 시퀀스를 끼워넣어도 기존 유저 진행도가 안 어긋난다 |
| `SaveKey`를 씬마다 `OnValidate`에서 랜덤 생성 | 저장 키는 등록 시 1회 지정. 씬마다 진행도가 파편화되지 않는다 |
| `TriggerType` enum + 종류별 필드 5개 + `switch` 2블록 | `[SerializeReference] ITutorialTrigger`. 트리거 추가 시 엔진을 안 건드린다 |
| `Button.onClick.AddListener`만 하고 해제 안 함 | `Arm`/`Disarm`을 `finally`로 짝맞춤 |
| 재개 시 저장 인덱스까지 `OnStageStart`/`OnStageEnd`를 **전부 재invoke** | 기본은 시퀀스 처음부터 재시작. 부작용 재실행 없음 |
| `SkipAllTutorials()`가 씬에 있는 튜토리얼만 순회 | `AllSkipped` 플래그 하나 (씬과 무관하게 전역) |
| `TutorialManager.OnUpdate` 공개 델리게이트 (Distance 트리거용) | 없음. 프레임 추적은 모듈(MonoBehaviour)의 `LateUpdate`에서만 |
| `TutorialManager.Instance` 정적 싱글톤 | VContainer 생성자 주입 |

---

## 설계

### 위치 / 파일 구성

```
Assets/FoundationDI/Runtime/Managers/TutorialManager/
  ITutorialManager.cs              공개 계약 + TutorialState
  TutorialManager.cs               진행 엔진 (순수 C#)
  TutorialSequence.cs              Step 목록 + 시작 조건 + 재개 모드
  TutorialStep.cs                  트리거 쌍 + 지연 + 모듈 목록
  TutorialManagerRegistration.cs   builder.RegisterTutorialManager()
  README.md

  Targets/
    TutorialTargetRef.cs           직접 참조 | 키
    TutorialTargetHandle.cs        살아있는 타깃 핸들
    ITutorialTargetRegistry.cs
    TutorialTargetRegistry.cs
    TutorialTarget.cs              MonoBehaviour — 키 등록/해제
    TutorialScreenRect.cs          UI/3D 공통 스크린 rect 계산

  Triggers/
    ITutorialTrigger.cs            + TutorialTriggerContext
    AutoTrigger.cs
    ManualTrigger.cs
    ButtonClickTrigger.cs
    MessageTrigger.cs              MessageTrigger<T> 추상 기반

  Modules/
    ITutorialModule.cs
    TutorialModuleBehaviour.cs     MonoBehaviour 기반 클래스
    HighlightModule.cs
    HandPointerModule.cs

  Authoring/
    TutorialSequenceBehaviour.cs   InjectableBehaviour
    TutorialStepBehaviour.cs

  Storage/
    ITutorialProgressStorage.cs
    PlayerPrefsTutorialProgressStorage.cs
```

네임스페이스는 전부 `DarkNaku.FoundationDI`. asmdef는 기존 `FoundationDI` 그대로(새 어셈블리 없음).

### 1. 공개 계약

```csharp
public enum TutorialState { NotStarted, Running, Completed }

public interface ITutorialManager : IDisposable
{
    bool IsRunning { get; }

    bool IsCompleted(string sequenceId);

    void Register(TutorialSequence sequence);     // 오써링 어댑터가 호출
    void Unregister(string sequenceId);

    void Skip();                     // 현재 실행 중인 시퀀스만
    void SkipAll();                  // 전역 플래그
    void Complete(string stepId);    // ManualTrigger 발동

    event Action<string> SequenceStarted;
    event Action<string> SequenceCompleted;
}
```

`Play()`가 없다. 재생은 `TutorialSequenceBehaviour`가 씬에서 스스로 등록·arm 하고, 게임 코드는 "봤나?"를 묻거나 스킵시키는 쪽만 필요하다. `Register`/`Unregister`는 게임 코드가 아니라 오써링 어댑터가 쓰는 표면이다.

생성자 의존:

```csharp
public TutorialManager(
    IMessageService message,
    ITutorialTargetRegistry targets,
    ITutorialProgressStorage storage)
```

### 2. 진행 엔진

```csharp
public sealed class TutorialSequence
{
    public string Id { get; }
    public int Order { get; }                    // 동시 발동 시 낮은 쪽 먼저
    public ResumeMode ResumeMode { get; }
    public float TargetTimeout { get; }          // 0 = 무한
    public ITutorialTrigger StartTrigger { get; }
    public IReadOnlyList<TutorialStep> Steps { get; }
}

public enum ResumeMode { RestartSequence, ResumeFromStep }

public sealed class TutorialStep
{
    public string Id { get; }
    public float StartDelay { get; }
    public float EndDelay { get; }
    public ITutorialTrigger StartTrigger { get; }
    public ITutorialTrigger EndTrigger { get; }
    public IReadOnlyList<ITutorialModule> Modules { get; }
    public TutorialTargetRef Target { get; }     // 모듈이 가리킬 대상
}
```

`TutorialStep.Target`은 **모듈**이 가리킬 대상이다. `ButtonClickTrigger`가 들고 있는 `TutorialTargetRef`는 **누를 버튼**이라 별개다. 보통 같은 값이지만("이 버튼을 하이라이트하고, 이 버튼을 눌러야 넘어감") 다를 수 있어서 분리한다.

`TutorialManager`의 수명 주기:

```
Register(sequence)
  └ storage.AllSkipped 또는 state == Completed 면 버림
  └ 아니면 후보 집합에 넣고 StartTrigger.Arm

StartTrigger 발동
  └ IsRunning 이면 대기열(Order 오름차순)에 넣음
  └ 아니면 즉시 실행

시퀀스 실행
  └ state = Running 기록
  └ 시작 Step 인덱스 결정 (ResumeMode)
  └ 각 Step 순서대로:
       StartTrigger.Arm → 발동 대기 → Disarm(finally)
       StartDelay 대기
       타깃 해석 (ResolveAsync)
       모듈 ShowAsync (순차, 모듈별 try/catch)
       EndTrigger.Arm → 발동 대기 → Disarm(finally)
       EndDelay 대기
       모듈 HideAsync (순차, 모듈별 try/catch)
       stepIndex 기록
  └ state = Completed 기록 → SequenceCompleted 발행 → 대기열 다음 시퀀스
```

**한 번에 하나만 실행한다.** 튜토리얼 연출이 겹치면 화면이 엉킨다. UIService의 `OperationQueue`와 같은 판단이다.

### 3. 트리거

```csharp
public interface ITutorialTrigger
{
    void Arm(TutorialTriggerContext context, Action onFired);
    void Disarm();
}

public readonly struct TutorialTriggerContext
{
    public IMessageService Message { get; }
    public ITutorialTargetRegistry Targets { get; }
}
```

`Awaitable`이 아니라 arm/disarm 구독 모델인 이유:

- `IMessageService.Subscribe<T>`가 이미 `IDisposable`을 돌려주는 구독 모델이라 그대로 얹힌다.
- 트리거는 `[SerializeReference]`로 직렬화되는 객체라 **생성자 주입이 불가능**하다. 의존을 `Arm`의 `context`로 넘기면 해결된다.
- NSubstitute 검증이 단순한 호출 확인으로 끝난다. `Awaitable` 기반이면 EditMode에서 완료 소스를 직접 깨워야 해서 테스트가 무거워진다.

시퀀스 러너가 이걸 `await`로 잇는 어댑터(`AwaitableCompletionSource` 사용)는 엔진 내부 한 곳에만 둔다.

| 트리거 | 인스펙터 필드 | 동작 |
|---|---|---|
| `AutoTrigger` | 없음 | `Arm` 즉시 발동 |
| `ManualTrigger` | `string _id` | `ITutorialManager.Complete(id)` 시 발동 |
| `ButtonClickTrigger` | `TutorialTargetRef _target` | 타깃 해석 → `Button.onClick` 구독 |
| `MessageTrigger<T>` | 서브클래스가 정의 | `Message.Subscribe<T>` + `Match(T)` 훅 |

지연은 트리거가 아니라 **Step의 필드**다. 트리거를 전부 동기로 유지할 수 있다.

`ButtonClickTrigger`가 `Button`이 아니라 `TutorialTargetRef`를 받는 것이 중요하다 — UIService가 런타임에 만든 팝업의 버튼도 트리거가 된다.

**인스펙터에서 제네릭 트리거 고르기:** `[SerializeReference]` + 게임 쪽의 구체 서브클래스 한 줄.

```csharp
[Serializable]
public sealed class ItemSpawnedTrigger : MessageTrigger<ItemSpawnedMessage>
{
    [SerializeField] private string _itemId;
    protected override bool Match(ItemSpawnedMessage m) => m.Id == _itemId;
}
```

리플렉션도 타입 이름 문자열도 없다. 드롭다운에 자동으로 뜬다.

### 4. 타깃 해석

```csharp
[Serializable]
public struct TutorialTargetRef
{
    [SerializeField] private Transform _direct;   // 씬 상주 — 인스펙터 드래그
    [SerializeField] private string _key;         // 런타임 생성 — TutorialTarget이 등록
}

public interface ITutorialTargetRegistry
{
    void Register(string key, Transform target);
    void Unregister(string key, Transform target);
    Awaitable<TutorialTargetHandle> ResolveAsync(TutorialTargetRef reference, CancellationToken token);
}

public sealed class TutorialTargetHandle : IDisposable
{
    public Transform Current { get; }        // 타깃이 없는 동안 null
    public event Action<Transform> Changed;
}
```

`TutorialTarget`(MonoBehaviour)이 `OnEnable`에 `Register`, `OnDisable`에 `Unregister`를 호출한다. UI 프리팹의 버튼에 붙여두면 UIService가 그 View를 띄울 때마다 자동 등록된다. **UIService는 이 존재를 모르고, 튜토리얼도 UIService에 의존하지 않는다.**

핸들 모델이 세 가지를 흡수한다:

- **팝업을 닫았다 다시 열어도 튜토리얼이 이어진다.** 타깃이 사라지면 모듈은 스스로 숨고 `ButtonClickTrigger`는 disarm, 다시 나타나면 둘 다 복귀한다.
- **파괴된 `Transform`은 `Current`가 `null`로 보인다** (Unity fake-null 체크).
- **같은 키 중복 등록**(풀에서 나온 View + 새 View)은 LIFO로 마지막 등록이 이긴다.

### 5. 모듈

```csharp
public interface ITutorialModule
{
    Awaitable ShowAsync(TutorialTargetHandle target, CancellationToken token);
    Awaitable HideAsync(CancellationToken token);
}
```

모듈은 타깃을 **자식으로 삼지 않는다.** 매 프레임 스크린 rect만 읽는다 — 이것이 `UIRoot`의 `DontDestroyOnLoad`를 무해하게 만드는 핵심이다. `ScreenSpaceOverlay` 캔버스는 하이어라키가 아니라 `sortingOrder`로 전역 정렬되므로, 모듈이 자기 root Canvas를 `UIRoot`보다 높은 `sortingOrder`로 들고 있으면 팝업 위에 그려진다.

```csharp
internal static class TutorialScreenRect
{
    public static bool TryGet(Transform target, Camera camera, out Rect screenRect);
}
```

`RectTransform`이면 코너 4점, 일반 `Transform`이면 `Renderer`/`Collider` 바운즈를 스크린으로 투영한다. 모듈이 UI 타깃과 3D 타깃을 구분할 필요가 없다.

카메라는 모듈 인스펙터 필드(씬 배치라 드래그 가능), 비어 있으면 `Camera.main` 폴백.

프레임 추적은 `TutorialModuleBehaviour.LateUpdate`에서만 한다. **순수 C# 엔진에는 프레임 펌프가 들어가지 않는다.**

기본 2종:

- **`HighlightModule`** — 타깃만 남기고 화면을 어둡게 덮고 바깥 클릭을 막는다. 셰이더/스텐실 없이 **구멍 텍스처 1장 + 상하좌우 딤 패널 4장**(레퍼런스의 `ClickBlockers[]`와 같은 방식). 딤 패널이 `raycastTarget`을 켜서 입력 차단이 부수효과로 따라온다.
- **`HandPointerModule`** — 타깃 스크린 위치에 손가락 스프라이트 + 탭 루프 애니메이션. `AnimationCurve` 인스펙터 커스터마이즈, 트윈 라이브러리 비의존(UIService 트랜지션 3종과 같은 방식).

### 6. 영속화

```csharp
public interface ITutorialProgressStorage
{
    TutorialState GetState(string sequenceId);
    void SetState(string sequenceId, TutorialState state);
    int  GetStepIndex(string sequenceId);
    void SetStepIndex(string sequenceId, int index);
    bool AllSkipped { get; set; }
    void Clear();
}
```

기본 구현 `PlayerPrefsTutorialProgressStorage`, 키는 `foundationdi.tutorial.{saveKey}.{sequenceId}.state`. SoundService의 `ISoundVolumeStorage`, AdService의 `IAdRemovalStorage`와 같은 자리.

`SkipAll`은 시퀀스를 순회하지 않고 `AllSkipped` 플래그 하나를 세운다. 씬에 없는 다른 레벨의 튜토리얼까지 확실히 덮는다.

**재개 정책 — 기본은 시퀀스 처음부터.** `ResumeMode.RestartSequence`가 기본값이다. Step 중간 재개는 앞선 Step들의 부작용이 이미 반영돼 있다는 걸 전제하는데 그걸 보장할 방법이 없다. 시퀀스가 짧다는 전제에서 처음부터 다시 하는 비용이 잘못 재개하는 위험보다 싸다. 긴 시퀀스는 `ResumeFromStep`으로 옵트인한다. `stepIndex`는 모드와 무관하게 항상 저장한다.

**재개도 `StartTrigger`를 기다린다.** `Running` 상태라고 씬 로드 즉시 재개하면, 3레벨 튜토리얼 도중 종료한 유저가 5레벨에 들어갔을 때 엉뚱하게 튀어나온다.

### 7. 오써링

```csharp
public sealed class TutorialSequenceBehaviour : InjectableBehaviour
{
    [SerializeField] private string _sequenceId;
    [SerializeField] private int _order;
    [SerializeField] private ResumeMode _resumeMode = ResumeMode.RestartSequence;
    [SerializeField] private float _targetTimeout;          // 0 = 무한
    [SerializeReference] private ITutorialTrigger _startTrigger;

    [Inject] private ITutorialManager _tutorial;
}
```

자식의 `TutorialStepBehaviour`들을 모아 `TutorialSequence`를 만들고 `_tutorial.Register(...)`한다. 씬에 직접 배치된 컴포넌트라 생성자 주입이 안 되므로 이 리포에 이미 있는 `InjectorService` / `InjectableBehaviour`를 쓴다. **새 인프라를 만들지 않는다.**

```csharp
public sealed class TutorialStepBehaviour : MonoBehaviour
{
    [SerializeField] private string _stepId;
    [SerializeField] private float _startDelay;
    [SerializeField] private float _endDelay;
    [SerializeField] private TutorialTargetRef _target;
    [SerializeReference] private ITutorialTrigger _startTrigger;
    [SerializeReference] private ITutorialTrigger _endTrigger;
    [SerializeField] private TutorialModuleBehaviour[] _modules;
}
```

### 8. DI 등록

```csharp
public class SceneLifetimeScope : LifetimeScope   // 씬에 배치
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterTutorialManager();               // 저장 키 "default"
        // 또는 builder.RegisterTutorialManager("chapter1");
    }
}
```

전제: 부모(루트) 스코프에 `IMessageService`와 `InjectorService`가 이미 등록되어 있어야 한다.

### 9. 게임 코드 사용 예

게임 코드는 원래 하던 대로 메시지만 발행한다.

```csharp
_message.Publish(new LevelStartedMessage { Level = 3 });
_message.Publish(new ItemSpawnedMessage { Id = "magnet" });
```

튜토리얼을 물어보거나 스킵시킬 때만 `ITutorialManager`를 쓴다.

```csharp
public sealed class SettingsPopupPresenter : UIPopupPresenter<SettingsView>
{
    private readonly ITutorialManager _tutorial;

    public void OnSkipTutorialButton() => _tutorial.SkipAll();

    public bool CanOpenShop() => _tutorial.IsCompleted("intro");
}
```

---

## 에러 처리

| 상황 | 정책 |
|---|---|
| 타깃이 영영 안 나타남 (키 오타 등) | `TargetTimeout`(기본 0=무한) 초과 시 경고 로그 → 시퀀스 중단, 상태를 `NotStarted`로 되돌림(다음에 조건 맞으면 재시도) |
| 모듈이 예외를 던짐 | 모듈별 `try/catch`로 격리 — 한 연출이 터져도 Step은 진행 (MessageService의 핸들러 격리와 같은 방식) |
| 트리거가 예외를 던짐 | 격리 + `Disarm`은 항상 `finally` |
| 씬 언로드 중 진행 | `Dispose()`가 CTS 취소 → 트리거 전부 disarm, 모듈 `HideAsync`는 건너뜀(오브젝트 파괴 중), `OperationCanceledException`은 삼킴 |
| 중복 `sequenceId` 등록 | 에러 로그, 뒤엣것 무시 |
| `Complete(stepId)`가 매칭 안 됨 | 경고 로그, 무시 |
| `TutorialTargetRef`가 비어 있음 | 타깃 없는 Step으로 취급(모듈이 화면 중앙 등 자체 폴백). 로그 없음 |

핵심 원칙: **튜토리얼이 게임을 영구 정지시키지 않는다.** 타깃을 못 찾으면 유저를 가두는 대신 중단하고 다음 기회에 재시도한다.

## 테스트

전부 EditMode(`FoundationDI.Tests`), 씬·프리팹·Unity 오브젝트 없이 돈다. 순수 C# 엔진과 MonoBehaviour 오써링을 가른 이유다.

- `ITutorialTrigger` / `ITutorialModule` / `ITutorialTargetRegistry` / `ITutorialProgressStorage`를 NSubstitute로 대체
- async 구간은 `AwaitableTest.Run(...)` — EditMode에서 `Awaitable.NextFrameAsync`가 완료되지 않으므로 필수
- 테스트 이름은 한국어 `should~` 형식

검증 대상:

1. 완료된 시퀀스는 등록해도 트리거를 arm 하지 않는다
2. `AllSkipped`면 어떤 시퀀스도 arm 하지 않는다
3. StartTrigger가 발동해야 시퀀스가 시작된다
4. Step이 StartTrigger → 모듈 Show → EndTrigger → 모듈 Hide 순서로 진행된다
5. 트리거는 발동 후 반드시 Disarm 된다
6. 시퀀스가 완료되면 Completed로 기록된다
7. 실행 중 다른 시퀀스가 발동하면 대기열에 들어간다 (Order 오름차순)
8. 재개 시 기본은 시퀀스 처음부터 시작한다
9. `ResumeFromStep`이면 저장된 stepIndex부터 시작한다
10. 재개도 StartTrigger를 기다린다
11. 타깃이 등록될 때까지 Step이 대기한다
12. 타깃이 사라지면 핸들의 Current가 null이 된다
13. 타깃이 다시 등록되면 핸들이 Changed를 쏜다
14. 타깃 타임아웃 시 시퀀스가 중단되고 상태가 NotStarted로 되돌아간다
15. 모듈이 예외를 던져도 다음 모듈과 Step이 진행된다
16. `Skip()`은 현재 시퀀스만 완료 처리한다
17. `SkipAll()`은 AllSkipped 플래그를 세운다
18. `Dispose()`가 모든 트리거를 disarm 하고 진행을 취소한다
19. 중복 sequenceId는 무시된다
20. `Complete(stepId)`가 ManualTrigger를 발동시킨다

## 범위 밖

- **시퀀스가 씬 경계를 가로지르는 경우.** 한 시퀀스의 Step들은 한 씬 안에서 시작해 끝난다. 필요해지면 `TutorialManager`를 루트 스코프로 올리고 오써링을 씬에서 떼는 별도 설계가 필요하다.
- **Collision / Distance 트리거.** `ITutorialTrigger`를 프로젝트에서 구현한다.
- **Arrow / PopUp / QuestMarker / Text3D / VideoPanel 모듈.** `TutorialModuleBehaviour`를 상속해 프로젝트에서 만든다.
- **분기 튜토리얼.** Step은 선형이다. 조건 분기가 필요하면 시퀀스를 나누고 각자의 `StartTrigger`로 표현한다.
- **튜토리얼 진행 상황의 서버 동기화.** `ITutorialProgressStorage`를 구현해 프로젝트에서 붙인다.
- **에디터 오써링 도구(마법사 창).** 1차 범위에서는 인스펙터 기본 드로어 + `[SerializeReference]` 드롭다운으로 간다.
