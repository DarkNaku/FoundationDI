# UIService 저작(authoring) 워크플로 설계

작성일: 2026-08-21

## 1. 문제

`UIService`는 런타임에 `[UIService]` 캔버스 루트를 **코드로 생성**한다
(`Runtime/Services/UIService/Controllers/UIRoot.cs`). 캔버스 렌더 모드,
`CanvasScaler` 설정, 4개 레이어(`[Page]`/`[BelowOverlay]`/`[Popup]`/`[AboveOverlay]`)의
이름과 순서가 모두 C# 코드에 하드코딩되어 있고, `UIServiceSettings`는 기준 해상도
하나만 들고 있다.

그 결과 디자이너가 UI 프리팹을 만들 때:

- 런타임과 동일한 캔버스(같은 스케일러·같은 기준 해상도)를 **손으로 임시 구성**한 뒤
  그 아래에서 작업해야 한다.
- 작업이 끝나면 임시 오브젝트를 지워야 한다 — 번거롭고, 실수로 커밋될 여지가 있다.
- 수정할 때마다 같은 절차를 반복해야 하고, 임시 캔버스 설정이 런타임 설정과
  어긋나면 **에디터에서 본 크기/스케일이 실제와 다르다**. (사용자가 지목한 가장 큰 통증)

추가로, 새 UI 요소 하나를 추가할 때마다 View 스크립트 · Presenter 스크립트 ·
프리팹 · `[UIPrefab]` 키 연결이라는 **동일한 보일러플레이트**를 매번 손으로 만든다.

## 2. 목표 / 비목표

**목표**

- 편집 중 보이는 캔버스 컨텍스트가 런타임과 **정의상 일치**한다(설정 드리프트 불가능).
- 프리팹을 열면 곧바로 올바른 크기/스케일에서 작업 가능하고, **정리할 임시 오브젝트가 없다**.
- 새 UI 요소 생성이 "이름 + 모드 입력 → 편집 가능한 프리팹이 열린 상태"로 끝난다.
- 프로그래머는 필요한 로직만, 디자이너는 배치만 담당한다.

**비목표**

- 씬에 놓은 프리뷰 루트를 자동 정리하는 도구는 만들지 않는다. 저장되지 않는
  오브젝트로 만들면 그 아래에 작업한 UI까지 유실될 위험이 있어 이득보다 사고 위험이 크다.
  루트가 프리팹이 된 이상 "드래그해서 쓰고 지운다"로 충분하다.
- 스크립트 템플릿의 사용자 커스터마이징(외부 템플릿 파일)은 이번 범위 밖이다.
- 트랜지션 미리보기(에디터에서 연출 재생)는 이번 범위 밖이다.

## 3. 설계

세 개의 하위 시스템으로 나뉘며, 뒤 단계가 앞 단계를 전제로 한다.

### 3.1 런타임 — 프리팹이 단일 진실 소스

`UIRoot`를 순수 C# 클래스에서 **프리팹 루트에 부착되는 MonoBehaviour**로 전환한다.

```csharp
[RequireComponent(typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))]
public sealed class UIRoot : MonoBehaviour
{
    [SerializeField] private RectTransform _pageLayer;
    [SerializeField] private RectTransform _belowOverlayLayer;
    [SerializeField] private RectTransform _popupLayer;
    [SerializeField] private RectTransform _aboveOverlayLayer;

    public GameObject GO => gameObject;
    public Transform PageLayer => _pageLayer;
    public Transform BelowOverlayLayer => _belowOverlayLayer;
    public Transform PopupLayer => _popupLayer;
    public Transform AboveOverlayLayer => _aboveOverlayLayer;
}
```

