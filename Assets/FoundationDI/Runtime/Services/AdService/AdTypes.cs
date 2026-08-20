using UnityEngine;

namespace DarkNaku.FoundationDI
{
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
