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
두 서비스를 찾고, 못 찾으면 그 기능(사운드 또는 햅틱)만 꺼진 채로 나머지는 정상 동작한다. SFX를
지정했는데 `ISoundService`가 없으면 **컴포넌트당** 한 번, 햅틱을 켰는데 `IHapticService`가 없으면
**세션당** 한 번만 경고를 남긴다(햅틱 쪽은 전역 미등록 보고라 인스턴스 수만큼 찍을 이유가 없다).

### 2) 씬에 배치

전용 GameObject 생성 메뉴는 없다 — 기존 GameObject의 **Add Component**에서 `FoundationDI/UI Button`
또는 `FoundationDI/UI State Button`을 검색해 붙인다(`[AddComponentMenu]`로 노출된다). `Button`을
대체하는 컴포넌트이므로 같은 오브젝트에 `Button`과 `UIButton`을 함께 두지 않는다.

인스펙터에서 SFX/Output/Volume/RandomPitch(Sound)와 UseHaptic/HapticImpact(Haptic)를 설정한다.
`UIStateButton`은 그 아래에 이미지 세트/텍스트 세트 목록과 `Deselect On Click`이 추가로 나온다.

**기존 `Button`을 붙였던 자리를 마이그레이션하는 경우** — Add Component 대신 인스펙터 하단의
컨텍스트 메뉴로 **Script**를 `UIButton`/`UIStateButton`으로 교체하는 경로를 더 많이 쓰게 된다. 이
경로는 `Reset()`을 부르지 않으므로 기존 `Button`의 `m_Transition`(예: `ColorTint`)과
`targetGraphic`이 그대로 남는다. 스왑 세트를 아직 추가하지 않았다면 인스펙터 경고도 뜨지 않으니,
**Transition을 손으로 `None`으로 바꿔야 한다**(위 "`transition`은 `None`으로 둘 것" 참고).

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

**기준값 복원.** `Normal`이 오버라이드하지 않는 필드를 다른 상태가 오버라이드하는 경우도 안전하다.
예를 들어 `Highlighted`에만 `Color`를 켜 노란색을 지정하면, 호버를 벗어날 때 **첫 `Apply` 시점에
캡처해 둔 기준값**으로 돌아온다. 그래서 실제 해석은 4단이다:

```
그 상태가 오버라이드하면            → 그 상태의 값
아니면 Normal이 오버라이드하면       → Normal의 값
아니면 다른 상태가 오버라이드하면     → 기준값(첫 Apply 시점의 타깃 값)
아무 상태도 오버라이드하지 않으면     → 아무것도 쓰지 않는다
```

마지막 줄이 중요하다. 아무 상태도 관리하지 않는 필드는 기준값으로도 되돌리지 않는다 — 게임 코드가
런타임에 바꾼 스프라이트를 버튼이 멋대로 되돌리면 안 되기 때문이다.

기준값은 `[NonSerialized]`라 프리팹 인스턴스마다 새로 잡히고, View가 풀에서 재사용돼도 다시 잡지
않는다. 즉 두 번째 표시에서도 첫 프리팹 값이 기준이다.

> **에디터에서는 아직 값이 구워진다.** `Selectable`이 `[ExecuteAlways]`라 에디터에서도 `OnValidate`
> → `DoStateTransition`이 돌고, 이 스왑은 uGUI의 `overrideSprite`/`CanvasRenderer`와 달리 직렬화
> 필드를 직접 쓴다. 인스펙터에서 `interactable`을 껐다 켜면 `Disabled` 값이 프리팹에 남을 수 있다.
> `plan.md`의 "대기: UIStateButton 복원 기준값"에 남아 있는 항목이다.

---

## 주의사항

**주의 1 — `onClick.RemoveAllListeners()`는 클릭 피드백도 지운다.**
`PlayFeedback()`(사운드+햅틱)은 다른 리스너와 마찬가지로 `Awake`에서 `onClick.AddListener`로 걸려
있다. `RemoveAllListeners()`를 부르고 자기 리스너만 다시 등록하면 피드백이 조용히 사라진다.
`onClick.AddListener(PlayFeedback)`을 함께 다시 걸어야 한다. **`UIStateButton`에서 `_deselectOnClick`을
켰다면 문제가 하나 더 있다** — 이 옵션은 `onClick`에 `Deselect`라는 **private** 메서드를 별도로
등록하는데, `RemoveAllListeners()`는 이것도 함께 지우고 `private`이라 게임 코드가 다시 등록할 수
없다. `_deselectOnClick`을 쓰는 버튼에서는 애초에 `RemoveAllListeners()`를 피하고, 필요하면 특정
리스너만 `onClick.RemoveListener(...)`로 지운다.

