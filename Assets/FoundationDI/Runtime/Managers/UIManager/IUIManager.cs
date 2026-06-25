namespace DarkNaku.FoundationDI
{
    public interface IUIManager
    {
        T Page<T>() where T : UIPresenterBase;
        T Popup<T>() where T : UIPresenterBase;
        T Overlay<T>() where T : UIPresenterBase;
    }
}
