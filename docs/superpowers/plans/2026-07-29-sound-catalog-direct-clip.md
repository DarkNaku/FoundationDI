# SoundCatalog 직접 클립 참조 전환 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `SoundCatalogSO`가 사운드를 `Key → AudioClip` 직접 참조로 들게 하고, `SoundService`에서 `IResourceService` 경유·캐싱·참조 카운팅을 제거해 사용·구현을 단순화한다.

**Architecture:** Unity는 어셈블리 단위로 컴파일되므로 `ISoundCatalog`를 한 번에 교체하면 소비 측 전체가 깨진다. 따라서 Tidy First 원칙에 맞춰 3단계로 안전하게 전환한다 — ① 클립 기반 API를 기존 문자열키 API와 **나란히 추가**(STRUCTURAL) → ② `SoundService`를 클립 기반으로 **전환**(BEHAVIORAL) → ③ 사용되지 않게 된 문자열키 API를 **제거**(STRUCTURAL). 각 단계 종료 시 어셈블리는 컴파일·그린 상태를 유지한다. 마지막에 문서(README/CLAUDE.md)를 개정한다.

**Tech Stack:** Unity 6000.3.17f1, C#, VContainer(DI), UniTask(async), R3, NUnit + NSubstitute(EditMode 테스트). 컴파일·테스트는 UnityMCP(`run_tests` EditMode, `read_console`)로 수행.

## Global Constraints

- 네임스페이스는 `DarkNaku.FoundationDI` (기존 파일 관습 준수).
- 재사용 코드는 `Assets/FoundationDI/` 안에만 위치.
- 테스트 함수 이름은 한국어 의도, `should~` 형식의 한국어 서술형.
- **STRUCTURAL과 BEHAVIORAL 변경을 같은 커밋에 섞지 않는다.** 커밋 제목에 `[STRUCTURAL]`/`[BEHAVIORAL]` 접두어.
- 스크립트 생성/수정 후 `read_console`로 `editor_state.isCompiling == false` 및 컴파일 에러 0을 먼저 확인한 뒤 테스트를 돌린다.
- 모킹은 NSubstitute, 테스트 어셈블리는 `FoundationDI.Tests`(Editor 플랫폼).
- 대상 게임 오디오 규모는 **소·중형, 전부 빌드 내장**(Addressables 오디오 핫업데이트/분할 다운로드 비대상).
- 현재 프로젝트에 `SoundCatalogSO` `.asset`이 없어 데이터 마이그레이션은 불필요.

**참조 스펙:** `docs/superpowers/specs/2026-07-29-sound-catalog-direct-clip-design.md`

---

## File Structure

- **Modify** `Assets/FoundationDI/Runtime/Services/SoundService/SoundCatalogSO.cs` — 데이터 모델(`SoundEntry`)·인터페이스(`ISoundCatalog`)·SO 구현을 클립 기반으로 전환.
- **Modify** `Assets/FoundationDI/Runtime/Services/SoundService/SoundService.cs` — `IResourceService` 의존/캐싱/Release 제거, 클립 직접 재생, `PreloadAsync`는 `LoadAudioData` 기반.
- **Modify** `Assets/FoundationDI/Tests/SoundCatalogTest.cs` — `SerializedObject`로 클립을 주입해 카탈로그를 구성, 클립 기반 API 검증.
- **Modify** `Assets/FoundationDI/Tests/SoundServiceTest.cs` — `IResourceService` 목 제거, 클립 반환 카탈로그 목으로 재작성. Preload는 열거 검증만.
- **Modify** `Assets/FoundationDI/Runtime/Services/SoundService/README.md` — 리소스키 → 직접 클립 참조로 사용법 개정.
- **Modify** `CLAUDE.md` — SoundService의 ResourceService 위임 예외 및 전환 예정 문구 정리.
- **변경 없음(확인만):** `SoundButton.cs`, `SoundButtonEditor.cs`, `SoundButtonTest.cs`(모두 `Keys`/`ISoundService`만 사용). `SoundServiceVContainerExtensions.RegisterSoundService` 시그니처(내부는 `IResourceService` 미참조라 그대로 컴파일됨).

