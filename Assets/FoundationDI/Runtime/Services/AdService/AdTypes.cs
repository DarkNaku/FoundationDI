using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public enum AdFormat { Banner, Interstitial, Rewarded }

    public enum AdShowOutcome
    {
        Shown,      // 전면: 정상 노출 후 닫힘
        Rewarded,   // 리워드: 보상 확정
        Dismissed,  // 리워드: 보상 없이 닫힘
        NotReady,   // 준비 안 됨 — 즉시 반환
        Failed,     // 표시 중 실패 / 중복 호출
        Blocked,    // AdsRemoved 등 정책 차단
    }

    public enum AdRevenuePrecision { Unknown, Estimated, PublisherDefined, Exact }

    public readonly struct AdReward
    {
        public string Label { get; }
        public double Amount { get; }
        public AdReward(string label, double amount) { Label = label; Amount = amount; }
    }

    public readonly struct AdError
    {
        public int Code { get; }
        public string Message { get; }
        public AdError(int code, string message) { Code = code; Message = message; }
        public override string ToString() => $"({Code}) {Message}";
    }

    public readonly struct AdShowResult
    {
        public AdShowOutcome Outcome { get; }
        public AdReward Reward { get; }   // Outcome == Rewarded 일 때만 유효
        public AdError Error { get; }     // Outcome == Failed 일 때만 유효

        private AdShowResult(AdShowOutcome outcome, AdReward reward, AdError error)
        {
            Outcome = outcome;
            Reward = reward;
            Error = error;
        }

        public static AdShowResult Shown() => new(AdShowOutcome.Shown, default, default);
        public static AdShowResult Rewarded(AdReward reward) => new(AdShowOutcome.Rewarded, reward, default);
        public static AdShowResult Dismissed() => new(AdShowOutcome.Dismissed, default, default);
        public static AdShowResult NotReady() => new(AdShowOutcome.NotReady, default, default);
        public static AdShowResult Failed(AdError error) => new(AdShowOutcome.Failed, default, error);
        public static AdShowResult Blocked() => new(AdShowOutcome.Blocked, default, default);

        public bool IsRewarded => Outcome == AdShowOutcome.Rewarded;

        // 광고가 실제로 화면에 떴는지. 보상 여부와 무관하다.
        public bool WasShown => Outcome is AdShowOutcome.Shown
                                        or AdShowOutcome.Rewarded
                                        or AdShowOutcome.Dismissed;
    }

    public readonly struct AdImpression
    {
        public AdFormat Format { get; }
        public string AdPlatform { get; }        // "AdMob"/"LevelPlay"/"AppLovin" → ad_platform
        public string NetworkName { get; }       // 실제 채운 네트워크           → ad_source
        public string AdUnitId { get; }          //                             → ad_unit_name
        public string NetworkPlacement { get; }  // instanceName / NetworkPlacement
        public string Placement { get; }         // 게임이 ShowAsync에 넘긴 배치명
        public double Revenue { get; }
        public string Currency { get; }          // AdMob은 USD가 아닐 수 있다 — 반드시 함께 사용
        public AdRevenuePrecision Precision { get; }
        public string CreativeId { get; }        // 없으면 null

        public AdImpression(AdFormat format, string adPlatform, string networkName, string adUnitId,
                            string networkPlacement, string placement, double revenue, string currency,
                            AdRevenuePrecision precision, string creativeId)
        {
            Format = format;
            AdPlatform = adPlatform;
            NetworkName = networkName;
            AdUnitId = adUnitId;
            NetworkPlacement = networkPlacement;
            Placement = placement;
            Revenue = revenue;
            Currency = currency;
            Precision = precision;
            CreativeId = creativeId;
        }
    }

    public readonly struct AdRetryPolicy
    {
        public int MaxAttempts { get; }
        public float BaseSeconds { get; }
        public float MaxDelaySeconds { get; }

        public AdRetryPolicy(int maxAttempts, float baseSeconds, float maxDelaySeconds)
        {
            MaxAttempts = maxAttempts;
            BaseSeconds = baseSeconds;
            MaxDelaySeconds = maxDelaySeconds;
        }

        public static AdRetryPolicy Default => new(5, 2f, 64f);

        // 지연 = base^attempt, 단 상한으로 클램프. attempt는 1부터 시작한다.
        public float DelayFor(int attempt)
        {
            return Mathf.Min(Mathf.Pow(BaseSeconds, attempt), MaxDelaySeconds);
        }
    }
}
