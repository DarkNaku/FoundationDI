using System;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// MessageService의 메시지로 발동한다. 게임 코드는 원래 발행하던 메시지를 그대로 발행하고
    /// 튜토리얼의 존재를 모른다.
    ///
    /// 인스펙터에서 고를 수 있으려면 구체 서브클래스를 한 줄 만든다:
    /// <code>
    /// [Serializable]
    /// public sealed class Level3Trigger : MessageTrigger&lt;LevelStartedMessage&gt;
    /// {
    ///     [SerializeField] private int _level = 3;
    ///     protected override bool Match(LevelStartedMessage m) => m.Level == _level;
    /// }
    /// </code>
    /// </summary>
    [Serializable]
    public abstract class MessageTrigger<T> : ITutorialTrigger
    {
        private IDisposable _subscription;
        private Action _onFired;
        private bool _fired;

        public void Arm(TutorialTriggerContext context, Action onFired)
        {
            _onFired = onFired;
            _fired = false;

            if (context.Message == null) return;

            _subscription = context.Message.Subscribe<T>(OnMessage);
        }

        public void Disarm()
        {
            _onFired = null;
            _subscription?.Dispose();
            _subscription = null;
        }

        /// <summary>오버라이드하지 않으면 해당 타입의 모든 메시지에 발동한다.</summary>
        protected virtual bool Match(T message) => true;

        private void OnMessage(T message)
        {
            if (_fired) return;
            if (!Match(message)) return;

            _fired = true;
            _onFired?.Invoke();
        }
    }
}