---

## Task 1: 카탈로그에 클립 기반 API를 나란히 추가 (STRUCTURAL)

기존 `TryGetResourceKey`/`PreloadResourceKeys`를 **유지한 채** `TryGetClip`/`PreloadClips`와 `AudioClip Clip` 필드를 추가한다. 이 단계에서는 `SoundService`가 아직 기존 문자열키 경로를 쓰므로 어셈블리는 계속 컴파일·그린이다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/SoundService/SoundCatalogSO.cs`
- Test: `Assets/FoundationDI/Tests/SoundCatalogTest.cs`

**Interfaces:**
- Produces:
  - `SoundEntry.Clip` (public `AudioClip`).
  - `bool ISoundCatalog.TryGetClip(string key, out AudioClip clip)`
  - `IEnumerable<AudioClip> ISoundCatalog.PreloadClips { get; }`
- Consumes: 없음(기존 파일 내부만).

- [ ] **Step 1: 클립 주입 테스트 헬퍼와 첫 실패 테스트 작성**

`SoundCatalogTest.cs` 상단에 `using UnityEditor;`를 추가하고, `SerializedObject`로 클립을 주입하는 헬퍼와 첫 테스트를 추가한다(기존 JSON 테스트/헬퍼는 그대로 둔다).

```csharp
// using UnityEditor; 를 파일 상단 using 목록에 추가

private static AudioClip MakeClip() => AudioClip.Create("clip", 1, 1, 1000, false);

private static SoundCatalogSO MakeCatalogWithClips(params (string key, AudioClip clip, bool preload)[] entries)
{
    var catalog = ScriptableObject.CreateInstance<SoundCatalogSO>();
    var so = new SerializedObject(catalog);
    var list = so.FindProperty("_entries");
    list.arraySize = entries.Length;
    for (int i = 0; i < entries.Length; i++)
    {
        var e = list.GetArrayElementAtIndex(i);
        e.FindPropertyRelative("Key").stringValue = entries[i].key;
        e.FindPropertyRelative("Clip").objectReferenceValue = entries[i].clip;
        e.FindPropertyRelative("Preload").boolValue = entries[i].preload;
    }
    so.ApplyModifiedPropertiesWithoutUndo();
    return catalog;
}

[Test]
public void 등록된_키는_클립으로_변환된다()
{
    var clip = MakeClip();
    var catalog = MakeCatalogWithClips(("Jump", clip, false));

    var found = ((ISoundCatalog)catalog).TryGetClip("Jump", out var result);

    Assert.IsTrue(found);
    Assert.AreSame(clip, result);

    Object.DestroyImmediate(catalog);
}
```

- [ ] **Step 2: 컴파일/실행해 실패 확인**

UnityMCP `read_console`로 컴파일 결과 확인 → `TryGetClip`/`Clip` 미정의로 **컴파일 에러**(RED). (Unity에서는 미정의 심볼 참조가 컴파일 에러로 나타나며, 이것이 이 단계의 실패 신호다.)

- [ ] **Step 3: `SoundCatalogSO`에 클립 기반 API 추가**

`SoundEntry`에 `Clip` 필드를 추가하고(기존 `ResourceKey` 유지), 인터페이스와 SO에 클립 API를 추가한다. `EnsureBuilt`가 문자열 맵과 클립 맵을 함께 빌드한다.

```csharp
[Serializable]
public struct SoundEntry
{
    public string Key;          // 논리 이름 (Play 인자, 드롭다운 표시)
    public string ResourceKey;  // (제거 예정) IResourceService 로드 키
    public AudioClip Clip;      // 직접 참조
    public bool Preload;        // 프리로드 대상 여부
}

