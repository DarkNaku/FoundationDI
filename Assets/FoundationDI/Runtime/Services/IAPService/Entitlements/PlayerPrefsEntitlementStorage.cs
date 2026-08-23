using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 기본 구현. 클라우드 세이브를 쓰는 게임은 IEntitlementStorage를 직접 구현해 등록한다.
    //
    // PlayerPrefs는 사용자가 조작할 수 있다 — 하지만 이 값은 "이미 스토어가 확인해 준 소유"의
    // 캐시일 뿐이고, 실제 구매는 매번 영수증 검증을 거친다. 위조된 캐시로 얻을 수 있는 것은
    // 이미 산 상품을 다시 사지 못하게 되는 것뿐이라 공격 가치가 없다.
    public sealed class PlayerPrefsEntitlementStorage : IEntitlementStorage
    {
        private const string KeyPrefix = "FoundationDI.IAP.Owned.";

        public bool IsOwned(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return false;

            return PlayerPrefs.GetInt(KeyPrefix + productId, 0) != 0;
        }

        public void SetOwned(string productId, bool owned)
        {
            if (string.IsNullOrEmpty(productId)) return;

            PlayerPrefs.SetInt(KeyPrefix + productId, owned ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
