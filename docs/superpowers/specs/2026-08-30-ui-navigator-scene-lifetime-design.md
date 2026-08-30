# UINavigator 설계 — UIService의 씬 단위 수명 전환과 개명

- 상태: 설계 확정
- 작성일: 2026-08-30
- 범위: `Assets/FoundationDI/Runtime/Services/UIService/` → `Runtime/Managers/UINavigator/`, `Editor/UIService/` → `Editor/UINavigator/`, 테스트·샘플·호스트 씬
- 버전: 0.8.2 → 0.9.0 (공개 API 파괴적 변경)

---

## 배경 / 목표

`UIService`는 지금 **앱 전역 수명**이다. 루트 `LifetimeScope`에 싱글턴으로 등록되고, 캔버스(`UIRoot`)는 `DontDestroyOnLoad`로 앱이 끝날 때까지 살아 있다. 씬이 바뀌면 `SceneManager.activeSceneChanged`를 받아 **자식 UI만** teardown하고 캔버스는 남긴다.

이 모델은 두 가지 비용을 만든다.

1. **수명이 두 갈래다.** 캔버스는 앱 수명, 그 안의 내용물은 씬 수명. 그래서 "씬 전환 시 무엇을 지우고 무엇을 남기는가"를 `ClearContent()`가 손으로 관리해야 하고, 이 코드가 컨테이너의 `Dispose` 경로와 **별개의 두 번째 정리 경로**로 존재한다. 정리 로직이 둘이면 둘 중 하나만 고치는 버그가 생긴다.
2. **소유자가 불분명하다.** 루트 스코프가 소유하지만 실제 파괴 시점은 씬 이벤트가 결정한다. 이 프로젝트에서 씬 수명을 갖는 것들(`PoolManager`, `TutorialManager`)은 이미 `Managers/` 아래에 있고 `Manager` 접미사를 쓴다 — `UIService`만 이름과 위치가 실제 수명과 어긋나 있다.

목표:

- **수명을 하나로 만든다.** 씬 `LifetimeScope`가 소유하고, 씬이 죽으면 프리젠터·풀·캔버스가 전부 함께 죽는다. 정리 경로는 `Dispose()` 하나뿐이다.
- **이름이 수명을 말하게 한다.** `UINavigator`로 개명하고 `Runtime/Managers/` 아래로 옮긴다.
- 씬 하나 안의 `Page`/`Popup`/`Overlay`만 책임진다. 그 이상은 이 컴포넌트의 일이 아니다.

비목표:

- **씬을 가로지르는 UI(로딩 화면·페이드).** 씬 전환 구간을 덮는 UI는 이 컴포넌트의 범위 밖이며, 쓰는 프로젝트가 별도 상주 캔버스로 만든다. 근거는 아래 "결정 5".
- **빌더 API·표시 모드·트랜지션·풀링 동작의 변경.** `Page<T>()`/`Popup<T>()`/`Overlay<T>()` 체인과 `OperationQueue` 직렬화, View 풀링, 트랜지션 해석 우선순위는 **한 줄도 바뀌지 않는다**. 이번 작업은 수명과 이름만 다룬다.
- **하위 호환 별칭.** `IUIService`를 `[Obsolete]`로 남기지 않는다. 근거는 아래 "결정 7".

---

## 결정 사항과 근거

### 1. 씬 `LifetimeScope`가 소유한다 — 서비스 인스턴스와 캔버스 둘 다

`builder.RegisterUINavigator(settings)`를 **씬 `LifetimeScope`에서** 호출한다. 등록 코드 자체는 지금과 같다(`Lifetime.Singleton`은 "그 스코프 안에서 하나"라는 뜻이므로 씬 스코프에 두면 그대로 씬당 하나가 된다).

```csharp
public static void RegisterUINavigator(this IContainerBuilder builder, UINavigatorSettings settings)
{
    builder.RegisterInstance(settings);
    builder.Register<UIInstanceFactory>(Lifetime.Singleton);
    builder.Register<UINavigator>(Lifetime.Singleton).As<IUINavigator>();
}
```

전제는 그대로다 — 호출 전에 `IResourceService`가 이미 등록되어 있어야 한다. 루트 스코프에 있어도 되고(VContainer 자식 스코프가 부모에서 해결한다) 씬 스코프에 있어도 된다.

**참조 카운트가 씬 단위로 균형을 이룬다.** 전용 `PoolManager`는 `Dispose()`에서 자기가 로드한 키마다 `IResourceService.Release(key)`를 부른다(`PoolManager.cs:106`). 따라서 `ResourceService`가 루트 수명으로 살아남더라도 씬이 잡은 참조는 씬이 죽을 때 전부 반납된다.

### 2. 캔버스는 계속 프리팹 지연 인스턴스화, `DontDestroyOnLoad`만 걷어낸다

