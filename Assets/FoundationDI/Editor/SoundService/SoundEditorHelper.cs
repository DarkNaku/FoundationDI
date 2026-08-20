using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>Audio Creator/Collection/Output Manager가 공유하는 에디터 동작 모음.</summary>
    internal static class SoundEditorHelper
    {
        internal static readonly Color32 OrangeColor = new(255, 192, 88, 255);
        internal static readonly Color32 GreyColor = new(142, 142, 142, 255);
        internal static readonly Color32 RedColor = new(255, 65, 65, 255);

        /// <summary>컬렉션 변경을 저장하고 해당 섹션의 유사 enum 코드를 다시 생성한다.</summary>
        internal static void SaveCollectionChanges(Sections section, bool saveAssets = true)
        {
            var settings = SoundServiceAssetLocator.GetOrCreateSettings();

            GenerateAudioEnum(settings, section);

            var collection = section == Sections.Sounds
                ? (Object)settings.SoundDataCollection
                : settings.MusicDataCollection;

            if (collection != null)
            {
                EditorUtility.SetDirty(collection);
            }

            if (!saveAssets) return;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>믹서에서 Output 목록을 다시 읽고 Output 유사 enum을 재생성한다.</summary>
        internal static void ReloadOutputsDatabase(bool saveAssets = true)
        {
            var settings = SoundServiceAssetLocator.GetOrCreateSettings();

            if (settings.MasterAudioMixer == null)
            {
                Debug.LogError("[SoundService] SoundServiceSettings에 Master AudioMixer가 지정되지 않았습니다.");
                return;
            }

            settings.OutputDataCollection.LoadOutputs(settings.MasterAudioMixer);

            EditorUtility.SetDirty(settings.OutputDataCollection);

            GenerateOutputsEnum(settings);

            if (!saveAssets) return;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>프리셋에 맞춰 오디오 클립의 임포트 설정을 바꾸고 재임포트한다.</summary>
        internal static void ChangeAudioClipImportSettings(AudioClip[] clips, CompressionPreset preset, bool forceMono)
        {
            foreach (var clip in clips)
            {
                if (clip == null) continue;

                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));

                if (importer == null) continue;

                var sampleSettings = importer.defaultSampleSettings;

                switch (preset)
                {
                    case CompressionPreset.AmbientMusic:
                        bool shortDuration = clip.length < 10f;
                        sampleSettings.loadType = shortDuration
                            ? AudioClipLoadType.CompressedInMemory
                            : AudioClipLoadType.Streaming;
                        sampleSettings.compressionFormat = shortDuration
                            ? AudioCompressionFormat.ADPCM
                            : AudioCompressionFormat.Vorbis;
                        sampleSettings.quality = 0.60f;
                        break;

                    case CompressionPreset.FrequentSound:
                        sampleSettings.loadType = AudioClipLoadType.DecompressOnLoad;
                        sampleSettings.compressionFormat = AudioCompressionFormat.ADPCM;
                        sampleSettings.quality = 1f;
                        break;

                    case CompressionPreset.OccasionalSound:
                        sampleSettings.loadType = AudioClipLoadType.CompressedInMemory;
                        sampleSettings.compressionFormat = AudioCompressionFormat.Vorbis;
                        sampleSettings.quality = 0.35f;
                        break;
                }

                importer.forceToMono = forceMono;
                sampleSettings.preloadAudioData = true;
                importer.loadInBackground = true;
                importer.defaultSampleSettings = sampleSettings;
                importer.SaveAndReimport();
            }
        }

        /// <summary>유사 enum 식별자로 쓸 수 있는 태그인지 검사한다(영숫자, 숫자로 시작 불가).</summary>
        internal static bool IsTagValid(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            if (Regex.IsMatch(tag, @"[^a-zA-Z0-9]")) return false;
            if (Regex.IsMatch(tag, "^[0-9]")) return false;

            return Regex.IsMatch(tag, @"[a-zA-Z]");
        }

        private static void GenerateAudioEnum(SoundServiceSettings settings, Sections section)
        {
            string typeName = section == Sections.Sounds ? nameof(SFX) : nameof(Track);

            var tags = section == Sections.Sounds
                ? settings.SoundDataCollection.Sounds.Select(sound => sound.Tag).ToArray()
                : settings.MusicDataCollection.MusicTracks.Select(track => track.Tag).ToArray();

            string folder = SoundServiceAssetLocator.GetGeneratedFolder(settings);

            PseudoEnumGenerator.Generate(typeName, tags, folder + typeName + "_Generated.cs");
        }

        private static void GenerateOutputsEnum(SoundServiceSettings settings)
        {
            var names = settings.OutputDataCollection.Outputs
                .Select(output => output.Name.Replace(" ", ""))
                .ToArray();

            string folder = SoundServiceAssetLocator.GetGeneratedFolder(settings);

            PseudoEnumGenerator.Generate(nameof(Output), names, folder + nameof(Output) + "_Generated.cs");
        }
    }
}
