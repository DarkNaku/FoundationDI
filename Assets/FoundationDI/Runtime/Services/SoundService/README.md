# SoundService

DI 기반 오디오 서비스. 태그 하나로 SFX·음악·플레이리스트·다이내믹 뮤직을 재생하고,
AudioMixer Output 볼륨과 3D 오클루전까지 한곳에서 다룬다.

진입점은 VContainer로 주입되는 `ISoundService` 하나이며, 런타임에 `Resources`를 쓰지 않는다.

---

## 1. 준비

### 1.1 설정 에셋 만들기

`Tools > FoundationDI > Sound > Settings`를 열고 **Create Settings**를 누른다.
`Assets/FoundationDI.Data/SoundService/` 아래에 다음이 생성된다.

```
Assets/FoundationDI.Data/SoundService/
├── SoundServiceSettings.asset
├── Collections/
│   ├── SoundCollection.asset
│   ├── MusicCollection.asset
│   └── OutputCollection.asset
└── Generated/
    ├── FoundationDI.asmref      ← 생성 코드를 런타임 어셈블리에 합류시킨다
    ├── SFX_Generated.cs
    ├── Track_Generated.cs
    └── Output_Generated.cs
```

`Data Root Path`를 바꾸면 다음 저장부터 그 경로를 쓴다.

설정 에셋은 여러 개 둘 수 있다(예: 샘플이 자체 설정을 들고 오는 경우). 이때 Settings 창 위쪽의
**Asset** 드롭다운에서 에디터 도구가 편집할 대상을 고른다. 선택은 프로젝트별로 기억된다.

> `SFX`/`Track`/`Output` 상수는 `partial struct`라 **프로젝트 전체에 한 벌만** 존재할 수 있다.
> 편집 대상을 바꿔 다른 위치에 상수를 생성하면, 도구가 이전 `Generated/` 폴더를 자동으로 정리한다.

### 1.2 DI 등록

```csharp
public class RootLifetimeScope : LifetimeScope
{
    public SoundServiceSettings soundSettings;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterSoundService(soundSettings);
        builder.RegisterInjector();   // 씬 배치 컴포넌트(MusicZone 등) 주입용
    }
}
```

`RegisterSoundService`는 설정 인스턴스, `ISoundVolumeStorage`(PlayerPrefs 기본 구현),
`SoundService`(`ISoundService` + 내부 `ISoundEngine`)를 싱글턴으로 등록한다.

---

## 2. 오디오 등록

`Tools > FoundationDI > Sound > Audio Creator`

1. **Sounds / Music** 탭을 고른다.
2. 클립을 드래그하거나 드롭 존을 클릭해서 고른다. **여러 개 넣으면 재생 때마다 무작위로 하나가 선택된다.**
3. 태그를 입력한다(영문/숫자만, 숫자로 시작 불가 — 코드 식별자가 되기 때문).
4. Compression Preset과 Force To Mono를 고르고 **Create**.

만들면 클립의 임포트 설정이 프리셋대로 바뀌고, `SFX`/`Track` 상수가 자동 생성된다.

| Preset | 용도 | Load Type / Format |
| --- | --- | --- |
| `FrequentSound` | 자주 재생되는 짧은 소리 | DecompressOnLoad / ADPCM |
| `OccasionalSound` | 가끔 재생되는 짧은 소리 | CompressedInMemory / Vorbis |
| `AmbientMusic` | 길고 오래 재생되는 음악 | 10초 미만 CompressedInMemory·ADPCM, 이상 Streaming·Vorbis |

수정·삭제·검색은 `Audio Collection` 창에서 한다.

---

## 3. 사용법

`Sound`/`Music`/`Playlist`/`DynamicMusic`은 **반드시 서비스의 팩토리로 만든다.**
만든 인스턴스는 필드에 보관해 재사용하는 것을 권장한다(재생 때마다 풀에서 소스를 빌려 쓴다).

