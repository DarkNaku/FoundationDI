# Components

씬 저작용 uGUI 위젯이 사는 자리다(서비스도 매니저도 아니다). 현재 두 컴포넌트가 있다.

- **`UIButton`** — uGUI `Button`을 상속해 클릭 시 SFX 재생 + 햅틱 `Impact`를 낸다.
- **`UIStateButton`** — `UIButton`을 상속해, 상태(Normal/Highlighted/Pressed/Selected/Disabled)별로
  여러 `Image`/텍스트를 동시에 스왑한다. uGUI 내장 Transition은 `targetGraphic` 하나에만 걸리지만,
  이 컴포넌트는 세트마다 다른 타깃을 몰 수 있다.

두 컴포넌트 모두 `Selectable`/`Graphic` 같은 uGUI 저수준 타입만 알 뿐 서비스 인터페이스에 강결합하지
않는다 — 서비스가 없어도 컴파일과 실행이 깨지지 않는다.

---

## 사용법

### 1) DI 전제 (VContainer)

`UIButton`은 `[Inject]` 필드가 아니라 `IObjectResolver`를 직접 받아 씬 배치 컴포넌트를 주입하는
`InjectorService` 경로를 탄다. 루트 `LifetimeScope`에서 `RegisterInjector()`를 호출해야 한다.

```csharp
public class RootLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterInjector();

        // 아래 둘은 선택적이다 — 등록하지 않으면 그 기능만 조용히 꺼진다.
        builder.RegisterSoundService(soundSettings);
        builder.RegisterHapticService();
    }
}
```

`ISoundService`/`IHapticService`는 **선택적**이다. `UIButton`은 `IObjectResolver.TryResolve`로
두 서비스를 찾고, 못 찾으면 그 기능(사운드 또는 햅틱)만 꺼진 채로 나머지는 정상 동작한다. SFX나
햅틱을 켰는데 해당 서비스가 없으면 컴포넌트당 한 번만 경고를 남긴다.

### 2) 씬에 배치

`GameObject > FoundationDI > UI > Button` / `State Button` 메뉴로 배치하거나, 기존 `Button`에
`UIButton`/`UIStateButton`을 붙인다(`Button`을 대체하는 컴포넌트이므로 같은 오브젝트에 `Button`과
`UIButton`을 함께 두지 않는다).

인스펙터에서 SFX/Output/Volume/RandomPitch(Sound)와 UseHaptic/HapticImpact(Haptic)를 설정한다.
`UIStateButton`은 그 아래에 이미지 세트/텍스트 세트 목록과 `Deselect On Click`이 추가로 나온다.

---

## 상태 5종과 폴백 규칙

`UIStateButton`이 쓰는 상태는 `UIButtonState`(`Normal`/`Highlighted`/`Pressed`/`Selected`/`Disabled`)
다섯 가지다. uGUI의 `Selectable.SelectionState`와 값·순서가 같지만 `protected` 중첩 enum이라 공개
API에서 쓸 수 없어 별도로 둔 것이므로, 캐스팅이 아니라 명시적 매핑으로 번역된다.

각 스왑 필드(이미지의 Sprite/Color/Visible, 텍스트의 Text/Color/Material)는 필드마다 독립적으로
3단 폴백을 따른다.

```
그 상태에서 오버라이드했다 → 그 값을 쓴다
그 상태에서 오버라이드하지 않았다 → Normal에서 오버라이드했다면 Normal 값을 쓴다
Normal도 오버라이드하지 않았다 → 그 필드를 건드리지 않는다(원래 값 유지)
```

폴백 대상이 **"가장 가까운 상태"가 아니라 항상 `Normal`이라는 점이 핵심**이다. 예를 들어 `Selected`를
지정하지 않으면 `Highlighted`가 아니라 `Normal`로 떨어진다. `Selected→Highlighted`로 떨어뜨리면
uGUI가 클릭한 버튼을 선택 상태로 계속 유지하는 성질과 겹쳐, 모바일에서 탭한 버튼이 계속 하이라이트된
채로 남는 결과가 된다.

---

## 주의사항

