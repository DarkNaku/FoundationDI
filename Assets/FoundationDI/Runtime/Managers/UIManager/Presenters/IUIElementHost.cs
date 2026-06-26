namespace DarkNaku.FoundationDI
{
    internal interface IUIElementHost
    {
        void RequestHide(UIPresenter element);
    }

    public interface IConfigurable<in TParams>
    {
        void Configure(TParams parameters);
    }
}
