using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// AudioMixer Output 볼륨을 조절하는 UI 슬라이더. 마지막으로 저장된 볼륨으로 자동 복원한다.
    /// </summary>
    [RequireComponent(typeof(Slider))]
    public class OutputVolumeSlider : InjectableBehaviour
    {
        [Inject] private ISoundService _soundService;

        [Header("Settings")]
        [SerializeField] private Output _targetOutput;

        [Header("References")]
        [SerializeField] private TextMeshProUGUI _outputTitleLabel;
        [SerializeField] private TextMeshProUGUI _percentageLabel;

        private Slider _volumeSlider;

        protected override void Awake()
        {
            base.Awake();

            _volumeSlider = GetComponent<Slider>();
            _volumeSlider.onValueChanged.AddListener(ChangeVolume);
        }

        private void Start()
        {
            EnsureInjected();

            if (_soundService == null)
            {
                Debug.LogError("[OutputVolumeSlider] ISoundService가 주입되지 않았습니다.");
                return;
            }

            if (_outputTitleLabel != null)
            {
                _outputTitleLabel.text = Regex.Replace(_targetOutput.ToString(), @"(\p{Ll})(\p{Lu})", "$1 $2");
            }

            SetLastSavedVolume();
        }

        /// <summary>Output 볼륨을 바꾸고 라벨을 갱신한다.</summary>
        public void ChangeVolume(float volume)
        {
            EnsureInjected();

            if (_soundService == null) return;

            _soundService.ChangeOutputVolume(_targetOutput, volume);

            RefreshPercentage(volume);
        }

        private void SetLastSavedVolume()
        {
            float volume = _soundService.GetSavedOutputVolume(_targetOutput.ToString());

            ChangeVolume(volume);

            _volumeSlider.SetValueWithoutNotify(volume);

            RefreshPercentage(volume);
        }

        private void RefreshPercentage(float volume)
        {
            if (_percentageLabel == null) return;

            _percentageLabel.text = $"{volume * 100:F0}%";
        }
    }
}
