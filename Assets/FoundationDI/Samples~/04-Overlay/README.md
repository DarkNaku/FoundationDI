# 04 Overlay

Above/Below 오버레이 + HUD 데이터 갱신 + 모달과의 관계.

## 시연 내용
- **Above/Below**(Popup 기준): `HudAboveOverlay`는 Popup **위**, `BackgroundBelowOverlay`(`Above => false`)는 Popup **아래**(배경).
- **HUD 데이터 갱신**: `HudAboveOverlay.AddScore()`를 호출해 점수 라벨 갱신(Overlay는 상주하며 재사용).
- **모달과의 관계**: Popup을 띄우면 BelowOverlay는 입력 차단되지만 AboveOverlay(HUD)는 입력 유지된다.

## 핵심 코드
```csharp
[UIPrefab("HudAbove")]
public class HudAboveOverlay : UIOverlayPresenter<HudOverlayView>   // Above(기본)
{
    private int _score;
    public void AddScore(int amount) { _score += amount; View.scoreLabel.text = $"Score: {_score}"; }
}

[UIPrefab("BackgroundBelow")]
public class BackgroundBelowOverlay : UIOverlayPresenter<BackgroundOverlayView>
{
    protected override bool Above => false;   // Below: Popup 아래
}

// 트랜지션은 UIManagerSettings의 DefaultOverlayTransition(Fade)을 사용.
// per-show 오버라이드가 필요하면 .WithTransition(asset) 체인.
```

## 실행
`Overlay.unity`를 열고 Play. 상단 HUD(Above)와 배경(Below)이 상주하고, Add Score로 점수가 갱신되며, Open Popup 시 배경은 차단·HUD는 유지된다.
