using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 타깃 해석 seam. 씬 상주 오브젝트는 직접 참조로, 런타임 생성 UI는 키로 해석한다.
    /// </summary>
    public interface ITutorialTargetRegistry
    {
        void Register(string key, Transform target);
        void Unregister(string key, Transform target);

        bool TryResolve(TutorialTargetRef reference, out Transform target);

        /// <summary>
        /// 타깃이 나타날 때까지 기다린다. timeoutSeconds가 0 이하면 무한 대기.
        /// 타임아웃되면 null을 돌려준다(예외를 던지지 않는다 — 튜토리얼이 게임을 막지 않게).
        /// </summary>
        Awaitable<TutorialTargetHandle> ResolveAsync(TutorialTargetRef reference,
                                                     float timeoutSeconds,
                                                     CancellationToken token);
    }
}
