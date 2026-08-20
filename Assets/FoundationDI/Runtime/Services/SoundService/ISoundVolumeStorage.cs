using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// Output 볼륨 영속화 seam. 기본 구현은 <see cref="PlayerPrefs"/>를 사용하며,
    /// 단위 테스트에서는 대체 구현을 주입해 외부 의존 없이 검증한다.
    /// </summary>
    public interface ISoundVolumeStorage
    {
        bool HasKey(string key);
        float GetFloat(string key, float defaultValue);
        void SetFloat(string key, float value);
        void Save();
    }

    /// <summary>PlayerPrefs 기반 기본 구현.</summary>
    public sealed class PlayerPrefsVolumeStorage : ISoundVolumeStorage
    {
        public bool HasKey(string key) => PlayerPrefs.HasKey(key);

        public float GetFloat(string key, float defaultValue) => PlayerPrefs.GetFloat(key, defaultValue);

        public void SetFloat(string key, float value) => PlayerPrefs.SetFloat(key, value);

        public void Save() => PlayerPrefs.Save();
    }
}