public interface ISoundCatalog
{
    bool TryGetResourceKey(string key, out string resourceKey); // (제거 예정)
    bool TryGetClip(string key, out AudioClip clip);
    IReadOnlyList<string> Keys { get; }
    IEnumerable<string> PreloadResourceKeys { get; }            // (제거 예정)
    IEnumerable<AudioClip> PreloadClips { get; }
}
```

`SoundCatalogSO` 내부에 클립 맵과 신규 멤버를 추가한다:

```csharp
private Dictionary<string, string> _map;
private Dictionary<string, AudioClip> _clipMap;
private List<string> _keys;

public IEnumerable<AudioClip> PreloadClips
{
    get
    {
        foreach (var entry in _entries)
        {
            if (entry.Preload && entry.Clip != null)
            {
                yield return entry.Clip;
            }
        }
    }
}

public bool TryGetClip(string key, out AudioClip clip)
{
    EnsureBuilt();
    return _clipMap.TryGetValue(key, out clip);
}
```

`EnsureBuilt`에서 `_clipMap`도 함께 채운다(기존 `_map` 로직과 같은 루프):

```csharp
private void EnsureBuilt()
{
    if (_map != null) return;

    _map = new Dictionary<string, string>();
    _clipMap = new Dictionary<string, AudioClip>();
    _keys = new List<string>();

    foreach (var entry in _entries)
    {
        if (string.IsNullOrEmpty(entry.Key)) continue;

        if (_map.ContainsKey(entry.Key))
        {
            Debug.LogWarning($"[SoundCatalogSO] Duplicate key '{entry.Key}', overwriting with last value.");
        }
        else
        {
            _keys.Add(entry.Key);
        }

        _map[entry.Key] = entry.ResourceKey;
        _clipMap[entry.Key] = entry.Clip;
    }
}
```

- [ ] **Step 4: 나머지 클립 API 테스트 추가**

```csharp
[Test]
public void 미등록_키는_클립_변환에_실패한다()
{
    var catalog = MakeCatalogWithClips();

    var found = ((ISoundCatalog)catalog).TryGetClip("None", out var clip);

    Assert.IsFalse(found);
    Assert.IsNull(clip);

    Object.DestroyImmediate(catalog);
}

[Test]
public void PreloadClips는_Preload가_true인_클립만_노출한다()
{
    var a = MakeClip();
    var b = MakeClip();
    var c = MakeClip();
    var catalog = MakeCatalogWithClips(("A", a, true), ("B", b, false), ("C", c, true));

    CollectionAssert.AreEquivalent(
        new[] { a, c },
        new List<AudioClip>(((ISoundCatalog)catalog).PreloadClips));

    Object.DestroyImmediate(catalog);
}
```

- [ ] **Step 5: 컴파일 확인 후 전체 EditMode 테스트 실행 → 그린**

`read_console`로 에러 0 확인 → UnityMCP `run_tests`(EditMode, `FoundationDI.Tests`). 신규 3개 포함 전체 PASS(기존 JSON 테스트도 유지되어 PASS).

- [ ] **Step 6: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/SoundService/SoundCatalogSO.cs \
        Assets/FoundationDI/Tests/SoundCatalogTest.cs
git commit -m "[STRUCTURAL] SoundCatalog에 클립 기반 API(TryGetClip/PreloadClips) 추가

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: SoundService를 클립 직접 재생으로 전환 (BEHAVIORAL)

`SoundService`가 `IResourceService` 대신 카탈로그의 클립을 직접 쓰도록 바꾸고, 캐싱/참조 카운팅/async 로드를 제거한다. `SoundServiceTest`를 함께 재작성한다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/SoundService/SoundService.cs`
- Test: `Assets/FoundationDI/Tests/SoundServiceTest.cs`

**Interfaces:**
- Consumes: `ISoundCatalog.TryGetClip`, `ISoundCatalog.PreloadClips` (Task 1 산출물).
- Produces:
  - `SoundService(ISoundCatalog catalog)` — 단일 인자 생성자.
  - `void Play(string key)`, `void PlayBGM(string key)`, `UniTask PreloadAsync()` (시그니처 유지, 내부 구현 변경).

