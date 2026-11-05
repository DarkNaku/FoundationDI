using System;
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
        // 옵션에 상품 목록이 있으면 그것을, 없으면 IAP Catalog를 쓴다.
        public static List<ProductDefinition> Resolve(IAPServiceOptions options, string storeName)
        {
            if (options?.CatalogSource == null) return LoadDefault(storeName);

            IEnumerable<IAPProductDefinition> products;

            try
            {
                products = options.CatalogSource();
            }
            catch (Exception e)
            {
                Debug.LogError($"[IAPService] 상품 목록(CatalogSource)을 읽지 못했다. 상품 없이 시작한다: {e.Message}\n{e}");
                return new List<ProductDefinition>();
            }

            return ToDefinitions(products, storeName);
        }

        public static List<ProductDefinition> ToDefinitions(IEnumerable<IAPProductDefinition> products, string storeName)
        {
            var definitions = new List<ProductDefinition>();
            var ids = new HashSet<string>();

            if (products == null) return definitions;

            foreach (var product in products)
            {
                if (string.IsNullOrEmpty(product.Id))
                {
                    Debug.LogWarning("[IAPService] 빈 ID의 상품 정의를 건너뛴다.");
                    continue;
                }

                if (!ids.Add(product.Id))
                {
                    Debug.LogWarning($"[IAPService] 상품 ID가 중복돼 뒤의 정의를 건너뛴다: {product.Id}");
                    continue;
                }

                var storeId = storeName == GooglePlay.Name ? product.GooglePlayId
                            : storeName == AppleAppStore.Name ? product.AppleId
                            : null;
                if (string.IsNullOrEmpty(storeId)) storeId = product.Id;

                var type = product.Type == IAPProductType.NonConsumable ? ProductType.NonConsumable : ProductType.Consumable;

                definitions.Add(new ProductDefinition(product.Id, storeId, type));
            }

            return definitions;
        }

        private static List<ProductDefinition> LoadDefault(string storeName)
        {
            var catalog = ProductCatalog.LoadDefaultCatalog();

            WarnIfCodelessAutoInitialization(catalog);

            if (catalog.IsEmpty())
            {
                Debug.LogWarning("[IAPService] IAP Catalog가 비어 있다. Services > In-App Purchasing > IAP Catalog에서 상품을 추가할 것.");
            }

            return ToDefinitions(catalog, storeName);
        }

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

        // Codeless 자동 초기화가 켜져 있으면 CodelessIAPStoreListener가 따로 스토어에 연결해 구매를
        // 처리한다. 이 서비스가 지급을 저장할 때까지 확정을 미루는 동안 다른 경로가 처리하면 그 규율이
        // 깨진다. 기본값이 true라 카탈로그를 처음 만들면 이 상태다 — 막지는 않고 크게 알린다.
        public static void WarnIfCodelessAutoInitialization(ProductCatalog catalog)
        {
            if (catalog == null || !catalog.enableCodelessAutoInitialization || catalog.IsEmpty()) return;

            Debug.LogError("[IAPService] IAP Catalog의 Codeless 자동 초기화(Automatically initialize UnityPurchasing)가 " +
                           "켜져 있다. IAPService와 함께 쓰면 구매가 두 경로로 처리된다 — IAP Catalog 창에서 끌 것.");
        }
    }
}
