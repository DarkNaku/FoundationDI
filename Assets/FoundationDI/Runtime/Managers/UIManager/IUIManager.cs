namespace DarkNaku.FoundationDI
{
    public interface IUIManager
    {
        bool IsPopupVisible { get; }
        T Page<T>() where T : UIPresenter;
        T Popup<T>() where T : UIPresenter;
        T Overlay<T>() where T : UIPresenter;
    }
}
