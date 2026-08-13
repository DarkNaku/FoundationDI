using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface IHapticProvider
    {
        void Impact(HapticImpact style);
        void Notification(HapticNotification type);
        void Selection();

        // 커브/패턴 재생. 완료(또는 취소) 시 완료되는 Awaitable을 반환한다.
        Awaitable PlayAsync(HapticCurve curve, CancellationToken cancellationToken);
        Awaitable PlayAsync(HapticPattern pattern, CancellationToken cancellationToken);

        void Stop();
        void Prewarm();
    }
}