- 인스턴스화 경로에서는 렌더 모드 / `uiScaleMode` / `screenMatchMode` / 기준 해상도 /
  레이어 이름·순서를 코드가 **일절 강제하지 않는다**. 전부 프리팹 인스펙터가 결정한다.
  (이 값들을 코드가 다루는 유일한 곳은 아래 `CreateDefault()`이며, 그것은 "기본 프리팹을
  조립하는 템플릿"이지 인스턴스화 시 덮어쓰는 로직이 아니다.)
- `UIServiceSettings`: `_referenceResolution` 제거 → `[SerializeField] UIRoot _rootPrefab` 추가.
- `UIService.Root` getter는 `new UIRoot(...)` 대신
  `Object.Instantiate(_settings.RootPrefab)` + `DontDestroyOnLoad`로 바꾼다.
  **호출부(`Root.PageLayer` 등 `UIService.cs`의 7곳)는 변경 없음.**
- `DiscardRoot()` / `Dispose()`의 `Destroy(_root.GO)` 경로도 그대로 동작한다.

**프리팹 미지정 시 폴백**: `public static UIRoot CreateDefault()` 정적 팩토리가 새
GameObject에 현재와 동일한 계층·설정을 조립하고, 부착된 `UIRoot` 컴포넌트를 반환한다
(기준 해상도 1920x1080). 제로 설정으로도 동작하고 기존 테스트/샘플이 깨지지 않는다.
이 팩토리는 3.2의 프리팹 생성 메뉴가 **동일하게 재사용**하므로, 코드 기본값과 프리팹이
어긋날 수 없다.

**깨지는 것 / 마이그레이션**: `UIServiceSettings.ReferenceResolution`이 사라진다.
기존 사용자는 루트 프리팹을 생성(3.2 ①)해 Settings에 연결하고, 기준 해상도를 그 프리팹의
`CanvasScaler`에 설정한다. 폴백 덕분에 미조치 시에도 동작은 하되 기준 해상도는
기본값이 된다. README에 마이그레이션 절을 추가하고 패키지 마이너 버전을 올린다.

### 3.2 에디터 — 프리팹 편집 환경

새 폴더 `Assets/FoundationDI/Editor/UIService/` (기존 `FoundationDI.Editor` asmdef에 합류).

**① `DarkNaku/UIService/Create UI Root Prefab...`**
`UIRoot.CreateDefault()`로 계층을 만들고 4개 레이어 참조를 자동 연결해 프리팹으로 저장한다.

**② `DarkNaku/UIService/Setup Prefab Editing Environment`**
Settings에 연결된 루트 프리팹 인스턴스 하나만 든 씬(`UIEditingEnvironment.unity`)을
생성하고 `EditorSettings.prefabUIEnvironment`에 지정한다. 해제용 메뉴도 함께 제공한다.
프로젝트 설정을 변경하므로 **자동 실행 없이 명시적 메뉴 실행만** 한다.

**결과**: 프로젝트 창에서 UI 프리팹을 더블클릭하면 실제 캔버스·실제 기준 해상도 안에서
열린다. 씬에는 아무것도 생기지 않고 정리할 것도 없다.

**Unity 동작 전제(확인됨)**: `prefabUIEnvironment`는 **격리(isolation) 모드에서만**
적용된다. 프로젝트 창에서의 더블클릭이 격리 모드이므로 목적에 부합한다.

**구현 1단계에서 실측할 항목**: 환경 씬 안에서 열린 프리팹이 어느 노드 아래에 배치되는가
(첫 `Canvas` 아래로 알려져 있으나 공식 문서에 명시가 없음). `[Page]` 레이어 아래까지
가지 못하더라도 캔버스 스케일은 정확하므로 목표는 달성된다.

### 3.3 에디터 — UI 요소 생성 마법사

**메뉴** `DarkNaku/UIService/Create UI Element...` — 이름과 모드(Page/Popup/Overlay)만
입력받고, 결과 경로는 미리보기로 표시한다.

**프로젝트 기본값**은 `ScriptableSingleton<T>` +
`[FilePath("ProjectSettings/FoundationDIUIEditor.asset", FilePathAttribute.Location.ProjectFolder)]`
에 저장하고 `Project Settings/DarkNaku/UIService`에 노출한다. EditorPrefs가 아니라
ProjectSettings인 이유는 팀원 간 공유·커밋이 되어야 규약이 유지되기 때문이다.

필드: 스크립트 루트 / 네임스페이스 / 프리팹 루트.

**로드 키 도출**: 프리팹 경로에 `/Resources/`가 있으면 그 뒤에서 확장자를 제거한 것이 키다
(`Assets/Resources/UI/Shop.prefab` → `UI/Shop`). Resources 밖이면 Addressables 주소로
간주하고 경로를 그대로 쓴다.

**생성 흐름 — 도메인 리로드 경계**

스크립트를 만든 직후에는 그 타입이 아직 존재하지 않아 `AddComponent` 할 수 없다.
반드시 2단계로 나눈다.

1. 이름 검증(유효한 C# 식별자, 기존 타입/파일과 중복 없음)
2. `<Name>View.cs` + `<Name>Presenter.cs` 생성
3. 대기 작업을 `SessionState`에 기록(도메인 리로드 생존) → `AssetDatabase.Refresh()`
4. `[DidReloadScripts]` 콜백에서 대기 작업을 픽업 → 리플렉션으로 생성된 View 타입을 찾아
   프리팹을 조립·저장
5. `AssetDatabase.OpenAsset(prefab)` → 격리 프리팹 모드 진입(3.2의 환경 씬 적용) →
   디자이너가 즉시 배치 시작

4단계가 실패하면(컴파일 에러 등) 대기 작업을 정리하고 콘솔에 원인을 남긴다.
좀비 상태로 남기지 않는다.

**생성물 예시** (Popup, 이름 `Shop`, 네임스페이스 `MyGame.UI`)

```csharp
// ShopView.cs
using DarkNaku.FoundationDI;

namespace MyGame.UI
{
    public class ShopView : UIView { }
}

// ShopPresenter.cs
using DarkNaku.FoundationDI;

namespace MyGame.UI
{
    [UIPrefab("UI/Shop")]
    public class ShopPresenter : UIPopupPresenter<ShopView>
    {
        protected override void OnInitialize() { }
    }
}
```

```
Assets/Resources/UI/Shop.prefab
Shop           RectTransform(stretch) + CanvasGroup + ShopView
├─ Background  Image(검정 α=0.5, stretch, raycastTarget=on → 모달 입력 차단)
└─ Content     RectTransform(중앙 정렬)
```

**모드별 프리팹 템플릿**

| 모드 | 루트 | 자식 |
|---|---|---|
| Page | RectTransform(stretch) + CanvasGroup + View | 없음 |
| Popup | 위와 동일 | `Background`(Image, stretch, 모달 차단), `Content`(중앙 정렬 RectTransform) |
| Overlay | 위와 동일 | 없음 |

Page와 Overlay는 전면 배경 `Image`가 없으므로 빈 영역의 입력이 자연히 아래로 통과한다.
`CanvasGroup.blocksRaycasts = false`는 쓰지 않는다 — 그렇게 하면 오버레이 안의 버튼까지
전부 죽어 HUD류 오버레이가 동작하지 않는다.

Popup의 `Background`/`Content` 분리는 기존 `SlideTransition`/`ScaleTransition`의
`_background`(Image) / `_content`(RectTransform) 필드와 그대로 맞물린다.

## 4. 테스트 전략

- **런타임(3.1)**: 기존 `Tests/Runtime/UIService/UIRootTests.cs`의 4개 테스트를 프리팹
  인스턴스화 경로 기준으로 재작성한다(레이어 4개 연결, `DontDestroyOnLoad` 소속).
  `CanvasScaler`/렌더 모드를 코드가 강제하지 않게 되었으므로 해당 단언은
  `CreateDefault()` 폴백에 대한 테스트로 옮긴다. Settings에 프리팹이 지정되면 그 프리팹이
  인스턴스화되는지, 미지정이면 폴백이 쓰이는지 검증한다.
- **에디터(3.2)**: 프리팹 빌더의 레이어 연결·캔버스 구성을 EditMode 테스트로 검증.
  환경 씬 설정 테스트는 `EditorSettings.prefabUIEnvironment` 원래 값을 반드시 원복한다.
- **에디터(3.3)**: 이름 검증 · 로드 키 도출 · 템플릿 문자열 생성을 **순수 함수로 분리**해
  EditMode 테스트한다. 프리팹 조립은 "View 타입 → GameObject" 함수로 분리해 테스트용
  더미 `UIView` 파생 타입으로 검증한다.
- **수동 검증 항목**: 도메인 리로드 왕복(스크립트 생성 → 컴파일 → 프리팹 조립 → 프리팹 모드
  진입)과, 환경 씬에서 프리팹이 붙는 위치.

## 5. 구현 순서

순서가 강제된다 — 마법사가 여는 프리팹 모드가 제대로 보이려면 편집 환경이 있어야 하고,
편집 환경에는 루트 프리팹이 먼저 있어야 한다.

1. **3.1 런타임 프리팹 전환** (`UIRoot` MonoBehaviour화, Settings 변경, 폴백, 테스트 재작성)
2. **3.2 프리팹 편집 환경** (루트 프리팹 생성 메뉴, 환경 씬 생성/해제 메뉴)
3. **3.3 생성 마법사** (ProjectSettings 기본값, 마법사 창, 2단계 생성, 템플릿)
4. **문서/버전** (UIService README 갱신 + 마이그레이션 절, 패키지 버전 업)

프로젝트 규약대로 STRUCTURAL 변경과 BEHAVIORAL 변경을 분리 커밋하고, 각 단계는
TDD 사이클(`/go` → `/green` → `/refactor` → `/commit`)로 진행한다.
