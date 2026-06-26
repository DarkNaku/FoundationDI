# UIView 입력 차단 — CanvasGroup.interactable 기반 전환 설계

**날짜**: 2026-06-26
**대상**: `Assets/FoundationDI/Runtime/Managers/UIManager/`
**상태**: 설계 승인됨 (구현 계획 대기)

## 배경 / 문제

현재 UIManager의 입력 차단은 `UIView.InputEnabled` → `GraphicRaycaster.enabled` 토글로 구현되어 있고, Popup 스택의 최상단만 활성화한다(`UpdatePopupModal`). 이 방식에는 두 가지 문제가 있다.

1. **암묵적 전제가 강제되지 않음**: `GraphicRaycaster`는 `[RequireComponent(typeof(Canvas))]`이므로, `InputEnabled`를 쓰려면 각 View 프리팹이 자체 (nested) Canvas + GraphicRaycaster를 가져야 한다. 그러나 `UIView`에 `RequireComponent`가 없어, 프리팹 제작자가 빠뜨리면 `InputEnabled`가 **조용히 no-op**이 되어 모달이 깨진다(에러·경고 없음).

2. **하위 레이어 차단 부재**: 현재는 Popup 스택만 토글하고 Page / BelowOverlay는 건드리지 않는다. 팝업이 떴을 때 하위 레이어(Page, 아래 Overlay)의 입력 차단은 **필수 요구사항**인데 시스템이 보장하지 않는다.

## 목표

- 모든 `UIView`에 `CanvasGroup`을 필수화하고, 입력 차단을 **`CanvasGroup.interactable`** 기반 요소 단위 통제로 전환한다.
- 팝업(모달) 표시 중 **Page + BelowOverlay + 하위 팝업**의 입력을 차단하고, **AboveOverlay는 유지**한다.
- 개별 View가 자체 Canvas/GraphicRaycaster를 갖지 않아도 되도록 구조를 단순화한다.
- 향후 "상위 Overlay까지 막는 모달"(서버 재접속 대기 등)을 얹을 수 있는 메커니즘만 열어둔다(이번엔 실제 API/팝업은 미구현).

## 핵심 결정 (브레인스토밍 결과)

| 결정 | 내용 | 근거 |
|---|---|---|
| 차단 시 비주얼 | **원래 모습 유지, 입력만 죽음** | 일반적 모달 UX |
| 입력 통제 속성 | **`CanvasGroup.interactable`** | "이 View가 입력을 받는가"의 의미. disabled 틴트는 감수 |
| 모달 정책 | **모든 팝업이 항상 모달** | 단순. 비모달은 Overlay로 표현 |
| AboveOverlay | **모달 중에도 입력 유지** | 항상 최상위 상주 UI(HUD/시스템 버튼) |
| 차단 세분도 | **요소 단위**(레이어 단위 아님) | 상위 Overlay까지 막는 모달을 표현하려면 요소 단위라야 함 |
| `blocksRaycasts` | **시스템 미관여, 프리팹 책임** | "흡수/통과"는 모드별 성격·인터랙티브 HUD 등 프리팹이 결정. raycastTarget으로 보정 |
| 구현 범위 | **기반만 구축** | 전체 차단 모달의 실제 API/팝업은 별도 작업 |

### 두 축의 분리 (중요)

- **`interactable`** = "이 View가 입력을 받는가" → `UIView.InputEnabled`가 통제. 모달 차단용.
- **`blocksRaycasts`** = "클릭을 흡수(true)/아래로 통과(false)시키는가" → 프리팹이 설정. 시스템은 건드리지 않는다.

두 속성은 독립이므로, 모달 하위 View는 `interactable=false`(컨트롤 비활성) + 프리팹의 `blocksRaycasts=true`(클릭 흡수)가 협력해 "회색·무반응 + 아래로도 안 샘"의 완전한 모달이 된다.

> `blocksRaycasts=false`는 그 CanvasGroup 하위 그래픽 전체를 레이캐스트에서 제외하므로, **그 View 자신의 버튼도 통과되어 못 누른다**. "화면을 덮되 빈 영역은 통과시키고 버튼만 받기"는 `blocksRaycasts`가 아니라 Graphic별 `raycastTarget`(빈 배경 false, 버튼 true)으로 푼다 — 프리팹 영역.

## 설계

### 1. UIView — CanvasGroup 필수화 + InputEnabled 재정의

