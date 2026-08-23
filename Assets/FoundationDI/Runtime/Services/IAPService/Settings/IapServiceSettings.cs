using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "IapServiceSettings", menuName = "FoundationDI/IAP Service Settings")]
    public class IapServiceSettings : ScriptableObject
    {
        [Header("Provider")]
        [SerializeField] private IapProviderType _provider = IapProviderType.UnityIAP;

        [Tooltip("에디터에서는 항상 Dummy provider를 쓴다. 실기 테스트가 필요할 때만 끈다.")]
        [SerializeField] private bool _forceDummyInEditor = true;

        [Header("Catalog")]
        [SerializeField] private List<IapProductEntry> _products = new();

        [Header("Debug")]
        [SerializeField] private bool _verboseLogging;

        [Header("Dummy Provider")]
        [SerializeField] private DummyIapOptions _dummyOptions = DummyIapOptions.Default;

        public IapProviderType Provider => _provider;
        public bool ForceDummyInEditor => _forceDummyInEditor;
        public DummyIapOptions DummyOptions => _dummyOptions;
        public IReadOnlyList<IapProductEntry> Products => _products;

        public IapServiceOptions ToOptions()
        {
            var definitions = new List<IapProductDefinition>(_products.Count);
            var seen = new HashSet<string>();

            foreach (var entry in _products)
            {
                if (entry == null) continue;

                if (string.IsNullOrEmpty(entry.Id))
                {
                    Debug.LogWarning("[IAPService] ID가 비어 있는 상품 항목을 건너뛴다.");
                    continue;
                }

                // 중복 ID는 카탈로그 조회를 모호하게 만든다. 먼저 온 쪽을 남긴다 —
                // 인스펙터에서 위에 있는 항목이 이기는 편이 예측 가능하다.
                if (!seen.Add(entry.Id))
                {
                    Debug.LogWarning($"[IAPService] 중복된 상품 ID를 건너뛴다: {entry.Id}");
                    continue;
                }

                definitions.Add(entry.ToDefinition());
            }

            return new IapServiceOptions(definitions, _verboseLogging);
        }

        // 테스트 전용. 인스펙터를 거치지 않고 카탈로그를 채운다.
        internal void SetProductsForTest(IReadOnlyList<IapProductEntry> products)
        {
            _products = new List<IapProductEntry>(products);
        }
    }
}
