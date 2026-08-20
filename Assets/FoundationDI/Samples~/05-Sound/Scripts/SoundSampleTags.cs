namespace DarkNaku.FoundationDI.Samples
{
    /// <summary>
    /// 샘플이 쓰는 오디오 태그.
    /// 생성된 <c>SFX</c>/<c>Track</c> 상수 대신 문자열 오버로드를 쓰는 이유는,
    /// 유사 enum 상수가 프로젝트의 컬렉션 내용에 따라 만들어지기 때문이다.
    /// 샘플 데이터를 아직 등록하지 않은 프로젝트에서도 이 스크립트가 컴파일되어야 한다.
    /// 실제 게임 코드에서는 <c>SFX.Click</c>처럼 생성된 상수를 쓰는 쪽이 안전하다.
    /// </summary>
    public static class SoundSampleTags
    {
        public const string Click = "SmpClick";
        public const string Coin = "SmpCoin";

        public const string Song1 = "SmpSong1";
        public const string Song2 = "SmpSong2";

        public const string LayerDrum = "SmpLayerDrum";
        public const string LayerBass = "SmpLayerBass";
        public const string LayerLead = "SmpLayerLead";

        public static readonly string[] All =
        {
            Click, Coin, Song1, Song2, LayerDrum, LayerBass, LayerLead
        };
    }
}
