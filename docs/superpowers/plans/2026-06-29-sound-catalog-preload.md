# 사운드 카탈로그 + 프리로드 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 사운드를 문자열키로 식별하는 카탈로그(ScriptableObject)와 비동기 프리로드 기능을 SoundService에 추가한다.

**Architecture:** `ISoundCatalog` 인터페이스로 문자열키→리소스키 매핑을 추상화하고(`SoundCatalog` ScriptableObject가 구현), SoundService가 이를 생성자로 주입받아 `Play`/`PlayBGM`을 카탈로그 경유(엄격 모드)로 동작시키며 `PreloadAsync()`로 `IResourceService.LoadAsync` 병렬 프리로드를 수행한다.

**Tech Stack:** Unity 6000.3, VContainer, UniTask, NSubstitute(테스트), Unity Test Framework(EditMode).

## Global Constraints

- 네임스페이스: `DarkNaku.FoundationDI` (단일).
- 에셋 로딩은 직접 `Resources`/`Addressables` 호출 금지 — `IResourceService`에 위임.
- 테스트는 EditMode, `FoundationDI.Tests` asmdef, NSubstitute로 seam 대체. 테스트 함수명은 한국어.
- 컴파일·테스트는 UnityMCP로만 수행: 스크립트 변경 후 `refresh_unity(compile=request, mode=force)` → `read_console(types=[error])`로 컴파일 확인 → `run_tests` + `get_test_job` 폴링.
- 커밋: STRUCTURAL/BEHAVIORAL 분리, 제목에 접두어. 커밋 메시지 끝에 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- 모든 테스트 통과 시에만 커밋.

## 파일 구조

- Create: `Assets/FoundationDI/Runtime/Services/SoundService/SoundCatalog.cs`
  - `SoundEntry`(struct), `ISoundCatalog`(interface), `SoundCatalog`(ScriptableObject) 한 파일에 응집 (기존 `SoundService.cs`가 `ISoundService`+`SoundService`를 한 파일에 두는 관행을 따름).
- Create: `Assets/FoundationDI/Runtime/Services/SoundService/SoundServiceVContainerExtensions.cs`
  - `RegisterSoundService` 확장 메서드.
- Modify: `Assets/FoundationDI/Runtime/Services/SoundService/SoundService.cs`
  - 생성자에 `ISoundCatalog` 추가, `Play`/`PlayBGM` 카탈로그 경유, `PreloadAsync` 추가, `ISoundService`에 `PreloadAsync` 선언, `using Cysharp.Threading.Tasks;` 추가.
- Create: `Assets/FoundationDI/Tests/SoundCatalogTest.cs`
- Modify: `Assets/FoundationDI/Tests/SoundServiceTest.cs`
  - 기존 모든 `new SoundService(resource)`를 카탈로그 mock 포함으로 마이그레이션, 카탈로그 헬퍼 추가.

---

