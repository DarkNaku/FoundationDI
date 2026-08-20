namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 오디오 클립 임포트 설정 프리셋. 에디터의 Audio Creator가 이 값에 따라
    /// <see cref="UnityEditor.AudioImporter"/> 설정을 적용한다.
    /// </summary>
    public enum CompressionPreset
    {
        AmbientMusic,
        FrequentSound,
        OccasionalSound
    }
}
