# 컴포넌트 기반 UI 트랜지션 설계

- 날짜: 2026-07-02
- 대상: `Assets/FoundationDI/Runtime/Managers/UIManager/`
- 네임스페이스: `DarkNaku.FoundationDI`

## 배경 / 문제

현재 UI 트랜지션은 `UITransitionAsset : ScriptableObject` 기반이다(Fade/Scale/Slide). ScriptableObject 에셋은 **씬 인스턴스를 참조할 수 없으므로**, "배경은 페이드로 등장하고 컨텐츠는 화면 밖에서 슬라이드"처럼 한 View 안에서 배경/컨텐츠를 분리해 서로 다른 연출을 주는 것이 불가능하다. 트랜지션은 항상 View 루트 `RectTransform` 하나에만 적용된다.

## 목표

트랜지션을 **MonoBehaviour 컴포넌트**로 전면 전환한다. 컴포넌트는 인스펙터에서 배경(CanvasGroup)과 컨텐츠(RectTransform)를 직접 지정할 수 있어, 팝업 등장 연출(배경 페이드 + 컨텐츠 슬라이드/스케일)을 기본 트랜지션으로 제공한다.

## 결정 사항 (확정)

- **적용 범위**: 트랜지션 전체를 컴포넌트로 전환한다. 기존 에셋 방식은 제거한다.
- **UIView 해석**: 인스펙터 에셋 필드(`_transition`)를 없애고, `GetComponent<IUITransition>()`로 같은 GameObject의 트랜지션 컴포넌트를 자동 탐색한다.
- **settings 기본 트랜지션 제거**: settings 레벨 공통 기본값(`DefaultPageTransition`/`DefaultPopupTransition`/`DefaultOverlayTransition`)을 제거하고, 각 View가 자신의 트랜지션 컴포넌트를 갖는 것으로 일원화한다. 컴포넌트가 없으면 `Noop`(즉시).
- **재생 타이밍**: 배경 페이드와 컨텐츠 이동(slide/scale)을 **동시(병렬, `UniTask.WhenAll`)** 재생한다. Hide는 역순을 동시에.
- **배경 필드**: **선택적**. 비어 있으면 페이드를 생략하고 컨텐츠 이동만 수행한다(dim 없는 팝업 지원).

## 아키텍처

### 인터페이스 (변경 없음)

`IUITransition`는 그대로 유지한다.

```csharp
public interface IUITransition
{
    UniTask ShowAsync(RectTransform target, CancellationToken ct);
    UniTask HideAsync(RectTransform target, CancellationToken ct);
}
```

`NoopTransition`(plain class, 즉시 완료 폴백)도 그대로 유지한다.

### 컴포넌트 계층 (신규)

```
UITransitionBehaviour (abstract MonoBehaviour, IUITransition)
    - 공통 필드: _duration, _ease(AnimationCurve), _unscaledTime
    - 공통 헬퍼: Animate(Action<float> apply, ct)   ← 기존 UITransitionAsset.Animate 이식
├── FadeTransition   : 단일 타겟 CanvasGroup 페이드
├── SlideTransition  : 배경 CanvasGroup 페이드 + 컨텐츠 RectTransform 슬라이드
└── ScaleTransition  : 배경 CanvasGroup 페이드 + 컨텐츠 RectTransform 스케일
```

기존 `Animate` 구현(트윈 라이브러리 비의존, `UniTask.Yield` + `Time.unscaled/deltaTime` 누적, `AnimationCurve` 보간)을 그대로 옮긴다.

### 각 컴포넌트 사양

**FadeTransition**
- 인스펙터: `_target`(CanvasGroup, 선택적. 미지정 시 View 루트 CanvasGroup).
- Show: `alpha 0 → 1`. Hide: `alpha 1 → 0`.

**SlideTransition**
- 인스펙터: `_background`(CanvasGroup, 선택적), `_content`(RectTransform, 미지정 시 `target`=View 루트로 폴백), `_direction`(Left/Right/Top/Bottom).
- Show: `WhenAll(배경 alpha 0→1, 컨텐츠 anchoredPosition 화면밖→home)`.
- Hide: `WhenAll(배경 alpha 1→0, 컨텐츠 home→화면밖)` 후 컨텐츠 `anchoredPosition`을 home으로 복원(캐시 재사용 시 다음 Show가 화면 밖 좌표를 home으로 캡처하는 버그 방지 — 기존 SlideTransitionAsset의 주의사항 유지).
- `_background`가 null이면 컨텐츠 이동만 수행.

**ScaleTransition**
- 인스펙터: `_background`(CanvasGroup, 선택적), `_content`(RectTransform, 미지정 시 `target`으로 폴백), `_fromScale`(기본 0.8).
- Show: `WhenAll(배경 alpha 0→1, 컨텐츠 localScale fromScale→1)`.
- Hide: `WhenAll(배경 alpha 1→0, 컨텐츠 localScale 1→fromScale)`.
- `_background`가 null이면 컨텐츠 스케일만 수행.

### UIView 변경

`Views/UIView.cs`:
- `[SerializeField] private UITransitionAsset _transition;` **제거**.
- `public IUITransition DefaultTransition { get; set; }` **제거**.
- 해석 우선순위 변경:

```csharp
private IUITransition _componentTransition;
private bool _resolvedComponent;

private IUITransition ResolveComponent()
{
    if (!_resolvedComponent)
    {
        _componentTransition = GetComponent<IUITransition>();
        _resolvedComponent = true;
    }
    return _componentTransition;
}

private IUITransition Resolve() => Transition ?? ResolveComponent() ?? Noop;
```

