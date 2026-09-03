using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class AnalyticsProviderFactory : IAnalyticsProviderFactory
    {
        // 모든 provider 비트를 한 번씩 훑기 위한 목록. Enum.GetValues는 할당이 생기고
        // None까지 포함되므로 직접 나열한다.
        private static readonly AnalyticsProviderType[] _all =
        {
            AnalyticsProviderType.Debug,
            AnalyticsProviderType.Firebase,
            AnalyticsProviderType.AppsFlyer,
            AnalyticsProviderType.Adjust,
            AnalyticsProviderType.Singular,
            AnalyticsProviderType.Airbridge,
        };

        // AdService의 팩토리와 달리 Dummy로 폴백하지 않는다. provider가 여럿인 이상,
        // 하나를 만들지 못했다고 나머지까지 버리거나 가짜로 대체할 이유가 없다 —
        // 만들지 못한 것만 에러 로그와 함께 건너뛰고 나머지는 그대로 동작시킨다.
        public IReadOnlyList<IAnalyticsProvider> CreateAll(
            AnalyticsProviderType types,
            AnalyticsServiceOptions options,
            IReadOnlyList<AnalyticsProviderSettings> providerSettings = null)
        {
            var providers = new List<IAnalyticsProvider>();
            var context = new AnalyticsProviderCreationContext(options, providerSettings);

            foreach (var type in _all)
            {
                if ((types & type) == 0) continue;

                if (!AnalyticsProviderRegistry.TryResolve(type, out var creator))
                {
                    Debug.LogError(ProviderDiagnostics.MissingCreator(
                        "AnalyticsService", type.ToString(), "이 provider만 건너뛴다."));
                    continue;
                }

                try
                {
                    var provider = creator(context);

                    if (provider == null)
                    {
                        Debug.LogError($"[AnalyticsService] {type} creator가 null을 반환했다. 건너뛴다.");
                        continue;
                    }

                    providers.Add(provider);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[AnalyticsService] {type} provider 생성 중 예외: {e}");
                }
            }

            return providers;
        }
    }
}
