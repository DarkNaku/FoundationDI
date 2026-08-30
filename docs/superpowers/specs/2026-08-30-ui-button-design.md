# UIButton / UIStateButton 설계 — 피드백 버튼과 상태 이미지 스왑

- 상태: 설계 확정
- 작성일: 2026-08-30
- 범위: `Assets/FoundationDI/Runtime/Components/`, `Assets/FoundationDI/Editor/Components/`
- 대체 대상: `Services/SoundService/Components/SoundButton.cs` (삭제)

---

## 배경 / 목표

게임의 거의 모든 버튼은 두 가지를 공통으로 요구한다 — **클릭음**과 **햅틱**. 그리고 상당수는 여기에 **상태별 이미지 교체**를 더한다. 지금은 전자를 `SoundButton`이 컴포지션으로(= `Button`에 따로 붙여서) 처리하고, 후자는 uGUI 내장 `Transition.SpriteSwap`이 **`targetGraphic` 단 하나에만** 적용한다. 배경·아이콘·라벨이 함께 바뀌어야 하는 실제 버튼은 내장 기능으로 표현되지 않는다.

목표:

- 버튼 하나로 사운드·햅틱이 끝날 것. 컴포넌트를 두 개 붙이지 않는다.
- 상태 스왑이 **타깃 여러 개**를 다룰 것. 배경 Image, 아이콘 Image, 라벨 Text를 한 버튼이 함께 몰 수 있어야 한다.
- 스왑 데이터가 **uGUI를 모를 것** — EditMode에서 `Selectable` 없이 단위 테스트된다.
- 서비스 미등록이 **버튼 때문에 앱을 깨뜨리지 않을 것**.

비목표:

- 테마/스킨 에셋(ScriptableObject) 외부화. 지금은 버튼마다 인스펙터 저작이다. 나중에 이 설계 위에 얹을 수 있다.
- `Toggle`·`Slider` 등 다른 `Selectable` 파생. 스왑 세트는 재사용 가능하게 설계하되 이번엔 버튼만 만든다.
- 상태별 폰트 에셋(타입페이스) 교체. 근거는 아래 "덜어낸 것".

---

## 결정 사항과 근거

### 1. `Button` 상속 (컴포지션 아님)

`Selectable.DoStateTransition(SelectionState, bool)`은 `protected`다(`Selectable.cs:655`). 상속하지 않으면 상태 전이 훅에 접근할 수 없고, 컴포지션으로 가면 uGUI가 이미 계산해 주는 상태를 폴링으로 흉내 내야 한다.

```csharp
public class UIButton : Button                 // 사운드 + 햅틱
public class UIStateButton : UIButton          // + 상태 스왑
```

### 2. 자체 `UIButtonState` enum (uGUI의 `SelectionState` 재사용 불가)

`Selectable.SelectionState`는 **`protected` 중첩 enum**이다(`Selectable.cs:715`). 공개 접근자도 없다(`currentSelectionState`도 `protected`, 609행). 세 지점에서 막힌다:

1. `UIImageStateSet`은 `Selectable` 파생이 아니라 타입 이름조차 볼 수 없다.
2. 파생 클래스 안에서도 `public void Apply(SelectionState)` 는 CS0051(액세스 수준 불일치)로 컴파일되지 않는다.
3. 테스트 어셈블리에서 `SelectionState.Pressed`라고 쓸 수 없다.

따라서 자체 enum을 둔다. 값 순서는 uGUI와 동일하게 맞추되, **매핑은 캐스팅이 아니라 명시적 `switch`**로 한다 — 캐스팅은 유니티가 순서를 바꾸면 조용히 틀린 상태를 그린다.

```csharp
public enum UIButtonState { Normal, Highlighted, Pressed, Selected, Disabled }
```

부수 효과가 오히려 이득이다: 스왑 데이터 전체가 uGUI를 모르는 순수 타입이 되어 EditMode 단위 테스트가 가능해지고, 나중에 `Toggle` 기반 위젯에 재사용할 여지가 생긴다.

