namespace DarkNaku.FoundationDI
{
    public interface IIAPProviderFactory
    {
        IIAPProvider Create(IAPProviderType type, DummyIAPOptions dummyOptions, bool forceDummy);
    }
}
