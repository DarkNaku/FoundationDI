namespace DarkNaku.FoundationDI
{
    internal sealed class PageController
    {
        public UIPresenterBase Current { get; private set; }
        public void SetCurrent(UIPresenterBase page) => Current = page;
        public void Clear() => Current = null;
    }
}