- per-show 오버라이드 `Transition` 프로퍼티와 `.WithTransition()` 빌더 API는 **유지**(컴포넌트 인스턴스도 `IUITransition`이므로 그대로 동작).

### UIManager 변경

`UIManager.cs`:
- `ShowAsync(UIPresenter presenter, IUITransition defaultTransition, CancellationToken ct)`에서 `defaultTransition` 파라미터 **제거**.
- 내부의 `presenter.ViewBase.DefaultTransition = defaultTransition;` 라인 **제거**.
- 호출부 3곳(`ShowPageAsync`/`ShowPopupAsync`/`ShowOverlayAsync`)에서 `_settings?.DefaultXxxTransition` 인자 **제거**.

### UIManagerSettings 변경

`Settings/UIManagerSettings.cs`:
- `_defaultPageTransition`/`_defaultPopupTransition`/`_defaultOverlayTransition` 필드 및 대응 프로퍼티 **제거**.
- `_referenceResolution` / `ReferenceResolution`은 유지.

## 제거 / 유지 목록

**제거**
- `Transitions/UITransitionAsset.cs` (+ .meta)
- `Transitions/FadeTransitionAsset.cs` (+ .meta)
- `Transitions/ScaleTransitionAsset.cs` (+ .meta)
- `Transitions/SlideTransitionAsset.cs` (+ .meta)
- `UIManagerSettings`의 트랜지션 3필드
- `UIView._transition`, `UIView.DefaultTransition`
- `UIManager.ShowAsync`의 `defaultTransition` 경로

**신규**
- `Transitions/UITransitionBehaviour.cs`
- `Transitions/FadeTransition.cs`
- `Transitions/SlideTransition.cs`
- `Transitions/ScaleTransition.cs`

**유지**
- `Transitions/IUITransition.cs`
- `Transitions/NoopTransition.cs`

## 데이터 흐름

1. 팝업 프리팹 루트(UIView)에 `SlideTransition` 컴포넌트를 부착하고, 인스펙터에서 `_background`(dim 이미지의 CanvasGroup)와 `_content`(팝업 본문 RectTransform)를 지정.
2. `uiManager.Popup<TPresenter>()` 호출 → ShowQueue를 통해 `UIView.ShowAsync(ct)` 실행.
3. `UIView.Resolve()`가 `GetComponent<IUITransition>()`로 `SlideTransition`을 찾음.
4. `SlideTransition.ShowAsync(viewRoot, ct)`가 `WhenAll(배경 페이드, 컨텐츠 슬라이드)` 실행.

## 에러 / 엣지 케이스

- **배경 미지정**: `_background == null` → 페이드 생략, 컨텐츠 이동만.
- **컨텐츠 미지정**: `_content == null` → 전달받은 `target`(View 루트)을 컨텐츠로 사용.
- **취소(ct)**: 기존 `Animate`의 취소 처리(즉시 최종 상태로 스냅) 유지.
- **캐시 재사용**: Slide Hide 종료 시 컨텐츠 위치를 home으로 복원해 다음 Show 오작동 방지.
- **CanvasGroup 부재**: FadeTransition 기본 타겟은 View 루트이며 `UIView`의 `[RequireComponent(CanvasGroup)]`로 보장. 배경/컨텐츠에 별도 CanvasGroup이 필요한 경우 사용자가 부착(배경 페이드를 쓰려면 배경에 CanvasGroup 필수).

## 테스트 전략

트랜지션은 시간 기반 애니메이션이므로 **PlayMode 테스트**로 검증한다(EditMode에는 UniTask 프레임 루프를 진행시킬 게임 루프가 없다). 짧은 `_duration`으로 실제 재생을 `await`한 뒤 최종 상태를 단언한다.

검증 항목(테스트 이름은 한국어 의도, `should~` 형식):
- Slide Show 완료 후 컨텐츠 `anchoredPosition == home`, 배경 `alpha == 1`.
- Slide Hide 완료 후 컨텐츠 `anchoredPosition`이 home으로 복원됨.
- 배경 미지정 시 컨텐츠만 이동하고 예외 없이 완료됨.
- 컨텐츠 미지정 시 View 루트가 이동 대상이 됨.
- Scale Show 완료 후 컨텐츠 `localScale == Vector3.one`, 배경 `alpha == 1`.
- Fade Show/Hide 후 타겟 `alpha`가 1/0.
- `UIView`가 부착된 트랜지션 컴포넌트를 `GetComponent`로 해석한다.

PlayMode 테스트 어셈블리(`FoundationDI.PlayModeTests` 등)를 신규로 추가하고 `FoundationDI` 런타임 asmdef, UniTask, NUnit, `UnityEngine.TestTools`를 참조한다.

## 문서

`Managers/UIManager/README.md`의 트랜지션 절을 컴포넌트 방식으로 갱신한다(에셋 CreateAssetMenu 언급 제거, 인스펙터 배경/컨텐츠 지정 방법 추가).

## 범위 밖 (YAGNI)

- 순차(sequential) 재생 옵션 — 현재는 병렬만.
- 배경/컨텐츠 외 임의 다중 타겟 조합(N개) — 배경 1 + 컨텐츠 1로 고정.
- 트윈 라이브러리 도입 — 기존 `Animate` 자체 보간 유지.
