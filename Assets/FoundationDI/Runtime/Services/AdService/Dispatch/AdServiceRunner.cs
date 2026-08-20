using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 씬에 보이지 않는 펌프. 로직은 없다 — UnityAdDispatcher.Pump를 부르기만 한다.
    [DefaultExecutionOrder(-100)]
    public class AdServiceRunner : MonoBehaviour
    {
        private UnityAdDispatcher _dispatcher;

        public static AdServiceRunner Create(UnityAdDispatcher dispatcher)
        {
            var go = new GameObject("[AdService] Runner") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);

            var runner = go.AddComponent<AdServiceRunner>();
            runner._dispatcher = dispatcher;
            return runner;
        }

        // Time.unscaledDeltaTime을 쓴다. 전면광고 표시 중에는 게임이 timeScale=0으로
        // 멈춰 있는 경우가 많은데, 그때도 재시도 타이머는 흘러야 한다.
        private void Update() => _dispatcher?.Pump(Time.unscaledDeltaTime);

        public void Detach()
        {
            _dispatcher = null;
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }
}
