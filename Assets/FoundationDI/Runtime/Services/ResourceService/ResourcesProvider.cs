using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// Resources.Load 기반 IResourceProvider 구현.
    /// </summary>
    /// <remarks>
    /// Resources는 Addressables 같은 핸들 해제가 없어 메모리 반환이 제한적이다.
    /// GameObject(프리팹) 등은 개별 언로드가 불가하며, 확실한 회수는
    /// Resources.UnloadUnusedAssets()(호출자 책임)로만 가능하다.
    /// </remarks>
    public class ResourcesProvider : IResourceProvider
    {
        private readonly Dictionary<string, Object> _assets = new();

        public async UniTask<T> LoadAsync<T>(string key) where T : Object
        {
            var request = Resources.LoadAsync<T>(key);
            await request.ToUniTask();
            var asset = request.asset as T;
            if (asset != null) _assets[key] = asset;
            return asset;
        }

        public T Load<T>(string key) where T : Object
        {
            var asset = Resources.Load<T>(key);
            if (asset != null) _assets[key] = asset;
            return asset;
        }

        public void Release(string key)
        {
            if (!_assets.TryGetValue(key, out var asset)) return;

            _assets.Remove(key);

            // Resources.UnloadAsset은 GameObject/Component에 사용할 수 없다.
            if (asset != null && asset is not GameObject && asset is not Component)
            {
                Resources.UnloadAsset(asset);
            }
        }
    }
}
