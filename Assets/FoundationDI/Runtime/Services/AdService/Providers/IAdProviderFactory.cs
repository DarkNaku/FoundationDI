namespace DarkNaku.FoundationDI
{
    public interface IAdProviderFactory
    {
        IAdProvider Create(AdProviderType type, DummyAdOptions dummyOptions, bool forceDummy);
    }
}