- [ ] **Step 1: 테스트를 클립 기반으로 재작성(실패 상태)**

`SoundServiceTest.cs` 전체를 아래로 교체한다(`IResourceService` 목 제거, 클립 반환 카탈로그 목).

```csharp
using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class SoundServiceTest
{
    private static AudioClip MakeClip() => AudioClip.Create("clip", 1, 1, 1000, false);

    private static ISoundCatalog Catalog(params (string key, AudioClip clip)[] entries)
    {
        var catalog = Substitute.For<ISoundCatalog>();
        foreach (var (key, clip) in entries)
        {
            var captured = clip;
            catalog.TryGetClip(key, out Arg.Any<AudioClip>())
                .Returns(call => { call[1] = captured; return true; });
        }
        catalog.Keys.Returns(entries.Select(e => e.key).ToList());
        return catalog;
    }

    [SetUp]
    public void SetUp()
    {
        PlayerPrefs.DeleteKey("SFX_ENABLED");
        PlayerPrefs.DeleteKey("BGM_ENABLED");
    }

    [Test]
    public void SFX_재생시_카탈로그에서_클립을_가져온다()
    {
        var catalog = Catalog(("sfx", MakeClip()));
        var sut = new SoundService(catalog) { SFXEnabled = true };

        sut.Play("sfx");

        catalog.Received(1).TryGetClip("sfx", out Arg.Any<AudioClip>());

        sut.Dispose();
    }

    [Test]
    public void 같은_프레임_SFX는_클립을_한번만_조회한다()
    {
        var catalog = Catalog(("sfx", MakeClip()));
        var sut = new SoundService(catalog) { SFXEnabled = true };

        sut.Play("sfx");
        sut.Play("sfx");

        // 프레임 중복 차단이 카탈로그 조회 전에 걸리므로 조회는 1회.
        catalog.Received(1).TryGetClip("sfx", out Arg.Any<AudioClip>());

        sut.Dispose();
    }

    [Test]
    public void BGM_재생시_카탈로그에서_클립을_가져온다()
    {
        var catalog = Catalog(("bgm", MakeClip()));
        var sut = new SoundService(catalog) { BGMEnabled = true };

        sut.PlayBGM("bgm");

        catalog.Received(1).TryGetClip("bgm", out Arg.Any<AudioClip>());

        sut.Dispose();
    }

    [Test]
    public void 카탈로그에_없는_SFX키는_재생하지_않고_에러를_남긴다()
    {
        var catalog = Catalog();
        var sut = new SoundService(catalog) { SFXEnabled = true };

        LogAssert.Expect(LogType.Error,
            new System.Text.RegularExpressions.Regex("not found in catalog"));

        sut.Play("missing");

        sut.Dispose();
    }

    [Test]
    public void 카탈로그에_없는_BGM키는_재생하지_않고_에러를_남긴다()
    {
        var catalog = Catalog();
        var sut = new SoundService(catalog) { BGMEnabled = true };

        LogAssert.Expect(LogType.Error,
            new System.Text.RegularExpressions.Regex("not found in catalog"));

        sut.PlayBGM("missing");

        sut.Dispose();
    }

    [Test]
    public void 생성_직후_SFX는_활성화_상태다()
    {
        var sut = new SoundService(Catalog());
        Assert.IsTrue(sut.SFXEnabled);
        sut.Dispose();
    }

    [Test]
    public void 생성_직후_BGM은_활성화_상태다()
    {
        var sut = new SoundService(Catalog());
        Assert.IsTrue(sut.BGMEnabled);
        sut.Dispose();
    }

    [Test]
    public void 생성_직후_BGM은_재생중이_아니다()
    {
        var sut = new SoundService(Catalog());
        Assert.IsFalse(sut.IsPlayingBGM);
        sut.Dispose();
    }

    [Test]
    public void SFX_활성화_상태는_PlayerPrefs에_영속된다()
    {
        var sut = new SoundService(Catalog());
        sut.SFXEnabled = false;
        sut.Dispose();

        var reloaded = new SoundService(Catalog());
        Assert.IsFalse(reloaded.SFXEnabled);
        reloaded.Dispose();
    }

    [Test]
    public void BGM_활성화_상태는_PlayerPrefs에_영속된다()
    {
        var sut = new SoundService(Catalog());
        sut.BGMEnabled = false;
        sut.Dispose();

        var reloaded = new SoundService(Catalog());
        Assert.IsFalse(reloaded.BGMEnabled);
        reloaded.Dispose();
    }

    [Test]
    public void BGM_재생중이면_IsPlayingBGM이_true다()
    {
        var catalog = Catalog(("bgm", MakeClip()));
        var sut = new SoundService(catalog);

        sut.PlayBGM("bgm");

        Assert.IsTrue(sut.IsPlayingBGM);

        sut.Dispose();
    }

    [UnityTest]
    public IEnumerator PreloadAsync는_카탈로그의_PreloadClips를_열거한다() => UniTask.ToCoroutine(async () =>
    {
        var catalog = Substitute.For<ISoundCatalog>();
        catalog.PreloadClips.Returns(new[] { MakeClip(), MakeClip() });
        var sut = new SoundService(catalog);

        await sut.PreloadAsync();

        _ = catalog.Received(1).PreloadClips;

        sut.Dispose();
    });
}
```

