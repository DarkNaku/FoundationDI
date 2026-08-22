using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "AnalyticsServiceSettings",
                     menuName = "FoundationDI/Analytics Service Settings")]
    public class AnalyticsServiceSettings : ScriptableObject
    {
        [Header("Providers")]
        [Tooltip("동시에 사용할 분석/MMP provider. 여러 개를 켜면 한 번의 API 호출이 전부로 브로드캐스트된다.")]
        [SerializeField] private AnalyticsProviderType _providers = AnalyticsProviderType.Debug;

        [Tooltip("켜면 에디터에서는 Debug provider만 생성한다. 개발 중 이벤트가 실제 대시보드를 오염시키는 것을 막는다.")]
        [SerializeField] private bool _forceDebugOnlyInEditor = true;

        [Header("Collection")]
        [Tooltip("CollectionEnabled의 초기값. 동의를 먼저 받아야 하는 앱은 꺼진 채로 출시하고 동의 후 켠다.")]
        [SerializeField] private bool _collectionEnabledByDefault = true;

        public AnalyticsProviderType Providers => _providers;
        public bool ForceDebugOnlyInEditor => _forceDebugOnlyInEditor;
        public bool CollectionEnabledByDefault => _collectionEnabledByDefault;

        public AnalyticsServiceOptions ToOptions() => new(_collectionEnabledByDefault);

        // 에디터에서 강제 Debug가 켜져 있으면 Debug 하나만 남긴다.
        public AnalyticsProviderType ResolveProviders(bool isEditor)
        {
            if (_forceDebugOnlyInEditor && isEditor) return AnalyticsProviderType.Debug;

            return _providers;
        }
    }
}
