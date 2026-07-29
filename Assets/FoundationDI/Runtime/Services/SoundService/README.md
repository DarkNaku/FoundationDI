# SoundService

SFX/BGM 재생과 **사운드 카탈로그**, **비동기 프리로드**를 제공하는 사운드 서비스입니다. 사운드를 논리 **문자열키**로 식별하고, 카탈로그가 보유한 **`AudioClip` 직접 참조**로 재생합니다. 버튼에 붙여 클릭 시 사운드를 재생하는 `SoundButton` 컴포넌트도 포함합니다.

- **사운드 카탈로그** — 문자열키 → **`AudioClip` 직접 참조**(`SoundCatalogSO` ScriptableObject). `Play("Jump")`처럼 친숙한 이름으로 재생
- **엄격 모드** — 카탈로그에 없는 키는 `Debug.LogError` 후 무시(오타/누락 조기 발견)
- **비동기 프리로드** — `PreloadAsync()`로 `Preload=true` 클립의 `AudioClip.LoadAudioData()`를 병렬 대기, 첫 재생 지연 제거
- **영속화** — SFX/BGM 볼륨과 활성화 상태를 `PlayerPrefs`에 저장
- **프레임당 중복 방지** — R3 `Observable.EveryUpdate`로 같은 프레임에 같은 SFX가 겹쳐 재생되는 것을 차단

---

## 사용법

### 1) 사운드 카탈로그 에셋 생성

프로젝트 창에서 **Create → DarkNaku → SoundCatalog**로 에셋을 만들고 항목을 채웁니다.

| 필드 | 설명 |
| --- | --- |
| `Key` | 논리 이름. `Play`의 인자, `SoundButton` 드롭다운에 표시 (예: `Jump`) |
| `Clip` | 재생할 `AudioClip`. 인스펙터에서 직접 드래그하는 직접 참조 |
| `Preload` | 프리로드 대상 여부 |

### 2) DI 등록 (VContainer)

`SoundService`는 등록된 `ISoundCatalog`에서 클립을 직접 가져와 재생하므로 `IResourceService` 등록은 필요하지 않습니다.