### 3. 5상태 1:1 매핑 + Normal 폴백

`Selected`를 버리고 4상태로 좁히면 매핑 결정이 생기는데, 어느 쪽으로 정해도 문제가 남는다. `currentSelectionState`(609-622행)의 판정 순서가 원인이다:

```csharp
if (!IsInteractable()) return SelectionState.Disabled;
if (isPointerDown)     return SelectionState.Pressed;
if (hasSelection)      return SelectionState.Selected;    // ← Highlighted보다 먼저
if (isPointerInside)   return SelectionState.Highlighted;
return SelectionState.Normal;
```

uGUI는 클릭한 버튼을 선택 상태로 남긴다. `Selected → Highlighted`로 매핑하면 **모바일에서 탭한 버튼이 계속 하이라이트된 채 남고**, `Selected → Normal`로 매핑하면 **PC에서 클릭 후 호버 하이라이트가 사라진다**.

그래서 1:1로 두고, 미지정을 **폴백 규칙**으로 해결한다. 어떤 상태의 어떤 필드에 대해:

```
그 상태가 이 필드를 오버라이드하면   → 그 상태의 값
아니면 Normal이 오버라이드하면       → Normal의 값
아니면                               → 아무것도 쓰지 않는다 (프리팹 원본 유지)
```

이 한 규칙이 셋을 동시에 해결한다:

- **모바일**: `Normal`/`Pressed`/`Disabled`만 채우면 탭 후 `Selected`가 `Normal`로 떨어진다. stuck 하이라이트 없음.
- **탭·토글 버튼**: `Selected`를 채운 버튼만 유지 표시가 켜진다. 옵트인.
- **"스프라이트만 바꾸고 색은 그대로"**: 아무도 색을 오버라이드하지 않으면 색을 아예 쓰지 않는다. 원본값 캡처가 필요 없다.

**폴백 대상이 `Normal`이라는 것이 핵심이다.** "가장 가까운 상태"(`Selected → Highlighted`)로 떨어뜨리면 정확히 stuck 하이라이트가 재발한다.

남는 것은 PC 미관 하나 — 클릭 후 마우스를 버튼 위에 둔 채로는 호버가 안 보인다. `_deselectOnClick`(기본 `false`) 옵트인으로 남긴다. 기본을 `false`로 두는 이유는 켜면 키보드·게임패드 내비게이션이 깨지고, `Selected` 슬롯이 마우스 경로에서 영영 안 뜨기 때문이다.

### 4. `Override` 플래그는 상태마다 둔다

세트 단위로 한 번만 두면 "이 세트는 Sprite를 스왑한다 → 5개 상태 전부가 Sprite를 제공해야 한다"가 되어 3번의 폴백이 성립하지 않는다.

### 5. 선택적 의존 (`IObjectResolver` + `TryResolve`)

`[Inject] IHapticService` 같은 필드를 들면, 프로젝트가 그 서비스를 등록하지 않았을 때 VContainer가 예외를 던진다. 이 프로젝트에는 그 예외를 흡수할 곳이 **거의 없다**:

| 경로 | 예외 지점 | 잡히는 곳 | 피해 |
|---|---|---|---|
| PoolManager 주입 (`PoolManager.cs:154`) | ObjectPool create func | `OperationQueue.cs` `catch (Exception) → LogException` | Show 1회 실패 + 인스턴스가 씬에 고아로 남음 |
| InjectorService 일괄 flush (씬 배치, 시작 시점) | `IStartable.Start()` | **없음** | 컨테이너 시작이 깨짐 |
| InjectorService 즉시 주입 (런타임 생성) | `Awake` ⊂ `Instantiate` ⊂ create func | 위 OperationQueue와 동일 | 위와 동일 |

