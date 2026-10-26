using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class UnityApplicationFocusSource : IApplicationFocusSource
    {
        public event Action<bool> FocusChanged
        {
            add => Application.focusChanged += value;
            remove => Application.focusChanged -= value;
        }
    }
}
