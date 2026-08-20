using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 인스펙터에서 편집 가능해야 하므로 [Serializable] + SerializeField.
    // readonly struct가 아닌 이유가 이것이다.
    [Serializable]
    public struct AdUnitId
    {
        [SerializeField] private string _android;
        [SerializeField] private string _ios;

        public AdUnitId(string android, string ios) { _android = android; _ios = ios; }

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

        public bool IsValid => !string.IsNullOrEmpty(Current);
    }
}