- [ ] **Step 2: 컴파일해 실패 확인**

`read_console` → `SoundService(catalog)` 단일 인자 생성자 부재로 컴파일 에러(RED).

- [ ] **Step 3: `SoundService`를 클립 기반으로 재작성**

`SoundService.cs`에서 `IResourceService`/`_table` 관련을 제거하고 아래처럼 바꾼다.

필드/생성자:

```csharp
private readonly ISoundCatalog _catalog;
private readonly Transform _root;
private AudioSource _bgmPlayer;
private HashSet<AudioSource> _sfxPlayers = new();
private HashSet<string> _playedClipInThisFrame = new();
private IDisposable _disposable;

public SoundService(ISoundCatalog catalog)
{
    _catalog = catalog;

    var root = new GameObject("[SoundService]");
    _root = root.transform;

    if (Application.isPlaying)
    {
        Object.DontDestroyOnLoad(root);
    }

    _bgmPlayer = new GameObject("BGM Player").AddComponent<AudioSource>();
    _bgmPlayer.transform.parent = _root;

    _disposable = Observable.EveryUpdate(UnityFrameProvider.PostLateUpdate).Subscribe(OnPostLateUpdate);
}
```

`Dispose` (Release 루프 제거):

```csharp
public void Dispose()
{
    _disposable?.Dispose();
    _disposable = null;

    // 플레이모드 종료 시 Unity의 오브젝트 파괴와 Container.Dispose 순서가 보장되지 않는다.
    // _root가 먼저 파괴되면 접근에서 MissingReferenceException이 나므로 fake-null 가드로 건너뛴다.
    if (_root != null)
    {
        if (Application.isPlaying)
        {
            Object.Destroy(_root.gameObject);
        }
        else
        {
            Object.DestroyImmediate(_root.gameObject);
        }
    }
}
```

`Play`/`PlayBGM` (카탈로그 클립 직접 사용):

