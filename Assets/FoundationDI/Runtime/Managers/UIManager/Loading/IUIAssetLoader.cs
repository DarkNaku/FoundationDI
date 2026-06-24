using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface IUIAssetLoader
    {
        GameObject Load(string key);
        void Release(string key);
    }
}
