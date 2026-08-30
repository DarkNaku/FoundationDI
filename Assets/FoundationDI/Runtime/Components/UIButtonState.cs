namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 버튼 시각 상태.
    /// 값과 순서는 uGUI의 <c>Selectable.SelectionState</c>와 같지만, 그 타입이 protected
    /// 중첩 enum이라(Selectable.cs:715) 공개 API나 테스트에서 쓸 수 없어 별도로 둔다.
    /// 순서가 같아도 캐스팅으로 변환하지 않는다 — 유니티가 순서를 바꾸면 조용히 틀린 상태를 그린다.
    /// </summary>
    public enum UIButtonState
    {
        Normal,
        Highlighted,
        Pressed,
        Selected,
        Disabled,
    }
}