```csharp
using VContainer;
using VContainer.Unity;
using DarkNaku.FoundationDI;

public class RootLifetimeScope : LifetimeScope
{
    [SerializeField] private SoundCatalogSO _soundCatalog;

    protected override void Configure(IContainerBuilder builder)
    {
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
        await _sound.PreloadAsync();   // Preload=true 클립의 LoadAudioData()를 병렬 대기 (로딩 화면 등에서)
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
| `PreloadAsync` | `UniTask PreloadAsync()` | 카탈로그의 `Preload=true` 클립들의 `AudioClip.LoadAudioData()`를 병렬 대기 |

### `ISoundCatalog`

문자열키 → `AudioClip` 매핑을 추상화한 seam. `SoundService`는 이 인터페이스에 의존하므로 테스트에서 mock으로 대체할 수 있습니다.

```csharp
public interface ISoundCatalog
{
    bool TryGetClip(string key, out AudioClip clip); // 문자열키 → AudioClip
    IReadOnlyList<string> Keys { get; }               // SoundButton 드롭다운 소스
    IEnumerable<AudioClip> PreloadClips { get; }       // Preload=true 항목의 AudioClip
}
```

### `SoundCatalogSO : ScriptableObject, ISoundCatalog`

`[CreateAssetMenu(menuName = "DarkNaku/SoundCatalog")]`. 직렬화된 `SoundEntry` 목록을 보유하고, 첫 조회 시 키→`AudioClip` 사전을 lazy 빌드합니다. 중복 `Key`는 마지막 값을 채택하고 경고를 남깁니다.

```csharp
[Serializable]
public struct SoundEntry
{
    public string Key;        // 논리 이름
    public AudioClip Clip;    // 재생할 AudioClip 직접 참조
    public bool Preload;      // 프리로드 대상 여부
}
```

### `SoundButton : InjectableBehaviour`

`[RequireComponent(typeof(Button))]`. `Button.onClick` → `Play()` → 주입된 `ISoundService.Play(key)`. `_sound`가 주입되지 않았으면 에러 로그 후 무시합니다.

### DI 등록

```csharp
public static void RegisterSoundService(this IContainerBuilder builder, SoundCatalogSO catalog);
```
`ISoundCatalog` 인스턴스 등록 + `ISoundService`/`SoundService` 싱글톤 등록. 클립을 카탈로그에서 직접 가져오므로 `IResourceService` 등록은 필요하지 않습니다.

---

## 매뉴얼

### 카탈로그 키 모델

- **문자열키(`Key`)** 가 `AudioClip`을 **직접 참조**합니다. `Play`/드롭다운은 문자열키를 사용하고, 재생 시 카탈로그에서 바로 해당 `AudioClip`을 가져옵니다.
- 인스펙터에서 클립을 드래그해 연결하므로 리소스 경로/키 문자열을 별도로 관리할 필요가 없습니다.
- 여러 문자열키가 같은 `AudioClip`을 가리켜도 됩니다(예: `Click`/`Tap` → 같은 효과음).

### 엄격 모드

- `Play`/`PlayBGM`에 카탈로그에 없는 키가 들어오면 `Debug.LogError` 후 무시합니다. 모든 사운드가 카탈로그를 거치므로 오타/누락을 즉시 발견할 수 있습니다.

### 프리로드

- `PreloadAsync()`는 `Preload=true` 클립들에 대해 `AudioClip.LoadAudioData()`를 호출하고 **병렬 대기**(`UniTask.WhenAll`)합니다.
- **전제: 클립 임포트 설정의 "Load In Background"** 가 켜져 있어야 실제로 비동기 디코딩됩니다. 꺼진 상태의 압축 클립은 `LoadAudioData()` 호출 자체가 메인 스레드를 동기 블로킹하므로, 프리로드 대상 클립은 이 옵션을 켜두는 것을 권장합니다.
- 로딩 화면 등에서 `await PreloadAsync()` 후 게임플레이를 시작하면 첫 재생 시 디코딩 히치가 사라집니다.

### 직접 참조라 런타임 로딩 위임 없음

- 클립은 `SoundCatalogSO`가 인스펙터에서 연결된 `AudioClip`을 컴파일 타임 직접 참조로 보유합니다. `Resources`/`Addressables`/`IResourceService`를 통한 런타임 로딩이나 참조 카운팅 해제가 없습니다.

### 볼륨 / 활성화 영속

- `VolumeSFX`/`VolumeBGM`/`SFXEnabled`/`BGMEnabled`는 `PlayerPrefs`에 저장됩니다. 저장값이 없으면 볼륨은 1, 활성화는 true가 기본입니다.
- `BGMEnabled`/`SFXEnabled`가 false면 해당 재생 호출이 무시됩니다.

### 프레임당 중복 방지

- 같은 프레임에 같은 키의 `Play`가 여러 번 호출되면 1회만 재생합니다(여러 오브젝트가 동시에 같은 효과음을 낼 때 소리 겹침 방지). R3 `Observable.EveryUpdate(PostLateUpdate)`로 매 프레임 말에 초기화됩니다.

### 테스트

- `Tests/SoundServiceTest.cs`는 `ISoundCatalog`를 NSubstitute로 대체해 `SoundService`의 재생·엄격 모드·프리로드 배선을 검증합니다.
- `Tests/SoundCatalogTest.cs`는 `SoundCatalogSO`에 `SerializedObject`로 `_entries`에 클립을 직접 주입해 키→클립 변환·중복 키 처리를 검증합니다.
- `Tests/SoundButtonTest.cs`는 `ISoundService`를 NSubstitute로 대체해 `SoundButton`의 클릭 → `Play(key)` 배선을 검증합니다.

### 한계 / 후속 과제

- **에러 처리** — 프리로드가 실제로 비동기인지, 메인 스레드를 블로킹하는지는 클립의 "Load In Background" 임포트 설정에 좌우됩니다(현재 `AudioClip.LoadAudioData()` 실패/예외에 대한 별도 처리는 없음).
- **스레드 안전성 없음** — Unity 메인 스레드 사용을 전제로 합니다.
- **카탈로그 정합성** — `SoundButton`의 에디터용 `Catalog`와 DI 등록 `ISoundCatalog`가 동일 에셋인지는 사용자 책임입니다.
