# 씬 컴포넌트 DI 인프라 + SoundButton 설계

- 작성일: 2026-06-29
- 대상: `Assets/FoundationDI/Runtime/DI/`(신규), `Assets/FoundationDI/Runtime/Services/SoundService/`, `Assets/FoundationDI/Editor/`(신규)
- 관련 규약: CLAUDE.md "SERVICE ARCHITECTURE", VContainer 주입 패턴

## 1. 목적

씬에 디자이너가 직접 배치하는(=LifetimeScope가 생성하지 않는) MonoBehaviour에 VContainer 의존성을 주입하는 **재사용 인프라**를 만들고, 그 첫 사용처로 **SoundButton**(클릭 시 카탈로그에서 고른 사운드를 재생하는 컴포넌트)을 구현한다.

문제: `RootLifetimeScope`는 호스트(`Assets/Scripts`) 타입이라 패키지(`FoundationDI`)가 직접 참조할 수 없고, 씬 배치 컴포넌트는 위치·계층·초기화 순서가 불확정이다.

## 2. 설계 결정 (확정)

1. **주입 전달**: 컴포넌트가 정적 진입점에 자신을 등록하면 인프라가 주입하는 **요청 큐 + self-inject** 방식.
2. **큐 처리**: 이벤트 드리븐 flush(폴링 없음). Request 시 컨테이너가 준비됐으면 즉시 `Inject`, 아니면 보류했다가 컨테이너 준비 시 1회 flush.
3. **통합**: 정적 파사드와 EntryPoint를 한 클래스 `InjectorService`로 통합.
4. **베이스 클래스**: `InjectableBehaviour`가 `Awake`에서 멱등 self-request를 캡슐화.
5. **키 선택 UX**: SoundButton에 `SoundCatalog` 참조 + `key` 문자열, 커스텀 드롭다운(신규 Editor asmdef).
6. **클릭 트리거**: `UnityEngine.UI.Button.onClick` 후킹, `[RequireComponent(typeof(Button))]`.

## 3. 아키텍처

### 3.1 `InjectorService` — DI 인프라 코어

정적 파사드(요청 큐 + resolver) + VContainer EntryPoint(`IStartable`/`IDisposable`)를 한 클래스로 통합.

```csharp
public sealed class InjectorService : IStartable, IDisposable
{
    private static IObjectResolver _resolver;
    private static readonly List<MonoBehaviour> _pending = new();

    private readonly IObjectResolver _resolverToBind;
    public InjectorService(IObjectResolver resolver) => _resolverToBind = resolver;

    public void Start()                         // 컨테이너 준비 시 1회 flush
    {
        _resolver = _resolverToBind;
        foreach (var t in _pending) if (t != null) _resolver.Inject(t);
        _pending.Clear();
    }

    public static void Request(MonoBehaviour target)
    {
        if (target == null) return;
        if (_resolver != null) { _resolver.Inject(target); return; }  // 준비됨 → 즉시
        _pending.Add(target);                                          // 미준비 → 보류
    }

    public void Dispose()                       // 정적 잔재 정리(도메인 리로드 off 대비)
    {
        _resolver = null;
        _pending.Clear();
    }
}
```

- 동적 생성 컴포넌트는 런타임(컨테이너 준비 완료 상태)에 `Request`하므로 즉시 주입된다.
- 정적 상태의 생명주기: 에디터에서 "도메인 리로드 비활성화" 시 플레이 종료 후 정적 잔재가 남으므로 `Dispose`에서 정리한다.

### 3.2 `InjectorVContainerExtensions`

```csharp
public static class InjectorVContainerExtensions
{
    public static void RegisterInjector(this IContainerBuilder builder)
    {
        builder.RegisterEntryPoint<InjectorService>();
    }
}
```
호스트 `RootLifetimeScope.Configure`에서 `builder.RegisterInjector();` 1줄. (VContainer가 `IObjectResolver`를 자동 제공하므로 별도 전제 없음.)

### 3.3 `InjectableBehaviour` — 씬 컴포넌트 베이스

```csharp
public abstract class InjectableBehaviour : MonoBehaviour
{
    private bool _requested;

    protected virtual void Awake() => EnsureInjected();

    protected void EnsureInjected()             // 멱등 + lazy 안전망
    {
        if (_requested) return;
        _requested = true;
        InjectorService.Request(this);
    }
}
```
`[Inject]` 필드를 가진 어떤 씬 컴포넌트든 상속하면 위치·계층·순서 무관하게 주입된다.

### 3.4 `SoundButton` — 첫 사용처

