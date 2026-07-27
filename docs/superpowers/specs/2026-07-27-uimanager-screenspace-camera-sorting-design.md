# UIManager Screen Space - Camera + Sorting Layer 정렬 설계

작성일: 2026-07-27

## 배경 / 문제

`UIRoot`가 만드는 Canvas는 현재 `RenderMode.ScreenSpaceOverlay`이고 생성 직후
`Object.DontDestroyOnLoad(GO)`로 씬을 넘어 상주한다(`UIRoot.cs:18,27`). Overlay는 항상
모든 것 위에 최상단으로 그려지고 Sorting Layer 정렬에 참여하지 않는다. 따라서 UI를 월드
스프라이트 사이(예: 배경 위, 특정 게임플레이 오브젝트 아래)에 끼워 넣을 수 없다.

Sorting Layer 정렬을 하려면 Canvas를 `ScreenSpaceCamera`로 두고 씬의 카메라를
`worldCamera`로 물려야 한다. 그런데 카메라는 **씬 소속**(씬 언로드 시 파괴)인 반면 현재 Canvas는
`DontDestroyOnLoad` 싱글턴이라 카메라보다 오래 산다. 씬을 한 번 바꾸면 `worldCamera`
참조가 죽어 정렬이 다시 깨지는 근본 충돌이 있다.

## 목표

- UI Canvas를 `ScreenSpaceCamera`로 전환하고 그 씬의 `Camera.main`에 바인딩해, UI 전체가
  월드 스프라이트 정렬 스택의 **한 지점**(하나의 sortingLayer/sortingOrder)에 삽입되게 한다.
- UI 수명을 **씬 스코프**로 만든다: 씬이 바뀌면 UI(열린 Page/Popup + 풀 캐시)를 초기화하고,
  새 씬에서 그 씬의 카메라로 다시 구성한다.

## 결정된 요구사항 (브레인스토밍)

- UI는 **씬마다 초기화**된다(씬 전환 넘어 유지하지 않음).
- 정렬은 **UI 전체가 한 지점**이면 충분 → 단일 Canvas + sortingLayer/sortingOrder.
- 카메라는 **`Camera.main` 자동** 바인딩(사용자 추가 설정 없음).

## 접근 방식

**UIManager가 씬 생명주기를 내부에서 처리한다.** UIManager는 지금처럼 루트 싱글턴으로 등록된
채 두고(소비자의 DI/씬 구성 불변), 내부에서 `SceneManager.activeSceneChanged`를 구독해
씬 전환 시 UIRoot를 갈아끼운다.

대안이던 "자식 LifetimeScope로 진짜 씬 스코프화"는 모든 게임플레이 씬에 스코프 배치를
강제해 UPM 패키지 소비자 부담이 크므로 채택하지 않는다.

## 설계 결정

### 1. UIManagerSettings에 정렬/거리 필드 추가

`UIManagerSettings.cs`에 인스펙터 노출 필드 + 읽기 전용 프로퍼티 추가:

| 필드 | 타입 | 기본값 | 프로퍼티 | 용도 |
|---|---|---|---|---|
| `_sortingLayerName` | `string` | `"Default"` | `SortingLayerName` | Canvas가 얹힐 Sorting Layer 이름 |
| `_sortingOrder` | `int` | `0` | `SortingOrder` | 같은 레이어 내 정렬 순서 |
| `_planeDistance` | `float` | `100f` | `PlaneDistance` | ScreenSpaceCamera 평면 거리 |

### 2. UIRoot 렌더 방식 전환 (`UIRoot.cs`)

- 생성자를 `UIRoot(UIManagerSettings settings, Func<Camera> cameraProvider = null)`로 변경.
  - settings가 null이면 지금처럼 안전한 기본값(1920x1080, "Default", 0, 100) 사용.
  - `cameraProvider`가 null이면 내부 기본값 `() => Camera.main` 사용. 테스트용 seam.
- `Object.DontDestroyOnLoad(GO);` **한 줄 제거** → `new GameObject`는 생성 시점의 active
  씬에 자동 소속(= "인스턴스를 만드는 씬"에 귀속).
- 카메라 바인딩:
  - `cameraProvider()`가 카메라를 반환하면 → `renderMode = ScreenSpaceCamera`,
    `worldCamera = 그 카메라`, `planeDistance = settings.PlaneDistance`,
    `sortingLayerID = SortingLayer.NameToID(settings.SortingLayerName)`,
    `sortingOrder = settings.SortingOrder`.
    (존재하지 않는 레이어 이름은 `NameToID`가 0=Default를 돌려주므로 조용히 Default로 정렬된다.)
  - 카메라가 없으면(null) → `ScreenSpaceOverlay`로 **폴백** + `Debug.LogWarning`.
    (로딩 화면 등 MainCamera 태그 카메라가 없는 순간에도 UI가 최상단으로라도 뜨게.)

### 3. UIManager 씬 생명주기 (`UIManager.cs`)

**공통 리셋 로직 `ResetSceneState()` 추출** — 씬 전환과 Dispose가 공유:

1. `_queue.CancelAndClear()` — 진행 중 Show/Hide 취소 + 대기 큐 비움(내부에서 새 CTS로 교체돼
   재사용 가능, `OperationQueue.cs:53`).
2. `_active`의 각 presenter에 트랜지션 없이 `OnBeforeHide→BeforeHide→OnAfterHide→AfterHide`
   동기 발화 → R3/MessagePipe 구독 해제(현재 `Dispose()` 186–190행과 동일 패턴).
3. `_active`/`_pages`/`_popups`/`_overlays` clear.
4. `_pool?.Dispose()` — 리소스 핸들 반환 + 풀 루트 파괴(fake-null 가드 내장,
   `PoolManager.cs:97`).
5. `_root != null && _root.GO != null`이면 Canvas GO 파괴(씬 언로드로 이미 파괴됐을 수 있어
   가드).
6. `_root = null; _pool = null;` → 다음 `Page<T>()` 시 새 active 씬에서 재구성.

**씬 전환 구독**:

- 생성자에서 `SceneManager.activeSceneChanged += OnActiveSceneChanged`, `Dispose()`에서 해제.
- `OnActiveSceneChanged(prev, next)`: `_disposed`거나 `_root == null`(그 씬에서 UI 미사용)이면
  no-op, 아니면 `ResetSceneState()`.
- 리셋은 GO 생존 여부와 무관하게 안전 — 모든 Unity 오브젝트 접근을 fake-null로 가드하고,
  presenter teardown은 C# 레벨 콜백이라 View 파괴 순서에 영향받지 않는다.

**Lifetime 게터 방어(additive 언로드 대비)**:

```csharp
private UIRoot Root
{
    get
    {
        if (_root != null && _root.GO == null) ResetSceneState(); // 씬 파괴로 fake-null → 재구성
        return _root ??= new UIRoot(_settings);
    }
}
```

`activeSceneChanged`는 active 씬이 바뀔 때만 발화하므로, additive로 UI 씬만 언로드되는 엣지는
이 게터 방어가 잡는다.

**Dispose 정리**: `_disposed = true` → `activeSceneChanged` 구독 해제 → `ResetSceneState()`
호출로 통합.

## 테스트 전략

프로젝트의 seam 분리 관례를 살려 가능한 많은 부분을 EditMode로 끌어온다.

### EditMode (`FoundationDI.Tests`, 기존 어셈블리 재사용)

- `UIManagerSettings`의 `SortingLayerName`/`SortingOrder`/`PlaneDistance`가 설정값을 반환한다.
- `cameraProvider`가 카메라를 주면 UIRoot Canvas가 `ScreenSpaceCamera` + 지정
  sortingOrder/planeDistance/worldCamera로 구성된다.
- `cameraProvider`가 null(카메라 없음)이면 Canvas가 `ScreenSpaceOverlay`로 폴백한다.
- 생성된 Canvas GO가 `DontDestroyOnLoad` 씬에 있지 **않다**(생성 시점 씬에 소속).
- (내부 진입점 직접 호출) `ResetSceneState()`가 `_root`/`_pool`을 null로 만들고, 이후
  `Root` 접근이 새 UIRoot를 재구성한다(리셋 순서·멱등성).

### PlayMode (신규 `FoundationDI.Tests.PlayMode` asmdef)

- 실제 `SceneManager.LoadScene`로 active 씬을 바꾸면 `_root`/`_pool`이 재구성되고 이전
  presenter의 teardown(구독 해제)이 발생한다.
- additive로 UI 씬만 언로드된 뒤 `Page<T>()` 호출 시 게터 방어로 재구성된다.

## 작업 순서 (Tidy First — 구조/행동 분리 커밋)

1. `[STRUCTURAL]` `UIManagerSettings`에 필드/프로퍼티 추가(기본값으로 기존 동작 불변),
   `UIRoot` 생성자 시그니처를 `(UIManagerSettings, Func<Camera>)`로 변경 + 호출부(UIManager) 수정.
   렌더 방식은 아직 Overlay 그대로 두어 동작 불변.
2. `[STRUCTURAL]` PlayMode 테스트용 `FoundationDI.Tests.PlayMode` asmdef 신설.
3. `[BEHAVIORAL]` UIRoot 카메라 바인딩(ScreenSpaceCamera + 정렬) + DontDestroyOnLoad 제거.
   TDD(RED→GREEN→REFACTOR). `plan.md`에 테스트 항목 추가.
4. `[BEHAVIORAL]` UIManager 씬 전환 구독 + `ResetSceneState()` + 게터 방어. TDD.

## 범위 밖 (YAGNI)

- UI 요소마다 서로 다른 Sorting Layer(중첩 Canvas) — "한 지점이면 충분"으로 확정.
- 명시적 카메라 지정 API / 렌더 모드 토글 설정 — `Camera.main` 자동으로 확정.
- 자식 LifetimeScope 기반 씬 스코프화 — 소비자 부담으로 미채택.
- 씬 전환 넘어 UI/풀 유지 — "씬마다 초기화"로 확정.
- 여러 additive 씬을 동시에 쓰며 active 씬은 안 바뀌는 복잡한 구성에서의 명시적 씬 소유 지정.
