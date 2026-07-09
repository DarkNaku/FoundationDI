# UIManager View 풀링 전환 설계

- 날짜: 2026-07-09
- 대상: `Assets/FoundationDI/Runtime/Managers/UIManager/`
- 성격: 행동적(BEHAVIORAL) 리팩토링 — 라이프사이클 계약 변경 포함

## 배경 / 문제

현재 UIManager는 `InstanceCache`로 **Presenter+View 쌍을 통째로 타입 키 캐시**한다. 여기서 파생된 복잡성:

- `ResetTransient()` — 재사용 전 subscribers/transition override를 지우지만 사용자 필드는 안 지움 → 잔류 상태(stale) 버그원.
- `_active` ↔ `_cache` 상호 배타 invariant를 손으로 유지.
- `ShowAsync`의 "캐시 재사용 시 비활성 GO 복구 / 직전 오버라이드 잔류 방지" 방어 코드.
- `_active`가 타입 키라 **같은 타입 동시 1개** 제약(옛 캐시 키의 잔재).

## 핵심 아이디어

무거운 것과 가벼운 것을 분리한다.

- **View(GameObject)** = 무겁다 → **풀로 재사용**. 기존 `PoolManager`(`IPoolItem` 기반)를 재사용한다.
- **Presenter(순수 C#)** = 가볍다(`Activator.CreateInstance` + VContainer `Inject`) → **매 show마다 새로 생성**.

이로써 잔류 상태 버그 부류가 원천 제거되고, Presenter 수명이 `create → show → hide → 버림`으로 선형화된다.

## 라이프사이클 모델

### Presenter (fresh, 선형)

```
Activator.CreateInstance<T>() → Inject → Bind(view, host)
→ OnInitialize()        // 매 show 1회 (= presenter 일생 1회)
→ OnBeforeShow → (트랜지션 ShowAsync) → OnAfterShow
→ OnBeforeHide → (트랜지션 HideAsync) → OnAfterHide   // OnAfterHide = 유일한 teardown 지점
→ 참조 버림(GC)
```

- `LifecycleEvent.Destroyed`, `OnDestroyElement`, `Fire(Destroyed)` **삭제**. presenter의 "죽음"은 곧 hide이므로 예전에 destroy에서 하던 정리는 전부 `OnAfterHide`로 내려온다.
- `ResetTransient()` **삭제** — fresh presenter라 잔류 상태가 없다.

### UIView (풀 재사용, `IPoolItem` 내부 구현)

`UIView`가 `IPoolItem`을 **명시적(private) 구현**하고, subclass에는 UIView 고유 이름의 훅 2개만 노출한다. 풀 어휘(`OnGetItem`/`OnReleaseItem` 등)는 공개 API로 새지 않는다.

| IPoolItem (내부) | subclass 노출 | 시점 | 처리 |
|---|---|---|---|
| `OnCreateItem` | `OnInitializeView()` *(기존 유지)* | 물리 인스턴스당 1회 | 뷰 자체 초기화 + 풀 상주용 `SetActive(false)` |
| `OnGetItem` | (노출 없음) | 풀에서 꺼낼 때 | 비움 — 활성화는 show 흐름이 제어 |
| `OnReleaseItem` | (노출 없음) | 풀로 돌아갈 때 | `SetActive(false)` (안전) |
| `OnDestroyItem` | `OnDestroyView()` *(신규)* | GameObject 실제 파괴 | subclass 정리 훅 |

- per-show/per-hide 타이밍은 Presenter의 `OnBeforeShow`/`OnAfterShow`/`OnBeforeHide`/`OnAfterHide`와 트랜지션이 이미 커버하므로 View에 별도 노출 훅을 두지 않는다(YAGNI).
- `OnInitializeView`는 presenter 존재와 무관한 **뷰 자체** 초기화이므로, 물리 생성 시점(presenter 바인딩 이전)에 실행돼도 계약상 문제 없다.

## 풀 소유 & 수명 정합

- UIManager가 **전용 `PoolManager` 인스턴스**를 소유한다. DI에 등록되는 씬 스코프 `IPoolManager`와는 **별개**다(씬 스코프 풀을 그대로 쓰면 씬 언로드 시 pooled View가 파괴되지만 UIManager 루트 싱글턴은 살아남아 수명 불일치).
- 전용 풀은 `IResourceService`(생성자 전제 의존)와 **UIManager Canvas(DontDestroyOnLoad) 아래의 풀 루트**로 구성한다. `PoolManager`가 만드는 `[PoolManager]` 루트가 Canvas 자식이 되어 DontDestroyOnLoad를 상속.
- `UIManager.Dispose()` → `_pool.Dispose()`(= 전 View `OnDestroyItem` 파괴 + `IResourceService.Release`) → Canvas 파괴.
- 풀 키 = `UIPrefabKeyResolver.Resolve(presenterType)` (기존 prefab 키 문자열; `PoolManager`가 이미 string 키).

## 활성화 / 부모 지정 정책 (프레임 flash 방지)

- pooled View는 풀에 있는 동안 **비활성**(`OnCreateItem`에서 `SetActive(false)`).
- **Show**: `AttachTo(layer)`로 레이어에 부모 지정 → `SetActive(true)` → 트랜지션 `ShowAsync`(alpha 0/오프스크린에서 시작). 부모 지정과 활성화가 트랜지션 직전에 일어나 첫 가시 프레임부터 트랜지션 상태.
- **Hide**: 트랜지션 `HideAsync` → `SetActive(false)` → 풀 루트로 reparent → `_pool.Release(view)`. 레이어에 비활성 잔여물이 남지 않음.

## Show / Hide / Dispose 흐름

### Acquire (Page/Popup/Overlay 공통 진입)

```
key = Resolve(typeof(T))
view = _pool.Get<UIView>(key)          // 최초면 Instantiate + OnInitializeView(1회), 이후 재사용
if (view == null) throw InvalidOperationException(...)   // 로드 실패/UIView 부재
presenter = _factory.CreatePresenter(typeof(T), view, this)  // CreateInstance + Inject + Bind
presenter.OnInitialize()               // 매 show 1회
_active.Add(presenter)                 // 인스턴스 집합
enqueue ShowXxxAsync(presenter)
return (T)presenter
```

- **경고/dedup 없음**: `_active`는 인스턴스 집합(`HashSet<UIPresenter>`)이며 타입 키가 아니다. 같은 타입 요청은 항상 새 인스턴스를 만든다.
- 같은 타입 팝업/오버레이 **스택 허용**(중첩 다이얼로그 등).
- **Page 재요청** = 새 인스턴스로 교체(새로고침). 중복 호출 방지는 호출자 책임(프레임워크 가드 없음).

### ShowXxxAsync (큐에서 순차 실행)

```
await UniTask.Yield(Update)             // 빌더 체인 등록 보장 (현행 유지)
[Page] 기존 페이지 있으면 HideAsync 후 교체
컨트롤러 등록(_pages/_popups/_overlays) + AttachTo(layer) + RefreshInputBlocking
SetActive(true) → OnBeforeShow → view.ShowAsync → OnAfterShow
```

### HideAsync (RequestHide → 큐)

```
if (!_active.Contains(presenter)) return  // 인스턴스 기준 중복 hide 무시
OnBeforeHide → view.HideAsync → SetActive(false) → OnAfterHide(구독 해제)
reparent view → 풀 루트, _pool.Release(view)
_active/_pages/_popups/_overlays에서 제거, presenter 버림
RefreshInputBlocking
```

### Dispose

```
활성 presenter마다: OnBeforeHide → OnAfterHide 동기 발화(트랜지션 없이, 구독 해제용) → 버림
_pool.Dispose()   // OnDestroyItem으로 전 View 파괴 + ResourceService.Release
_active/컨트롤러 Clear, Canvas 파괴
```

## 구독 해제 계약 (중요)

fresh presenter가 pooled View의 위젯(버튼 등)에 매 show마다 이벤트를 건다. View가 풀로 돌아갔다가 **다음 fresh presenter가 같은 View를 재사용**하면 이전 구독이 남아 중복/유령 핸들러가 쌓인다.

**계약: presenter는 자신이 건 모든 View 구독을 `OnAfterHide`에서 해제한다.** 프레임워크는 자동 해제를 제공하지 않는다(수동 컨벤션).

- 스펙 필수 산출물: **README/샘플에서 이 패턴을 명확히 시연·문서화**한다.
- (범위 밖 옵션) 추후 R3 기반 show-lifetime DisposableBag 헬퍼를 추가할 수 있으나 이번엔 하지 않는다.

## 컴포넌트 책임 / 인터페이스 변경

- `UIInstanceFactory`: 프리팹 로드/Instantiate 책임을 **풀에 이관**. 남는 역할은 `CreatePresenter(Type, UIView, IUIElementHost)` = `CreateInstance` + `IObjectResolver.Inject` + `Bind`. 의존은 `IObjectResolver`만.
- `UIManager`: 생성자에 `IResourceService`(전용 풀 구성용) 추가. `_pool`(전용 PoolManager) 소유, `InstanceCache` 제거, `_active`를 인스턴스 집합으로 변경.
- `UIView`: `IPoolItem` 구현. `OnInitializeView`(기존), `OnDestroyView`(신규) 노출. 활성화/부모는 UIManager show/hide 흐름이 제어.
- `PoolManager`: UI 재사용을 위한 **최소 조정만**(가능하면 무변경). `Get<UIView>(key)`가 null 반환 시 UIManager가 예외 처리.

## 삭제 대상 요약

- `Controllers/InstanceCache.cs` (파일 삭제)
- `UIPresenter`: `LifecycleEvent.Destroyed`, `OnDestroyElement`, `Fire(Destroyed)` 경로, `ResetTransient()`
- `UIManager`: `_cache` 필드/사용, `Acquire`의 캐시 분기 및 "이미 활성" 경고, `ShowAsync`의 캐시 재사용 방어 코드

## 테스트 계획 (plan.md 항목화 대상)

1. 같은 타입 Show→Hide→Show 시 **새 Presenter 인스턴스**가 생성된다(인스턴스 동일성으로 검증).
2. Hide 후 View는 파괴되지 않고 **풀에 상주**, 재Show 시 **같은 View 재사용**된다(인스턴스 동일성).
3. 재Show 시 `OnInitializeView`는 **재호출 안 됨**(물리 1회), `OnInitialize`는 **매번 호출**된다.
4. `OnAfterHide`가 hide마다 호출된다(구독 해제 지점 보장).
5. 같은 타입 팝업을 2번 Show하면 **2개가 스택**된다(dedup 없음).
6. Page가 떠 있는 상태에서 같은 타입 Page 재요청 시 **기존 Hide + 새 인스턴스 Show**(교체).
7. Dispose 시 활성 presenter의 `OnAfterHide` 발화 + 풀 View 전부 파괴(`OnDestroyView` 호출).
8. (누수 가드) Show→Hide→Show 반복 후 View 위젯에 중복 핸들러가 없다(샘플 컨벤션 검증).

## 알려진 위험 / 범위 밖

- **구독 누수**: `OnAfterHide` 수동 해제를 잊으면 누수. 완화책은 문서/샘플. 자동화(R3 헬퍼)는 범위 밖.
- **같은 타입 다중 인스턴스**: 이제 허용되지만, 이를 적극 활용하는 신규 API(예: 명시적 인스턴스 핸들)는 이번 범위 아님. `_active` 인스턴스화까지만.
- **PoolManager 성숙도**: 현 코드는 ResourceService 위임 + fake-null 가드가 되어 있어 재사용 가능 수준으로 판단. 전용 인스턴스로 UI 수명에 귀속시켜 씬 스코프 이슈를 분리.
- 스레드 안전성 없음(메인 스레드 전제) — 현행 유지.