```csharp
[RequireComponent(typeof(CanvasGroup))]
public abstract class UIView : MonoBehaviour
{
    private CanvasGroup _canvasGroup;
    private CanvasGroup CanvasGroup => _canvasGroup ??= GetComponent<CanvasGroup>();

    public bool InputEnabled
    {
        get => CanvasGroup.interactable;
        set => CanvasGroup.interactable = value;
    }
}
```

- `[RequireComponent(typeof(CanvasGroup))]`로 모든 View 프리팹에 CanvasGroup 보장 → no-op 함정 제거.
- 기존 `GraphicRaycaster` 멤버(`_raycaster`, `Raycaster`) **제거**.
- `blocksRaycasts`는 손대지 않음(프리팹 설정 보존).

### 2. UIManager — 요소 단위 모달 차단

기존 `UpdatePopupModal()`을 활성 요소 전체를 재계산하는 `RefreshInputBlocking()`으로 확장한다.

| 모달 팝업 존재 | Page(current) | BelowOverlay 전부 | Popup 스택 | AboveOverlay 전부 |
|---|---|---|---|---|
| 있음(≥1) | `false` | `false` | 최상단만 `true`, 나머지 `false` | 유지(`true`) |
| 없음(0) | `true` | `true` | — | `true` |

- 각 대상의 `view.InputEnabled`(= `CanvasGroup.interactable`)를 설정.
- 호출 시점: 활성 집합이 바뀔 때 — Popup Show/Hide, Page Show, Overlay Show/Hide.
- 활성 집합 조회는 기존 컨트롤러(`_pages.Current`, `_popups.All`, `_overlays`의 Above/Below 구분)를 활용.
- **확장 지점(미구현)**: 규칙을 내부적으로 *"최상단 모달 요소보다 렌더 순서가 아래인 활성 View를 차단"* 형태로 둔다. 지금은 모달 기준선이 "팝업"이지만, 나중에 더 위에 뜨는 모달(재접속 대기)을 추가하면 AboveOverlay까지 자연스럽게 차단된다.

### 3. Canvas 구조 단순화

- 개별 View는 자체 Canvas/GraphicRaycaster **불필요**. 레이캐스트는 루트 `[UIManager]` Canvas의 GraphicRaycaster 하나가 전담(EventSystem 레이캐스트에 필수이므로 유지).
- nested Canvas 강제가 사라져 프리팹 제작이 단순해진다.

### 4. 트랜지션 정리

- Fade는 같은 CanvasGroup의 `alpha`를, InputEnabled는 `interactable`을 사용 — 다른 속성이라 충돌 없음.
- `UITransitionAsset.EnsureCanvasGroup`(없으면 AddComponent)은 RequireComponent로 항상 존재하므로 `GetComponent`로 단순화.

### 5. 테스트

- `UIViewTests`: `InputEnabled`가 **`CanvasGroup.interactable`**을 토글하는지로 변경(기존 GraphicRaycaster 테스트 대체). TestView 프리팹/GameObject는 `Canvas` 대신 `CanvasGroup`을 부착.
- 신규(EditMode 또는 PlayMode): 모달 팝업 표시 시 **Page/BelowOverlay/하위팝업 차단 + AboveOverlay 유지 + 최상단 팝업 활성** 검증.
- 기존 `UIManagerFlowTests` 통과 유지.

## 변경 성격 / 커밋 분리

- **STRUCTURAL**: GraphicRaycaster → CanvasGroup 멤버 교체, `RequireComponent` 추가, 트랜지션 `EnsureCanvasGroup` 정리.
- **BEHAVIORAL**: 모달 시 Page/BelowOverlay까지 차단하는 새 동작, `interactable` 기반 차단으로의 전환.

두 성격을 같은 커밋에 섞지 않는다(프로젝트 규약).

## 범위 밖 (이번 작업 제외)

- AboveOverlay까지 막는 "전체 차단 모달"의 실제 API(모달 스코프)·팝업 구현.
- 단일 Canvas batching 성능 최적화(레이어별 중첩 Canvas 등).
- `blocksRaycasts`/`raycastTarget`에 대한 시스템 차원의 통제(프리팹 책임으로 둠).

## 문서 갱신 대상

- `Assets/FoundationDI/Runtime/Managers/UIManager/README.md`: `InputEnabled` 설명(GraphicRaycaster → CanvasGroup.interactable), 모달 차단 범위.
- 필요 시 CLAUDE.md의 UIManager 설명.
