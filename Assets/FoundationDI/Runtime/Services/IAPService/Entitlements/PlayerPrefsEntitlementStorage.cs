using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 기본 구현. 클라우드 세이브를 쓰는 게임은 IEntitlementStorage를 직접 구현해 등록한다.
    //
    // PlayerPrefs는 사용자가 조작할 수 있다 — 하지만 이 값은 "이미 스토어가 확인해 준 소유"의
    // 캐시일 뿐이고, 스토어 조회가 성공할 때마다 확정 목록으로 덮인다. 위조한 소유는 오프라인으로
    // 버티는 동안만 유지되고 다음 온라인 실행에서 회수된다.
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
