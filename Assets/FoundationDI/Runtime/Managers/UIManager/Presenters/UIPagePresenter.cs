namespace DarkNaku.FoundationDI
{
    public abstract class UIPagePresenter<TView> : UIPresenterBuilder<UIPagePresenter<TView>, TView>
        where TView : UIView
    {
    }
}