```csharp
public class Player : MonoBehaviour
{
    private readonly ISoundService _sound;

    private Sound _jump;

    [Inject]
    public Player(ISoundService sound) => _sound = sound;

    private void Awake()
    {
        _jump = _sound.CreateSound(SFX.Jump)
            .SetVolume(0.7f)
            .SetRandomPitch()
            .SetFollowTarget(transform)
            .SetOutput(Output.SFX);
    }

    public void Jump() => _jump.Play();
}
```

> 선언부(필드 이니셜라이저)에서 만들지 말고 `Awake`/`Start`에서 만든다. 컨테이너 주입 시점 때문이다.

### 3.1 Sound

```csharp
var footstep = _sound.CreateSound(SFX.Footstep)
    .SetVolume(0.6f)
    .SetRandomPitch(new Vector2(0.9f, 1.1f))   // 반복감 제거
    .SetPlayProbability(0.8f)                  // 80% 확률로만 재생
    .SetSpatialSound()                         // 3D
    .SetHearDistance(2f, 30f)
    .SetVolumeRolloffCurve(VolumeRolloffCurve.Linear)
    .SetOcclusion()                            // 벽에 가리면 저역 통과 + 감쇠
    .SetFollowTarget(transform)
    .SetId("footstep")                         // 참조 없이 서비스로 제어할 때 쓰는 키
    .SetFadeOut(0.2f)
    .OnComplete(() => Debug.Log("done"));

footstep.Play(fadeInTime: 0.1f);
footstep.Pause(0.2f);
footstep.Resume(0.2f);
footstep.Stop(0.3f);
```

특정 클립을 고정하려면 `SetClipByIndex(i)`, 매번 무작위로 바꾸려면 `SetRandomClip()`(기본값).

재생 중에도 바꿀 수 있는 값: `ChangeVolume(v, lerpTime)`, `ChangePitch(p, lerpTime)`.

조회 가능한 상태: `Using` / `Playing` / `Paused` / `Volume` / `Pitch` / `PlayingTime` /
`CurrentLoopCycleTime` / `CompletedLoopCycles` / `ClipDuration` / `Clip` / `ClipIndex`.

### 3.2 Music

`Sound`와 거의 같지만 기본이 2D(`SetSpatialSound(false)`)이고 `Track` 태그를 쓴다.
`Play()`를 다시 부르면 이전 재생을 정지하고 새로 시작한다.

```csharp
var bgm = _sound.CreateMusic(Track.Stage1)
    .SetLoop()
    .SetVolume(0.5f)
    .SetOutput(Output.BGM);

bgm.Play(fadeInTime: 2f);
```

### 3.3 Playlist

여러 트랙을 순서대로 이어 재생한다.

```csharp
var playlist = _sound.CreatePlaylist(Track.Song1, Track.Song2, Track.Song3)
    .SetLoop()
    .SetFadeIn(1f)
    .SetFadeOut(1f)
    .OnNextTrackStart(() => Debug.Log("next"));

playlist.Play();
playlist.AddToPlaylist(Track.Song4);   // 재생 중에도 추가 가능
playlist.Shuffle();                    // 순서 섞기
```

`CurrentPlaylistClip` / `NextPlaylistClip` / `PlaylistClipsTags` / `PlayListDuration` /
`ReproducedTracks`로 상태를 볼 수 있다.

### 3.4 DynamicMusic

같은 길이의 레이어(드럼, 베이스, 기타…)를 **동시에** 재생하고 레이어별 볼륨을 실시간으로 섞는다.

```csharp
var dynamic = _sound.CreateDynamicMusic(Track.Drums, Track.Bass, Track.Guitar)
    .SetLoop()
    .SetAllVolumes(0f);

dynamic.Play();
dynamic.ChangeTrackVolume(Track.Drums, 1f, lerpTime: 2f);   // 전투 진입 시 드럼만 페이드 인
```

태그가 중복되거나 비어 있으면 에러를 남기고 재생하지 않는다.

### 3.5 참조 없이 제어하기

`SetId(...)`를 걸어 두면 서비스에서 바로 제어할 수 있다.

