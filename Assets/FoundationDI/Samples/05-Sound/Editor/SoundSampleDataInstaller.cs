using System.Collections.Generic;
using System.Linq;
using DarkNaku.FoundationDI;
using DarkNaku.FoundationDI.Editor;
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.SamplesEditor
{
    /// <summary>
    /// 샘플 오디오를 프로젝트의 사운드/음악 컬렉션에 개별로 설치하고 되돌린다.
    /// Audio Creator 창에서 손으로 하는 작업을 스크립트로 하는 예시이기도 하다.
    /// </summary>
    public static class SoundSampleDataInstaller
    {
        /// <summary>이 묶음의 태그가 대상 컬렉션에 하나라도 들어 있으면 true.</summary>
        public static bool IsInstalled(SoundSampleAudioSet set)
        {
            var settings = SoundServiceAssetLocator.FindSettings();

            if (settings == null) return false;

            return set.Sfx.Any(group => HasSound(settings, group.Tag)) ||
                   set.Music.Any(group => HasMusic(settings, group.Tag));
        }

        /// <summary>샘플 오디오를 프로젝트 컬렉션에 등록하고 유사 enum을 다시 생성한다.</summary>
        public static void Install(SoundSampleAudioSet set)
        {
            var settings = SoundServiceAssetLocator.GetOrCreateSettings();
            string audioFolder = set.ResolveAudioFolder();

            if (audioFolder == null) return;

            bool addedSfx = set.Sfx.Count(group => InstallSfx(settings, group, audioFolder)) > 0;
            bool addedMusic = set.Music.Count(group => InstallMusic(settings, group, audioFolder)) > 0;

            if (addedSfx) SoundEditorHelper.SaveCollectionChanges(Sections.Sounds);
            if (addedMusic) SoundEditorHelper.SaveCollectionChanges(Sections.Music);

            Debug.Log(addedSfx || addedMusic
                ? $"[{set.SampleName}] 샘플 오디오를 '{AssetDatabase.GetAssetPath(settings)}'에 등록했습니다."
                : $"[{set.SampleName}] 이미 모두 등록되어 있습니다.");
        }

        /// <summary>등록했던 샘플 오디오를 컬렉션에서 제거하고 유사 enum을 다시 생성한다.</summary>
        public static void Uninstall(SoundSampleAudioSet set)
        {
            var settings = SoundServiceAssetLocator.FindSettings();

            if (settings == null)
            {
                Debug.LogWarning($"[{set.SampleName}] SoundServiceSettings 에셋이 없습니다.");
                return;
            }

            bool removedSfx = false;
            bool removedMusic = false;

            foreach (var group in set.Sfx)
            {
                if (!HasSound(settings, group.Tag)) continue;

                settings.SoundDataCollection.RemoveSound(group.Tag);
                removedSfx = true;
            }

            foreach (var group in set.Music)
            {
                if (!HasMusic(settings, group.Tag)) continue;

                settings.MusicDataCollection.RemoveMusicTrack(group.Tag);
                removedMusic = true;
            }

            if (removedSfx) SoundEditorHelper.SaveCollectionChanges(Sections.Sounds);
            if (removedMusic) SoundEditorHelper.SaveCollectionChanges(Sections.Music);

            Debug.Log(removedSfx || removedMusic
                ? $"[{set.SampleName}] 샘플 오디오를 '{AssetDatabase.GetAssetPath(settings)}'에서 제거했습니다."
                : $"[{set.SampleName}] 제거할 항목이 없습니다.");
        }

        /// <summary>
        /// 샘플이 들고 있는 자체 설정 에셋을 이 묶음으로 채운다.
        /// 샘플이 프로젝트 데이터와 무관하게 단독 실행되도록 만드는 용도다.
        /// </summary>
        public static void FillSelfContainedSettings(SoundSampleAudioSet set, SoundServiceSettings settings)
        {
            string audioFolder = set.ResolveAudioFolder();

            if (audioFolder == null) return;

            settings.SoundDataCollection.RemoveAll();
            settings.MusicDataCollection.RemoveAll();

            foreach (var group in set.Sfx) InstallSfx(settings, group, audioFolder);
            foreach (var group in set.Music) InstallMusic(settings, group, audioFolder);

            EditorUtility.SetDirty(settings.SoundDataCollection);
            EditorUtility.SetDirty(settings.MusicDataCollection);

            AssetDatabase.SaveAssets();
        }

        private static bool HasSound(SoundServiceSettings settings, string tag) =>
            settings.SoundDataCollection != null &&
            settings.SoundDataCollection.Sounds.Any(sound => sound.Tag == tag);

        private static bool HasMusic(SoundServiceSettings settings, string tag) =>
            settings.MusicDataCollection != null &&
            settings.MusicDataCollection.MusicTracks.Any(track => track.Tag == tag);

        private static bool InstallSfx(SoundServiceSettings settings, SoundSampleAudioSet.Group group,
            string audioFolder)
        {
            if (HasSound(settings, group.Tag)) return false;

            var clips = LoadClips(group.ClipNames, audioFolder);

            if (clips.Length == 0) return false;

            if (!settings.SoundDataCollection.CreateSound(clips, group.Tag, group.Preset, false, out string message))
            {
                Debug.LogError($"[SoundService] {message}");
                return false;
            }

            SoundEditorHelper.ChangeAudioClipImportSettings(clips, group.Preset, false);

            return true;
        }

        private static bool InstallMusic(SoundServiceSettings settings, SoundSampleAudioSet.Group group,
            string audioFolder)
        {
            if (HasMusic(settings, group.Tag)) return false;

            var clips = LoadClips(group.ClipNames, audioFolder);

            if (clips.Length == 0) return false;

            if (!settings.MusicDataCollection.CreateMusicTrack(clips, group.Tag, group.Preset, false,
                    out string message))
            {
                Debug.LogError($"[SoundService] {message}");
                return false;
            }

            SoundEditorHelper.ChangeAudioClipImportSettings(clips, group.Preset, false);

            return true;
        }

        private static AudioClip[] LoadClips(IEnumerable<string> clipNames, string audioFolder)
        {
            var clips = new List<AudioClip>();

            foreach (var name in clipNames)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(audioFolder + name + ".wav");

                if (clip == null)
                {
                    Debug.LogError($"[SoundService] 오디오를 찾지 못했습니다: {audioFolder}{name}.wav");
                    continue;
                }

                clips.Add(clip);
            }

            return clips.ToArray();
        }
    }
}