첫 표시 시점에 `UINavigatorSettings.RootPrefab`을 `Instantiate`하는 현재 방식을 유지한다. 부모 지정 없이 인스턴스화하면 **활성 씬에 붙으므로**, `DontDestroyOnLoad` 호출을 지우는 것만으로 캔버스가 씬에 귀속된다.

씬 하이어라키에 `UIRoot`를 직접 저작하고 `RegisterComponentInHierarchy`로 주입하는 안은 채택하지 않는다. 씬마다 배치를 강제하고 미배치 실패 경로를 새로 만드는데, 얻는 것(에디터에서 미리 보임)은 `Create UI Root Prefab`으로 만든 프리팹을 프리팹 모드에서 열면 이미 얻어진다.

### 3. `activeSceneChanged` 경로를 전면 삭제한다

`SceneManager.activeSceneChanged` 구독, `OnActiveSceneChanged`, 씬 전환용 `ClearContent()` 호출을 모두 없앤다. 씬 전환 대응이 **"인스턴스가 통째로 죽는다"** 하나로 수렴한다.

`ClearContent()`의 본문(활성 프리젠터 teardown + 컨트롤러 비우기 + 풀 dispose)은 살아남아 `Dispose()`의 일부가 된다. 사라지는 것은 이 정리를 **씬 이벤트가 촉발하던 경로**다.

이것이 이번 변경의 핵심 이득이다. 정리 경로가 둘(씬 이벤트 / 컨테이너 dispose)에서 하나로 준다.

### 4. `Dispose`를 파괴 순서에 대해 견고하게 만든다

씬 언로드 시 Unity의 `GameObject` 파괴와 `LifetimeScope`의 컨테이너 dispose는 **순서가 보장되지 않는다.** 따라서 두 방향을 모두 막는다.

**(a) dispose 이후 재생성 금지** — `Root` getter에 `_disposed` 가드를 넣는다. 현재 getter는 `_root == null`이면 무조건 새 캔버스를 만든다. dispose 도중이나 이후에 이 경로가 타면 **파괴되는 씬이 아니라 다음 씬에 고아 캔버스가 남는다.** 지금은 `Page`/`Popup`/`Overlay` 진입점에만 `_disposed` 검사가 있어 내부 경로(`Pool` getter → `Root`)가 뚫려 있다.

**(b) 이미 파괴된 캔버스에 대한 dispose 허용** — 캔버스가 씬과 함께 먼저 파괴된 뒤 `Dispose()`가 오는 경우 예외 없이 통과해야 한다. 현재 `if (_root != null && _root.GO != null)` 검사가 이 역할을 하고, `_pool?.Dispose()` 경로도 같은 가정 위에서 이미 방어되어 있다(`PoolManager.cs:111` 주석). 테스트로 이 계약을 고정한다.

### 5. 씬을 가로지르는 UI는 범위 밖이다

캔버스가 씬과 함께 죽으므로, **씬 A 언로드 ~ 씬 B 스코프 기동 사이에는 이 컴포넌트가 그리는 UI가 하나도 없다.** 지금까지는 `DontDestroyOnLoad` 캔버스가 이 구간을 덮고 있었다.

이 구간(로딩 화면·페이드)은 쓰는 프로젝트가 자기 상주 캔버스로 처리한다. `UINavigator`에 "일부 레이어만 상주" 같은 예외를 두면 결정 1에서 얻은 단일 수명이 곧바로 무너진다 — 어떤 프리젠터가 어느 수명에 속하는지 호출부가 매번 알아야 하고, 리페어런팅과 정렬 순서 관리가 따라 들어온다.

### 6. Additive 씬 = 캔버스 여러 개 (의도된 결과)

씬 둘이 각자 `LifetimeScope`를 가지면 `UINavigator`도 둘, 캔버스도 둘이다. 이것은 "각 씬이 자기 UI를 갖는다"의 직접적 귀결이므로 막지 않는다. 겹침 정렬은 프로젝트가 각 `RootPrefab`의 `Canvas.sortingOrder`로 정한다 — 코어는 관여하지 않는다.

### 7. Clean break — 구 이름을 남기지 않는다

`IUIService`/`RegisterUIService`를 `[Obsolete]` 별칭으로 남기지 않고 전부 치환한다.

수명까지 함께 바뀌기 때문이다. 별칭만 남기면 기존 코드가 **컴파일은 되면서 조용히 틀린 동작을 한다** — 루트 스코프의 `RegisterUIService(...)` 호출이 그대로 컴파일되어 전역 수명을 유지하고, 이름만 바뀐 줄 알던 사용자는 씬 UI가 안 죽는 이유를 찾아 헤맨다. 컴파일 에러로 터뜨려 등록 위치를 다시 보게 만드는 편이 낫다.

