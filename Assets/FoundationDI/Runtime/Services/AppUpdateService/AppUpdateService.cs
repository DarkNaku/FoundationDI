using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class AppUpdateService : IAppUpdateService
    {
        private readonly Func<IAppUpdateStore> _storeFactory;

        private IAppUpdateStore _store;
        private bool _isBroken;

        public AppUpdateService(Func<IAppUpdateStore> storeFactory)
        {
            _storeFactory = storeFactory;
        }

        public async Awaitable<AppUpdateCheckResult> CheckAsync()
        {
            var store = Store;
            if (store == null) return AppUpdateCheckResult.Unknown();

            try
            {
                return await store.CheckAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AppUpdateService] 업데이트를 확인하지 못했다: {e.Message}");
                return AppUpdateCheckResult.Unknown();
            }
        }

        public bool OpenStore()
        {
            try
            {
                return Store?.OpenStore() ?? false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AppUpdateService] 스토어를 열지 못했다: {e.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            (_store as IDisposable)?.Dispose();
            _store = null;
        }

        private IAppUpdateStore Store
        {
            get
            {
                if (_store != null || _isBroken) return _store;

                try
                {
                    _store = _storeFactory();
                }
                catch (Exception e)
                {
                    _isBroken = true;
                    Debug.LogWarning($"[AppUpdateService] 스토어 창구를 만들지 못했다: {e.Message}");
                }

                return _store;
            }
        }
    }
}
