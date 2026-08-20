using System.Collections.Generic;
using System.Linq;
using DarkNaku.FoundationDI;
using DarkNaku.FoundationDI.Editor;
using DarkNaku.FoundationDI.Samples;
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.SamplesEditor
{
    /// <summary>
    /// 샘플 오디오를 프로젝트의 사운드/음악 컬렉션에 한 번에 등록한다.
    /// Audio Creator 창에서 손으로 하는 작업과 같은 일을 스크립트로 하는 예시이기도 하다.
    /// </summary>
    public static class SoundSampleDataImporter
    {
        private const string AudioFolder = "Assets/FoundationDI/Samples/05-Sound/Audio/";

        [MenuItem("Tools/FoundationDI/Sound/Samples/Import Sample Audio", false, 80)]
        public static void Import()
        {
            var settings = SoundServiceAssetLocator.GetOrCreateSettings();

            bool addedSfx = ImportSfx(settings);
            bool addedMusic = ImportMusic(settings);

            if (addedSfx)
            {
                SoundEditorHelper.SaveCollectionChanges(Sections.Sounds);
            }

            if (addedMusic)
            {
                SoundEditorHelper.SaveCollectionChanges(Sections.Music);
            }

            Debug.Log(addedSfx || addedMusic
                ? "[SoundSample] 샘플 오디오를 등록했습니다. 05-Sound/Sound.unity 를 열고 Play 하세요."
                : "[SoundSample] 이미 모든 샘플 오디오가 등록되어 있습니다.");
        }

        [MenuItem("Tools/FoundationDI/Sound/Samples/Remove Sample Audio", false, 81)]
        public static void Remove()
        {
            var settings = SoundServiceAssetLocator.FindSettings();

            if (settings == null)
            {
                Debug.LogWarning("[SoundSample] SoundServiceSettings 에셋이 없습니다.");
                return;
            }

            foreach (var tag in SoundSampleTags.All)
            {
                settings.SoundDataCollection.RemoveSound(tag);
                settings.MusicDataCollection.RemoveMusicTrack(tag);
            }

            SoundEditorHelper.SaveCollectionChanges(Sections.Sounds);
            SoundEditorHelper.SaveCollectionChanges(Sections.Music);

            Debug.Log("[SoundSample] 샘플 오디오를 제거했습니다.");
        }

        private static bool ImportSfx(SoundServiceSettings settings)
        {
            var collection = settings.SoundDataCollection;

            bool added = false;

            added |= CreateSound(collection, SoundSampleTags.Click, CompressionPreset.FrequentSound,
                "SFX_SmpClick_1", "SFX_SmpClick_2", "SFX_SmpClick_3");
            added |= CreateSound(collection, SoundSampleTags.Coin, CompressionPreset.FrequentSound,
                "SFX_SmpCoin");

            return added;
        }

        private static bool ImportMusic(SoundServiceSettings settings)
        {
            var collection = settings.MusicDataCollection;

            bool added = false;

            added |= CreateMusic(collection, SoundSampleTags.Song1, "MUS_SmpSong1");
            added |= CreateMusic(collection, SoundSampleTags.Song2, "MUS_SmpSong2");
            added |= CreateMusic(collection, SoundSampleTags.LayerDrum, "MUS_SmpLayerDrum");
            added |= CreateMusic(collection, SoundSampleTags.LayerBass, "MUS_SmpLayerBass");
            added |= CreateMusic(collection, SoundSampleTags.LayerLead, "MUS_SmpLayerLead");

            return added;
        }

        private static bool CreateSound(SoundDataCollection collection, string tag, CompressionPreset preset,
            params string[] clipNames)
        {
            if (collection.Sounds.Any(sound => sound.Tag == tag)) return false;

            var clips = LoadClips(clipNames);

            if (clips.Length == 0) return false;

            if (!collection.CreateSound(clips, tag, preset, false, out string message))
            {
                Debug.LogError($"[SoundSample] {message}");
                return false;
            }

            SoundEditorHelper.ChangeAudioClipImportSettings(clips, preset, false);

            return true;
        }

        private static bool CreateMusic(MusicDataCollection collection, string tag, params string[] clipNames)
        {
            if (collection.MusicTracks.Any(track => track.Tag == tag)) return false;

            var clips = LoadClips(clipNames);

            if (clips.Length == 0) return false;

            // 샘플 클립은 4초 남짓이라 스트리밍 대신 메모리 압축으로 들어간다.
            const CompressionPreset preset = CompressionPreset.AmbientMusic;

            if (!collection.CreateMusicTrack(clips, tag, preset, false, out string message))
            {
                Debug.LogError($"[SoundSample] {message}");
                return false;
            }

            SoundEditorHelper.ChangeAudioClipImportSettings(clips, preset, false);

            return true;
        }

        private static AudioClip[] LoadClips(IEnumerable<string> clipNames)
        {
            var clips = new List<AudioClip>();

            foreach (var name in clipNames)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFolder + name + ".wav");

                if (clip == null)
                {
                    Debug.LogError($"[SoundSample] 오디오를 찾지 못했습니다: {AudioFolder}{name}.wav");
                    continue;
                }

                clips.Add(clip);
            }

            return clips.ToArray();
        }
    }
}