**주의 1 — `onClick.RemoveAllListeners()`는 클릭 피드백도 지운다.**
`PlayFeedback()`(사운드+햅틱)은 다른 리스너와 마찬가지로 `Awake`에서 `onClick.AddListener`로 걸려
있다. `RemoveAllListeners()`를 부르고 자기 리스너만 다시 등록하면 피드백이 조용히 사라진다.
`onClick.AddListener(PlayFeedback)`을 함께 다시 걸어야 한다.

**주의 2 — `UITextSwap.Text`(문자열 스왑)는 로컬라이제이션과 충돌할 수 있다.**
상태별로 문자열 자체를 바꾸는 기능은 로컬라이제이션 시스템이 같은 `Text`/`TMP_Text`를 갱신하는
경로와 겹칠 수 있다. 두 시스템이 같은 타깃의 문자열을 서로 다른 시점에 덮어쓰면 최종 표시 문자열이
어느 쪽 것인지 불명확해진다. 로컬라이즈되는 텍스트에는 `Text` 스왑을 쓰지 않는 것을 권장한다.

**주의 3 — 폰트 에셋(타입페이스) 스왑은 지원하지 않는다.**
`UITextStateValue`가 스왑하는 것은 문자열·색·머티리얼뿐이며 TMP의 폰트 에셋(`TMP_FontAsset`) 자체는
바꾸지 않는다. 상태에 따라 굵기나 외곽선처럼 폰트를 바꿔야 표현되는 효과가 필요하면, TMP 표준 방식대로
Material Preset을 만들어 `Material` 필드로 교체한다.

**주의 4 — `Output`을 비우면 믹서를 우회한다.**
`Output`은 이 클릭음을 어느 `AudioMixerGroup`으로 보낼지 고르는 항목이다. 비워 두면
`Sound.SetOutput`이 `null`을 그대로 넘기고 `SoundSource`가 `outputAudioMixerGroup`을 `null`로
세팅해 버려 믹서를 통째로 지나친다. 그 결과 유저가 효과음 볼륨을 0으로 내려도 버튼 클릭음은 그대로
난다. SoundService에는 아직 "기본 Output" 개념이 없으므로 지금은 반드시 채워야 한다(기본 Output
지원은 `plan.md`의 대기 항목으로 남아 있다).

---

## `transition`은 `None`으로 둘 것

`UIStateButton`은 `Button`(→`Selectable`)의 `transition`(`m_Transition`)을 그대로 물려받는다.
기본값 `ColorTint`를 켜 두면 uGUI가 `targetGraphic.color`를 자체적으로 보간하는데, 이 컴포넌트의
`Color` 스왑도 같은 `Graphic.color`를 쓰므로 **두 색 변경이 곱해져 적용**된다. `Reset()`이 새로 붙일
때 `transition = None`으로 초기화하고, 인스펙터도 스왑 세트가 하나라도 있는데 `Transition`이
`None`이 아니면 경고를 띄운다. `Animation`/`SpriteSwap` 등 다른 Transition을 병행하고 싶다면 이
경고를 인지한 상태에서 직접 켠다.

---

## `_deselectOnClick`

기본값은 꺼짐(`false`)이다. 켜면 클릭 직후 `EventSystem.SetSelectedGameObject(null)`을 호출해 현재
선택을 해제한다 — PC에서 클릭 후에도 마우스가 버튼 위에 있으면 호버 하이라이트만 남기고 싶을 때 쓴다.

**켜면 키보드/게임패드 내비게이션과 `Selected` 상태 표시가 동작하지 않는다.** 선택을 매 클릭마다
해제하므로 방향키/게임패드로 다음 컨트롤로 이동할 기준(현재 선택된 오브젝트)이 사라지고,
`UIButtonState.Selected` 스왑도 적용될 시점 없이 곧바로 해제된다. 키보드/게임패드 내비게이션을
지원해야 하는 UI에서는 끄고 둔다.

---

## API

### `UIButton : Button`

| 멤버 | 시그니처 | 설명 |
| --- | --- | --- |
| `PlayFeedback` | `public void PlayFeedback()` | SFX 재생 + 햅틱 `Impact`. `onClick`에 자동으로 걸린다. `RemoveAllListeners()` 후 재배선할 때 공개된다. |
| `Construct` | `[Inject] public void Construct(IObjectResolver resolver)` | `ISoundService`/`IHapticService`를 각각 `TryResolve`. 둘 다 선택적. |

