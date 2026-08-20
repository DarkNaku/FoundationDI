# 05 Sound

`SoundService`의 주요 기능을 한 화면에서 눌러 보는 샘플.

**이 샘플은 단독으로 동작한다.** 자체 `SoundServiceSettings`와 컬렉션을 `Data/`에 들고 있어서,
Package Manager에서 이 샘플만 import한 뒤 `Sound.unity`를 열고 Play하면 바로 소리가 난다.
프로젝트의 다른 사운드 데이터와 섞이지 않는다.

## 시연 내용

- **SFX** — 같은 태그(`SmpClick`)에 클립 3개를 묶어 두고 누를 때마다 다른 클립 + 다른 피치로 재생.
  `SetPlayProbability`로 확률 재생도 함께 보여 준다.
- **Music** — 페이드 인/아웃, 일시정지/재개, 재생 중 볼륨 보간(`ChangeVolume(v, lerpTime)`).
- **Playlist** — 두 곡을 이어 재생, `Shuffle()`, `OnNextTrackStart` 콜백.
- **Dynamic Music** — 같은 길이의 레이어 3개(드럼/베이스/리드)를 동시에 재생하고
  슬라이더로 레이어별 볼륨만 섞는다.
- **전체 제어** — `PauseAll` / `ResumeAll` / `StopAll`, 그리고 `SetId`로 걸어 둔 id를 이용한
  참조 없는 개별 정지.

## 실행

`Sound.unity`를 열고 Play. 준비 작업은 없다.

## 내 프로젝트에 샘플 오디오 가져오기 (선택)

샘플 클립을 **내 프로젝트의** 사운드/음악 컬렉션에도 등록하고 싶다면:

`Tools > FoundationDI > Sound > Sample Data > 05 Sound > Install Into Project`

편집 대상 설정 에셋(`Tools > FoundationDI > Sound > Settings`에서 고른 것)의 컬렉션에
`Smp*` 태그가 추가되고 `SFX`/`Track` 상수가 다시 생성된다. 되돌리려면 같은 메뉴의
**Remove From Project**. 이미 설치되어 있으면 Install이, 없으면 Remove가 회색으로 비활성화된다.

> 샘플마다 이 메뉴 한 쌍이 따로 생긴다. 샘플을 여러 개 설치해도 서로 간섭하지 않는다.

## 구성

```
05-Sound/
├── Sound.unity                       SoundSampleScope + SoundSampleDemo만 있는 씬
├── Audio/                            절차적으로 합성한 무저작권 클립 9개
├── Data/                             ★ 이 샘플 전용 데이터 (단독 실행의 핵심)
│   ├── SoundServiceSettings.asset
│   └── Collections/{Sound,Music,Output}Collection.asset
├── Editor/
│   ├── SoundSampleAudioSet.cs        샘플이 들고 오는 오디오 묶음의 형태
│   ├── SoundSampleData.cs            이 샘플의 오디오 정의(태그 ↔ 클립)
│   ├── SoundSampleDataInstaller.cs   설치/제거/자체 데이터 채우기
│   └── SoundSampleMenu.cs            샘플별 메뉴 항목
└── Scripts/
    ├── SoundSampleScope.cs           컴포지션 루트
    ├── SoundSampleTags.cs            샘플이 쓰는 문자열 태그
    └── SoundSampleDemo.cs            OnGUI 데모 패널
```

UI 프리팹을 두지 않고 `OnGUI`로 그린 이유는, 샘플의 초점을 UI 배선이 아니라
사운드 API 자체에 두기 위해서다.

## 핵심 코드

```csharp
// 컴포지션 루트 — 샘플 전용 설정 에셋을 주입한다.
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
> 유사 enum 상수는 **프로젝트에 한 벌만** 만들어지는데, 샘플은 자체 데이터로 도는 탓에
> 그 한 벌에 샘플 태그가 없을 수 있기 때문이다.
> **실제 게임 코드에서는 오타를 컴파일 타임에 잡아 주는 생성 상수를 쓰는 쪽이 낫다.**

## 오디오 출처

`Audio/`의 9개 클립은 사인·삼각·사각파와 노이즈로 합성한 것이라 저작권 제약이 없다.
