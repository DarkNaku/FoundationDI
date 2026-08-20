# 05 Sound

`SoundService`의 주요 기능을 한 화면에서 눌러 보는 샘플.

## 시연 내용

- **SFX** — 같은 태그(`SmpClick`)에 클립 3개를 묶어 두고 누를 때마다 다른 클립 + 다른 피치로 재생.
  `SetPlayProbability`로 확률 재생도 함께 보여 준다.
- **Music** — 페이드 인/아웃, 일시정지/재개, 재생 중 볼륨 보간(`ChangeVolume(v, lerpTime)`).
- **Playlist** — 두 곡을 이어 재생, `Shuffle()`, `OnNextTrackStart` 콜백.
- **Dynamic Music** — 같은 길이의 레이어 3개(드럼/베이스/리드)를 동시에 재생하고
  슬라이더로 레이어별 볼륨만 섞는다.
- **전체 제어** — `PauseAll` / `ResumeAll` / `StopAll`, 그리고 `SetId`로 걸어 둔 id를 이용한
  참조 없는 개별 정지.

## 실행 방법

1. **`Tools > FoundationDI > Sound > Samples > Import Sample Audio`** 실행.
   `Audio/` 폴더의 클립이 프로젝트의 사운드/음악 컬렉션에 `Smp*` 태그로 등록되고,
   `SFX`/`Track` 상수가 다시 생성된다.
2. `Sound.unity`를 열고 Play.
3. 왼쪽 패널의 버튼과 슬라이더를 눌러 본다.

되돌리려면 `Tools > FoundationDI > Sound > Samples > Remove Sample Audio`.

> 1번을 건너뛰면 패널이 안내 문구만 표시한다. 태그 데이터는 씬이 아니라
> **프로젝트의 컬렉션 에셋**에 들어 있기 때문이다.

## 구성

```
05-Sound/
├── Sound.unity                     SoundSampleScope + SoundSampleDemo만 있는 씬
├── Audio/                          절차적으로 만든 무저작권 샘플 클립 9개
├── Editor/
│   └── SoundSampleDataImporter.cs  샘플 클립을 컬렉션에 일괄 등록/제거
└── Scripts/
    ├── SoundSampleScope.cs         컴포지션 루트
    ├── SoundSampleTags.cs          샘플이 쓰는 문자열 태그
    └── SoundSampleDemo.cs          OnGUI 데모 패널
```

UI 프리팹을 두지 않고 `OnGUI`로 그린 이유는, 샘플의 초점을 UI 배선이 아니라
사운드 API 자체에 두기 위해서다.

## 핵심 코드

```csharp
// 컴포지션 루트
public class SoundSampleScope : LifetimeScope
{
    [SerializeField] private SoundServiceSettings _soundSettings;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterSoundService(_soundSettings);
        builder.RegisterInjector();   // 씬 배치 컴포넌트 주입
    }
}

// 빌더는 한 번만 만들어 재사용한다. Play()마다 풀에서 소스를 빌려 쓴다.
_click = _sound.CreateSound("SmpClick").SetVolume(0.7f).SetSpatialSound(false);
_click.SetRandomPitch().Play();

_music = _sound.CreateMusic("SmpSong1").SetLoop().SetId("sample-music");
_music.Play(fadeInTime: 1f);
_sound.Stop("sample-music", 0.3f);            // 참조 없이 id로 제어

_dynamicMusic = _sound.CreateDynamicMusic("SmpLayerDrum", "SmpLayerBass", "SmpLayerLead").SetLoop();
_dynamicMusic.Play(0.5f);
_dynamicMusic.ChangeTrackVolume("SmpLayerLead", 1f, lerpTime: 0.2f);
```

> 샘플은 생성된 `SFX.SmpClick` 상수 대신 문자열 오버로드를 쓴다.
> 유사 enum 상수는 프로젝트 컬렉션 내용에 따라 만들어지므로, 데이터를 아직 등록하지 않은
> 프로젝트에서도 스크립트가 컴파일되어야 하기 때문이다.
> **실제 게임 코드에서는 오타를 컴파일 타임에 잡아 주는 생성 상수를 쓰는 쪽이 낫다.**

## 오디오 출처

`Audio/`의 9개 클립은 사인/삼각/사각파와 노이즈로 합성한 것이라 저작권 제약이 없다.
생성 방식은 커밋 이력을 참고.
