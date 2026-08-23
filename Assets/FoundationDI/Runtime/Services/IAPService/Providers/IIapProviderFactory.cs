namespace DarkNaku.FoundationDI
{
    public interface IIapProviderFactory
    {
        IIapProvider Create(IapProviderType type, DummyIapOptions dummyOptions, bool forceDummy);
    }
}
