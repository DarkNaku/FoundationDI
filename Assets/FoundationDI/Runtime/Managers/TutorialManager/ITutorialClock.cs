using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 진행 엔진이 Unity 시간에 직접 붙지 않게 하는 seam.
    /// EditMode에서는 Awaitable.WaitForSecondsAsync/NextFrameAsync가 완료되지 않으므로
    /// 이 seam이 없으면 지연이 들어간 경로를 테스트할 수 없다.
    /// </summary>
    public interface ITutorialClock
    {
        Awaitable DelayAsync(float seconds, CancellationToken token);
        Awaitable NextFrameAsync(CancellationToken token);
    }
}
