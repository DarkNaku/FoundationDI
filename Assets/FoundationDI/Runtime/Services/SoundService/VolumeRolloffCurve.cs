namespace DarkNaku.FoundationDI
{
    /// <summary>거리에 따른 볼륨 감쇠 곡선 종류.</summary>
    public enum VolumeRolloffCurve
    {
        /// <summary>실제 소리에 가깝게 자연스럽게 감쇠한다.</summary>
        Logarithmic,

        /// <summary>일정한 비율로 감쇠한다.</summary>
        Linear
    }
}
