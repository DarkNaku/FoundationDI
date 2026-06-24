using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DarkNaku.FoundationDI
{
    public sealed class AddressablesUILoader : IUIAssetLoader
    {
        private readonly Dictionary<string, AsyncOperationHandle<GameObject>> _handles = new();

        public GameObject Load(string key)
        {
            if (_handles.TryGetValue(key, out var existing) && existing.IsValid())
                return existing.Result;

            var handle = Addressables.LoadAssetAsync<GameObject>(key);
            var prefab = handle.WaitForCompletion();
            _handles[key] = handle;
            return prefab;
        }

        public void Release(string key)
        {
            if (_handles.TryGetValue(key, out var handle))
            {
                if (handle.IsValid()) Addressables.Release(handle);
                _handles.Remove(key);
            }
        }
    }
}