`package.json`을 0.9.0으로 올리고 README에 구→신 대응표와 **등록 위치 이동** 안내를 싣는다.

### 8. 프리팹 편집 환경 메뉴를 제거한다

`Tools/FoundationDI/UI/Setup Prefab Editing Environment`와 `Clear Prefab Editing Environment`를 없앤다. UI 디자이너용 확인 방법은 나중에 다시 설계한다.

삭제 대상: `Editor/UIService/UIEditingEnvironment.cs`, `Tests/Editor/UIService/UIEditingEnvironmentTests.cs`, `Assets/UIEditingEnvironment.unity`(+ `.meta`).

유지: `Create UI Root Prefab`, `Create UI Element...` — 확인용이 아니라 생성용이다.

이 제거는 수명 변경과 무관하므로 **별도의 첫 커밋**으로 분리한다.

---

## 이름 맵

| 현재 | 변경 후 |
|---|---|
| `Runtime/Services/UIService/` | `Runtime/Managers/UINavigator/` |
| `Editor/UIService/` | `Editor/UINavigator/` |
| `Tests/Editor/UIService/`, `Tests/Runtime/UIService/` | `Tests/Editor/UINavigator/`, `Tests/Runtime/UINavigator/` |
| `IUIService` | `IUINavigator` |
| `UIService` | `UINavigator` |
| `UIServiceSettings` (클래스 + `.asset`) | `UINavigatorSettings` |
| `UIServiceVContainerExtensions` | `UINavigatorVContainerExtensions` |
| `RegisterUIService` | `RegisterUINavigator` |
| GO 이름·로그 접두어 `[UIService]` | `[UINavigator]` |
| 테스트 클래스 `UIService*Tests` | `UINavigator*Tests` |

**개명하지 않는 것**: `UIPresenter`, `UIPagePresenter`, `UIPopupPresenter`, `UIOverlayPresenter`, `UIView`, `UIRoot`, `UIPrefabAttribute`, `UIPrefabKeyResolver`, `UIInstanceFactory`, `IUIElementHost`, `OperationQueue`, `PageController`/`PopupController`/`OverlayController`, 트랜지션 일체(`IUITransition`, `FadeTransition`, …), 에디터 마법사(`UIElement*`).

이들은 **서비스 이름이 아니라 표시 요소의 이름**이다. `UI` 접두어는 계속 정확하고, 개명하면 diff만 커진다. `[UIPrefab("키")]` 속성 이름이 그대로라 사용자 코드의 프리젠터 선언부는 손댈 필요가 없다.

네임스페이스는 `DarkNaku.FoundationDI` 그대로다.

**GUID 보존**: 이동은 `git mv`로 `.cs`와 `.cs.meta`를 함께 옮긴다. 프리팹과 `.asset`의 스크립트 참조는 GUID 기반이므로 파일명·클래스명이 바뀌어도 끊기지 않는다. `RootLifetimeScope.prefab`의 `settings` 필드 참조도 필드 이름이 유지되므로 그대로다.

---

## 호출부 변경

- **호스트 프로젝트**: `RootLifetimeScope.Configure`에서 `RegisterUIService(settings)`를 제거하고, `Assets/Scenes/Test.unity`에 `SceneLifetimeScope`(신규, `Assets/Scripts/LifetimeScopes/`)를 배치해 그쪽에서 `RegisterUINavigator(settings)`를 부른다. VContainer가 부모 스코프를 자동 탐색하므로 `IResourceService`는 루트에서 계속 해결된다. 씬 수명이 실제로 도는지 에디터에서 확인할 수 있는 유일한 경로다.
  - `TestHubBootstrap`(`IUIService` 생성자 주입)과 `TestHubPresenters`의 `[Inject] IUIService` 필드도 함께 옮겨진 스코프에서 해결되어야 한다 — `RegisterEntryPoint<TestHubBootstrap>()`도 씬 스코프로 이동한다.
- **샘플 5종**: `SampleLifetimeScope`는 이미 씬에 배치된 `LifetimeScope`라 **이름만 치환하면 그대로 씬 스코프**가 된다. 등록 위치 이동이 필요 없다.
- **`TutorialManager`**: 코드는 `UIView`/`UIPresenter`만 참조하므로 무영향. README의 "`UIRoot`의 `DontDestroyOnLoad`와 무관하게 동작한다" 서술만 갱신한다(이제 `UIRoot`가 `DontDestroyOnLoad`가 아니다 — 결론은 같지만 근거가 바뀐다).
- **`DummyAdCanvas`**: `UIView`를 쓰지 않는 자체 캔버스라 무영향.

---

## 테스트 계획

### 폐기

`Tests/Runtime/UIService/UIServiceSceneResetTests.cs` — 두 테스트 모두 "씬 전환 시 인스턴스는 살고 내용만 리셋된다"를 전제한다. 그 동작이 사라지므로 파일째 교체한다.