```csharp
_sound.Pause("footstep", fadeOutTime: 0.2f);
_sound.Resume("footstep", fadeInTime: 0.2f);
_sound.Stop("footstep");

_sound.PauseAll(0.5f);   // 일시정지 메뉴
_sound.ResumeAll(0.5f);
_sound.StopAll();        // 씬 전환
```

---

## 4. Output (AudioMixer)

`Tools > FoundationDI > Sound > Output Manager`

1. Master AudioMixer를 지정한다.
2. 믹서에서 그룹을 만들고, 그룹의 **Volume을 우클릭 → Expose to script** 한 뒤
   Exposed Parameter 이름을 **그룹 이름과 똑같이**(공백 제거) 바꾼다.
3. **Reload Outputs**를 누르면 `Output` 상수가 생성된다.

볼륨은 `PlayerPrefs`에 Output 이름을 키로 저장되고, 다음 실행에서 자동 복원된다.

```csharp
_sound.ChangeOutputVolume(Output.BGM, 0.5f);
float saved = _sound.GetSavedOutputVolume("BGM");
```

저장 백엔드를 바꾸려면 `RegisterSoundService` 대신 직접 등록한다.

```csharp
builder.RegisterInstance(settings);
builder.Register<ISoundVolumeStorage, MyCloudSaveStorage>(Lifetime.Singleton);
builder.Register<SoundService>(Lifetime.Singleton).As<ISoundService>();
```

---

## 5. 오클루전

`SetOcclusion()`을 켠 소스는 리스너 → 소스 방향으로 레이캐스트를 쏴 차폐 정도(0~1)를 구하고,
`AudioLowPassFilter` 컷오프와 볼륨을 그 값으로 보간한다. 직선 레이가 대부분 막히면
리스너 주위에 링을 만들어 회절(모서리로 돌아 들어오는 소리)을 근사한다.

파라미터는 Settings 창의 **Occlusion** 섹션에서 조절한다.

| 항목 | 뜻 |
| --- | --- |
| `EnableOcclusion` | 전역 스위치. 끄면 `SetOcclusion()` 호출이 무시된다 |
| `OcclusionLayers` | 장애물로 취급할 레이어 |
| `MaxDistance` | 이 거리보다 멀면 계산하지 않는다 |
| `MinCutoff` / `MaxCutoff` | 완전히 가렸을 때 / 안 가렸을 때의 로우패스 컷오프 |
| `MinVolumeMultiplier` | 완전히 가렸을 때의 볼륨 배수 |
| `MaxBounces` / `BounceRadiusMin` / `BounceRaysPerCircle` | 회절 근사용 링 설정 |
| `CheckInterval` | 소스별 재계산 주기(초) |
| `LerpSpeed` | 변화 반응 속도 |

> 3D Physics 레이캐스트를 쓴다. 2D 전용 프로젝트라면 `EnableOcclusion`을 꺼 두면 된다.

---

## 6. 씬 컴포넌트

`GameObject > FoundationDI > Sound >` 메뉴로 배치한다.
모두 `InjectableBehaviour`라 `builder.RegisterInjector()`가 필요하다.

| 컴포넌트 | 하는 일 |
| --- | --- |
| `MusicZone` | 구/박스 영역 안에서만 들리는 음악. 영역 밖 페이드 구간에서 거리 비례로 감쇠. Music / Playlist / DynamicMusic 모드 |
| `OutputVolumeSlider` | `Slider`로 Output 볼륨 조절 + 저장값 복원 + 백분율 라벨 |
| `VolumeSlider` | 백분율 라벨이 붙은 범용 슬라이더. `UnityEvent<float>`로 값을 흘려보낸다 |

버튼 클릭음은 이 목록에 없다 — `Runtime/Components/`의 `UIButton`을 쓴다(사운드+햅틱 통합
버튼, 자세한 내용은 [Components README](../../Components/README.md)).

---

## 7. 구조

