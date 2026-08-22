using DarkNaku.FoundationDI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

// 스모크 확인용 임시 컴포넌트. 확인이 끝나면 지운다.
//
// 확인하려는 것: 초기화 전 발행한 이벤트가 버려지지 않고 초기화 시 유저 상태 뒤에 순서대로
// 흘러나오는가, 수집 게이트가 실제로 막는가, provider가 여럿일 때 한 번의 호출이 전부로 가는가.
// Debug provider의 콘솔 출력으로 눈으로 확인한다.
public class AnalyticsServiceSmokeTest : MonoBehaviour
{
    [Inject] private IAnalyticsService _analytics;
    [Inject] private IAdService _ads;

    private int _level = 1;

    private async void Start()
    {
        // AdServiceSmokeTest와 같은 이유로 자가 주입한다(임시 컴포넌트라 스코프에 등록하지 않는다).
        LifetimeScope.Find<RootLifetimeScope>().Container.Inject(this);

        // 광고 수익 → 분석. 서비스가 자동으로 해주지 않는 한 줄짜리 배선이다.
        _ads.Paid += _analytics.LogAdImpression;

        // 일부러 초기화 '전에' 발행한다. 버퍼링됐다가 아래 InitializeAsync에서 흘러나와야 한다.
        _analytics.LogEvent("app_boot");
        _analytics.SetUserProperty("cohort", "before-init-1");
        _analytics.SetUserProperty("cohort", "before-init-2");   // 이 값만 남아야 한다
        _analytics.SetUserId("smoke-player");

        Debug.Log("[Smoke] 초기화 시작 — 위 호출들은 아직 provider로 나가지 않았어야 한다");

        var ok = await _analytics.InitializeAsync();

        Debug.Log($"[Smoke] 초기화: {ok} (IsInitialized={_analytics.IsInitialized})");
    }

    private void OnGUI()
    {
        if (_analytics == null) return;

        if (GUI.Button(new Rect(300, 20, 260, 60), $"레벨 클리어 ({_level})"))
        {
            _analytics.LogEvent("level_complete", new AnalyticsParams
            {
                { "level", (long)_level },
                { "clear_time", 34.5 },
                { "difficulty", "hard" },
            });

            _analytics.SetUserProperty("player_level", _level.ToString());
            _level++;
        }

        if (GUI.Button(new Rect(300, 100, 260, 60), "구매"))
        {
            _analytics.LogPurchase(new PurchaseInfo(
                productId: "gem_pack_medium",
                price: 4.99,
                currency: "USD",
                quantity: 2,
                transactionId: System.Guid.NewGuid().ToString(),
                extra: new AnalyticsParams { { "shop_tab", "featured" } }));
        }

        if (GUI.Button(new Rect(300, 180, 260, 60), $"수집: {_analytics.CollectionEnabled}"))
        {
            _analytics.CollectionEnabled = !_analytics.CollectionEnabled;
        }
    }
}