## Task 1: SoundCatalog ScriptableObject + ISoundCatalog

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/SoundService/SoundCatalog.cs`
- Test: `Assets/FoundationDI/Tests/SoundCatalogTest.cs`

**Interfaces:**
- Consumes: 없음.
- Produces:
  - `struct SoundEntry { public string Key; public string ResourceKey; public bool Preload; }`
  - `interface ISoundCatalog { bool TryGetResourceKey(string key, out string resourceKey); IReadOnlyList<string> Keys { get; } IEnumerable<string> PreloadResourceKeys { get; } }`
  - `sealed class SoundCatalog : ScriptableObject, ISoundCatalog` — `[SerializeField] private List<SoundEntry> _entries` 보유. 조회는 첫 호출 시 lazy 빌드.

- [ ] **Step 1: 실패 테스트 작성** — `Assets/FoundationDI/Tests/SoundCatalogTest.cs` 생성

테스트에서 SO에 데이터를 주입하려면 `JsonUtility.FromJsonOverwrite`를 쓴다(직렬화 필드 `_entries`, `SoundEntry`의 public 필드명을 그대로 사용).

```csharp
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class SoundCatalogTest
{
    private static SoundCatalog MakeCatalog(string json)
    {
        var catalog = ScriptableObject.CreateInstance<SoundCatalog>();
        JsonUtility.FromJsonOverwrite(json, catalog);
        return catalog;
    }

    [Test]
    public void 등록된_키는_리소스키로_변환된다()
    {
        var catalog = MakeCatalog(
            "{\"_entries\":[{\"Key\":\"Jump\",\"ResourceKey\":\"sfx/jump\",\"Preload\":false}]}");

        var found = ((ISoundCatalog)catalog).TryGetResourceKey("Jump", out var resourceKey);

        Assert.IsTrue(found);
        Assert.AreEqual("sfx/jump", resourceKey);

        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void 미등록_키는_변환에_실패한다()
    {
        var catalog = MakeCatalog("{\"_entries\":[]}");

        var found = ((ISoundCatalog)catalog).TryGetResourceKey("None", out _);

        Assert.IsFalse(found);

        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void Keys는_등록_순서대로_노출된다()
    {
        var catalog = MakeCatalog(
            "{\"_entries\":[{\"Key\":\"A\",\"ResourceKey\":\"r/a\",\"Preload\":false}," +
            "{\"Key\":\"B\",\"ResourceKey\":\"r/b\",\"Preload\":false}]}");

        CollectionAssert.AreEqual(new[] { "A", "B" }, (List<string>)((ISoundCatalog)catalog).Keys);

        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void PreloadResourceKeys는_Preload가_true인_항목만_노출한다()
    {
        var catalog = MakeCatalog(
            "{\"_entries\":[{\"Key\":\"A\",\"ResourceKey\":\"r/a\",\"Preload\":true}," +
            "{\"Key\":\"B\",\"ResourceKey\":\"r/b\",\"Preload\":false}," +
            "{\"Key\":\"C\",\"ResourceKey\":\"r/c\",\"Preload\":true}]}");

        CollectionAssert.AreEquivalent(
            new[] { "r/a", "r/c" },
            new List<string>(((ISoundCatalog)catalog).PreloadResourceKeys));

        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void 중복_키는_경고를_남기고_마지막_값을_채택한다()
    {
        var catalog = MakeCatalog(
            "{\"_entries\":[{\"Key\":\"X\",\"ResourceKey\":\"r/x1\",\"Preload\":false}," +
            "{\"Key\":\"X\",\"ResourceKey\":\"r/x2\",\"Preload\":false}]}");

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Duplicate key"));

        var found = ((ISoundCatalog)catalog).TryGetResourceKey("X", out var resourceKey);

        Assert.IsTrue(found);
        Assert.AreEqual("r/x2", resourceKey);

        Object.DestroyImmediate(catalog);
    }
}
```

- [ ] **Step 2: 컴파일/실패 확인**

UnityMCP: `refresh_unity(compile=request, mode=force)` → `read_console(types=[error])`. `SoundCatalog`/`SoundEntry`/`ISoundCatalog` 미정의로 컴파일 에러가 나야 한다(= RED, 아직 타입 없음).

- [ ] **Step 3: 최소 구현** — `Assets/FoundationDI/Runtime/Services/SoundService/SoundCatalog.cs` 생성

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [Serializable]
    public struct SoundEntry
    {
        public string Key;          // 논리 이름 (Play 인자, 드롭다운 표시)
        public string ResourceKey;  // ResourceService 로드 키
        public bool Preload;        // 프리로드 대상 여부
    }

    public interface ISoundCatalog
    {
        bool TryGetResourceKey(string key, out string resourceKey);
        IReadOnlyList<string> Keys { get; }
        IEnumerable<string> PreloadResourceKeys { get; }
    }

    [CreateAssetMenu(fileName = "SoundCatalog", menuName = "DarkNaku/SoundCatalog")]
    public sealed class SoundCatalog : ScriptableObject, ISoundCatalog
    {
        [SerializeField] private List<SoundEntry> _entries = new();

        private Dictionary<string, string> _map;
        private List<string> _keys;

        public IReadOnlyList<string> Keys
        {
            get
            {
                EnsureBuilt();
                return _keys;
            }
        }

        public IEnumerable<string> PreloadResourceKeys
        {
            get
            {
                foreach (var entry in _entries)
                {
                    if (entry.Preload)
                    {
                        yield return entry.ResourceKey;
                    }
                }
            }
        }

        public bool TryGetResourceKey(string key, out string resourceKey)
        {
            EnsureBuilt();
            return _map.TryGetValue(key, out resourceKey);
        }

        private void EnsureBuilt()
        {
            if (_map != null) return;

            _map = new Dictionary<string, string>();
            _keys = new List<string>();

            foreach (var entry in _entries)
            {
                if (string.IsNullOrEmpty(entry.Key)) continue;

                if (_map.ContainsKey(entry.Key))
                {
                    Debug.LogWarning($"[SoundCatalog] Duplicate key '{entry.Key}', overwriting with last value.");
                }
                else
                {
                    _keys.Add(entry.Key);
                }

                _map[entry.Key] = entry.ResourceKey;
            }
        }
    }
}
```

- [ ] **Step 4: 컴파일 확인 후 테스트 통과 확인**

UnityMCP: `refresh_unity(compile=request, mode=force)` → `read_console(types=[error])`(에러 0) → `run_tests(mode=EditMode, assembly_names=[FoundationDI.Tests], test_names=[SoundCatalogTest])` → `get_test_job`. 5개 모두 PASS 기대.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/SoundService/SoundCatalog.cs Assets/FoundationDI/Runtime/Services/SoundService/SoundCatalog.cs.meta Assets/FoundationDI/Tests/SoundCatalogTest.cs Assets/FoundationDI/Tests/SoundCatalogTest.cs.meta
git commit -m "$(cat <<'EOF'
[BEHAVIORAL] SoundCatalog ScriptableObject 및 ISoundCatalog 추가

- 문자열키→리소스키 매핑, Keys/PreloadResourceKeys 노출
- 중복 키 경고(마지막 값 채택), lazy 조회 빌드
- SoundCatalogTest 5개 추가

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: SoundService에 카탈로그 통합 (엄격 모드) + 기존 테스트 마이그레이션

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/SoundService/SoundService.cs`
- Test: `Assets/FoundationDI/Tests/SoundServiceTest.cs`

**Interfaces:**
- Consumes: `ISoundCatalog`(Task 1), `IResourceService`(기존).
- Produces:
  - 생성자 시그니처 변경: `SoundService(IResourceService resourceService, ISoundCatalog catalog)`.
  - `Play(string key)` / `PlayBGM(string key)`가 `catalog.TryGetResourceKey`로 변환 후 동작(미등록 시 `Debug.LogError` + return).

> 주의: 생성자 시그니처가 바뀌므로 기존 `SoundServiceTest`의 모든 `new SoundService(resource)`가 컴파일 실패한다. 이 태스크에서 한꺼번에 마이그레이션한다.

- [ ] **Step 1: 마이그레이션 + 신규 테스트 작성** — `SoundServiceTest.cs` 전면 수정

카탈로그 헬퍼를 추가하고, 기존 테스트의 `Play("sfx")`/`PlayBGM("bgm")`가 카탈로그를 거쳐 동일 리소스키로 매핑되도록 한다(예: `"sfx"→"sfx"`). 파일 상단 `using` 블록을 아래로 교체:

```csharp
using System.Linq;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
```

`MakeClip` 아래에 헬퍼 추가:

```csharp
    private static ISoundCatalog Catalog(params (string key, string resourceKey)[] entries)
    {
        var catalog = Substitute.For<ISoundCatalog>();
        foreach (var (key, resourceKey) in entries)
        {
            var captured = resourceKey;
            catalog.TryGetResourceKey(key, out Arg.Any<string>())
                .Returns(call =>
                {
                    call[1] = captured;
                    return true;
                });
        }
        catalog.Keys.Returns(entries.Select(e => e.key).ToList());
        return catalog;
    }
```

기존/신규 테스트를 아래 형태로 맞춘다(생성자 2번째 인자에 `Catalog(...)` 전달). 카탈로그가 필요 없는 테스트(초기값/영속성/IsPlayingBGM 정지)는 빈 카탈로그 `Catalog()`를 넘긴다. 전체 교체본:

```csharp
    [Test]
    public void SFX_재생시_ResourceService에_클립로드를_위임한다()
    {
        var clip = MakeClip();
        var resource = Substitute.For<IResourceService>();
        resource.Load<AudioClip>("sfx").Returns(clip);
        var sut = new SoundService(resource, Catalog(("sfx", "sfx"))) { SFXEnabled = true };

        sut.Play("sfx");

        resource.Received(1).Load<AudioClip>("sfx");

        sut.Dispose();
    }

    [Test]
    public void 같은_SFX_재생시_클립로드는_한번만_위임한다()
    {
        var clip = MakeClip();
        var resource = Substitute.For<IResourceService>();
        resource.Load<AudioClip>("sfx").Returns(clip);
        var sut = new SoundService(resource, Catalog(("sfx", "sfx"))) { SFXEnabled = true };

        sut.Play("sfx");
        sut.Play("sfx");

        resource.Received(1).Load<AudioClip>("sfx");

        sut.Dispose();
    }

    [Test]
    public void BGM_재생시_ResourceService에_클립로드를_위임한다()
    {
        var clip = MakeClip();
        var resource = Substitute.For<IResourceService>();
        resource.Load<AudioClip>("bgm").Returns(clip);
        var sut = new SoundService(resource, Catalog(("bgm", "bgm"))) { BGMEnabled = true };

        sut.PlayBGM("bgm");

        resource.Received(1).Load<AudioClip>("bgm");

        sut.Dispose();
    }

    [Test]
    public void Dispose시_로드한_모든_키를_Release한다()
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<AudioClip>("sfx").Returns(MakeClip());
        resource.Load<AudioClip>("bgm").Returns(MakeClip());
        var sut = new SoundService(resource, Catalog(("sfx", "sfx"), ("bgm", "bgm")))
            { SFXEnabled = true, BGMEnabled = true };

        sut.Play("sfx");
        sut.PlayBGM("bgm");
        sut.Dispose();

        resource.Received(1).Release("sfx");
        resource.Received(1).Release("bgm");
    }

    [Test]
    public void 카탈로그에_없는_SFX키는_로드하지_않고_에러를_남긴다()
    {
        var resource = Substitute.For<IResourceService>();
        var sut = new SoundService(resource, Catalog()) { SFXEnabled = true };

        UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
            new System.Text.RegularExpressions.Regex("not found in catalog"));

        sut.Play("missing");

        resource.DidNotReceive().Load<AudioClip>(Arg.Any<string>());

        sut.Dispose();
    }
```

그리고 카탈로그 무관 테스트들의 생성자 호출을 `new SoundService(resource, Catalog())`로 바꾼다(아래 5개):
`생성_직후_SFX는_활성화_상태다`, `생성_직후_BGM은_활성화_상태다`, `생성_직후_BGM은_재생중이_아니다`, `SFX_활성화_상태는_PlayerPrefs에_영속된다`(2곳의 `new SoundService(resource)` → `new SoundService(resource, Catalog())`), `BGM_활성화_상태는_PlayerPrefs에_영속된다`(2곳), `BGM_재생중이면_IsPlayingBGM이_true다`(이 테스트는 `Catalog(("bgm","bgm"))` 사용하고 `PlayBGM("bgm")` 유지).

- [ ] **Step 2: 컴파일/실패 확인**

UnityMCP: `refresh_unity` → `read_console(types=[error])`. 현재 `SoundService` 생성자는 인자 1개라 컴파일 에러(생성자 2-인자 없음) = RED.

- [ ] **Step 3: SoundService 구현 변경** — `SoundService.cs`

(a) 상단 using에 추가:

```csharp
using Cysharp.Threading.Tasks;
```

(b) 필드/생성자: `_catalog` 필드 추가 및 생성자 시그니처 변경. 기존
```csharp
        private readonly IResourceService _resourceService;
        private readonly Transform _root;
```
를
```csharp
        private readonly IResourceService _resourceService;
        private readonly ISoundCatalog _catalog;
        private readonly Transform _root;
```
로, 기존
```csharp
        public SoundService(IResourceService resourceService)
        {
            _resourceService = resourceService;
            _table = new Dictionary<string, AudioClip>();
```
를
```csharp
        public SoundService(IResourceService resourceService, ISoundCatalog catalog)
        {
            _resourceService = resourceService;
            _catalog = catalog;
            _table = new Dictionary<string, AudioClip>();
```
로 변경.

(c) `Play` 메서드를 카탈로그 경유로 교체:

```csharp
        public void Play(string key)
        {
            if (Mathf.Approximately(VolumeSFX, 0f) || !SFXEnabled) return;
            if (_playedClipInThisFrame.Contains(key)) return;

            if (!_catalog.TryGetResourceKey(key, out var resourceKey))
            {
                Debug.LogError($"[SoundService] Play : Key not found in catalog. ({key})");
                return;
            }

            var player = GetPlayer();
            var clip = GetClip(resourceKey);

            if (clip == null) return;

            player.clip = clip;
            player.loop = false;
            player.volume = VolumeSFX;
            player.Play();

            _playedClipInThisFrame.Add(key);
        }
```

(d) `PlayBGM` 메서드를 카탈로그 경유로 교체:

```csharp
        public void PlayBGM(string key)
        {
            if (Mathf.Approximately(VolumeBGM, 0f) || !BGMEnabled) return;

            if (!_catalog.TryGetResourceKey(key, out var resourceKey))
            {
                Debug.LogError($"[SoundService] PlayBGM : Key not found in catalog. ({key})");
                return;
            }

            var clip = GetClip(resourceKey);

            if (clip == null) return;

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

(`GetClip(string key)`는 그대로 두되 인자로 리소스키를 받는다. 내부 로직 변경 없음.)

- [ ] **Step 4: 컴파일 확인 후 전체 테스트 통과 확인**

UnityMCP: `refresh_unity` → `read_console(types=[error])`(에러 0) → `run_tests(mode=EditMode, assembly_names=[FoundationDI.Tests])` → `get_test_job`. SoundCatalogTest 5 + SoundServiceTest(마이그레이션 10 + 신규 1 = 11) + ResourceServiceTest 9 = **25개 PASS** 기대.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/SoundService/SoundService.cs Assets/FoundationDI/Tests/SoundServiceTest.cs
git commit -m "$(cat <<'EOF'
[BEHAVIORAL] SoundService Play/PlayBGM을 카탈로그 경유 엄격 모드로 변경

- 생성자에 ISoundCatalog 주입, 문자열키→리소스키 변환 후 재생
- 카탈로그 미등록 키는 Debug.LogError 후 무시(엄격 모드)
- 기존 SoundServiceTest를 카탈로그 mock 기반으로 마이그레이션 + 미등록 키 테스트 추가

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: PreloadAsync 구현

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/SoundService/SoundService.cs`
- Test: `Assets/FoundationDI/Tests/SoundServiceTest.cs`

**Interfaces:**
- Consumes: `ISoundCatalog.PreloadResourceKeys`, `IResourceService.LoadAsync<AudioClip>`.
- Produces: `UniTask PreloadAsync()` (ISoundService에 선언, SoundService에 구현). 프리로드된 리소스키는 `_table` 캐시에 채워져 이후 `Play`가 추가 `Load` 없이 사용.

- [ ] **Step 1: 실패 테스트 작성** — `SoundServiceTest.cs` 끝에 추가

UniTask 비동기 테스트는 ResourceServiceTest의 `UniTask.ToCoroutine` + `[UnityTest]` 패턴을 따른다. 파일 상단 using에 다음 추가:

```csharp
using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEngine.TestTools;
```

테스트 추가:

```csharp
    [UnityTest]
    public IEnumerator PreloadAsync는_Preload대상_리소스키를_LoadAsync로_로드한다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.LoadAsync<AudioClip>("r/a").Returns(UniTask.FromResult(MakeClip()));
        resource.LoadAsync<AudioClip>("r/c").Returns(UniTask.FromResult(MakeClip()));
        var catalog = Substitute.For<ISoundCatalog>();
        catalog.PreloadResourceKeys.Returns(new[] { "r/a", "r/c" });
        var sut = new SoundService(resource, catalog);

        await sut.PreloadAsync();

        _ = resource.Received(1).LoadAsync<AudioClip>("r/a");
        _ = resource.Received(1).LoadAsync<AudioClip>("r/c");

        sut.Dispose();
    });

    [UnityTest]
    public IEnumerator 프리로드된_키_재생시_추가_Load없이_캐시를_사용한다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.LoadAsync<AudioClip>("r/a").Returns(UniTask.FromResult(MakeClip()));
        var catalog = Substitute.For<ISoundCatalog>();
        catalog.PreloadResourceKeys.Returns(new[] { "r/a" });
        catalog.TryGetResourceKey("A", out Arg.Any<string>())
            .Returns(call => { call[1] = "r/a"; return true; });
        var sut = new SoundService(resource, catalog) { SFXEnabled = true };

        await sut.PreloadAsync();
        sut.Play("A");

        resource.DidNotReceive().Load<AudioClip>(Arg.Any<string>());

        sut.Dispose();
    });
```

- [ ] **Step 2: 컴파일/실패 확인**

UnityMCP: `refresh_unity` → `read_console(types=[error])`. `PreloadAsync` 미정의로 컴파일 에러 = RED.

- [ ] **Step 3: PreloadAsync 구현** — `SoundService.cs`

(a) `ISoundService` 인터페이스에 선언 추가(`void StopBGM();` 아래):

```csharp
        UniTask PreloadAsync();
```

(b) `SoundService`에 구현 추가(`StopBGM` 메서드 아래):

```csharp
        public async UniTask PreloadAsync()
        {
            var tasks = new List<UniTask>();

            foreach (var resourceKey in _catalog.PreloadResourceKeys)
            {
                tasks.Add(PreloadOneAsync(resourceKey));
            }

            await UniTask.WhenAll(tasks);
        }

        private async UniTask PreloadOneAsync(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey)) return;
            if (_table.ContainsKey(resourceKey)) return;

            var clip = await _resourceService.LoadAsync<AudioClip>(resourceKey);

            if (clip != null)
            {
                _table[resourceKey] = clip;
            }
        }