```
SoundService/
├── Domain/
│   ├── CompressionPreset.cs
│   ├── SoundData.cs              태그 + 클립 배열 + 임포트 설정
│   ├── OutputData.cs             이름 + AudioMixerGroup
│   ├── SoundDataCollection.cs    SFX 데이터베이스 (SO)
│   ├── MusicDataCollection.cs    음악 데이터베이스 (SO)
│   ├── OutputDataCollection.cs   Output 데이터베이스 (SO)
│   └── SoundServiceSettings.cs   데이터 참조 + 오클루전 설정 (SO)
├── Tags/                         SFX / Track / Output 유사 enum(partial struct)
├── Components/                   MusicZone / OutputVolumeSlider / VolumeSlider
├── ISoundService.cs              공개 API
├── ISoundEngine.cs               빌더·소스가 쓰는 내부 seam
├── SoundService.cs               소스 풀 + Output 볼륨 + 오클루전 계산
├── SoundSource.cs                풀링되는 재생 유닛 (MonoBehaviour)
├── Sound / Music / Playlist / DynamicMusic.cs
├── ISoundVolumeStorage.cs        볼륨 영속화 seam
└── VolumeRolloffCurve.cs
```

- **소스 풀**: `[SoundService] Sources Pool` GameObject 아래에 `AudioSource`를 재사용한다.
  `DontDestroyOnLoad`라 씬을 넘어가도 유지되고, 서비스 `Dispose` 시 정리된다.
- **유사 enum**: `SFX`/`Track`/`Output`은 `[SerializeField] string`을 감싼 `partial struct`다.
  에디터가 `Generated/` 폴더에 상수를 생성하고, 같은 폴더의 `.asmref`가 그 코드를
  런타임 어셈블리(`FoundationDI`)에 합류시킨다. 상수가 아직 없어도 `FromTag("...")`나
  문자열 오버로드로 쓸 수 있다.

---

## 8. 동작 참고

설계상 의도한 동작이라 헷갈리기 쉬운 지점을 모았다.

- **태그 조회 실패는 `null`이다.** 등록되지 않은 태그로 재생을 시도하면 경고를 남기고
  아무 소리도 내지 않는다. 다른 클립을 대신 재생하지 않으므로, 오타는 조용히 넘어가지 않고
  "소리가 안 난다"로 드러난다.
- **`Sound.Play()`를 루프 중에 다시 부르면** 이전 재생을 정지하고 새로 시작한다.
  `Music`/`Playlist`/`DynamicMusic`은 루프 여부와 무관하게 항상 그렇게 동작한다.
- **`Playlist.AddToPlaylist`는 재생 중이 아니어도 안전하다.** 큐와 `PlaylistClipsTags`에
  함께 반영되고, 재생 중이면 진행 중인 큐에도 밀어 넣는다.
- **`NextPlaylistClip`은 큐가 비면 `null`이다.** 예외를 던지지 않는다.
- **페이드 아웃 중에는 `SetVolume`이 즉시 반영되지 않는다.** 경고를 남기고, 다음 페이드 인이
  새 볼륨까지 올라간다.
- **`DynamicMusic`의 콜백은 첫 번째 레이어에서만 발생한다.** 레이어 수만큼 중복 호출되지 않는다.
- **`Playlist`의 `OnNextTrackStart`는 첫 트랙에서도 호출된다.** `ReproducedTracks`도 첫 트랙부터 센다.
- **`SetOcclusion()`은 설정의 `EnableOcclusion`이 꺼져 있으면 무시된다.**
  또한 켜면 자동으로 `SetSpatialSound(true)`가 된다(레이캐스트가 의미 있으려면 3D여야 한다).
- **오클루전은 3D Physics 레이캐스트를 쓴다.** 2D 전용 프로젝트라면 `EnableOcclusion`을 꺼 둔다.
- **소스 풀은 `DontDestroyOnLoad`다.** 씬을 넘어가도 재생이 이어지고, `ISoundService.Dispose()`에서
  일괄 정리된다.
