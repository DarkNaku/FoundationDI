using System.IO;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class ResourcesUILoader : IUIAssetLoader
    {
        public GameObject Load(string key)
        {
            var prefab = Resources.Load<GameObject>(key);
            if (prefab == null)
                throw new FileNotFoundException($"[UIManager] Resources에서 prefab을 찾을 수 없습니다: {key}");
            return prefab;
        }

        public void Release(string key) { /* Resources 시스템이 수명 관리 */ }
    }
}
