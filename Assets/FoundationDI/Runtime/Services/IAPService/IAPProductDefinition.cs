namespace DarkNaku.FoundationDI
{
    // 코드(게임 스펙 표)로 넘기는 상품 정의. IAPServiceOptions.CatalogSource에 쓴다.
    // 스토어별 ID가 비어 있으면 공용 ID를 그대로 쓴다(IAP Catalog와 같은 규칙).
    public readonly struct IAPProductDefinition
    {
        public string Id { get; }
        public IAPProductType Type { get; }
        public string GooglePlayId { get; }
        public string AppleId { get; }

        public IAPProductDefinition(string id, IAPProductType type, string googlePlayId = null, string appleId = null)
        {
            Id = id;
            Type = type;
            GooglePlayId = googlePlayId;
            AppleId = appleId;
        }
    }
}
