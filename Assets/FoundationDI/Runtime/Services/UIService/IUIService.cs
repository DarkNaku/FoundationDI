namespace DarkNaku.FoundationDI
{
    public interface IUIService
    {
        bool IsPopupVisible { get; }
        T Page<T>() where T : UIPresenter;
        T Popup<T>() where T : UIPresenter;
        T Overlay<T>() where T : UIPresenter;
    }
}
