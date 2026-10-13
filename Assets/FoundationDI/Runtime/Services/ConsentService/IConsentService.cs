using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 약관 수락 → (선행 작업) → Google UMP → (옵션) ATT를 한 흐름으로 묻는다.
    // AdService·AnalyticsService가 초기화 앞에서 RequestAsync를 await하므로 게임이 직접 부를
    // 필요는 없다. 로딩 화면처럼 시점을 직접 고르고 싶을 때만 먼저 부른다.
    public interface IConsentService : IDisposable
    {
        // 재진입 안전하다. 진행 중이면 같은 결과에 편승하고, 직전 결과가 확정(광고 가능 + 분석 판정됨)
        // 이면 즉시 그 결과를 돌려준다. 아니면 UMP 단계만 다시 수행한다. 절대 영원히 매달리지 않는다.
        Awaitable<ConsentResult> RequestAsync();

        // 현재 상태를 매번 다시 읽는다.
        ConsentResult Current { get; }

        bool IsPolicyAccepted { get; }

        // 기존 유저 자동 동의용. 세이브가 있으면 RequestAsync 전에 부른다.
        void AcceptPolicy();

        // 현지화된 문구로 바꾼다. RequestAsync 전에 부른다.
        void SetPolicyContent(PolicyAgreementContent content);

        // 설정 화면에 "개인정보 옵션" 버튼을 보여 줄 조건.
        bool IsPrivacyOptionsRequired { get; }

        // UMP 옵션 폼을 다시 연다. 닫힌 뒤 ConsentChanged를 발행한다.
        Awaitable ShowPrivacyOptionsAsync();

        ITrackingAuthorization TrackingAuthorization { get; }

        // 옵션 폼이 닫혔거나 재판정 결과가 바뀌었을 때.
        event Action ConsentChanged;
    }
}
