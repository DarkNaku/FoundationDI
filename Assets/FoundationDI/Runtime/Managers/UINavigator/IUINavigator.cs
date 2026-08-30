namespace DarkNaku.FoundationDI
{
    public interface IUINavigator
    {
        bool IsPopupVisible { get; }
        T Page<T>() where T : UIPresenter;
        T Popup<T>() where T : UIPresenter;
        T Overlay<T>() where T : UIPresenter;
    }
}
