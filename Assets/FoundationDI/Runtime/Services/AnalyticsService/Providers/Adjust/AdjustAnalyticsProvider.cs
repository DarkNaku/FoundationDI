using AdjustSdk;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // Adjust(MMP) 어댑터. 팬아웃·버퍼·예외 격리·수집 게이트는 AnalyticsService가 이미
    // 처리하므로 여기서는 번역만 한다.
    //
    // Firebase 어댑터와 다른 점이 셋 있다.
    //
    // 1. 이벤트를 "이름"이 아니라 대시보드가 발급한 토큰으로 보낸다. 그래서 이 어댑터만
    //    자기 설정(AdjustAnalyticsSettings)에 이름→토큰 매핑표를 든다.
    // 2. 런타임 SetUserId가 없다. AdjustConfig.ExternalDeviceId는 초기화 시점 전용인데
    //    AnalyticsService는 유저 상태를 초기화 이후에 flush하므로 늘 늦는다. 대신 전역
    //    콜백 파라미터로 모든 이벤트에 실어 보낸다.
    // 3. 수집 게이트가 파라미터가 아니라 Enable()/Disable() 두 메서드다.
    //
    // 그리고 이 어댑터만 IAnalyticsFlushHook을 구현한다. Adjust는 첫 세션(=인스톨) 패키지를
    // InitSdk 시점에 만들어 보내므로 그 뒤에 붙는 전역 콜백 파라미터가 인스톨 레코드에만
    // 빠지는데, 코어가 버퍼된 유저 상태를 flush하는 것은 InitializeAsync를 await한 뒤다.
    // 그래서 첫 세션을 지연시켜 두고 flush가 끝났다는 훅에서 풀어 준다.
    //
    // 스레드: Adjust의 정적 API는 내부에서 네이티브 브리지로 넘기고 콜백만 비동기로 돌려준다.
    // 우리는 콜백을 구독하지 않으므로 추가 마샬링이 필요 없다 — IAnalyticsProvider의 모든
    // 메서드는 AnalyticsService가 메인 스레드에서만 부른다.
    public sealed class AdjustAnalyticsProvider : IAnalyticsProvider, IAnalyticsFlushHook
    {
        private readonly AdjustAnalyticsSettings _settings;
        private readonly AdjustEventTokenMap _tokens;

        private bool _warnedMissingPurchaseToken;

        // 지연을 걸어 둔 첫 세션이 아직 묶여 있는지. 풀어 주는 쪽을 한 번으로 제한한다 —
        // 이미 나간 세션에 대고 EndFirstSessionDelay를 다시 부를 이유가 없다.
        private bool _firstSessionDelayed;

        public AdjustAnalyticsProvider(AdjustAnalyticsSettings settings)
        {
            _settings = settings;
            _tokens = new AdjustEventTokenMap(settings);
        }

        public string Name => "Adjust";

        // Adjust의 InitSdk는 완료 콜백이 없다(세션 전송 성공은 SessionSuccessDelegate로 따로 온다).
        // 초기화가 "끝났다"고 말할 수 있는 시점이 호출 직후이므로 동기적으로 완료시킨다.
        public Awaitable<bool> InitializeAsync()
        {
            if (_settings == null)
            {
                Debug.LogError("[Analytics/Adjust] AdjustAnalyticsSettings가 없다. " +
                               "AnalyticsServiceSettings의 Provider Settings 목록에 에셋을 넣어라.");
                return Completed(false);
            }

            var appToken = _settings.AppToken;

            if (string.IsNullOrEmpty(appToken))
            {
                // 토큰 없이 InitSdk를 부르면 Adjust가 로그만 남기고 아무것도 하지 않는다.
                // 그 상태로 true를 돌려주면 이후 모든 전송이 조용히 사라진다.
                Debug.LogError("[Analytics/Adjust] 현재 빌드 타깃의 앱 토큰이 비어 있다. " +
                               "AdjustAnalyticsSettings에서 Android/iOS 앱 토큰을 채워라.");
                return Completed(false);
            }

            var config = new AdjustConfig(appToken, _settings.Environment)
            {
                LogLevel = _settings.LogLevel,
                IsSendingInBackgroundEnabled = _settings.SendInBackground,
            };

            if (_settings.DelayFirstSession)
            {
                // 이 플래그를 켜면 누군가 EndFirstSessionDelay를 부를 때까지 SDK가 첫 세션을
                // 보내지 않는다. 우리 쪽 해제 지점은 OnBufferedStateFlushed 하나뿐이다.
                config.IsFirstSessionDelayEnabled = true;
                _firstSessionDelayed = true;
            }

            Adjust.InitSdk(config);

            return Completed(true);
        }

        // Adjust는 파라미터가 아니라 SDK 전체 스위치다. 꺼 두면 세션조차 전송하지 않는다.
        public void SetCollectionEnabled(bool enabled)
        {
            if (enabled)
            {
                Adjust.Enable();
            }
            else
            {
                Adjust.Disable();
            }
        }

        public void LogEvent(string name, AnalyticsParams parameters)
        {
            if (!_tokens.TryResolve(name, out var token)) return;

            var adjustEvent = new AdjustEvent(token);

            AppendCallbackParameters(adjustEvent, parameters);

            Adjust.TrackEvent(adjustEvent);
        }

        public void LogPurchase(PurchaseInfo purchase)
        {
            var token = _settings != null ? _settings.PurchaseEventToken : null;

            if (string.IsNullOrEmpty(token))
            {
                WarnMissingPurchaseTokenOnce();
                return;
            }

            var adjustEvent = new AdjustEvent(token);

            // Firebase와 같은 이유로 단가가 아니라 Revenue(= 단가 * 수량)를 보낸다.
            adjustEvent.SetRevenue(purchase.Revenue, purchase.Currency);

            if (!string.IsNullOrEmpty(purchase.ProductId))
            {
                adjustEvent.ProductId = purchase.ProductId;
            }

            if (!string.IsNullOrEmpty(purchase.TransactionId))
            {
                // Adjust는 DeduplicationId가 같은 매출 이벤트를 한 번만 집계한다.
                // IAPService는 스토어 재전달·복원에서 같은 거래를 다시 지급 경로에 태우므로
                // (IAPService README의 IIapFulfillment), 이게 없으면 매출이 부풀어 오른다.
                adjustEvent.DeduplicationId = purchase.TransactionId;
                adjustEvent.AddCallbackParameter("transaction_id", purchase.TransactionId);
            }

            if (purchase.Quantity != 1)
            {
                adjustEvent.AddCallbackParameter("quantity", purchase.Quantity.ToString());
            }

            AppendCallbackParameters(adjustEvent, purchase.Extra);

            Adjust.TrackEvent(adjustEvent);
        }

        // 광고 수익은 이벤트가 아니라 전용 API다. 토큰이 필요 없다.
        public void LogAdImpression(AdImpression impression)
        {
            var adRevenue = new AdjustAdRevenue(AdjustAdRevenueSource.Resolve(impression.AdPlatform));

            adRevenue.SetRevenue(impression.Revenue, impression.Currency);

            adRevenue.AdImpressionsCount = 1;
            adRevenue.AdRevenueNetwork = impression.NetworkName;
            adRevenue.AdRevenueUnit = impression.AdUnitId;

            // 네트워크 배치명이 있으면 그쪽이 대시보드와 맞는다. 없을 때만 게임이 붙인 배치명을 쓴다.
            adRevenue.AdRevenuePlacement = string.IsNullOrEmpty(impression.NetworkPlacement)
                ? impression.Placement
                : impression.NetworkPlacement;

            adRevenue.AddCallbackParameter("ad_format", impression.Format.ToString());

            Adjust.TrackAdRevenue(adRevenue);
        }

        // Adjust에는 런타임 SetUserId가 없다. 전역 콜백 파라미터는 이후 모든 이벤트의
        // 콜백 URL에 붙으므로 사실상 같은 역할을 한다.
        public void SetUserId(string userId)
        {
            var key = _settings != null ? _settings.UserIdCallbackKey : null;

            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("[Analytics/Adjust] User Id Callback Key가 비어 있어 유저 ID를 보내지 않는다.");
                return;
            }

            SetGlobalCallbackParameter(key, userId);
        }

        public void SetUserProperty(string name, string value)
        {
            if (string.IsNullOrEmpty(name)) return;

            SetGlobalCallbackParameter(name, value);
        }

        // 코어가 버퍼된 유저 상태와 이벤트를 모두 내보낸 직후. 여기서 첫 세션을 풀어 주면
        // AddGlobalCallbackParameter로 붙인 파라미터가 전부 인스톨 레코드에 실린다.
        public void OnBufferedStateFlushed()
        {
            EndFirstSessionDelayIfDelayed();
        }

        // Adjust의 정적 API에는 해제할 인스턴스가 없다. 다만 flush 전에 서비스가 해제되면
        // 첫 세션이 묶인 채로 남으므로, 그 경우에는 여기서 풀어 준다.
        public void Dispose()
        {
            EndFirstSessionDelayIfDelayed();
        }

        private void EndFirstSessionDelayIfDelayed()
        {
            if (!_firstSessionDelayed) return;

            _firstSessionDelayed = false;

            Adjust.EndFirstSessionDelay();
        }

        // 빈 값은 지운다. 빈 문자열로 덮어쓰면 이후 모든 이벤트에 빈 파라미터가 계속 붙는다.
        private static void SetGlobalCallbackParameter(string key, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                Adjust.RemoveGlobalCallbackParameter(key);
                return;
            }

            Adjust.AddGlobalCallbackParameter(key, value);
        }

        // Adjust의 콜백 파라미터는 문자열만 받는다. AnalyticsParamValue.ToString()이
        // long/double을 각각 소수점 손실 없이 찍는다(double은 "R").
        private static void AppendCallbackParameters(AdjustEvent adjustEvent, AnalyticsParams parameters)
        {
            if (parameters == null) return;

            foreach (var pair in parameters)
            {
                adjustEvent.AddCallbackParameter(pair.Key, pair.Value.ToString());
            }
        }

        private void WarnMissingPurchaseTokenOnce()
        {
            if (_warnedMissingPurchaseToken) return;

            _warnedMissingPurchaseToken = true;

            Debug.LogWarning("[Analytics/Adjust] Purchase Event Token이 비어 있어 구매를 전송하지 않는다. " +
                             "AdjustAnalyticsSettings에서 채워라.");
        }

        private static Awaitable<bool> Completed(bool value)
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(value);
            return source.Awaitable;
        }
    }
}