### 신규: `UINavigatorDisposeTests` (PlayMode)

- Dispose하면 캔버스 GO가 파괴된다
- Dispose하면 활성 presenter가 `OnAfterHide`까지 teardown된다
- 캔버스는 `DontDestroyOnLoad` 씬이 아니라 활성 씬에 속한다
- 활성 씬이 바뀌어도 인스턴스가 스스로 내용을 리셋하지 않는다 (결정 3의 회귀 방지)
- Dispose 이후 `Page` 요청은 `ObjectDisposedException`이고 캔버스를 새로 만들지 않는다 (결정 4a)
- 캔버스가 먼저 파괴된 뒤 Dispose해도 예외가 없다 (결정 4b)

### 이름 치환만

나머지 EditMode 15개 · PlayMode 12개 파일은 타입명·클래스명 치환 후 그대로 통과해야 한다. **커밋 2(개명) 시점에 전체 그린이 유지되는 것이 개명이 순수 구조 변경이라는 증거다.**

`UIEditingEnvironmentTests.cs`는 결정 8에 따라 삭제한다.

---

## 커밋 분할

구조/행동을 섞지 않는다.

1. `[STRUCTURAL]` 프리팹 편집 환경 메뉴를 제거한다
2. `[STRUCTURAL]` UIService를 UINavigator로 개명하고 Managers 아래로 옮긴다 — 동작 무변, 전 테스트 그린
3. `[BEHAVIORAL]` UINavigator를 씬 수명으로 전환한다 — 위 신규 테스트 목록을 항목별 RED→GREEN
4. `[STRUCTURAL]` 호스트 씬을 씬 스코프로 재배선한다 (`SceneLifetimeScope` + `Test.unity`)
5. `[STRUCTURAL]` 문서·샘플·CLAUDE.md를 갱신하고 0.9.0으로 올린다

커밋 2와 3의 순서가 중요하다. 개명을 먼저 하면 3에서 쓰는 새 테스트가 처음부터 최종 이름으로 작성되어, 행동 변경 diff에 이름 치환이 섞이지 않는다.

---

## 마이그레이션 안내 (README에 실을 내용)

| 구 (0.8.x) | 신 (0.9.0) |
|---|---|
| `IUIService` | `IUINavigator` |
| `UIServiceSettings` | `UINavigatorSettings` |
| `builder.RegisterUIService(settings)` | `builder.RegisterUINavigator(settings)` |

**등록 위치가 바뀐다**: 루트 `LifetimeScope` → 씬 `LifetimeScope`. `IResourceService`는 루트에 남겨도 된다.

**동작이 바뀐다**: 씬이 언로드되면 캔버스·풀·프리젠터가 모두 파괴된다. 씬을 가로질러 살아남아야 하는 UI(로딩 화면·페이드)는 이 컴포넌트 밖에서 별도 캔버스로 만든다.

---

## 덜어낸 것

- **`[Obsolete]` 호환 별칭** — 결정 7. 조용히 틀린 동작보다 컴파일 에러가 낫다.
- **레이어 단위 상주 옵션** — 결정 5. 단일 수명을 즉시 무너뜨린다.
- **루트 스코프 등록 동시 지원(멀티 인스턴스)** — 캔버스 두 개의 정렬 규칙과 주입 식별(자식 스코프 shadowing) 규칙을 코어가 떠안게 된다. 필요해지면 그때 별도 설계로 다룬다.
- **씬 하이어라키 저작형 `UIRoot`** — 결정 2.
- **`UIPresenter`/`UIView` 계열 개명** — 표시 요소 이름은 이미 정확하다.

---

## 리스크

- **대량 파일 이동**: 런타임 26 + 에디터 9 + 테스트 27 파일이 폴더째 움직인다. `.meta` 동반 이동이 빠지면 프리팹·`.asset` 참조가 끊긴다. `git mv`로 쌍을 유지하고, 이동 직후 Unity 리프레시 → 콘솔 에러 0 → 전체 테스트 그린을 커밋 2의 완료 조건으로 삼는다.
- **`.asset` 파일명 변경**: `UIServiceSettings.asset` → `UINavigatorSettings.asset`. GUID는 유지되므로 `RootLifetimeScope.prefab`의 참조는 살아 있지만, 이름을 바꾸면서 씬/프리팹을 저장하지 않으면 에디터 재시작 후에야 반영되는 것처럼 보일 수 있다.
- **호스트 씬 재배선**: `Test.unity` 편집이 필요하다. 스코프 부모 관계를 잘못 잡으면 `IResourceService` 해결에 실패한다 — 씬 스코프의 `parentReference`가 `RootLifetimeScope`를 찾도록 확인한다.