```csharp
public void Play(string key)
{
    if (Mathf.Approximately(VolumeSFX, 0f) || !SFXEnabled) return;
    if (_playedClipInThisFrame.Contains(key)) return;

    if (!_catalog.TryGetClip(key, out var clip) || clip == null)
    {
        Debug.LogError($"[SoundService] Play : Clip not found in catalog. ({key})");
        return;
    }

    var player = GetPlayer();
    player.clip = clip;
    player.loop = false;
    player.volume = VolumeSFX;
    player.Play();

    _playedClipInThisFrame.Add(key);
}

public void PlayBGM(string key)
{
    if (Mathf.Approximately(VolumeBGM, 0f) || !BGMEnabled) return;

    if (!_catalog.TryGetClip(key, out var clip) || clip == null)
    {
        Debug.LogError($"[SoundService] PlayBGM : Clip not found in catalog. ({key})");
        return;
    }

    if (_bgmPlayer.isPlaying)
    {
        _bgmPlayer.Stop();
    }

    _bgmPlayer.clip = clip;
    _bgmPlayer.loop = true;
    _bgmPlayer.volume = VolumeBGM;
    _bgmPlayer.Play();
}
```

`PreloadAsync` + 헬퍼(`GetClip`/`PreloadOneAsync`/`_table` 제거로 대체):

```csharp
public async UniTask PreloadAsync()
{
    var clips = _catalog.PreloadClips;
    if (clips == null) return;

    var tasks = new List<UniTask>();

    foreach (var clip in clips)
    {
        if (clip == null) continue;
        tasks.Add(LoadAudioDataAsync(clip));
    }

    await UniTask.WhenAll(tasks);
}

private static async UniTask LoadAudioDataAsync(AudioClip clip)
{
    // 임포트 설정 "Load In Background"가 켜진 압축 클립만 실제 비동기 로드된다.
    // 이미 로드된(절차적 클립 포함) 경우 즉시 반환한다.
    if (clip.loadState == AudioDataLoadState.Loaded) return;

    clip.LoadAudioData();
    await UniTask.WaitWhile(() => clip.loadState == AudioDataLoadState.Loading);
}
```

`GetPlayer`, `OnPostLateUpdate`, 볼륨/enabled 프로퍼티, 상수는 기존 그대로 유지한다. `System.Linq` using이 다른 곳에서 쓰이지 않으면 제거한다(경고 방지).

- [ ] **Step 4: 컴파일 확인 후 전체 EditMode 테스트 실행 → 그린**

`read_console`로 에러 0 확인 → `run_tests`(EditMode, `FoundationDI.Tests`). 재작성한 `SoundServiceTest` 전체 PASS. `SoundCatalogTest`/`SoundButtonTest`도 계속 PASS(회귀 없음).

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/SoundService/SoundService.cs \
        Assets/FoundationDI/Tests/SoundServiceTest.cs
git commit -m "[BEHAVIORAL] SoundService가 카탈로그 클립을 직접 재생하고 ResourceService 의존 제거

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: 사용되지 않게 된 문자열키 API 제거 (STRUCTURAL)

`SoundService`가 더 이상 문자열키 경로를 쓰지 않으므로 `ResourceKey`/`TryGetResourceKey`/`PreloadResourceKeys`와 문자열 맵, 그리고 그것들만 검증하던 옛 카탈로그 테스트를 제거한다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/SoundService/SoundCatalogSO.cs`
- Test: `Assets/FoundationDI/Tests/SoundCatalogTest.cs`

**Interfaces:**
- Produces(최종형): `SoundEntry { string Key; AudioClip Clip; bool Preload; }`, `ISoundCatalog { TryGetClip; Keys; PreloadClips; }`.

- [ ] **Step 1: 옛 문자열키 테스트 제거 및 나머지 검증 이관**

`SoundCatalogTest.cs`에서 JSON 기반 헬퍼 `MakeCatalog(string json)`와 이를 쓰는 옛 테스트 4개(`등록된_키는_리소스키로_변환된다`, `미등록_키는_변환에_실패한다`, `Keys는_등록_순서대로_노출된다`, `PreloadResourceKeys는_Preload가_true인_항목만_노출한다`, `중복_키는_경고를_남기고_마지막_값을_채택한다`)를 삭제한다. `Keys 순서`와 `중복 키 경고`는 클립 헬퍼로 재작성해 유지한다.

```csharp
[Test]
public void Keys는_등록_순서대로_노출된다()
{
    var catalog = MakeCatalogWithClips(("A", MakeClip(), false), ("B", MakeClip(), false));

    CollectionAssert.AreEqual(new[] { "A", "B" }, (List<string>)((ISoundCatalog)catalog).Keys);

    Object.DestroyImmediate(catalog);
}

