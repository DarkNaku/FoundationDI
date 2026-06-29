namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// Addressables 백엔드를 사용하는 ResourceService 구체 구현.
    /// 무파라미터 단일 생성자라 Register&lt;IResourceService, AddressableResourceService&gt;로 등록할 수 있다.
    /// </summary>
    public sealed class AddressableResourceService : ResourceService
    {
        public AddressableResourceService() : base(new AddressableResourceProvider())
        {
        }
    }
}