`PoolManager.cs`·`InjectorService.cs`·`UIService.cs` 셋 다 `try/catch`가 0건이고, VContainer의 `StartableLoopItem.MoveNext`(`PlayerLoopItem.cs:29-37`)는 `exceptionHandler == null`이면 그대로 `throw`한다. 이 프로젝트는 `EntryPointExceptionHandler`를 등록하지 않는다.

버튼은 모든 씬에 깔리는 컴포넌트다. "햅틱 미등록" 하나로 View가 안 뜨거나 컨테이너가 안 뜨는 건 대가가 너무 크다. 그래서 컨테이너가 **항상 스스로 등록하는**(`ContainerBuilder.cs:161`) `IObjectResolver` 하나만 받고 개별 서비스는 `TryResolve`로 조회한다. 이러면 `UIButton`은 구조적으로 위 세 경로 어디에서도 예외를 낼 수 없다.

대가는 의존이 시그니처에 드러나지 않는다는 것이다. README로 보완한다.

> 위 표의 취약점 자체는 `UIButton`과 무관한 **기존 결함**이다(`[Inject]` 필드를 든 아무 컴포넌트나 해당된다). 이번 설계에 섞지 않고 `plan.md`의 별도 대기 항목으로 뺀다.

---

## 구성 요소

### 배치

```
Assets/FoundationDI/Runtime/Components/          ← 신설
    UIButtonState.cs
    UIButton.cs
    UIStateButton.cs
    UIImageStateSet.cs
    UITextStateSet.cs
    README.md
Assets/FoundationDI/Editor/Components/           ← 신설
    UIButtonEditor.cs
    UIStateButtonEditor.cs
    UIStateValueDrawers.cs           ← UIImageStateValueDrawer + UITextStateValueDrawer 둘 다 여기 있다
```

기존 구조는 `Runtime/Services/*`와 `Runtime/Managers/*` 둘뿐인데 `UIButton`은 둘 다 아니다. SoundService와 HapticService **두 서비스를** 소비하므로 어느 한 서비스 폴더 아래에 두면 소속이 틀린다. UIService 아래도 아니다 — UIService에 대한 의존이 0이다. "서비스를 소비하는 씬 저작용 위젯"을 담는 세 번째 버킷을 연다.

어셈블리는 기존 `FoundationDI` 그대로다. `Unity.TextMeshPro`가 이미 `FoundationDI.asmdef` 참조에 있어 런타임 asmdef 수정은 없다. 에디터는 `ButtonEditor` 상속을 위해 `FoundationDI.Editor.asmdef`에 **`UnityEditor.UI` 참조 한 줄**을 추가한다(현재 참조는 `FoundationDI` 하나).

> `SoundService/Components/`의 `VolumeSlider`·`OutputVolumeSlider`·`MusicZone`도 성격상 같은 자리지만 이번 범위 밖이다. 건드리지 않는다.

### 타입

| 타입 | 종류 | 역할 |
|---|---|---|
| `UIButtonState` | enum | 공개 5상태 |
| `UIButton : Button` | MonoBehaviour | 클릭 시 사운드 + 햅틱 |
| `UIStateButton : UIButton` | MonoBehaviour | 상태별 이미지/텍스트 스왑 |
| `UIImageSwap` / `UITextSwap` | `[Flags]` enum | 그 상태가 덮어쓸 필드 |
| `UIImageStateValue` / `UITextStateValue` | `[Serializable]` struct | 한 상태의 값 묶음 |
| `UIImageStateSet` / `UITextStateSet` | `[Serializable]` class | 타깃 1개 + 5상태 |

---

## UIButton — 피드백

### 주입

