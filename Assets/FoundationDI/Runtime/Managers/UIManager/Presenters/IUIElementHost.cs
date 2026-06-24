namespace DarkNaku.FoundationDI
{
    internal interface IUIElementHost
    {
        void RequestHide(UIPresenterBase element);
        void RequestDestroy(UIPresenterBase element);
    }

    public interface IConfigurable<in TParams>
    {
        void Configure(TParams parameters);
    }
}
