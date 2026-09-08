using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public abstract class InitializeItem : ScriptableObject
    {
        public abstract Awaitable InitializeAsync(IServiceResolver resolver);
    }
}