```

- [ ] **Step 4: 컴파일 확인 후 전체 테스트 통과 확인**

UnityMCP: `refresh_unity` → `read_console(types=[error])`(에러 0) → `run_tests(mode=EditMode, assembly_names=[FoundationDI.Tests])` → `get_test_job`. **27개 PASS** 기대(이전 25 + 신규 2).

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/SoundService/SoundService.cs Assets/FoundationDI/Tests/SoundServiceTest.cs
git commit -m "$(cat <<'EOF'
[BEHAVIORAL] SoundService.PreloadAsync 비동기 프리로드 추가

- 카탈로그 PreloadResourceKeys를 IResourceService.LoadAsync로 병렬 로드
- 로드한 클립을 리소스키 캐시(_table)에 채워 첫 재생 지연 제거
- PreloadAsync 테스트 2개 추가

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: RegisterSoundService DI 확장 메서드

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/SoundService/SoundServiceVContainerExtensions.cs`

**Interfaces:**
- Consumes: `SoundCatalog`(Task 1), `ISoundService`/`SoundService`(Task 2,3).
- Produces: `IContainerBuilder.RegisterSoundService(SoundCatalog catalog)`.

> 이 태스크는 VContainer 등록 배선만 추가한다. EditMode 단위 테스트로 컨테이너 빌드까지 검증하면 실제 `SoundService` 생성자가 GameObject를 만들어 부수효과가 크므로, 검증은 **컴파일 성공 + 기존 전체 테스트 그린 유지**로 한다(과한 통합 테스트는 YAGNI).

