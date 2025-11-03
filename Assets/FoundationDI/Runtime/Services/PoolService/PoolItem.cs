using System;
using UnityEngine;
using Cysharp.Threading.Tasks;

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
        gameObject.SetActive(false);
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
    
    private async UniTask ReleaseAsync(float delay) {
        await UniTask.Delay(TimeSpan.FromSeconds(delay));
            
        PD.Release(this);
    }
}
