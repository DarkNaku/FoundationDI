using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace DarkNaku.FoundationDI
{
    /// <summary>AudioMixer의 그룹 목록 스냅샷. 에디터의 Output Manager가 믹서에서 다시 읽어 채운다.</summary>
    [CreateAssetMenu(fileName = "OutputCollection", menuName = "DarkNaku/FoundationDI/Output Collection")]
    public class OutputDataCollection : ScriptableObject
    {
        [SerializeField] private OutputData[] _outputs = Array.Empty<OutputData>();

        private Dictionary<string, OutputData> _outputsDictionary = new();

        public OutputData[] Outputs => _outputs;

        private void OnEnable()
        {
            Init();
        }

        /// <summary>믹서의 모든 그룹을 읽어 목록을 다시 만든다(에디터 전용 흐름).</summary>
        public void LoadOutputs(AudioMixer mixer)
        {
            if (mixer == null)
            {
                Debug.LogError("[SoundService] Master AudioMixer가 설정되지 않아 Output을 읽을 수 없습니다.");
                return;
            }

            var mixerGroups = mixer.FindMatchingGroups(null);
            var loadedOutputs = new OutputData[mixerGroups.Length];

            for (int i = 0; i < loadedOutputs.Length; i++)
            {
                loadedOutputs[i] = new OutputData(mixerGroups[i].name.Replace(" ", ""), mixerGroups[i]);
            }

            _outputs = loadedOutputs;

            Init();
        }

        /// <summary>이름으로 AudioMixerGroup을 찾는다. 없으면 경고 후 null.</summary>
        public AudioMixerGroup GetOutput(string name)
        {
            if (_outputsDictionary == null || _outputsDictionary.Count != (_outputs?.Length ?? 0))
            {
                Init();
            }

            if (_outputsDictionary.TryGetValue(name.Replace(" ", ""), out var outputData)) return outputData.Output;

            Debug.LogWarning($"[SoundService] '{name}' Output이 존재하지 않습니다.");
            return null;
        }

        private void Init()
        {
            _outputsDictionary ??= new Dictionary<string, OutputData>();
            _outputsDictionary.Clear();

            if (_outputs == null) return;

            foreach (var outputData in _outputs)
            {
                if (outputData == null || string.IsNullOrEmpty(outputData.Name)) continue;

                _outputsDictionary[outputData.Name] = outputData;
            }
        }
    }
}
