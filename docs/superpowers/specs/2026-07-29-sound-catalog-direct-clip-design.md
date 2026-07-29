# SoundCatalog 직접 클립 참조 전환 설계

- 날짜: 2026-07-29
- 상태: 승인됨 (구현 계획 대기)
- 관련: `2026-06-29-sound-catalog-preload-design.md` (리소스 키 기반 카탈로그를 이 문서가 대체)

## 배경 / 문제

현재 `SoundCatalogSO`는 `Key(논리 이름) → ResourceKey(문자열)` 매핑만 들고 있고, `SoundService`는 재생 시마다 `IResourceService.Load<AudioClip>(resourceKey)`로 실제 클립을 로드해 `_table`에 캐싱한다.

이 구조는 세 가지 부담을 만든다.

1. 클립을 Resources/Addressables에 배치하고, 그 경로/주소를 문자열로 정확히 적어야 한다(오타 시 런타임 로드 실패).
2. 카탈로그에 `Key`와 `ResourceKey` 두 필드를 이중 관리한다.
3. `SoundService`가 캐싱·참조 카운팅·async 로드·Dispose 시 Release까지 떠안는다.

## 결정

이 파운데이션을 쓸 게임의 오디오 규모는 **소·중형, 전부 빌드 내장**을 전제한다. Addressables 핫업데이트/분할 다운로드가 필요 없으므로, 사운드 클립을 카탈로그에 **직접 참조(AudioClip)**로 들고 `SoundService`에서 `IResourceService` 경유를 제거한다.

### CLAUDE.md 규약과의 관계

"모든 에셋 로딩은 `IResourceService`에 위임" 규약에서 사운드를 예외로 뺀다. 직접 참조는 런타임 Addressables/Resources 호출이 아니라 **컴파일 타임 에셋 참조**이므로, 위임할 "런타임 로딩" 자체가 사라지는 것이다(규약 위반이 아니라 적용 대상에서 제외). 구현 시 CLAUDE.md의 SoundService 설명과 "향후 SoundService도 ResourceService 위임 전환 예정" 문구를 이에 맞게 수정한다.

## 설계

### 1. 데이터 모델 (`SoundCatalogSO`)

```csharp
[Serializable]
public struct SoundEntry
{
    public string Key;       // 논리 이름 (Play 인자, 드롭다운 표시)
    public AudioClip Clip;   // 직접 참조 (기존 ResourceKey 문자열을 대체)
    public bool Preload;     // 첫 재생 디코딩 히치 방지 대상
}
```

- 기존 `ResourceKey` 필드는 **완전 제거**한다(마이그레이션 없음 — 현재 프로젝트에 카탈로그 `.asset`이 없음).

`ISoundCatalog` 인터페이스:

```csharp
public interface ISoundCatalog
{
    bool TryGetClip(string key, out AudioClip clip);   // was TryGetResourceKey
    IReadOnlyList<string> Keys { get; }                 // 유지 (에디터 드롭다운 소스)
    IEnumerable<AudioClip> PreloadClips { get; }        // was PreloadResourceKeys
}
```

`SoundCatalogSO` 내부:

- `_map`을 `Dictionary<string, AudioClip>`으로 변경.
- 중복 `Key` 경고 로직 유지.
- `PreloadClips`는 `entry.Preload && entry.Clip != null`인 항목의 `Clip`을 yield.
- `TryGetClip`은 `_map` 조회 결과를 그대로 반환. clip이 null인 항목의 null 처리는 소비 측(`SoundService`)의 null 가드에 맡긴다.

### 2. `SoundService` 단순화

- 생성자: `SoundService(ISoundCatalog catalog)` — **`IResourceService` 의존 제거**.
- 삭제: `_resourceService`, `_table`(캐싱), `GetClip`의 로드 폴백, `PreloadOneAsync`의 refcount 정리, `Dispose`의 `Release` 루프.
- `Play(key)` / `PlayBGM(key)`: 기존 `TryGetResourceKey → Load` 2단계를 `_catalog.TryGetClip(key, out clip)` 1단계로 축소. 프레임 중복 차단(`_playedClipInThisFrame`), 볼륨/enabled 가드, null 가드는 유지.
- `PreloadAsync()`: `PreloadClips`를 돌며 각 클립에 `LoadAudioData()`를 호출하고 `loadState`가 `Loaded`(또는 `Failed`)가 될 때까지 대기해 디코딩 히치를 선제 제거.
  - ⚠️ **전제**: 진짜 비동기 로드는 클립 임포트 설정의 "Load In Background"가 켜져 있어야 한다. 꺼져 있으면 `LoadAudioData()`가 메인 스레드를 동기 블로킹한다. 이 전제를 README에 명시한다.
- `Dispose`: `_disposable` 정리 + `_root` fake-null 가드 파괴만 남는다(기존 로직 유지, Release 루프만 제거).

### 3. 등록부

- `RegisterSoundService(this IContainerBuilder, SoundCatalogSO)` 시그니처 **유지**.
- "IResourceService가 먼저 등록되어야 함" 전제 주석 삭제(더 이상 불필요).

### 4. 소비 측 영향

- `SoundButton` / `SoundButtonEditor`: `Keys` 드롭다운과 `ISoundService.Play(key)`만 사용 → 변경 없음.
- README(`Services/SoundService/README.md`): 리소스 키 → 직접 클립 참조로 사용법 개정, Preload의 "Load In Background" 전제 추가.

### 5. 테스트

- **`SoundCatalogTest`**: JSON 역직렬화(`ResourceKey` 주입) 대신, `AudioClip.Create(...)`로 만든 클립을 `SerializedObject`로 `_entries`에 주입해 카탈로그를 구성. `TryGetClip` / `Keys` / `PreloadClips`(Preload=true만 노출) 검증.
- **`SoundServiceTest`**: `IResourceService` 목 제거. `ISoundCatalog` 목이 `AudioClip.Create` 클립을 반환하도록 재구성. Play/PlayBGM/프레임 중복 차단/볼륨·enabled 가드 테스트는 그대로 성립.
- **Preload 테스트 범위(결정)**: EditMode에서는 **`PreloadAsync`가 `PreloadClips`를 열거하는지**만 검증한다. 실제 `LoadAudioData()`/`loadState` 전이는 임포트 설정 의존이라 EditMode 단위 테스트가 취약하므로 다루지 않는다(필요 시 수동/PlayMode 확인).
- `SoundButtonTest`: `ISoundService` 목만 사용 → 변경 없음.

## 범위 밖 (Non-goals)

- Addressables 기반 오디오 핫업데이트/분할 다운로드.
- `PoolService` 등 다른 서비스의 로딩 로직 변경.
- 오디오 믹서 그룹, 페이드 등 재생 기능 확장.

## 리스크 / 유의점

- 직접 참조는 SO 로드 시 참조된 클립 에셋이 모두 메모리에 물린다(개별 언로드 불가). 소·중형 전제에서 수용.
- `PreloadAsync`의 비동기성은 임포트 설정에 의존(위 ⚠️ 참고).
