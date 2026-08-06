using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    public abstract class InitializeItem : ScriptableObject
    {
        public abstract Awaitable InitializeAsync(IObjectResolver resolver);
    }
}
