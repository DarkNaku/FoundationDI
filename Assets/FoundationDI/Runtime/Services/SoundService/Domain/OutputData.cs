using System;
using UnityEngine;
using UnityEngine.Audio;

namespace DarkNaku.FoundationDI
{
    /// <summary>AudioMixerGroup과 노출된 볼륨 파라미터 이름의 쌍.</summary>
    [Serializable]
    public class OutputData
    {
        [SerializeField] private string _name;
        [SerializeField] private AudioMixerGroup _output;

        public string Name { get => _name; set => _name = value; }
        public AudioMixerGroup Output { get => _output; set => _output = value; }

        public OutputData(string name, AudioMixerGroup output)
        {
            _name = name;
            _output = output;
        }
    }
}