[Test]
public void 중복_키는_경고를_남기고_마지막_값을_채택한다()
{
    var last = MakeClip();
    var catalog = MakeCatalogWithClips(("X", MakeClip(), false), ("X", last, false));

    LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Duplicate key"));

    var found = ((ISoundCatalog)catalog).TryGetClip("X", out var clip);

    Assert.IsTrue(found);
    Assert.AreSame(last, clip);

    Object.DestroyImmediate(catalog);
}
```

이 시점의 `SoundCatalogTest.cs`는 `using UnityEngine.TestTools;`(LogAssert)와 `using UnityEditor;`가 필요하다. 미사용이 된 using은 정리한다.

- [ ] **Step 2: `SoundCatalogSO`에서 문자열키 API 제거(최종형)**

`SoundCatalogSO.cs`를 아래 최종형으로 정리한다.

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [Serializable]
    public struct SoundEntry
    {
        public string Key;        // 논리 이름 (Play 인자, 드롭다운 표시)
        public AudioClip Clip;    // 직접 참조
        public bool Preload;      // 첫 재생 디코딩 히치 방지 대상
    }

    public interface ISoundCatalog
    {
        bool TryGetClip(string key, out AudioClip clip);
        IReadOnlyList<string> Keys { get; }
        IEnumerable<AudioClip> PreloadClips { get; }
    }

    [CreateAssetMenu(fileName = "SoundCatalog", menuName = "DarkNaku/SoundCatalog")]
    public sealed class SoundCatalogSO : ScriptableObject, ISoundCatalog
    {
        [SerializeField] private List<SoundEntry> _entries = new();

        private Dictionary<string, AudioClip> _map;
        private List<string> _keys;

        public IReadOnlyList<string> Keys
        {
            get
            {
                EnsureBuilt();
                return _keys;
            }
        }

        public IEnumerable<AudioClip> PreloadClips
        {
            get
            {
                foreach (var entry in _entries)
                {
                    if (entry.Preload && entry.Clip != null)
                    {
                        yield return entry.Clip;
                    }
                }
            }
        }

        public bool TryGetClip(string key, out AudioClip clip)
        {
            EnsureBuilt();
            return _map.TryGetValue(key, out clip);
        }

        private void EnsureBuilt()
        {
            if (_map != null) return;

            _map = new Dictionary<string, AudioClip>();
            _keys = new List<string>();

            foreach (var entry in _entries)
            {
                if (string.IsNullOrEmpty(entry.Key)) continue;

                if (_map.ContainsKey(entry.Key))
                {
                    Debug.LogWarning($"[SoundCatalogSO] Duplicate key '{entry.Key}', overwriting with last value.");
                }
                else
                {
                    _keys.Add(entry.Key);
                }

                _map[entry.Key] = entry.Clip;
            }
        }
    }
}
```

- [ ] **Step 3: 컴파일 확인 후 전체 EditMode 테스트 실행 → 그린**

`read_console` 에러 0 확인 → `run_tests`(EditMode). `SoundCatalogTest`(클립 기반만 남음)·`SoundServiceTest`·`SoundButtonTest` 전체 PASS.

- [ ] **Step 4: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/SoundService/SoundCatalogSO.cs \
        Assets/FoundationDI/Tests/SoundCatalogTest.cs
git commit -m "[STRUCTURAL] SoundCatalog의 사용되지 않는 리소스키 API 제거

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: 문서 개정 (STRUCTURAL)

README와 CLAUDE.md를 직접 클립 참조 모델에 맞게 갱신한다. 코드 변경 없음.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/SoundService/README.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: README 개정**