주요 직렬화 필드: `_sfx`(SFX), `_output`(Output), `_volume`(0~1, 기본 1), `_randomPitch`(bool),
`_useHaptic`(bool, 기본 true), `_hapticImpact`(HapticImpact, 기본 Light).

### `UIStateButton : UIButton`

| 멤버 | 시그니처 | 설명 |
| --- | --- | --- |
| `ApplyState` | `internal void ApplyState(UIButtonState state)` | 모든 이미지/텍스트 세트에 상태를 적용. `internal`인 이유는 EventSystem 없이 EditMode 테스트가 5상태를 직접 호출하기 위해서다 — 게임 코드에 공개하면 실제 선택 상태와 어긋난 시각 상태를 만들 수 있다. |

주요 직렬화 필드: `_imageSets`(`List<UIImageStateSet>`), `_textSets`(`List<UITextStateSet>`),
`_deselectOnClick`(bool, 기본 false).

### `UIButtonState`

```csharp
public enum UIButtonState { Normal, Highlighted, Pressed, Selected, Disabled }
```

### `UIImageStateSet` / `UITextStateSet`

`Selectable`을 전혀 모르는 순수 타입이라 EditMode에서 단독으로 테스트된다.

```csharp
[Flags] public enum UIImageSwap { None, Sprite, Color, Visible }

public struct UIImageStateValue
{
    public UIImageSwap Override;
    public Sprite Sprite;
    public Color Color;
    public bool Visible;
}

public class UIImageStateSet
{
    public Image Target;
    public UIImageStateValue Normal, Highlighted, Pressed, Selected, Disabled;
    public void Apply(UIButtonState state);
}
```

```csharp
[Flags] public enum UITextSwap { None, Text, Color, Material }

public struct UITextStateValue
{
    public UITextSwap Override;
    public string Text;
    public Color Color;
    public Material Material;
}

public class UITextStateSet
{
    public Graphic Target; // TMP_Text와 Text의 공통 조상
    public UITextStateValue Normal, Highlighted, Pressed, Selected, Disabled;
    public void Apply(UIButtonState state);
}
```

- 색은 타깃 종류와 무관하게 항상 `Graphic.color`에 들어간다.
- 텍스트 머티리얼은 TMP 타깃이면 `fontSharedMaterial`, 레거시 `Text` 타깃이면 `material`에 들어간다
  (TMP에 `Graphic.material`을 그냥 대입하면 TMP 자체 머티리얼 관리와 충돌하기 때문).
- `Target`이 `null`이면 `Apply`는 예외 없이 아무 일도 하지 않는다.

---

## 에디터

`Assets/FoundationDI/Editor/Components/`에 커스텀 인스펙터가 있다.

- `UIButtonEditor` — 기본 `ButtonEditor` 위에 Sound/Haptic 섹션을 추가로 그린다.
- `UIStateButtonEditor` — 위 에디터를 상속해 State Swap 섹션을 추가하고, 스왑 세트가 있는데
  `Transition`이 `None`이 아니면 경고, 세트의 `Target`이 비어 있으면 경고를 띄운다.
- `UIStateValueDrawers` — `UIImageStateValue`/`UITextStateValue`의 `PropertyDrawer`. `Override`
  플래그로 켠 필드만 인스펙터에 노출한다.

---

## 테스트

EditMode 단위 테스트(`Assets/FoundationDI/Tests/`)는 `UIStateButton.ApplyState(UIButtonState)`를
직접 호출해 EventSystem 포인터 시뮬레이션 없이 5상태 매핑과 폴백 규칙을 검증한다. 서비스 주입은
`SetServicesForTest`/`ConfigureForTest`(둘 다 `internal`) 헬퍼로 대체한다.

EditMode 범위 밖: `Pressed`/`Highlighted`/`Selected`로의 실제 전이는 `EventSystem` 포인터 시뮬레이션이
필요해 PlayMode 확인으로 대신한다. `Disabled` 경로(→ `interactable = false`)로의 switch 배선은
`ApplyState` 직접 호출로 확인된다.

## 한계 / 후속 과제

- SoundService에 "기본 Output" 개념이 없어 `Output`을 비우면 믹서를 우회한다(위 주의 4). 별도 계획
  대상(`plan.md`).
- 폰트 에셋 자체는 스왑 대상이 아니다(위 주의 3).
