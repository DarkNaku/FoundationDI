using System;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace DarkNaku.FoundationDI
{
    public interface IPoolItem
    {
        GameObject GO { get; }
        PoolData PD { get; set; }

        void OnCreateItem();
        void OnGetItem();
        void OnReleaseItem();
        void OnDestroyItem();
        void Release(float delay);
    }

    public class PoolItem : MonoBehaviour, IPoolItem
    {
        public GameObject GO => gameObject;

        public PoolData PD { get; set; }

        public virtual void OnCreateItem()
        {
        }

        public virtual void OnGetItem()
        {
            gameObject.SetActive(true);
        }

        public virtual void OnReleaseItem()
        {
            // this가 파괴되지 않았는지 먼저 확인
            if (this == null) return;

            // GameObject가 파괴되었을 수 있으니 체크
            if (gameObject != null)
            {
                gameObject.SetActive(false);
            }
        }

        public virtual void OnDestroyItem()
        {
        }

        public void Release(float delay = 0f)
        {
            if (delay > 0f)
            {
                ReleaseAsync(delay).Forget();
            }
            else
            {
                PD?.Release(this);
            }
        }

        private async UniTask ReleaseAsync(float delay)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: this.GetCancellationTokenOnDestroy());
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // this와 PD가 모두 존재하는지 확인
            if (this == null || PD == null) return;

            PD.Release(this);
        }
    }
}