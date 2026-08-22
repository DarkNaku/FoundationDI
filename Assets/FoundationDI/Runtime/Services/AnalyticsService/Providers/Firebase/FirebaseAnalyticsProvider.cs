using System;
using System.Collections.Generic;
using Firebase;
using Firebase.Analytics;
using Firebase.Extensions;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // Firebase Analytics 어댑터. 팬아웃·버퍼·예외 격리·수집 게이트는 AnalyticsService가 이미
    // 처리하므로 여기서는 번역만 한다.
    //
    // 스레드: Firebase의 의존성 확인은 Task로 오므로 ContinueWithOnMainThread로 메인 스레드에
    // 복귀시킨다. IAnalyticsProvider의 나머지 메서드는 AnalyticsService가 메인 스레드에서만
    // 부르므로 추가 마샬링이 필요 없다.
    public sealed class FirebaseAnalyticsProvider : IAnalyticsProvider
    {
        private bool _ready;

        public string Name => "Firebase";

        public Awaitable<bool> InitializeAsync()
        {
            if (_ready) return Completed(true);

            var source = new AwaitableCompletionSource<bool>();

            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogError($"[Analytics/Firebase] 의존성 확인이 실패했다: {task.Exception}");
                    source.SetResult(false);
                    return;
                }

                var status = task.Result;

                if (status != DependencyStatus.Available)
                {
                    // google-services.json / GoogleService-Info.plist 가 없으면 여기서 막힌다.
                    Debug.LogError($"[Analytics/Firebase] 의존성을 해결하지 못했다: {status}");
                    source.SetResult(false);
                    return;
                }

                // DefaultInstance 접근이 FirebaseApp 초기화를 확정시킨다.
                _ = FirebaseApp.DefaultInstance;
                _ready = true;
                source.SetResult(true);
            });

            return source.Awaitable;
        }

        public void SetCollectionEnabled(bool enabled) =>
            FirebaseAnalytics.SetAnalyticsCollectionEnabled(enabled);

        public void LogEvent(string name, AnalyticsParams parameters)
        {
            FirebaseParamConverter.WarnIfInvalidEventName(name);

            var converted = FirebaseParamConverter.Convert(parameters);

            if (converted.Length == 0)
            {
                FirebaseAnalytics.LogEvent(name);
                return;
            }

            FirebaseParamConverter.WarnIfTooManyParameters(name, converted.Length);
            FirebaseAnalytics.LogEvent(name, converted);
        }

        public void LogPurchase(PurchaseInfo purchase)
        {
            var parameters = new List<Parameter>
            {
                // Firebase의 value는 단가가 아니라 거래 총액이다. 수량을 곱하지 않으면 매출이 샌다.
                new Parameter(FirebaseAnalytics.ParameterValue, purchase.Revenue),
                new Parameter(FirebaseAnalytics.ParameterCurrency, purchase.Currency ?? string.Empty),
                new Parameter(FirebaseAnalytics.ParameterQuantity, (long)purchase.Quantity),
            };

            if (!string.IsNullOrEmpty(purchase.ProductId))
            {
                parameters.Add(new Parameter(FirebaseAnalytics.ParameterItemID, purchase.ProductId));
            }

            if (!string.IsNullOrEmpty(purchase.TransactionId))
            {
                parameters.Add(new Parameter(FirebaseAnalytics.ParameterTransactionID, purchase.TransactionId));
            }

            FirebaseParamConverter.Append(parameters, purchase.Extra);
            FirebaseParamConverter.WarnIfTooManyParameters(FirebaseAnalytics.EventPurchase, parameters.Count);

            FirebaseAnalytics.LogEvent(FirebaseAnalytics.EventPurchase, parameters.ToArray());
        }

        public void LogAdImpression(AdImpression impression)
        {
            // AdImpression의 필드는 ad_impression 파라미터와 그대로 1:1이다(AdService README 4.1).
            var parameters = new[]
            {
                new Parameter(FirebaseAnalytics.ParameterAdPlatform, impression.AdPlatform ?? string.Empty),
                new Parameter(FirebaseAnalytics.ParameterAdSource, impression.NetworkName ?? string.Empty),
                new Parameter(FirebaseAnalytics.ParameterAdUnitName, impression.AdUnitId ?? string.Empty),
                new Parameter(FirebaseAnalytics.ParameterAdFormat, impression.Format.ToString()),
                new Parameter(FirebaseAnalytics.ParameterValue, impression.Revenue),

                // Currency를 빼고 USD로 가정하면 AdMob에서 매출 집계가 틀어진다(AdService README 4.1).
                new Parameter(FirebaseAnalytics.ParameterCurrency, impression.Currency ?? string.Empty),
            };

            FirebaseAnalytics.LogEvent(FirebaseAnalytics.EventAdImpression, parameters);
        }

        public void SetUserId(string userId) => FirebaseAnalytics.SetUserId(userId);

        public void SetUserProperty(string name, string value)
        {
            FirebaseParamConverter.WarnIfInvalidEventName(name);
            FirebaseAnalytics.SetUserProperty(name, value);
        }

        // FirebaseAnalytics는 정적 API라 해제할 인스턴스가 없다.
        public void Dispose()
        {
        }

        private static Awaitable<bool> Completed(bool value)
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(value);
            return source.Awaitable;
        }
    }
}
