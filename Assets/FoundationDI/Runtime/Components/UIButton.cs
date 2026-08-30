using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 클릭 시 사운드와 햅틱을 내는 uGUI 버튼.
    /// 서비스는 선택적으로 주입된다 — 등록되지 않은 서비스는 그 기능만 조용히 꺼진다.
    /// </summary>
    [AddComponentMenu("FoundationDI/UI Button")]
    public class UIButton : Button
    {
        [Header("Sound")]
        [SerializeField] private SFX _sfx;
        [SerializeField] private Output _output;
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [SerializeField] private bool _randomPitch;

        [Header("Haptic")]
        [SerializeField] private bool _useHaptic = true;
        [SerializeField] private HapticImpact _hapticImpact = HapticImpact.Light;

        private ISoundService _soundService;
        private IHapticService _hapticService;

        private Sound _sound;
        private bool _requested;
        private bool _warnedSound;
        private bool _warnedHaptic;

        /// <summary>
        /// 개별 서비스가 아니라 리졸버를 받는다. [Inject] 필드로 서비스를 직접 받으면
        /// 미등록 시 VContainer가 예외를 던지는데, 이 프로젝트에는 그 예외를 흡수할 곳이
        /// 거의 없다(PoolManager.cs:154 / InjectorService.Start 둘 다 try/catch가 없다).
        /// IObjectResolver는 컨테이너가 항상 스스로 등록한다(ContainerBuilder.cs:161).
        /// </summary>
        [Inject]
        public void Construct(IObjectResolver resolver)
        {
            if (resolver == null) return;

            resolver.TryResolve(out _soundService);
            resolver.TryResolve(out _hapticService);
        }

        protected override void Awake()
        {
            base.Awake();

            EnsureInjected();

            // Button.Press()가 OnPointerClick/OnSubmit 양쪽에서 호출되므로
            // 리스너 하나로 마우스·터치·게임패드 Submit이 전부 커버된다.
            onClick.AddListener(PlayFeedback);
        }

        private void EnsureInjected()
        {
            if (_requested) return;
            _requested = true;
            InjectorService.Request(this);
        }

        /// <summary>
        /// 클릭 피드백을 재생한다.
        /// <c>onClick.RemoveAllListeners()</c>를 부른 뒤 다시 배선할 수 있도록 공개한다.
        /// </summary>
        public void PlayFeedback()
        {
            PlaySound();
            PlayHaptic();
        }

        private void PlaySound()
        {
            if (_sfx.IsNull) return;

            if (_soundService == null)
            {
                if (!_warnedSound)
                {
                    _warnedSound = true;
                    Debug.LogWarning($"[UIButton] SFX '{_sfx}'가 지정됐지만 ISoundService가 등록되지 않았습니다.", this);
                }
                return;
            }

            if (_sound == null)
            {
                var created = _soundService.CreateSound(_sfx);
                if (created == null) return; // 서비스가 null을 돌려줄 수 있다(대체품/테스트) — 빌더 체인 NRE 방지

                // UI 버튼은 스크린 공간에 있어 3D 감쇠가 의미 없다. 켜면 리스너 위치에 따라
                // 클릭음 볼륨이 달라지는 버그로만 나타나므로 항상 끈다.
                _sound = created
                    .SetVolume(_volume)
                    .SetSpatialSound(false)
                    .SetOutput(_output);
            }

            if (_randomPitch) _sound.SetRandomPitch();

            _sound.Play();
        }

        private void PlayHaptic()
        {
            if (!_useHaptic) return;

            if (_hapticService == null)
            {
                if (!_warnedHaptic)
                {
                    _warnedHaptic = true;
                    Debug.LogWarning("[UIButton] 햅틱이 켜져 있지만 IHapticService가 등록되지 않았습니다.", this);
                }
                return;
            }

            // IHapticService.Enabled(유저 설정)는 서비스가 이미 전역 게이트한다.
            // 여기서 이중으로 막지 않는다.
            _hapticService.Impact(_hapticImpact);
        }

        internal void ConfigureForTest(SFX sfx, Output output, bool useHaptic, HapticImpact impact)
        {
            _sfx = sfx;
            _output = output;
            _useHaptic = useHaptic;
            _hapticImpact = impact;
        }

        internal void SetServicesForTest(ISoundService sound, IHapticService haptic)
        {
            _soundService = sound;
            _hapticService = haptic;
        }
    }
}
