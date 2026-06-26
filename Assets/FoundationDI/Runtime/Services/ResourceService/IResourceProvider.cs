using Cysharp.Threading.Tasks;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    public interface IResourceProvider
    {
        UniTask<T> LoadAsync<T>(string key) where T : Object;
        T Load<T>(string key) where T : Object;
        void Release(string key);
    }
}
