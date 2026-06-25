using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    internal sealed class PopupController
    {
        private readonly List<UIPresenterBase> _stack = new();
        public IReadOnlyList<UIPresenterBase> All => _stack;
        public UIPresenterBase Top => _stack.Count > 0 ? _stack[^1] : null;
        public void Push(UIPresenterBase popup) => _stack.Add(popup);
        public void Remove(UIPresenterBase popup) => _stack.Remove(popup);
        public void Clear() => _stack.Clear();
    }
}
