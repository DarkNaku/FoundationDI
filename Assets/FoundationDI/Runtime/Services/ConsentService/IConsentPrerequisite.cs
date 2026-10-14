using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // UMP를 시작하기 전에 끝나 있어야 하는 작업. Google은 "UMP 시작 전에 Firebase를 초기화하라"고
    // 요구한다(consent mode를 UMP가 Firebase에 기록하기 때문이다). 이 순서를 문서가 아니라
    // 구조로 강제하려고 둔다 — 광고 초기화가 먼저 불려도 UMP 앞에서 이것들을 기다린다.
    //
    // 구현은 SDK 초기화까지만 기다리고 동의 결과를 기다리면 안 된다(교착).
    public interface IConsentPrerequisite
    {
        Awaitable PrepareAsync();
    }
}
