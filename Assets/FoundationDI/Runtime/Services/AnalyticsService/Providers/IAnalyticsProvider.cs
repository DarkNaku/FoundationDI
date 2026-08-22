using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 분석 SDK 하나를 감싸는 seam. 팬아웃·버퍼·예외 격리·수집 게이트는 전부 AnalyticsService가
    // 처리하므로, 어댑터는 "내가 아는 SDK의 모양으로 번역해서 넘긴다"만 한다.
    //
    // 계약: 모든 메서드는 메인 스레드에서 호출된다. SDK 콜백을 메인 스레드로 마샬링하는
    // 책임은 어댑터에 있다 — AdService의 IFullScreenAdapter와 같은 계약이다.
    //
    // 구조체 파라미터에 in을 쓰지 않는다. in이 붙으면 Action<T>에 대입할 수 없어
    // `_ads.Paid += _analytics.LogAdImpression;` 한 줄 배선이 컴파일되지 않는다.
    public interface IAnalyticsProvider : IDisposable
    {
        // 로그·진단용 식별자. "Firebase" / "Debug" 처럼 사람이 읽는 이름.
        string Name { get; }

        Awaitable<bool> InitializeAsync();

        void SetCollectionEnabled(bool enabled);

        void LogEvent(string name, AnalyticsParams parameters);
        void LogPurchase(PurchaseInfo purchase);
        void LogAdImpression(AdImpression impression);

        void SetUserId(string userId);
        void SetUserProperty(string name, string value);
    }
}
