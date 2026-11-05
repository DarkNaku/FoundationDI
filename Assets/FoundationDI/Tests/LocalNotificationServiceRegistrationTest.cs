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

    private class DisabledStorage : ILocalNotificationStorage
    {
        public bool IsEnabled { get; set; }
        public bool HasAskedPermission { get; set; }
        public bool TryMarkOpened(string data) => true;
    }

    [Test]
    public void 게임이_저장소를_먼저_등록했으면_그것을_쓴다()
    {
        var scheduled = new List<LocalNotification>();
        LocalNotifierRegistry.Register(_ => new RecordingNotifier(scheduled));

        var builder = new ContainerBuilder();
        builder.RegisterValue(new DisabledStorage(), new[] { typeof(ILocalNotificationStorage) });
        builder.RegisterLocalNotificationService(_settings);
        builder.RegisterType(typeof(Planner), new[] { typeof(ILocalNotificationPlanner) },
                             Lifetime.Singleton, Resolution.Lazy);

        using var container = builder.Build();
        container.Resolve<ILocalNotificationService>().Reschedule();

        Assert.IsInstanceOf<DisabledStorage>(container.Resolve<ILocalNotificationStorage>());
        Assert.IsEmpty(scheduled, "게임 저장소가 알림을 꺼 두었다");
    }

    [Test]
    public void 채널_옵션은_알림_창구를_처음_만들_때_바꾼다()
    {
        LocalNotificationOptions? created = null;
        LocalNotifierRegistry.Register(options =>
        {
            created = options;
            return new RecordingNotifier(new List<LocalNotification>());
        });
        var configured = 0;

        var builder = new ContainerBuilder();
        builder.RegisterLocalNotificationService(_settings, options =>
        {
            configured++;
            return options.WithAndroidChannel("알림", "리마인더");
        });

        using var container = builder.Build();
        var service = container.Resolve<ILocalNotificationService>();

        Assert.AreEqual(0, configured, "게임 문구가 준비된 뒤(처음 쓸 때) 부른다");

        service.Reschedule();

        Assert.AreEqual(1, configured);
        Assert.AreEqual("알림", created.Value.AndroidChannelName);
        Assert.AreEqual("리마인더", created.Value.AndroidChannelDescription);
        Assert.AreEqual(_settings.AndroidChannelId, created.Value.AndroidChannelId, "채널 ID는 바꾸지 않는다");
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

        public Awaitable<string> GetLastOpenedDataAsync()
        {
            var source = new AwaitableCompletionSource<string>();
            source.SetResult(null);
            return source.Awaitable;
        }
    }
}