`UIButton : Button`이므로 `InjectableBehaviour`를 같이 상속할 수 없다(C# 단일 상속). `InjectorService.Request(MonoBehaviour)`가 정적 메서드라 멱등 self-request를 직접 재현한다.

```csharp
[Inject]
public void Construct(IObjectResolver resolver)
{
    resolver.TryResolve(out _sound);
    resolver.TryResolve(out _haptic);
}

protected override void Awake()
{
    base.Awake();                    // Selectable.Awake
    EnsureInjected();
    onClick.AddListener(PlayFeedback);
}

private void EnsureInjected()
{
    if (_requested) return;
    _requested = true;
    InjectorService.Request(this);
}
```

**View 하위에 있을 때도 주입된다 — 경로가 두 개다.**

1. `PoolManager.cs:154`의 `_resolver?.InjectGameObject(go)`가 View 인스턴스 생성 시 **자식 계층 전체의 모든 MonoBehaviour**에 주입한다(`ObjectResolverUnityExtensions.cs:36`의 `InjectGameObjectRecursive`, 활성/비활성 무관).
2. 위 `Awake`의 self-request.

프리팹 루트가 활성 저장이면 `Instantiate`(143행) 직후 `Awake`가 돌아 2번이 먼저, 아니면 1번이 먼저다. 어느 순서든 `Construct`가 두 번 불릴 뿐이고 하는 일이 `TryResolve` 대입 두 줄이라 멱등이다. View는 풀링되지만 주입은 팩토리 안(생성 1회)에서만 일어나고 `_requested`도 인스턴스당 1회라 반환/재획득에 재주입은 없다. 주입원이 루트 리졸버라 씬 전환·`DontDestroyOnLoad`와도 무관하다.

### 발동 지점

`onClick` 리스너로 간다. `Button.Press()`(`Button.cs:64`)가 `IsActive()`/`IsInteractable()`를 검사한 뒤 `m_OnClick.Invoke()`를 부르고 `OnPointerClick`(109행)·`OnSubmit`(148행)이 **둘 다** 여기를 지난다. 리스너 하나로 마우스·터치·게임패드 Submit이 전부 커버되고 가드는 uGUI 것을 물려받는다. `OnPointerClick`/`OnSubmit`을 각각 오버라이드하면 그 가드를 베껴야 해서 유니티가 바꾸면 어긋난다.

대가: 게임 코드가 `onClick.RemoveAllListeners()`를 부르면 피드백도 날아간다. `PlayFeedback()`을 `public`으로 열어 두고 README에 적는다.

### 인스펙터

```csharp
[Header("Sound")]
[SerializeField] private SFX _sfx;
[SerializeField] private Output _output;
[SerializeField, Range(0f, 1f)] private float _volume = 1f;
[SerializeField] private bool _randomPitch;

[Header("Haptic")]
[SerializeField] private bool _useHaptic = true;
[SerializeField] private HapticImpact _hapticImpact = HapticImpact.Light;
```

`SoundButton`에 있던 `_spatialSound`는 뺐다. UI 버튼은 스크린 공간에 있어 3D 감쇠가 의미 없고, 켜면 리스너 위치에 따라 클릭음 볼륨이 달라지는 버그로만 나타난다. `SetSpatialSound(false)` 고정이다.

`Sound` 인스턴스는 `SoundButton`처럼 캐시해 연타 시 재할당하지 않는다.

`_useHaptic`은 **저작 단위** 스위치이고 `IHapticService.Enabled`는 유저 설정(PlayerPrefs)이다. 서비스가 이미 전역 게이트를 하므로 버튼은 이중으로 막지 않는다.

햅틱은 `Impact` 계열만 노출한다. `Notification`/`Selection`/커스텀 `HapticPattern`은 버튼 클릭의 의미 범위를 넘고 인스펙터만 복잡해진다.

### 조용한 실패 방지

```
_sfx가 지정됐는데 _sound == null        → 버튼당 1회 경고
_useHaptic == true인데 _haptic == null  → 버튼당 1회 경고
```

**설정은 해 놨는데 서비스가 없는** 조합에서만 경고한다. 아무것도 설정 안 한 버튼이나 의도적으로 서비스를 안 쓰는 프로젝트는 조용하다. `AdjustAnalyticsSettings`가 매핑표에 없는 이벤트 이름을 이름당 한 번만 경고하는 것과 같은 패턴이다.

---

## UIStateButton — 상태 스왑

### 이미지 세트

```csharp
[Flags]
public enum UIImageSwap { None = 0, Sprite = 1 << 0, Color = 1 << 1, Visible = 1 << 2 }

[Serializable]
public struct UIImageStateValue
{
    public UIImageSwap Override;
    public Sprite Sprite;
    public Color Color;
    public bool Visible;
}

[Serializable]
public class UIImageStateSet
{
    public Image Target;
    public UIImageStateValue Normal, Highlighted, Pressed, Selected, Disabled;

    public void Apply(UIButtonState state);
}
```

`Apply`는 필드마다 3단 폴백을 돈다:

```csharp
private bool TryResolve(UIButtonState state, UIImageSwap field, out UIImageStateValue v)
{
    var s = Get(state);                                    // 5개 필드 중 하나를 고르는 switch
    if ((s.Override & field) != 0)      { v = s;      return true; }
    if ((Normal.Override & field) != 0) { v = Normal; return true; }
    v = default; return false;                             // 아무것도 쓰지 않는다
}
```

`Visible`은 `Target.enabled`에 쓴다 — `gameObject.SetActive`가 아니다. 자식 계층과 레이아웃을 흔들지 않고 렌더링·레이캐스트만 빠진다.

### 텍스트 세트

```csharp
[Flags]
public enum UITextSwap { None = 0, Text = 1 << 0, Color = 1 << 1, Material = 1 << 2 }

[Serializable]
public struct UITextStateValue
{
    public UITextSwap Override;
    public string Text;
    public Color Color;
    public Material Material;
}

[Serializable]
public class UITextStateSet
{
    public Graphic Target;      // TMP_Text 또는 UnityEngine.UI.Text
    public UITextStateValue Normal, Highlighted, Pressed, Selected, Disabled;

    public void Apply(UIButtonState state);
}
```

타깃이 `Graphic`인 이유는 `TMP_Text`와 `Text`의 유일한 공통 조상이기 때문이다. 필드별 분기:

| 필드 | TMP_Text | UnityEngine.UI.Text |
|---|---|---|
| Color | `Graphic.color` — 공통, 분기 없음 | 〃 |
| Text | `tmp.text` | `txt.text` |
| Material | `tmp.fontSharedMaterial` | `graphic.material` |

Material 분기가 중요하다. TMP에 `Graphic.material`을 그냥 대입하면 TMP 자체 머티리얼 관리와 충돌한다. 아웃라인·글로우를 상태별로 바꾸는 **TMP 표준 방식이 곧 Material Preset 교체**이므로 `fontSharedMaterial`이 "아웃라인 등 스타일 에셋" 요구를 커버한다.

**덜어낸 것 — 폰트 에셋(타입페이스) 교체.** `TMP_FontAsset`과 `Font`의 공통 조상이 `UnityEngine.Object`뿐이라, 넣으면 인스펙터에 아무 에셋이나 받는 필드가 생기고 런타임 타입 체크로 걸러야 한다. 상태별로 서체 자체를 바꾸는 경우는 드물고 굵기·외곽선 차이는 Material Preset으로 충분하다.

**문자열 스왑 주의.** 다국어를 쓰는 프로젝트는 상태별 문자열이 로컬라이제이션과 충돌한다. README에 명시한다.

### 배선

```csharp
public class UIStateButton : UIButton
{
    [SerializeField] private List<UIImageStateSet> _imageSets = new();
    [SerializeField] private List<UITextStateSet>  _textSets  = new();
    [SerializeField] private bool _deselectOnClick;

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        ApplyState(Map(state));                    // Map = 명시적 switch
    }

    internal void ApplyState(UIButtonState state)
    {
        for (int i = 0; i < _imageSets.Count; i++) _imageSets[i].Apply(state);
        for (int i = 0; i < _textSets.Count; i++)  _textSets[i].Apply(state);
    }

#if UNITY_EDITOR
    protected override void Reset()
    {
        base.Reset();                              // Selectable.Reset: targetGraphic 할당
        transition = Transition.None;              // 내장 ColorTint와 이중 적용 방지
    }
#endif
}
```

`base.DoStateTransition`은 **부른다.** 내장 `Transition`을 죽이지 않아야 `Animation` 트랜지션을 병행하는 팀이 산다. 대신 기본값(`ColorTint` + `targetGraphic` 자동 할당)이 우리 Color 스왑과 곱해지는 사고를 막으려고 `Reset`에서 `Transition.None`으로 내린다. `Selectable.Reset`은 `#if UNITY_EDITOR` 안에 있으므로(`Selectable.cs:602`) 오버라이드도 같은 가드로 감싼다.

초기 상태 적용은 공짜다 — `Selectable.OnEnable`(497행)이 마지막에 `DoStateTransition(currentSelectionState, true)`를 부른다.

`ApplyState`가 `internal`인 이유는 `Runtime/AssemblyInfo.cs`에 `InternalsVisibleTo("FoundationDI.Tests")`가 이미 있어, `EventSystem` 없이도 테스트가 5상태 각각의 적용을 직접 검증할 수 있기 때문이다. 게임 코드에 공개하면 실제 선택 상태와 어긋난 시각 상태를 만들 수 있어 열지 않는다.

`_deselectOnClick`이 켜져 있으면 `PlayFeedback` 이후 `EventSystem.current?.SetSelectedGameObject(null)`을 불러 `Selected`를 즉시 벗긴다. `UIStateButton`에만 두는 이유는 이 옵션이 순수하게 시각 문제를 위한 것이기 때문이다.

---

## 에디터

| 타입 | 역할 |
|---|---|
| `UIButtonEditor : UnityEditor.UI.ButtonEditor` | 기본 Button 인스펙터 + Sound/Haptic 섹션 |
| `UIStateButtonEditor : UIButtonEditor` | 위 + 세트 리스트 + 경고 HelpBox |
| `UIImageStateValueDrawer` / `UITextStateValueDrawer`(둘 다 `UIStateValueDrawers.cs`) | `Override`에서 켜진 필드만 그린다 |

`ButtonEditor` 상속이 `interactable`·`transition`·`navigation`·`onClick` 기본 UI를 공짜로 준다.

드로어가 실질적으로 중요하다. 드로어 없이 그리면 세트 하나가 **5상태 × (Override + 값 3개) = 20줄**로 펼쳐진다. 켜진 필드만 그리면 보통의 "스프라이트만 스왑" 세트는 5줄로 줄어든다.

HelpBox 경고 두 가지:

- 세트가 비어 있지 않은데 `transition != None` → 내장 ColorTint/SpriteSwap과 이중 적용
- 세트의 `Target`이 비어 있음 → 조용히 아무 일도 안 일어나는 상태

---

## 테스트

`FoundationDI.Tests`(EditMode). 스왑 세트가 `Selectable`을 모르므로 대부분이 순수 단위 테스트다 — `new GameObject().AddComponent<Image>()`로 타깃을 만들고 `Apply`를 직접 부른다. Canvas도 EventSystem도 필요 없다.

```
폴백 규칙
- 상태가 필드를 오버라이드하면 그 상태의 값을 쓴다
- 상태가 오버라이드하지 않으면 Normal 값으로 떨어진다
- Normal도 오버라이드하지 않으면 그 필드를 건드리지 않는다
- Selected를 지정하지 않으면 Normal로 떨어진다
- 색만 오버라이드하면 스프라이트는 원본 그대로다

이미지 세트
- 타깃이 null이면 예외 없이 아무 일도 하지 않는다
- Visible 오버라이드는 타깃의 enabled를 바꾼다

텍스트 세트
- TMP 타깃의 문자열이 바뀐다
- 레거시 Text 타깃의 문자열이 바뀐다
- 색은 타깃 종류와 무관하게 Graphic.color에 들어간다
- TMP 머티리얼은 fontSharedMaterial에 들어간다
- 레거시 Text 머티리얼은 material에 들어간다

버튼 통합
- ApplyState에 각 상태를 넣으면 세트가 그 상태로 적용된다
- interactable을 끄면 Disabled 세트가 적용된다
- 서비스가 하나도 등록되지 않아도 클릭이 예외를 내지 않는다
- 사운드 서비스가 등록되면 클릭 시 지정한 SFX로 `CreateSound`가 호출된다
```

`interactable` 테스트가 성립하는 근거: `Selectable.interactable` setter가 `OnSetProperty()`(`Selectable.cs:535`) → `DoStateTransition`을 부른다. 컴포넌트를 붙이고 `interactable = false`만 대입하면 매핑 경로 전체가 한 번 돈다.

**테스트하지 않는 것**: `Pressed`/`Highlighted`/`Selected` 매핑은 `EventSystem` 포인터 시뮬레이션이 필요해 EditMode 범위 밖이다. `Disabled` 경로로 `switch` 배선 자체는 확인되고, 나머지는 플레이 모드 확인으로 대신한다. (AdService 3사 어댑터, Firebase/Adjust 어댑터와 같은 처리다.) `Sound` 빌더 체인(`SetVolume`/`SetSpatialSound`/`SetOutput`/`SetRandomPitch`/`Play`)과 `SetSpatialSound(false)` 자체도 자동화 커버리지가 없다 — `Sound`가 `internal` 생성자를 가진 구체 클래스라 NSubstitute가 대체하지 못하고, `ISoundService.CreateSound`의 테스트 더블은 `null`을 돌려준다. `UIButton`은 그 `null`을 보고 조기 반환하므로 빌더 체인은 EditMode에서 한 번도 실행되지 않는다.

---

## 마이그레이션

`Services/SoundService/Components/SoundButton.cs`를 삭제한다. 프리팹·씬 참조 0건이고(guid `2049a19eb4c084303abe9cbf57a5c22f` 검색 결과 `.meta` 자신뿐), 언급은 문서 3곳과 샘플 스크립트 1개뿐이다.

갱신 대상:

- `Assets/FoundationDI/Runtime/Services/SoundService/README.md`
- `Assets/FoundationDI/Runtime/Services/InjectorService/README.md` (첫 사용처 예시가 `SoundButton`)
- `Assets/FoundationDI/README.md`
- `Assets/FoundationDI/Samples~/05-Sound/Scripts/SoundSampleScope.cs`
- `CLAUDE.md` (새 `Runtime/Components/` 버킷 소개)

패키지 버전은 `0.8.0` → `0.8.1`(패치 증가).

---

## 커밋 분리

Tidy First 원칙에 따라 구조 변경을 먼저, 별도 커밋으로 낸다.

1. `[STRUCTURAL]` `FoundationDI.Editor.asmdef`에 `UnityEditor.UI` 참조 추가
2. `[BEHAVIORAL]` `UIButtonState` + 스왑 세트 + 폴백 규칙 (TDD)
3. `[BEHAVIORAL]` `UIButton` 피드백
4. `[BEHAVIORAL]` `UIStateButton` 배선
5. `[STRUCTURAL]` 에디터 인스펙터/드로어
6. `[STRUCTURAL]` `SoundButton` 삭제 + 문서 갱신 + 버전 0.8.1

---

## 범위 밖 (별도 항목)

`plan.md`에 대기 항목으로 남긴다 — `UIButton`과 무관한 기존 결함이다.

```
## 대기: InjectorService/PoolManager 주입 실패 격리
- [ ] 주입이 실패한 컴포넌트가 있어도 나머지 pending이 모두 주입된다
- [ ] 풀 생성 중 주입이 실패해도 인스턴스가 씬에 고아로 남지 않는다
```
