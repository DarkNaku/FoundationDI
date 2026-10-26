using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 예약할 알림 하나. 시각은 기기 현지 시각이다(OS 예약 API가 현지 시각을 받는다).
    public readonly struct LocalNotification
    {
        public DateTime FireTime { get; }
        public string Title { get; }
        public string Body { get; }

        public LocalNotification(DateTime fireTime, string title, string body)
        {
            FireTime = fireTime;
            Title = title;
            Body = body;
        }
    }

    // 무엇을 언제 보낼지는 게임이 정한다. 서비스는 다시 예약할 때마다 등록된 planner를 전부 불러
    // 결과를 합친다 — 리마인더·데일리 선물·복귀 알림처럼 성격이 다른 일정을 각자 따로 둘 수 있다.
    //
    // 다시 예약하는 순간은 늘 앱이 켜져 있는 때다. 그 사이 상태가 바뀌었으면(오늘 선물을 받음 등)
    // 다음 예약에서 빠지도록 매번 현재 상태로 새로 계산한다.
    public interface ILocalNotificationPlanner
    {
        // now는 기기 현지 시각이다. now 이전의 알림은 서비스가 걸러낸다.
        IEnumerable<LocalNotification> Plan(DateTime now);
    }

    // OS 알림 창구. 모바일은 FoundationDI.MobileNotifications 어댑터(com.unity.mobile.notifications),
    // 에디터·미지원 플랫폼은 UnsupportedLocalNotifier다.
    public interface ILocalNotifier
    {
        bool HasPermission { get; }

        // 시스템 권한 창을 띄운다 — 이미 정해졌으면 창 없이 바로 끝난다. 허용했으면 true.
        Awaitable<bool> RequestPermissionAsync();

        void CancelAll();

        void Schedule(LocalNotification notification);
    }

    // 알림 켜기/끄기(게임 설정)와 권한을 물었는지를 영속화한다.
    public interface ILocalNotificationStorage
    {
        bool IsEnabled { get; set; }
        bool HasAskedPermission { get; set; }
    }

    // 다시 예약하는 시점(앱이 포커스를 잃을 때)의 seam. Application.focusChanged는 테스트가 발화할 수 없다.
    public interface IApplicationFocusSource
    {
        event Action<bool> FocusChanged;
    }

    // 알림 창구를 만들 때 넘기는 값. SO(LocalNotificationServiceSettings)를 어댑터가 직접 알지 않게 한다.
    public readonly struct LocalNotificationOptions
    {
        public string AndroidChannelId { get; }
        public string AndroidChannelName { get; }
        public string AndroidChannelDescription { get; }
        public string AndroidSmallIcon { get; }

        public LocalNotificationOptions(string androidChannelId, string androidChannelName,
                                        string androidChannelDescription, string androidSmallIcon)
        {
            AndroidChannelId = androidChannelId;
            AndroidChannelName = androidChannelName;
            AndroidChannelDescription = androidChannelDescription;
            AndroidSmallIcon = androidSmallIcon;
        }
    }
}
