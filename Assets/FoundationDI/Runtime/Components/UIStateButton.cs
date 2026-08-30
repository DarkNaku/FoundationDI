using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 상태별로 여러 Image/Text를 스왑하는 버튼.
    /// uGUI 내장 Transition은 targetGraphic 하나에만 걸리지만, 이 버튼은 세트마다
    /// 다른 타깃을 몰 수 있다.
    /// </summary>
    [AddComponentMenu("FoundationDI/UI State Button")]
    public class UIStateButton : UIButton
    {
        [Header("State Swap")]
        [SerializeField] private List<UIImageStateSet> _imageSets = new List<UIImageStateSet>();
        [SerializeField] private List<UITextStateSet> _textSets = new List<UITextStateSet>();

        [Tooltip("클릭 직후 선택을 해제한다. PC에서 클릭 후에도 호버 하이라이트를 유지하려면 켠다. " +
                 "켜면 키보드/게임패드 내비게이션과 Selected 상태 표시가 동작하지 않는다.")]
        [SerializeField] private bool _deselectOnClick;

        protected override void Awake()
        {
            base.Awake();

            if (_deselectOnClick) onClick.AddListener(Deselect);
        }

        private void Deselect()
        {
            if (EventSystem.current == null) return;
            if (EventSystem.current.currentSelectedGameObject != gameObject) return;

            EventSystem.current.SetSelectedGameObject(null);
        }

        // base를 부르는 이유: 내장 Transition(특히 Animation)을 병행하는 팀이 살아야 한다.
        // 기본값 ColorTint가 우리 Color 스왑과 곱해지는 사고는 Reset()과 인스펙터 경고로 막는다.
        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);

            ApplyState(Map(state));
        }

        // 값 순서가 같아도 캐스팅하지 않는다 — 유니티가 순서를 바꾸면 조용히 틀린 상태를 그린다.
        private static UIButtonState Map(SelectionState state)
        {
            switch (state)
            {
                case SelectionState.Highlighted: return UIButtonState.Highlighted;
                case SelectionState.Pressed: return UIButtonState.Pressed;
                case SelectionState.Selected: return UIButtonState.Selected;
                case SelectionState.Disabled: return UIButtonState.Disabled;
                default: return UIButtonState.Normal;
            }
        }

        /// <summary>
        /// 모든 세트에 상태를 적용한다.
        /// internal인 이유는 EventSystem 없이 테스트가 5상태를 직접 검증하기 위해서다.
        /// 게임 코드에 공개하면 실제 선택 상태와 어긋난 시각 상태를 만들 수 있다.
        /// </summary>
        internal void ApplyState(UIButtonState state)
        {
            for (int i = 0; i < _imageSets.Count; i++) _imageSets[i]?.Apply(state);
            for (int i = 0; i < _textSets.Count; i++) _textSets[i]?.Apply(state);
        }

        internal void SetSetsForTest(List<UIImageStateSet> imageSets, List<UITextStateSet> textSets)
        {
            _imageSets = imageSets ?? new List<UIImageStateSet>();
            _textSets = textSets ?? new List<UITextStateSet>();
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();

            // 내장 ColorTint가 targetGraphic에 자동 할당되면 우리 Color 스왑과 곱해진다.
            transition = Transition.None;
        }
#endif
    }
}