```csharp
[RequireComponent(typeof(Button))]
public sealed class SoundButton : InjectableBehaviour
{
    [Inject] private ISoundService _sound;
    [SerializeField] private SoundCatalog _catalog;
    [SerializeField] private string _key;

    protected override void Awake()
    {
        base.Awake();                           // self-request
        GetComponent<Button>().onClick.AddListener(Play);
    }

    public void Play()
    {
        EnsureInjected();                        // 안전망(미주입 시 시도)
        if (_sound == null)
        {
            Debug.LogError("[SoundButton] ISoundService가 주입되지 않았습니다.");
            return;
        }
        _sound.Play(_key);
    }
}
```
- `_catalog`는 에디터 드롭다운 소스 전용(런타임 재생은 `ISoundService`가 자체 카탈로그로 처리). 즉 SoundButton은 카탈로그를 직접 조회하지 않고 `_key`만 `ISoundService.Play`에 넘긴다.
- 주의: 인스펙터에서 SoundButton에 붙인 `_catalog`와 DI로 등록된 `ISoundCatalog`(SoundService가 쓰는 것)는 **동일 에셋이어야** `_key`가 런타임에 유효하다. 이 정합성은 사용자 책임(문서화).

### 3.5 키 드롭다운 (Editor)

- 신규 `Assets/FoundationDI/Editor/FoundationDI.Editor.asmdef` (참조: `FoundationDI`, `UnityEditor`).
- `SoundButton` 전용 커스텀 `Editor`(또는 `_key`용 PropertyDrawer)가 같은 오브젝트의 `_catalog.Keys`를 팝업(`EditorGUILayout.Popup`)으로 표시.
- `SoundCatalog.Keys`는 lazy 빌드라 에디터에서도 동작.
- `_catalog` 미할당이면 일반 텍스트 필드로 폴백.

## 4. 데이터 흐름

1. 부팅: 호스트가 `RegisterInjector()` 등록. SoundService도 등록(이전 작업).
2. 씬 로드: SoundButton.Awake → `InjectorService.Request(this)`. 컨테이너 준비 전이면 보류 큐.
3. 컨테이너 준비: `InjectorService.Start()` → 보류분 flush → SoundButton의 `[Inject] _sound` 채워짐.
4. 클릭: `Button.onClick` → `Play()` → `_sound.Play(_key)` → SoundService가 카탈로그로 리소스키 변환·재생.

## 5. 에러 처리

- `Play` 시 `_sound` null: `Debug.LogError` 후 무시(스코프 미구성/등록 누락 진단).
- `Request(null)`: 무시.
- 정적 상태 잔재: `InjectorService.Dispose`에서 정리.
- 카탈로그에 없는 `_key`: SoundService의 엄격 모드가 `Debug.LogError`(이전 작업에서 구현됨).

## 6. 위치 / asmdef

- `Runtime/DI/InjectorService.cs`, `Runtime/DI/InjectableBehaviour.cs`, `Runtime/DI/InjectorVContainerExtensions.cs` (네임스페이스 `DarkNaku.FoundationDI`)
- `Runtime/Services/SoundService/SoundButton.cs`
- `Editor/SoundButtonEditor.cs` + `Editor/FoundationDI.Editor.asmdef` (신규)

## 7. 테스트 전략 (EditMode, NSubstitute)

- `InjectorService`:
  - 컨테이너 준비 전 `Request` → 즉시 주입되지 않고 보류됨(`Inject` 미호출).
  - `Start`(resolver 바인딩) → 보류분에 대해 `Inject` 호출.
  - 준비 후 `Request` → 즉시 `Inject`.
  - `Dispose` → 정적 상태 초기화(이후 Request가 다시 보류로 동작).
  - 정적 상태라 `[SetUp]`/`[TearDown]`에서 `Dispose`로 리셋. `IObjectResolver`는 NSubstitute.
- `SoundButton`:
  - 클릭(`Play()` 호출) 시 주입된 `ISoundService.Play(_key)` 호출 검증. `_sound`/`_key`는 테스트에서 주입/설정(리플렉션 또는 내부 접근).
  - PropertyDrawer/Editor는 에디터 GUI라 단위 테스트 제외.

## 8. 범위 밖 (향후)

- 다른 종류의 씬 DI 컴포넌트(추후 `InjectableBehaviour` 재사용).
- SoundButton의 hover/long-press 등 추가 인터랙션.
- `_catalog`(에디터용)와 DI `ISoundCatalog`의 자동 정합성 검증.
