# SoundService

SFX/BGM 재생과 **사운드 카탈로그**, **비동기 프리로드**를 제공하는 사운드 서비스입니다. 사운드를 논리 **문자열키**로 식별하고, 클립 로딩은 [`IResourceService`](../ResourceService/README.md)에 위임합니다. 버튼에 붙여 클릭 시 사운드를 재생하는 `SoundButton` 컴포넌트도 포함합니다.

- **사운드 카탈로그** — 문자열키 → 리소스키 매핑(`SoundCatalogSO` ScriptableObject). `Play("Jump")`처럼 친숙한 이름으로 재생
- **엄격 모드** — 카탈로그에 없는 키는 `Debug.LogError` 후 무시(오타/누락 조기 발견)
- **비동기 프리로드** — `PreloadAsync()`로 `IResourceService.LoadAsync` 병렬 로드, 첫 재생 지연 제거
- **영속화** — SFX/BGM 볼륨과 활성화 상태를 `PlayerPrefs`에 저장
- **프레임당 중복 방지** — R3 `Observable.EveryUpdate`로 같은 프레임에 같은 SFX가 겹쳐 재생되는 것을 차단

---

## 사용법

### 1) 사운드 카탈로그 에셋 생성

프로젝트 창에서 **Create → DarkNaku → SoundCatalog**로 에셋을 만들고 항목을 채웁니다.

| 필드 | 설명 |
| --- | --- |
| `Key` | 논리 이름. `Play`의 인자, `SoundButton` 드롭다운에 표시 (예: `Jump`) |
| `ResourceKey` | `IResourceService` 로드 키(Addressables 주소/키) |
| `Preload` | 프리로드 대상 여부 |

### 2) DI 등록 (VContainer)

`SoundService`는 클립 로딩을 `IResourceService`에 위임하므로, `RegisterSoundService` **전에** `IResourceService`가 등록되어 있어야 합니다.

```csharp
using VContainer;
using VContainer.Unity;
using DarkNaku.FoundationDI;

public class RootLifetimeScope : LifetimeScope
{
    [SerializeField] private SoundCatalogSO _soundCatalog;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.Register<IResourceService, AddressableResourceService>(Lifetime.Singleton);
        builder.RegisterSoundService(_soundCatalog);
    }
}
```

### 3) 프리로드 / 재생

```csharp
public class GameFlow
{
    private readonly ISoundService _sound;
    public GameFlow(ISoundService sound) => _sound = sound;

    public async UniTask LoadAsync()
    {
        await _sound.PreloadAsync();   // Preload=true 항목을 병렬 로드 (로딩 화면 등에서)
    }

    public void OnJump() => _sound.Play("Jump");     // 문자열키로 SFX 재생
    public void OnTitle() => _sound.PlayBGM("Title"); // BGM 재생(루프)
}
```

### 4) 볼륨 / 활성화

```csharp
_sound.VolumeSFX = 0.8f;   // PlayerPrefs에 영속
_sound.BGMEnabled = false; // BGM 끄기(영속). 끈 상태에서 PlayBGM은 무시됨
```

### 5) SoundButton

`UnityEngine.UI.Button`이 있는 GameObject에 `SoundButton`을 붙이면 클릭 시 지정한 키의 사운드가 재생됩니다. 커스텀 인스펙터의 **Catalog** 드롭다운에서 **프로젝트 안의 `SoundCatalogSO`** 중 하나를 고르며(프로젝트에 카탈로그가 하나뿐이면 자동 선택), 카탈로그를 고르면 **Key**가 드롭다운으로 표시됩니다.

> `SoundButton`의 `Catalog`는 **에디터 키 드롭다운 소스 전용**입니다. 런타임 재생은 DI로 등록된 `ISoundCatalog`가 처리하므로, 둘은 **동일 에셋**이어야 `Key`가 유효합니다(프로젝트에 카탈로그가 하나뿐이면 자연히 일치). `SoundButton`은 씬 배치 컴포넌트 주입 인프라([InjectorService](../InjectorService/README.md))를 통해 `ISoundService`를 주입받습니다.

---

## API

### `ISoundService : IDisposable`

| 멤버 | 시그니처 | 설명 |
| --- | --- | --- |
| `SFXEnabled` | `bool { get; set; }` | SFX 재생 on/off. `PlayerPrefs`에 영속, 기본값 활성화 |
| `BGMEnabled` | `bool { get; set; }` | BGM 재생 on/off. `PlayerPrefs`에 영속, 기본값 활성화 |
| `IsPlayingBGM` | `bool { get; }` | 현재 BGM 재생 중 여부 |
| `VolumeSFX` | `float { get; set; }` | SFX 볼륨(0~1). `PlayerPrefs`에 영속 |
| `VolumeBGM` | `float { get; set; }` | BGM 볼륨(0~1). `PlayerPrefs`에 영속, 재생 중 BGM에 즉시 반영 |
| `Play` | `void Play(string key)` | 카탈로그 키로 SFX 1회 재생. 같은 프레임 중복 키는 무시 |
| `PlayBGM` | `void PlayBGM(string key)` | 카탈로그 키로 BGM 재생(루프). 기존 BGM은 교체 |
| `StopBGM` | `void StopBGM()` | BGM 정지 |
| `PreloadAsync` | `UniTask PreloadAsync()` | 카탈로그의 `Preload=true` 항목을 병렬 로드해 캐시를 채움 |

