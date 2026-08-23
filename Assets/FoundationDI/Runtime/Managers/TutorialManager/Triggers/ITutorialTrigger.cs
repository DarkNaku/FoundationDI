using System;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 트리거가 받는 의존. 트리거는 [SerializeReference]로 직렬화되는 객체라
    /// 생성자 주입이 불가능하므로 Arm 시점에 컨텍스트로 받는다.
    /// </summary>
    public readonly struct TutorialTriggerContext
    {
        public IMessageService Message { get; }
        public ITutorialTargetRegistry Targets { get; }

        public TutorialTriggerContext(IMessageService message, ITutorialTargetRegistry targets)
        {
            Message = message;
            Targets = targets;
        }
    }

    /// <summary>"언제 넘어가나". Arm/Disarm은 반드시 짝을 맞춘다.</summary>
    public interface ITutorialTrigger
    {
        void Arm(TutorialTriggerContext context, Action onFired);
        void Disarm();
    }
}
