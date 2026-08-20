using DarkNaku.FoundationDI;
using DarkNaku.FoundationDI.Samples;

namespace DarkNaku.FoundationDI.SamplesEditor
{
    /// <summary>샘플별 오디오 묶음 정의. 샘플을 추가하면 여기에 항목을 하나 더 만든다.</summary>
    public static class SoundSampleData
    {
        public static readonly SoundSampleAudioSet Sound = new()
        {
            SampleName = "05 Sound",
            AudioFolderName = "Audio",
            Sfx = new[]
            {
                // 클립 3개를 한 태그에 묶어 두면 재생 때마다 무작위로 하나가 선택된다.
                new SoundSampleAudioSet.Group(SoundSampleTags.Click, CompressionPreset.FrequentSound,
                    "SFX_SmpClick_1", "SFX_SmpClick_2", "SFX_SmpClick_3"),
                new SoundSampleAudioSet.Group(SoundSampleTags.Coin, CompressionPreset.FrequentSound,
                    "SFX_SmpCoin")
            },
            Music = new[]
            {
                new SoundSampleAudioSet.Group(SoundSampleTags.Song1, CompressionPreset.AmbientMusic, "MUS_SmpSong1"),
                new SoundSampleAudioSet.Group(SoundSampleTags.Song2, CompressionPreset.AmbientMusic, "MUS_SmpSong2"),
                new SoundSampleAudioSet.Group(SoundSampleTags.LayerDrum, CompressionPreset.AmbientMusic,
                    "MUS_SmpLayerDrum"),
                new SoundSampleAudioSet.Group(SoundSampleTags.LayerBass, CompressionPreset.AmbientMusic,
                    "MUS_SmpLayerBass"),
                new SoundSampleAudioSet.Group(SoundSampleTags.LayerLead, CompressionPreset.AmbientMusic,
                    "MUS_SmpLayerLead")
            }
        };
    }
}
