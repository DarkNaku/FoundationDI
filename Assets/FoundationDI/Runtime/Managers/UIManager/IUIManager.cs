namespace DarkNaku.FoundationDI
{
    public interface IUIManager
    {
        T Page<T>() where T : UIPresenter;
        T Popup<T>() where T : UIPresenter;
        T Overlay<T>() where T : UIPresenter;
    }
}
