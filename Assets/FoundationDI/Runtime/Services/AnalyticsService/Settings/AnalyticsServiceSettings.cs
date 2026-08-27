using System.Collections.Generic;
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

        [Tooltip("어댑터 고유 설정 에셋(예: AdjustAnalyticsSettings). 코어는 내용을 모르고 " +
                 "해당 어댑터에 그대로 넘긴다. 설정이 필요 없는 어댑터는 비워 둔다.")]
        [SerializeField] private List<AnalyticsProviderSettings> _providerSettings = new();

        [Header("Collection")]
        [Tooltip("CollectionEnabled의 초기값. 동의를 먼저 받아야 하는 앱은 꺼진 채로 출시하고 동의 후 켠다.")]
        [SerializeField] private bool _collectionEnabledByDefault = true;

        public AnalyticsProviderType Providers => _providers;
        public bool ForceDebugOnlyInEditor => _forceDebugOnlyInEditor;
        public bool CollectionEnabledByDefault => _collectionEnabledByDefault;

        // SDK가 프로젝트에서 빠지면 그 어댑터의 설정 타입도 사라져 항목이 null이 된다.
        // 그대로 넘긴다 — 소비 지점(GetSettings<T>)이 타입 패턴 매칭이라 null은 그냥 안 걸린다.
        // 여기서 걸러 내면 SDK가 돌아왔을 때 인스펙터의 빈 칸이 사라져 다시 끌어다 놔야 한다.
        public IReadOnlyList<AnalyticsProviderSettings> ProviderSettings => _providerSettings;

        public AnalyticsServiceOptions ToOptions() => new(_collectionEnabledByDefault);

        // 에디터에서 강제 Debug가 켜져 있으면 Debug 하나만 남긴다.
        public AnalyticsProviderType ResolveProviders(bool isEditor)
        {
            if (_forceDebugOnlyInEditor && isEditor) return AnalyticsProviderType.Debug;

            return _providers;
        }
    }
}
