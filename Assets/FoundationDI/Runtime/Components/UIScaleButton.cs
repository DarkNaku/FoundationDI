using UnityEngine;
using UnityEngine.EventSystems;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 호버하면 커지고 누르면 작아지는 버튼.
    /// 스케일은 자식 <see cref="_scaleTarget"/>에만 걸려 버튼 자신의 히트 영역은 변하지 않는다.
    /// </summary>
    [AddComponentMenu("FoundationDI/UI Scale Button")]
    public class UIScaleButton : UIButton
    {
        [Header("Scale")]
        [Tooltip("스케일이 걸릴 자식. 버튼 자신을 지정하면 히트 영역이 함께 변한다.")]
        [SerializeField] private RectTransform _scaleTarget;
        [SerializeField] private float _highlightedScale = 1.1f;
        [SerializeField] private float _pressedScale = 0.95f;

        [Tooltip("켜면 비활성일 때 아래 배율을 쓴다. 끄면 본래 크기(1)로 돌아간다.")]
        [SerializeField] private bool _overrideDisabledScale;
        [SerializeField] private float _disabledScale = 1f;

        [Header("Transition")]
        [SerializeField] private float _duration = 0.1f;
        [SerializeField] private AnimationCurve _curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Time.timeScale을 무시한다. 일시정지 메뉴에서도 버튼이 반응하려면 켜 둔다.")]
        [SerializeField] private bool _unscaledTime = true;

        private bool _pointerInside;
        private bool _pointerDown;
        private float _targetScale = 1f;
        private float _currentScale = 1f;
        private float _fromScale = 1f;
        private float _elapsed;

        // 프리팹 값이 아니라 첫 적용 시점의 자식 스케일을 기준으로 삼는다.
        // [NonSerialized]라 프리팹 인스턴스마다 새로 잡힌다.
        [System.NonSerialized] private Vector3 _baseScale = Vector3.one;
        [System.NonSerialized] private bool _baseCaptured;

        /// <summary>지금 향하고 있는 배율. 1이 본래 크기다.</summary>
        internal float TargetScale => _targetScale;

        /// <summary>보간 중인 현재 배율.</summary>
        internal float CurrentScale => _currentScale;

        protected override void OnEnable()
        {
            base.OnEnable();

            // 활성화 시점의 포인터 위치는 알 수 없으므로 밖에 있다고 본다.
            ResetToTarget();
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            // 풀링된 View가 확대된 채로 다음 표시에 재사용되지 않도록 즉시 되돌린다.
            ResetToTarget();
        }

        private void Update()
        {
            // Selectable이 [ExecuteAlways]라 에디터에서도 돈다.
            if (!Application.isPlaying) return;

            Tick(_unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
        }

        // interactable 변경을 잡는 유일한 지점이다.
        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);

            // localScale은 직렬화 프로퍼티라, 막지 않으면 인스펙터에서 interactable을 껐다 켜는
            // 것만으로 확대된 스케일이 프리팹에 구워진다.
            if (!Application.isPlaying) return;

            RefreshTarget();

            if (instant) SnapToTarget();
        }

        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);

            _pointerInside = true;
            RefreshTarget();
        }

        public override void OnPointerExit(PointerEventData eventData)
        {
            base.OnPointerExit(eventData);

            _pointerInside = false;
            RefreshTarget();
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);

            _pointerDown = true;
            RefreshTarget();
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);

            _pointerDown = false;
            RefreshTarget();
        }

        /// <summary>
        /// 현재 포인터·활성 상태로 목표 배율을 다시 계산한다.
        /// SelectionState를 쓰지 않는 이유: uGUI의 우선순위가 Pressed > Selected > Highlighted라
        /// PC에서 클릭 후 떼면 Selected로 남아 Highlighted로 돌아오지 않는다.
        /// internal인 이유는 DoStateTransition이 에디터에서 값을 굽지 않도록 막혀 있어
        /// EditMode 테스트가 활성 상태 변화를 직접 반영시켜야 하기 때문이다.
        /// </summary>
        internal void RefreshTarget()
        {
            var previous = _targetScale;

            if (!IsInteractable())
            {
                // 폴백 대상은 UIStateButton과 같이 언제나 Normal이다.
                _targetScale = _overrideDisabledScale ? _disabledScale : 1f;
            }
            else if (_pointerInside)
            {
                _targetScale = _pointerDown ? _pressedScale : _highlightedScale;
            }
            else
            {
                _targetScale = 1f;
            }

            if (Mathf.Approximately(previous, _targetScale)) return;

            // 목표가 바뀌면 진행 중이던 값에서 다시 출발한다 — 연타해도 끊기지 않는다.
            _fromScale = _currentScale;
            _elapsed = 0f;

            if (_duration > 0f) return;

            _currentScale = _targetScale;
            Apply();
        }

        /// <summary>
        /// 보간을 한 프레임 진행한다.
        /// Awaitable이 아니라 이 형태인 이유는 EditMode에서 프레임 펌프 없이 보간 전체를
        /// 검증하기 위해서다(EditMode에서는 Awaitable의 프레임 대기가 완료되지 않는다).
        /// </summary>
        internal void Tick(float deltaTime)
        {
            if (Mathf.Approximately(_currentScale, _targetScale)) return;

            if (_duration <= 0f)
            {
                SnapToTarget();
                return;
            }

            _elapsed += deltaTime;

            var t = Mathf.Clamp01(_elapsed / _duration);

            if (t >= 1f)
            {
                SnapToTarget();
                return;
            }

            // LerpUnclamped인 이유: 되튀는 커브(Back/Overshoot)를 그대로 살린다.
            var k = _curve == null ? t : _curve.Evaluate(t);

            _currentScale = Mathf.LerpUnclamped(_fromScale, _targetScale, k);

            Apply();
        }

        private void ResetToTarget()
        {
            if (!Application.isPlaying) return;

            _pointerInside = false;
            _pointerDown = false;

            RefreshTarget();
            SnapToTarget();
        }

        private void SnapToTarget()
        {
            _currentScale = _targetScale;
            _fromScale = _targetScale;
            _elapsed = 0f;

            Apply();
        }

        // 스케일이 걸리는 곳은 자식뿐이다. 버튼 자신을 건드리면 레이캐스트 영역이 함께 변해
        // 축소 시 경계에서 exit→확대→enter→축소 진동이 난다.
        private void Apply()
        {
            if (_scaleTarget == null) return;

            if (!_baseCaptured)
            {
                _baseCaptured = true;
                _baseScale = _scaleTarget.localScale;
            }

            _scaleTarget.localScale = _baseScale * _currentScale;
        }

        internal void ConfigureScaleForTest(RectTransform scaleTarget, float highlighted, float pressed, float duration)
        {
            _scaleTarget = scaleTarget;
            _highlightedScale = highlighted;
            _pressedScale = pressed;
            _duration = duration;
        }

        internal void ConfigureCurveForTest(AnimationCurve curve)
        {
            _curve = curve;
        }

        internal void ConfigureDisabledScaleForTest(bool overrideDisabled, float disabled)
        {
            _overrideDisabledScale = overrideDisabled;
            _disabledScale = disabled;
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();

            // 자식이 하나면 그게 콘텐츠 루트일 확률이 높다. 아니면 인스펙터에서 직접 지정한다.
            if (_scaleTarget == null && transform.childCount == 1)
            {
                _scaleTarget = transform.GetChild(0) as RectTransform;
            }
        }
#endif
    }
}
