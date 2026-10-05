using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

namespace DarkNaku.FoundationDI
{
    // Unity IAP Catalog(IAPProductCatalog.json)를 FetchProducts에 넘길 정의로 바꾼다.
    // 카탈로그 ID가 곧 게임 코드가 쓰는 공용 ID이고, 스토어별 ID가 비어 있으면 공용 ID를 그대로 쓴다 —
    // 대부분의 게임이 양 스토어에 같은 ID를 올리기 때문이다.
    internal static class UnityIAPCatalog
    {
        public static List<ProductDefinition> ToDefinitions(ProductCatalog catalog, string storeName)
        {
            var definitions = new List<ProductDefinition>();

            if (catalog == null) return definitions;

            foreach (var item in catalog.allValidProducts)
            {
                // 구독은 범위 밖이다. 조용히 넘기면 "왜 상품이 안 보이지"가 늦게 발견된다.
                if (item.type == ProductType.Subscription)
                {
                    Debug.LogWarning($"[IAPService] 구독 상품은 지원하지 않아 건너뛴다: {item.id}");
                    continue;
                }

                var storeId = item.GetStoreID(storeName);
                if (string.IsNullOrEmpty(storeId)) storeId = item.id;

                definitions.Add(new ProductDefinition(item.id, storeId, item.type));
            }

            return definitions;
        }
    }
}
