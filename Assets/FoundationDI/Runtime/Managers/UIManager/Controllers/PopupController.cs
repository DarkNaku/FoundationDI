using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    internal sealed class PopupController
    {
        private readonly List<UIPresenter> _stack = new();
        public IReadOnlyList<UIPresenter> All => _stack;
        public UIPresenter Current => _stack.Count > 0 ? _stack[^1] : null;
        public void Add(UIPresenter popup) => _stack.Add(popup);
        public void Remove(UIPresenter popup) => _stack.Remove(popup);
        public void Clear() => _stack.Clear();
    }
}