### `ISoundCatalog`

문자열키 → 리소스키 매핑을 추상화한 seam. `SoundService`는 이 인터페이스에 의존하므로 테스트에서 mock으로 대체할 수 있습니다.

```csharp
public interface ISoundCatalog
{
    bool TryGetResourceKey(string key, out string resourceKey); // 문자열키 → 리소스키
    IReadOnlyList<string> Keys { get; }                         // SoundButton 드롭다운 소스
    IEnumerable<string> PreloadResourceKeys { get; }            // Preload=true 항목의 리소스키
}
```

### `SoundCatalogSO : ScriptableObject, ISoundCatalog`

`[CreateAssetMenu(menuName = "DarkNaku/SoundCatalog")]`. 직렬화된 `SoundEntry` 목록을 보유하고, 첫 조회 시 키→리소스키 사전을 lazy 빌드합니다. 중복 `Key`는 마지막 값을 채택하고 경고를 남깁니다.

```csharp
[Serializable]
public struct SoundEntry
{
    public string Key;          // 논리 이름
    public string ResourceKey;  // IResourceService 로드 키
    public bool Preload;        // 프리로드 대상 여부
}
```

### `SoundButton : InjectableBehaviour`

`[RequireComponent(typeof(Button))]`. `Button.onClick` → `Play()` → 주입된 `ISoundService.Play(key)`. `_sound`가 주입되지 않았으면 에러 로그 후 무시합니다.

### DI 등록

```csharp
public static void RegisterSoundService(this IContainerBuilder builder, SoundCatalogSO catalog);
```
`ISoundCatalog` 인스턴스 등록 + `ISoundService`/`SoundService` 싱글톤 등록. **전제: `IResourceService` 선등록.**

---

## 매뉴얼

### 카탈로그 키 모델

- **문자열키(`Key`)** 와 **리소스키(`ResourceKey`)** 를 분리합니다. `Play`/드롭다운은 문자열키를, 실제 로딩은 리소스키를 사용합니다.
- 분리 이유: 리소스 경로가 바뀌어도 `Key`는 안정적이고, 인스펙터에 친숙한 이름을 노출할 수 있습니다.
- 여러 문자열키가 같은 리소스키를 가리켜도 됩니다(예: `Click`/`Tap` → 같은 효과음).

### 엄격 모드

- `Play`/`PlayBGM`에 카탈로그에 없는 키가 들어오면 `Debug.LogError` 후 무시합니다. 모든 사운드가 카탈로그를 거치므로 오타/누락을 즉시 발견할 수 있습니다.

### 프리로드

- `PreloadAsync()`는 `Preload=true` 항목의 리소스키를 `IResourceService.LoadAsync`로 **병렬 로드**(`UniTask.WhenAll`)하고 내부 캐시를 채웁니다.
- 중복 리소스키는 제거(`Distinct`)되어 한 번만 로드됩니다. 프리로드된 키는 이후 `Play` 시 추가 로드 없이 재생됩니다.
- 로딩 화면 등에서 `await PreloadAsync()` 후 게임플레이를 시작하면 첫 재생 지연이 사라집니다.

### 리소스 로딩 위임

- 클립 로딩은 `IResourceService`에 위임합니다. `SoundService`가 캐시한 리소스키는 `Dispose` 시 각각 `Release`되어 참조 카운팅 짝이 맞습니다.

### 볼륨 / 활성화 영속

- `VolumeSFX`/`VolumeBGM`/`SFXEnabled`/`BGMEnabled`는 `PlayerPrefs`에 저장됩니다. 저장값이 없으면 볼륨은 1, 활성화는 true가 기본입니다.
- `BGMEnabled`/`SFXEnabled`가 false면 해당 재생 호출이 무시됩니다.

### 프레임당 중복 방지

- 같은 프레임에 같은 키의 `Play`가 여러 번 호출되면 1회만 재생합니다(여러 오브젝트가 동시에 같은 효과음을 낼 때 소리 겹침 방지). R3 `Observable.EveryUpdate(PostLateUpdate)`로 매 프레임 말에 초기화됩니다.

### 테스트

- EditMode 단위 테스트(`Tests/SoundServiceTest.cs`, `Tests/SoundCatalogTest.cs`, `Tests/SoundButtonTest.cs`)는 `IResourceService`/`ISoundCatalog`를 NSubstitute로 대체해 위임·엄격 모드·프리로드·재생 배선을 검증합니다.

### 한계 / 후속 과제

- **에러 처리** — 프리로드 로드 실패 처리는 `IResourceService`의 동작에 위임합니다(현재 로드 예외 처리는 미구현).
- **스레드 안전성 없음** — Unity 메인 스레드 사용을 전제로 합니다.
- **카탈로그 정합성** — `SoundButton`의 에디터용 `Catalog`와 DI 등록 `ISoundCatalog`가 동일 에셋인지는 사용자 책임입니다.
