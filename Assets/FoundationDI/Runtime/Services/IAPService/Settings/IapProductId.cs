using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 인스펙터에서 편집 가능해야 하므로 [Serializable] + SerializeField.
    // readonly struct가 아닌 이유가 이것이다.
    //
    // AdUnitId와 다른 점은 폴백이다. 광고 단위 ID는 스토어별로 반드시 다르지만, 인앱 상품은
    // 대부분의 게임이 양 스토어에 같은 ID를 올린다 — 그래서 오버라이드가 비면 공용 ID를 쓴다.
    [Serializable]
    public struct IapProductId
    {
        [SerializeField] private string _android;
        [SerializeField] private string _ios;

        public IapProductId(string android, string ios)
        {
            _android = android;
            _ios = ios;
        }

        public string Android => _android;
        public string iOS => _ios;

        // 에디터에서는 UNITY_ANDROID/UNITY_IOS가 빌드 타깃을 따라가므로
        // 에디터 실행 중에도 현재 타깃의 ID가 나온다.
        public string Current
        {
#if UNITY_ANDROID
            get => _android;
#elif UNITY_IOS
            get => _ios;
#else
            get => string.Empty;
#endif
        }

        public string Resolve(string fallbackId) => string.IsNullOrEmpty(Current) ? fallbackId : Current;
    }
}