- [ ] **Step 1: 확장 메서드 작성** — 파일 생성

```csharp
using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class SoundServiceVContainerExtensions
    {
        /// <summary>
        /// SoundService를 컨테이너에 등록한다.
        /// 전제: 호출 전에 <see cref="IResourceService"/>가 이미 등록되어 있어야 한다
        /// (SoundService가 클립 로드를 IResourceService에 위임함).
        /// </summary>
        public static void RegisterSoundService(this IContainerBuilder builder, SoundCatalog catalog)
        {
            builder.RegisterInstance<ISoundCatalog>(catalog);
            builder.Register<ISoundService, SoundService>(Lifetime.Singleton);
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인 후 전체 테스트 통과 확인**

UnityMCP: `refresh_unity` → `read_console(types=[error])`(에러 0) → `run_tests(mode=EditMode, assembly_names=[FoundationDI.Tests])` → `get_test_job`. **27개 PASS 유지** 기대.

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/SoundService/SoundServiceVContainerExtensions.cs Assets/FoundationDI/Runtime/Services/SoundService/SoundServiceVContainerExtensions.cs.meta
git commit -m "$(cat <<'EOF'
[BEHAVIORAL] RegisterSoundService DI 확장 메서드 추가

- ISoundCatalog 인스턴스 등록 + ISoundService/SoundService 싱글톤 등록
- IResourceService 선등록 전제(UIManager 패턴 준수)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## 자체 검토 결과

- **스펙 커버리지**: 3.1 ISoundCatalog → Task1, 3.2 SoundCatalog/SoundEntry → Task1, 3.3 SoundService(생성자/Play/PreloadAsync) → Task2·3, 3.4 RegisterSoundService → Task4, 5 에러처리(엄격/중복키) → Task2/Task1, 6 테스트전략 → 각 Task. 7(SoundButton 등)은 의도적 범위 밖. 누락 없음.
- **플레이스홀더**: 없음(모든 스텝에 실제 코드/명령 포함).
- **타입 일관성**: `TryGetResourceKey(string, out string)`, `PreloadResourceKeys`, `Keys`, `PreloadAsync()`, 생성자 `(IResourceService, ISoundCatalog)`가 전 태스크에서 동일하게 사용됨.
