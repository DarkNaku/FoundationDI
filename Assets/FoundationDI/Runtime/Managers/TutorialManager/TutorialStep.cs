using System.Collections.Generic;
using System.Linq;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 튜토리얼의 한 단계. StartTrigger가 발동하면 모듈을 보여주고,
    /// EndTrigger가 발동하면 숨기고 다음 Step으로 넘어간다.
    /// </summary>
    public sealed class TutorialStep
    {
        public TutorialStep(string id,
                            ITutorialTrigger startTrigger,
                            ITutorialTrigger endTrigger,
                            IReadOnlyList<ITutorialModule> modules,
                            TutorialTargetRef target,
                            float startDelay,
                            float endDelay)
        {
            Id = id;
            StartTrigger = startTrigger ?? new AutoTrigger();
            EndTrigger = endTrigger ?? new AutoTrigger();
            Modules = modules == null
                ? new List<ITutorialModule>()
                : modules.Where(m => m != null).ToList();
            Target = target;
            StartDelay = startDelay < 0f ? 0f : startDelay;
            EndDelay = endDelay < 0f ? 0f : endDelay;
        }

        public string Id { get; }

        public ITutorialTrigger StartTrigger { get; }

        public ITutorialTrigger EndTrigger { get; }

        public IReadOnlyList<ITutorialModule> Modules { get; }

        /// <summary>모듈이 가리킬 대상. 트리거가 가리키는 대상과는 별개다.</summary>
        public TutorialTargetRef Target { get; }

        public float StartDelay { get; }

        public float EndDelay { get; }
    }
}
