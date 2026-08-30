namespace DarkNaku.FoundationDI
{
    internal sealed class PageController
    {
        public UIPresenter Current { get; private set; }
        public void SetCurrent(UIPresenter page) => Current = page;
        public void Clear() => Current = null;
    }
}
