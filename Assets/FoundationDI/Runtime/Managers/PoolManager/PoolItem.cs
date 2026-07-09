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
        public GameObject GO
        {
            get
            {
                // MonoBehaviour가 파괴되지 않았을 때만 gameObject 반환
                return this != null ? gameObject : null;
            }
        }

        public PoolData PD { get; set; }

        // 실제 반환마다 증가한다. 지연 반환은 예약 시점의 세대를 캡처해 두고,
        // 발동 시 세대가 바뀌었으면(그 사이 반환/재사용됨) 중복 반환을 건너뛴다.
        private int _releaseGeneration;

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
                ReleaseAsync(delay, _releaseGeneration).Forget();
            }
            else
            {
                DoRelease();
            }
        }

        private void DoRelease()
        {
            // this는 UnityEngine.Object라 fake-null이 정상 감지된다.
            if (this == null || PD == null) return;

            // 반환 세대를 올려 대기 중인 지연 반환을 무효화한다.
            _releaseGeneration++;

            PD.Release(this);
        }

        private async UniTask ReleaseAsync(float delay, int generation)
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

            // 예약 이후 이미 반환/재사용됐다면(세대 불일치) 중복 반환하지 않는다.
            if (_releaseGeneration != generation) return;

            DoRelease();
        }
    }
}