다음을 반영한다:
- 도입부/특징: "문자열키 → 리소스키 매핑" → "문자열키 → **AudioClip 직접 참조**". "클립 로딩은 `IResourceService`에 위임" 문구 삭제.
- 사용법 표: `ResourceKey` 행을 `Clip`(인스펙터에서 드래그하는 `AudioClip`)으로 교체.
- DI 등록 섹션: "`RegisterSoundService` 전에 `IResourceService`가 등록되어 있어야 합니다" 문구 및 예제의 `IResourceService` 선등록 요구 제거(등록 예제 자체는 남기되 사운드는 `IResourceService` 불요임을 명시).
- 프리로드 설명: "`IResourceService.LoadAsync` 병렬 로드" → "`Preload=true` 클립의 `AudioClip.LoadAudioData()`를 병렬 대기". **전제 추가**: 진짜 비동기 로드는 클립 임포트 설정 **"Load In Background"**가 켜져 있어야 하며, 꺼진 압축 클립은 `LoadAudioData()`가 메인 스레드를 동기 블로킹함.
- API 섹션: `ISoundCatalog` 코드블록을 `TryGetClip`/`PreloadClips`로, `SoundEntry` 코드블록을 `Clip` 필드로 교체. `PreloadAsync` 설명을 클립 프리로드로 수정.
- 매뉴얼: "카탈로그 키 모델"의 리소스키 분리 설명을 직접 참조 설명으로 교체(여러 `Key`가 같은 `AudioClip`을 가리켜도 됨은 유지). "리소스 로딩 위임" 절 삭제(또는 "직접 참조라 런타임 로딩 위임 없음"으로 대체). "한계/후속 과제"의 프리로드 에러 처리 항목을 임포트 설정 의존으로 갱신.
- 테스트 절: `IResourceService` NSubstitute 대체 언급 제거, 카탈로그 클립을 `SerializedObject`로 주입해 검증한다고 수정.

- [ ] **Step 2: CLAUDE.md 개정**

- "핵심 서비스 > SoundService" 설명의 "클립 로드도 Resources→Addressables fallback" 문구를 "클립은 `SoundCatalogSO`가 `AudioClip` 직접 참조로 보유(런타임 로딩 없음)"로 수정.
- "리소스 로딩은 ResourceService에 위임한다" 규약 하단의 "향후 `PoolService`/`SoundService`의 중복 로딩 로직도 `IResourceService` 위임으로 전환 예정" 문구에서 **SoundService를 제거**(PoolService만 남김). SoundService는 컴파일 타임 직접 참조라 위임 대상이 아님을 한 줄로 명시.

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/SoundService/README.md CLAUDE.md
git commit -m "[STRUCTURAL] SoundService 직접 클립 참조 전환에 맞춰 문서 개정

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review 결과

- **Spec 커버리지:** 스펙의 데이터 모델(§1)=Task 1·3, SoundService 단순화(§2)=Task 2, 등록부(§3)=Task 2(시그니처 유지 확인), 소비 측/문서(§4)=Task 4, 테스트(§5)=Task 1~3. `ResourceKey` 완전 제거=Task 3. Preload EditMode 열거 검증=Task 2 Step 1의 `PreloadAsync는_카탈로그의_PreloadClips를_열거한다`. 누락 없음.
- **Placeholder 스캔:** "적절히 처리" 류 문구 없음. 모든 코드 스텝에 실제 코드 포함.
- **타입 일관성:** `TryGetClip(string, out AudioClip)`·`PreloadClips`·`SoundService(ISoundCatalog)` 시그니처가 Task 1→2→3 전반에서 일치. 테스트 목의 `TryGetClip(key, out Arg.Any<AudioClip>())`도 인터페이스와 일치.
- **주의:** `LoadAudioDataAsync`는 절차적 `AudioClip.Create` 클립에서 `loadState == Loaded`로 즉시 반환되어 EditMode 테스트가 멈추지 않음. 실제 압축 클립의 비동기성은 임포트 설정 의존(README에 명시).
