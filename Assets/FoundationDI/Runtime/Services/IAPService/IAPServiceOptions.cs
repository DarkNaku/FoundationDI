namespace DarkNaku.FoundationDI
{
    // RegisterIAPService에 넘기는 설정. 기본값만으로 0.16 이전과 같이 동작한다.
    //   builder.RegisterIAPService(new IAPServiceOptions { VerboseLogging = true });
    public sealed class IAPServiceOptions
    {
        public bool VerboseLogging { get; set; }
    }
}
