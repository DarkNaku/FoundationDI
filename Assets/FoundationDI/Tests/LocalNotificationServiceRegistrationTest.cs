using System;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
using Resolution = Reflex.Enums.Resolution;

public class LocalNotificationServiceRegistrationTest
{
    private class Planner : ILocalNotificationPlanner
    {
        public IEnumerable<LocalNotification> Plan(DateTime now)
        {
            yield return new LocalNotification(now.AddDays(1), "planned", "body");
        }
    }

    private LocalNotificationServiceSettings _settings;

    [SetUp]
    public void SetUp()
    {
        // 플레이 세션에서 어댑터가 등록해 둔 것이 정적 상태로 남아 있을 수 있다.
        LocalNotifierRegistry.Reset();
        _settings = ScriptableObject.CreateInstance<LocalNotificationServiceSettings>();
    }

    [TearDown]
    public void TearDown()
    {
        LocalNotifierRegistry.Reset();
        UnityEngine.Object.DestroyImmediate(_settings);
    }

    [Test]
    public void 등록하면_planner들을_모아_서비스를_만든다()
    {
        var scheduled = new List<LocalNotification>();
        LocalNotifierRegistry.Register(_ => new RecordingNotifier(scheduled));

        var builder = new ContainerBuilder();
        builder.RegisterLocalNotificationService(_settings);
        builder.RegisterType(typeof(Planner), new[] { typeof(ILocalNotificationPlanner) },
                             Lifetime.Singleton, Resolution.Lazy);

        using var container = builder.Build();
        var service = container.Resolve<ILocalNotificationService>();
        service.Reschedule();

        Assert.AreEqual(1, scheduled.Count);
        Assert.AreEqual("planned", scheduled[0].Title);
    }

    [Test]
    public void 모바일_알림_어댑터가_없으면_예약하지_않는_창구로_떨어진다()
    {
        Assert.IsInstanceOf<UnsupportedLocalNotifier>(LocalNotifierRegistry.Create(_settings.ToOptions()));
    }

    private class RecordingNotifier : ILocalNotifier
    {
        private readonly List<LocalNotification> _scheduled;

        public RecordingNotifier(List<LocalNotification> scheduled) => _scheduled = scheduled;

        public bool HasPermission => true;

        public Awaitable<bool> RequestPermissionAsync()
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(true);
            return source.Awaitable;
        }

        public void CancelAll() => _scheduled.Clear();

        public void Schedule(LocalNotification notification) => _scheduled.Add(notification);
    }
}