**주의 2 — `UITextSwap.Text`(문자열 스왑)는 로컬라이제이션과 충돌할 수 있다.**
상태별로 문자열 자체를 바꾸는 기능은 로컬라이제이션 시스템이 같은 `Text`/`TMP_Text`를 갱신하는
경로와 겹칠 수 있다. 두 시스템이 같은 타깃의 문자열을 서로 다른 시점에 덮어쓰면 최종 표시 문자열이
어느 쪽 것인지 불명확해진다. 로컬라이즈되는 텍스트에는 `Text` 스왑을 쓰지 않는 것을 권장한다.

**주의 3 — 폰트 에셋(타입페이스) 스왑은 지원하지 않는다.**
`UITextStateValue`가 스왑하는 것은 문자열·색·머티리얼뿐이며 TMP의 폰트 에셋(`TMP_FontAsset`) 자체는
바꾸지 않는다. 상태에 따라 굵기나 외곽선처럼 폰트를 바꿔야 표현되는 효과가 필요하면, TMP 표준 방식대로
Material Preset을 만들어 `Material` 필드로 교체한다.

**주의 4 — `Output`을 비우면 설정의 기본 Output을 탄다.**
`Output`은 이 클릭음을 어느 `AudioMixerGroup`으로 보낼지 고르는 항목이다. 비워 두면
`SoundServiceSettings.DefaultOutput`으로 해석된다. **그 기본값도 비어 있으면** `SoundSource`가
`outputAudioMixerGroup`을 `null`로 세팅해 믹서를 통째로 지나치고, 그러면 유저가 효과음 볼륨을 0으로
내려도 버튼 클릭음은 그대로 난다. UI 클릭음이 볼륨 설정을 따르게 하려면 버튼의 `Output`을 채우거나
`SoundServiceSettings`의 `Default Output`을 지정한다.

**주의 5 — `_sfx`/`_volume`/`_output`은 첫 클릭 이후 런타임 변경이 반영되지 않는다.**
`UIButton`은 첫 클릭 시 `Sound`를 한 번 빌드해 `_sound` 필드에 캐싱하고, 이후 클릭은 그 인스턴스의
`Play()`만 다시 부른다. 즉 `_sfx`/`_volume`/`_output`을 코드로 바꿔도 이미 캐싱된 `_sound`에는
반영되지 않는다(`_randomPitch`만 클릭마다 다시 적용된다). 런타임에 값을 바꿔야 한다면 새
`UIButton` 인스턴스를 쓰거나, 캐시를 무효화하는 API가 추가되기 전까지는 이 제약을 감안한다.

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
  `Transition`이 `None`이 아니면 경고, 세트의 `Target`이 비어 있으면 경고, `Normal`이 오버라이드하지
  않는 필드를 다른 상태가 오버라이드하면 경고를 띄운다(위 "상태 5종과 폴백 규칙"의 하자).
- `UIImageStateValueDrawer`/`UITextStateValueDrawer`(`UIStateValueDrawers.cs`) — 각각
  `UIImageStateValue`/`UITextStateValue`의 `PropertyDrawer`. `Override` 플래그로 켠 필드만
  인스펙터에 노출한다.

---

## 테스트

EditMode 단위 테스트(`Assets/FoundationDI/Tests/`)는 `UIStateButton.ApplyState(UIButtonState)`를
직접 호출해 EventSystem 포인터 시뮬레이션 없이 5상태 매핑과 폴백 규칙을 검증한다. 서비스 주입은
`SetServicesForTest`/`ConfigureForTest`(둘 다 `internal`) 헬퍼로 대체한다.

EditMode 범위 밖: `Pressed`/`Highlighted`/`Selected`로의 실제 전이는 `EventSystem` 포인터 시뮬레이션이
필요해 PlayMode 확인으로 대신한다. `Disabled` 경로(→ `interactable = false`)로의 switch 배선은
`ApplyState` 직접 호출로 확인된다.

## 한계 / 후속 과제

- `Output`과 `SoundServiceSettings.DefaultOutput`이 둘 다 비면 믹서를 우회한다(위 주의 4).
- 폰트 에셋 자체는 스왑 대상이 아니다(위 주의 3).
