using System;

namespace DarkNaku.FoundationDI
{
    // OS 앱 내 리뷰 창(Android Play In-App Review, iOS SKStoreReviewController)을 띄운다.
    //
    // 언제 부를지는 게임이 정한다(레벨 규칙 등). 리뷰 창은 OS가 횟수를 제한해 요청해도 뜨지 않을 수 있고,
    // 떴는지·몇 점을 줬는지는 알 수 없다. 우리 팝업으로 의견을 먼저 묻는 것은 Google이 리뷰 창 앞에서,
    // Apple은 자체 리뷰 요청 팝업으로 막는다 — OS 창만 띄운다.
    public interface IReviewService : IDisposable
    {
        // 리뷰 창을 곧 띄울 것 같으면 미리 준비한다(승리 창이 뜰 때 등). Android는 ReviewInfo를 받아 두어
        // Request 때 바로 띄운다. 그 외에는 아무것도 하지 않는다.
        void Prepare();

        // 리뷰 창을 띄워 달라고 요청한다. 요청을 보냈으면 true — 창이 뜬다는 뜻은 아니다.
        bool Request();
    }
}
