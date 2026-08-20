using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 버튼 클릭 시 지정한 SFX를 재생하는 컴포넌트.
    /// <see cref="Sound"/> 인스턴스를 재사용하므로 연타에도 새 객체를 만들지 않는다.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class SoundButton : InjectableBehaviour
    {
        [Inject] private ISoundService _soundService;

        [SerializeField] private SFX _sfx;
        [SerializeField] private Output _output;
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [SerializeField] private bool _randomPitch;
        [SerializeField] private bool _spatialSound;

        private Sound _sound;

        protected override void Awake()
        {
            base.Awake();

            GetComponent<Button>().onClick.AddListener(Play);
        }

        /// <summary>지정한 SFX를 재생한다.</summary>
        public void Play()
        {
            EnsureInjected();

            if (_soundService == null)
            {
                Debug.LogError("[SoundButton] ISoundService가 주입되지 않았습니다.");
                return;
            }

            if (_sfx.IsNull)
            {
                Debug.LogWarning("[SoundButton] 재생할 SFX가 지정되지 않았습니다.");
                return;
            }

            _sound ??= _soundService.CreateSound(_sfx)
                .SetVolume(_volume)
                .SetSpatialSound(_spatialSound)
                .SetOutput(_output);

            if (_randomPitch)
            {
                _sound.SetRandomPitch();
            }

            _sound.Play();
        }
    }
}
