using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 게임 코드가 아는 유일한 분석 API. 등록된 provider가 몇 개든 호출은 한 번이다.
    //
    // 로깅 API가 전부 동기 void인 이유: 5사 SDK 모두 로깅이 fire-and-forget이고 게임 코드가
    // 전송 완료를 기다릴 이유가 없다. InitializeAsync만 비동기다(Firebase가 의존성 확인에서 실패할 수 있다).
    public interface IAnalyticsService : IDisposable
    {
        bool IsInitialized { get; }

        // 재진입 안전하다. 진행 중이면 같은 결과에 편승하고, 이미 초기화됐으면 즉시 true다.
        Awaitable<bool> InitializeAsync();

        // false면 모든 로깅이 호출 즉시 드롭된다(버퍼에도 들어가지 않는다).
        // 초기값은 설정에서 오며 영속화하지 않는다 — 동의 판단과 그 기록은 게임의 책임이다.
        bool CollectionEnabled { get; set; }

        void LogEvent(string name);
        void LogEvent(string name, AnalyticsParams parameters);
        void LogPurchase(PurchaseInfo purchase);

        // AdService의 AdImpression을 그대로 받는다. `_ads.Paid += _analytics.LogAdImpression;`
        void LogAdImpression(AdImpression impression);

        void SetUserId(string userId);   // null이면 해제
        void SetUserProperty(string name, string value);
    }
}
