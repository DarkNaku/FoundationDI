using System.Collections.Generic;
using System.Linq;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 조건 하나로 발동하는 Step 묶음. 시퀀스끼리는 순서가 아니라 각자의 StartTrigger로 발동한다.
    /// 완료 여부는 Id 단위로 영속화되므로 시퀀스를 중간에 추가·삭제해도 진행도가 어긋나지 않는다.
    /// </summary>
    public sealed class TutorialSequence
    {
        public TutorialSequence(string id,
                                ITutorialTrigger startTrigger,
                                IReadOnlyList<TutorialStep> steps,
                                int order = 0,
                                ResumeMode resumeMode = ResumeMode.RestartSequence,
                                float targetTimeout = 0f)
        {
            Id = id;
            StartTrigger = startTrigger ?? new AutoTrigger();
            Steps = steps == null
                ? new List<TutorialStep>()
                : steps.Where(s => s != null).ToList();
            Order = order;
            ResumeMode = resumeMode;
            TargetTimeout = targetTimeout < 0f ? 0f : targetTimeout;
        }

        public string Id { get; }

        public ITutorialTrigger StartTrigger { get; }

        public IReadOnlyList<TutorialStep> Steps { get; }

        /// <summary>동시 발동 시 낮은 쪽이 먼저 실행된다.</summary>
        public int Order { get; }

        public ResumeMode ResumeMode { get; }

        /// <summary>타깃을 기다리는 최대 시간. 0이면 무한.</summary>
        public float TargetTimeout { get; }
    }
}
