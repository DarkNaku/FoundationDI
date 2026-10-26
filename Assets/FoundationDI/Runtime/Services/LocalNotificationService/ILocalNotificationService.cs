using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface ILocalNotificationService : IDisposable
    {
        // 게임 설정의 알림 켜기/끄기. 끄면 예약을 지우고, 켜면 다시 예약한다. 영속화된다.
        bool IsEnabled { get; set; }

        bool HasPermission { get; }

        // 지금 한 번 예약하고, 앞으로 앱이 포커스를 잃을 때마다 다시 예약한다.
        void Start();

        // 예약을 전부 지우고 planner들로 다시 넣는다. 권한이 없거나 꺼져 있으면 지우기만 한다.
        // 예약한 알림 수를 돌려준다.
        int Reschedule();

        // 처음 부를 때만 시스템 권한 창을 띄운다(창을 기다리지 않는다). 허용되면 예약한다.
        // 이번에 창을 띄웠으면 true — 같은 때 다른 OS 창(리뷰 등)을 띄우지 않는 데 쓴다.
        bool TryRequestPermissionOnce();

        // 물었는지와 상관없이 권한 창을 요청한다(설정 화면의 "알림 켜기" 등). 허용되면 예약한다.
        Awaitable<bool> RequestPermissionAsync();
    }
}
