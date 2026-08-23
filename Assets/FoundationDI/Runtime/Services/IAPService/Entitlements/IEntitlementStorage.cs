namespace DarkNaku.FoundationDI
{
    // 비소모성 소유의 로컬 캐시. 진실의 원천은 스토어지만 오프라인에서도 IsOwned가 답해야 한다.
    public interface IEntitlementStorage
    {
        bool IsOwned(string productId);
        void SetOwned(string productId, bool owned);
    }
}
