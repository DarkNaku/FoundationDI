using DarkNaku.FoundationDI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

// 스모크 확인용 임시 컴포넌트. 확인이 끝나면 지운다.
public class IapServiceSmokeTest : MonoBehaviour
{
    [Inject] private IIapService _iap;

    private string _lastResult = "-";

    private async void Start()
    {
        // AdServiceSmokeTest와 같은 이유로 자가 주입한다 — 이 오브젝트는 스코프 계층에 속하지 않는다.
        LifetimeScope.Find<RootLifetimeScope>().Container.Inject(this);

        _iap.Purchased += p => Debug.Log(
            $"[Smoke] 구매 확정: {p.ProductId} {p.Price:F2} {p.CurrencyCode} restored={p.IsRestored}");
        _iap.OwnedChanged += id => Debug.Log($"[Smoke] 소유 변경: {id}");

        var ok = await _iap.InitializeAsync();
        Debug.Log($"[Smoke] IAP 초기화: {ok}");

        foreach (var product in _iap.Products)
        {
            Debug.Log($"[Smoke] 상품: {product.Id} / {product.Title} / {product.LocalizedPrice}");
        }
    }

    private void OnGUI()
    {
        if (_iap == null) return;

        var y = 20f;

        foreach (var product in _iap.Products)
        {
            var owned = _iap.IsOwned(product.Id) ? " (보유)" : string.Empty;

            if (GUI.Button(new Rect(320, y, 300, 60), $"{product.Id} {product.LocalizedPrice}{owned}"))
            {
                Buy(product.Id);
            }

            y += 70f;
        }

        if (GUI.Button(new Rect(320, y, 300, 60), "구매 복원")) Restore();

        GUI.Label(new Rect(320, y + 70, 300, 30), $"최근 결과: {_lastResult}");
    }

    private async void Buy(string productId)
    {
        var result = await _iap.PurchaseAsync(productId);
        _lastResult = $"{productId} → {result.Outcome}";
        Debug.Log($"[Smoke] 구매 결과: {_lastResult} {result.Error}");
    }

    private async void Restore()
    {
        var result = await _iap.RestoreAsync();
        _lastResult = $"복원 → success={result.Success} count={result.RestoredCount}";
        Debug.Log($"[Smoke] {_lastResult}");
    }
}
