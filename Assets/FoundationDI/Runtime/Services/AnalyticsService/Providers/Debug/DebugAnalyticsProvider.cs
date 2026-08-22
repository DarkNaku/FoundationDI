using System.Text;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // SDK 없이 전체 흐름(버퍼 flush 순서, 팬아웃, 수집 게이트)을 실기에서 눈으로 확인하기 위한
    // provider다. AdService의 Dummy provider와 같은 역할이며, 에디터에서 실제 대시보드를
    // 오염시키지 않고 개발하기 위한 기본 선택지이기도 하다.
    public sealed class DebugAnalyticsProvider : IAnalyticsProvider
    {
        private const string Tag = "[Analytics/Debug]";

        public string Name => "Debug";

        public Awaitable<bool> InitializeAsync()
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(true);
            Debug.Log($"{Tag} 초기화 완료");
            return source.Awaitable;
        }

        public void SetCollectionEnabled(bool enabled) =>
            Debug.Log($"{Tag} 수집 {(enabled ? "켬" : "끔")}");

        public void LogEvent(string name, AnalyticsParams parameters) =>
            Debug.Log($"{Tag} {name} {Format(parameters)}");

        public void LogPurchase(PurchaseInfo purchase) =>
            Debug.Log($"{Tag} purchase {purchase.ProductId} " +
                      $"{purchase.Revenue} {purchase.Currency} x{purchase.Quantity} " +
                      $"tx={purchase.TransactionId} {Format(purchase.Extra)}");

        public void LogAdImpression(AdImpression impression) =>
            Debug.Log($"{Tag} ad_impression {impression.AdPlatform}/{impression.NetworkName} " +
                      $"{impression.Format} {impression.AdUnitId} " +
                      $"{impression.Revenue} {impression.Currency}");

        public void SetUserId(string userId) => Debug.Log($"{Tag} user_id = {userId ?? "(해제)"}");

        public void SetUserProperty(string name, string value) =>
            Debug.Log($"{Tag} user_property {name} = {value}");

        public void Dispose() => Debug.Log($"{Tag} 해제");

        private static string Format(AnalyticsParams parameters)
        {
            if (parameters == null || parameters.Count == 0) return "{ }";

            var builder = new StringBuilder("{ ");
            var first = true;

            foreach (var pair in parameters)
            {
                if (!first) builder.Append(", ");

                builder.Append(pair.Key).Append('=').Append(pair.Value);
                first = false;
            }

            return builder.Append(" }").ToString();
        }
    }
}
