using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 백분율 라벨이 붙은 범용 슬라이더. 값이 바뀌면 <see cref="UnityEvent{T}"/>로 알린다.
    /// 다이내믹 뮤직 레이어 볼륨 같은 임의의 값을 인스펙터에서 연결해 쓸 때 사용한다.
    /// </summary>
    [RequireComponent(typeof(Slider))]
    public class VolumeSlider : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TextMeshProUGUI _percentageLabel;

        [Header("Settings")]
        [SerializeField, Range(0f, 1f)] private float _initialValue = 0.5f;

        [Header("Events")]
        [SerializeField] private UnityEvent<float> _onValueChange;

        private Slider _slider;

        private void Awake()
        {
            _slider = GetComponent<Slider>();
            _slider.onValueChanged.AddListener(ChangeValue);
        }

        private void Start()
        {
            _slider.SetValueWithoutNotify(_initialValue);

            ChangeValue(_initialValue);
        }

        private void ChangeValue(float value)
        {
            _onValueChange?.Invoke(value);

            if (_percentageLabel == null) return;

            _percentageLabel.text = $"{value * 100:F0}%";
        }
    }
}
