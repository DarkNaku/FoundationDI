using System;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 타깃 버튼 클릭으로 발동한다. Button 직접 참조가 아니라 TutorialTargetRef를 받으므로
    /// UINavigator가 런타임에 만든 팝업의 버튼도 트리거가 된다.
    /// </summary>
    [Serializable]
    public sealed class ButtonClickTrigger : ITutorialTrigger
    {
        [SerializeField] private TutorialTargetRef _target;

        private Button _button;
        private Action _onFired;

        public ButtonClickTrigger()
        {
        }

        public ButtonClickTrigger(TutorialTargetRef target)
        {
            _target = target;
        }

        public void Arm(TutorialTriggerContext context, Action onFired)
        {
            _onFired = onFired;

            if (context.Targets == null) return;
            if (!context.Targets.TryResolve(_target, out var target)) return;
            if (target == null) return;
            if (!target.TryGetComponent(out _button)) return;

            _button.onClick.AddListener(OnClick);
        }

        public void Disarm()
        {
            _onFired = null;

            if (_button != null) _button.onClick.RemoveListener(OnClick);

            _button = null;
        }

        private void OnClick() => _onFired?.Invoke();
    }
}
