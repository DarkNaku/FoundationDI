namespace DarkNaku.FoundationDI
{
    internal sealed class PageController
    {
        public UIPresenterBase Active { get; private set; }
        public void SetActive(UIPresenterBase page) => Active = page;
        public void Clear() => Active = null;
    }
}
