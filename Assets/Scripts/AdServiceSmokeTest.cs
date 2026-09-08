using DarkNaku.FoundationDI;
using UnityEngine;
using Reflex.Attributes;
using Reflex.Extensions;
using Reflex.Injectors;

// 스모크 확인용 임시 컴포넌트. 확인이 끝나면 지운다.
public class AdServiceSmokeTest : MonoBehaviour
{
    [Inject] private IAdService _ads;

    private async void Start()
    {
        // 이 오브젝트는 씬의 ContainerScope 계층 밖에 있을 수 있어
        // 자동 주입을 못 받을 수 있다(루트 스코프는 ReflexSettings가 DontDestroyOnLoad로
        // 자동 생성한다) — 그래서 [Inject]가 자동 실행되지 않는다. 임시 컴포넌트라
        // 프리팹의 Auto Inject Game Objects에 등록하지 않고 자가 주입으로 해결한다.
        AttributeInjector.Inject(this, gameObject.scene.GetSceneContainer());

        _ads.Paid += imp => Debug.Log(
            $"[Smoke] 임프레션: platform={imp.AdPlatform} source={imp.NetworkName} " +
            $"format={imp.Format} value={imp.Revenue:F4} {imp.Currency}");
        _ads.Loaded += f => Debug.Log($"[Smoke] 로드됨: {f}");
        _ads.Closed += f => Debug.Log($"[Smoke] 닫힘: {f}");

        var ok = await _ads.InitializeAsync();
        Debug.Log($"[Smoke] 초기화: {ok}");

        _ads.Banner.HeightChanged += h => Debug.Log($"[Smoke] 배너 높이: {h}");
        _ads.Banner.Show();
    }

    private void OnGUI()
    {
        if (GUI.Button(new Rect(20, 20, 260, 60), "전면 표시")) ShowInterstitial();
        if (GUI.Button(new Rect(20, 100, 260, 60), "보상 표시")) ShowRewarded();
        if (GUI.Button(new Rect(20, 180, 260, 60), $"광고제거: {_ads.AdsRemoved}"))
            _ads.AdsRemoved = !_ads.AdsRemoved;
    }

    private async void ShowInterstitial()
    {
        var result = await _ads.Interstitial.ShowAsync("smoke");
        Debug.Log($"[Smoke] 전면 결과: {result.Outcome}");
    }

    private async void ShowRewarded()
    {
        var result = await _ads.Rewarded.ShowAsync("smoke");
        Debug.Log($"[Smoke] 보상 결과: {result.Outcome} amount={result.Reward.Amount}");
    }
}
