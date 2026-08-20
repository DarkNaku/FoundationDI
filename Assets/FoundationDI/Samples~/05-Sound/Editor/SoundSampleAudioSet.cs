using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DarkNaku.FoundationDI;

namespace DarkNaku.FoundationDI.SamplesEditor
{
    /// <summary>
    /// 한 샘플이 들고 오는 오디오 묶음. 샘플마다 하나씩 정의하고
    /// <see cref="SoundSampleDataInstaller"/>로 개별 설치/제거한다.
    /// </summary>
    public sealed class SoundSampleAudioSet
    {
        /// <summary>태그 하나에 묶이는 클립 묶음.</summary>
        public sealed class Group
        {
            public string Tag;
            public CompressionPreset Preset;
            public string[] ClipNames;

            public Group(string tag, CompressionPreset preset, params string[] clipNames)
            {
                Tag = tag;
                Preset = preset;
                ClipNames = clipNames;
            }
        }

        /// <summary>메뉴와 로그에 쓰는 이름.</summary>
        public string SampleName;

        /// <summary>클립이 놓인 폴더. 샘플을 다른 위치로 import해도 찾을 수 있게 폴더명으로 재탐색한다.</summary>
        public string AudioFolderName;

        public Group[] Sfx = System.Array.Empty<Group>();
        public Group[] Music = System.Array.Empty<Group>();

        /// <summary>이 묶음이 쓰는 모든 태그.</summary>
        public IEnumerable<string> AllTags
        {
            get
            {
                foreach (var group in Sfx) yield return group.Tag;
                foreach (var group in Music) yield return group.Tag;
            }
        }

        /// <summary>
        /// 클립 폴더의 실제 경로를 찾는다.
        /// Package Manager로 샘플을 import하면 경로가 <c>Assets/Samples/&lt;패키지&gt;/&lt;버전&gt;/...</c>로 바뀌므로
        /// 고정 경로 대신 폴더명으로 검색한다.
        /// </summary>
        public string ResolveAudioFolder()
        {
            var guids = AssetDatabase.FindAssets($"{AudioFolderName} t:Folder");

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (path.EndsWith("/" + AudioFolderName)) return path + "/";
            }

            Debug.LogError($"[{SampleName}] '{AudioFolderName}' 폴더를 찾지 못했습니다.");

            return null;
        }
    }
}
