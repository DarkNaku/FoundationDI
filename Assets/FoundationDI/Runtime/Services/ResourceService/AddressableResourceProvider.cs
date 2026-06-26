using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    public class AddressableResourceProvider : IResourceProvider
    {
        private readonly Dictionary<string, AsyncOperationHandle> _handles = new();

        public async UniTask<T> LoadAsync<T>(string key) where T : Object
        {
            var handle = Addressables.LoadAssetAsync<T>(key);
            _handles[key] = handle;
            return await handle.ToUniTask();
        }

        public T Load<T>(string key) where T : Object
        {
            var handle = Addressables.LoadAssetAsync<T>(key);
            _handles[key] = handle;
            return handle.WaitForCompletion();
        }

        public void Release(string key)
        {
            if (!_handles.TryGetValue(key, out var handle)) return;

            _handles.Remove(key);

            if (handle.IsValid())
            {
                Addressables.Release(handle);
            }
        }
    }
}
