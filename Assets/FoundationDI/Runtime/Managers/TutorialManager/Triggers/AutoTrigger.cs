using System;

namespace DarkNaku.FoundationDI
{
    /// <summary>Arm 즉시 발동. 지연은 Step의 StartDelay/EndDelay가 담당한다.</summary>
    [Serializable]
    public sealed class AutoTrigger : ITutorialTrigger
    {
        public void Arm(TutorialTriggerContext context, Action onFired) => onFired?.Invoke();

        public void Disarm()
        {
        }
    }
}
