using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>"무엇을 보여주나". 구현체는 보통 MonoBehaviour(TutorialModuleBehaviour)다.</summary>
    public interface ITutorialModule
    {
        Awaitable ShowAsync(TutorialTargetHandle target, CancellationToken token);
        Awaitable HideAsync(CancellationToken token);
    }
}
