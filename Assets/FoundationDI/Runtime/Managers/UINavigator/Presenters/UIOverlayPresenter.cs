namespace DarkNaku.FoundationDI
{
    public abstract class UIOverlayPresenter<TView> : UIPresenterBuilder<UIOverlayPresenter<TView>, TView>, IOverlayPlacement
        where TView : UIView
    {
        protected internal virtual bool Above => true;
        bool IOverlayPlacement.Above => Above;
    }
}
