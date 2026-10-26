using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 로컬 알림 예약 엔진. 일정은 ILocalNotificationPlanner들이 정하고, 이 서비스는 언제 다시 예약할지와
    // 권한·켜기/끄기만 맡는다(BlockAway·MahjongTile의 LocalNotifications에서 게임 규칙을 걷어냈다).
    //
    // 예약은 늘 "전부 지우고 다시 넣는다". 부분 갱신은 OS별 ID 관리가 필요하고, 다시 넣는 비용은
    // 무시할 만하다. 다시 넣는 시점은 Start와 앱이 포커스를 잃을 때다 — 앱을 내리기 직전의 상태가
    // 다음 알림에 반영된다.
    //
    // 알림 때문에 게임이 멈추면 안 되므로 예외는 삼키고 경고만 남긴다.
    public sealed class LocalNotificationService : ILocalNotificationService
    {
        private readonly Func<ILocalNotifier> _notifierFactory;
        private readonly IReadOnlyList<ILocalNotificationPlanner> _planners;
        private readonly ILocalNotificationStorage _storage;
        private readonly IApplicationFocusSource _focus;
        private readonly Func<DateTime> _now;

        private ILocalNotifier _notifier;
        private bool _isStarted;
        private bool _isDisposed;

        // notifierFactory는 처음 쓸 때 한 번 부른다. 모바일 알림 센터 초기화는 저사양 기기에서
        // 0.5~1초 걸려 앱 시작 경로(DI 해석)에 두지 않는다.
        public LocalNotificationService(Func<ILocalNotifier> notifierFactory,
                                        IEnumerable<ILocalNotificationPlanner> planners,
                                        ILocalNotificationStorage storage,
                                        IApplicationFocusSource focus,
                                        Func<DateTime> now = null)
        {
            _notifierFactory = notifierFactory;
            _planners = new List<ILocalNotificationPlanner>(planners ?? Array.Empty<ILocalNotificationPlanner>());
            _storage = storage;
            _focus = focus;
            _now = now ?? (() => DateTime.Now);
        }

        private ILocalNotifier Notifier => _notifier ??= _notifierFactory() ?? new UnsupportedLocalNotifier();

        public bool IsEnabled
        {
            get => _storage.IsEnabled;
            set
            {
                if (_storage.IsEnabled == value) return;

                _storage.IsEnabled = value;
                Reschedule();
            }
        }

        public bool HasPermission
        {
            get
            {
                try
                {
                    return Notifier.HasPermission;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LocalNotificationService] 알림 권한을 확인하지 못했다: {e.Message}");
                    return false;
                }
            }
        }

        public void Start()
        {
            if (_isDisposed) return;

            if (!_isStarted)
            {
                _isStarted = true;
                _focus.FocusChanged += OnFocusChanged;
            }

            Reschedule();
        }

        public int Reschedule()
        {
            if (_isDisposed) return 0;

            try
            {
                Notifier.CancelAll();

                if (!_storage.IsEnabled || !Notifier.HasPermission) return 0;

                var now = _now();
                var count = 0;

                foreach (var notification in CollectPlans(now))
                {
                    // 지난 시각을 넘기면 OS마다 다르게 군다(즉시 울리거나 버린다). 예약 시점에 거른다.
                    if (notification.FireTime <= now) continue;

                    Notifier.Schedule(notification);
                    count++;
                }

                Debug.Log($"[LocalNotificationService] 알림 {count}개를 예약했다");
                return count;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalNotificationService] 알림을 예약하지 못했다: {e.Message}");
                return 0;
            }
        }

        public bool TryRequestPermissionOnce()
        {
            if (_isDisposed) return false;

            try
            {
                if (_storage.HasAskedPermission) return false;

                // 창이 떠 있는 동안 앱이 꺼져도 다시 묻지 않도록 먼저 적는다.
                _storage.HasAskedPermission = true;

                if (Notifier.HasPermission)
                {
                    Reschedule();
                    return false;
                }

                _ = RequestPermissionAsync();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalNotificationService] 알림 권한을 요청하지 못했다: {e.Message}");
                return false;
            }
        }

        public async Awaitable<bool> RequestPermissionAsync()
        {
            try
            {
                _storage.HasAskedPermission = true;

                if (!await Notifier.RequestPermissionAsync())
                {
                    Debug.Log("[LocalNotificationService] 알림 권한이 거절되었다");
                    return false;
                }

                Reschedule();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalNotificationService] 알림 권한을 요청하지 못했다: {e.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_isStarted)
            {
                _focus.FocusChanged -= OnFocusChanged;
                _isStarted = false;
            }
        }

        // planner 하나가 터져도 나머지 일정은 나가야 한다.
        private List<LocalNotification> CollectPlans(DateTime now)
        {
            var result = new List<LocalNotification>();

            foreach (var planner in _planners)
            {
                try
                {
                    var planned = planner.Plan(now);
                    if (planned != null) result.AddRange(planned);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LocalNotificationService] {planner.GetType().Name}가 일정을 만들지 못했다: {e.Message}");
                }
            }

            return result;
        }

        private void OnFocusChanged(bool isFocused)
        {
            if (!isFocused) Reschedule();
        }
    }
}
