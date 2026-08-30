namespace DarkNaku.FoundationDI
{
    internal interface IUIElementHost
    {
        void RequestHide(UIPresenter element);
    }

    /// <summary>
    /// Presenter에 파라미터를 주입하기 위한 인터페이스. <see cref="Configure"/>는 View가 바인딩되기 전에 동기로 호출된다.
    /// </summary>
    /// <remarks>
    /// <b>주의:</b> <c>Configure</c>는 View에 접근하지 말 것 — 호출 시점에 View가 아직 바인딩되지 않았을 수 있다.
    /// 전달 params만 저장하고 View 접근은 <c>OnInitialize</c>/<c>OnBeforeShow</c>에서 수행한다.
    /// </remarks>
    public interface IConfigurable<in TParams>
    {
        void Configure(TParams parameters);
    }
}
