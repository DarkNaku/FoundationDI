# 01 Basic Usage

`Page` / `Popup` / `Overlay` 세 가지 표시 모드의 기본 사용법.

## 시연 내용
- `Page<T>()` / `Popup<T>()` / `Overlay<T>()` 호출 즉시 표시(자동-show, `.Show()` 불필요).
- Popup 스택: `SettingsPopup` 위에 `ConfirmPopup`(서로 다른 타입)을 쌓아 LIFO 시연. 같은 타입을 다시 호출하면 중복 무시(경고).
- 모달 입력 차단: Popup이 뜨면 하위 Page의 `CanvasGroup.interactable`이 false가 된다.

## 핵심 코드
```csharp
[UIPrefab("MainMenu")]
public class MainMenuPage : UIPagePresenter<MainMenuView>
{
    [Inject] private IUIManager _ui;

    protected override void OnInitialize()
    {
        View.openPopupButton.onClick.AddListener(() => _ui.Popup<SettingsPopup>());
        View.openOverlayButton.onClick.AddListener(() => _ui.Overlay<HudOverlay>());
    }
}

// 부트스트랩
public class BasicUsageDemo : IStartable
{
    private readonly IUIManager _ui;
    public BasicUsageDemo(IUIManager ui) => _ui = ui;
    public void Start() => _ui.Page<MainMenuPage>();
}
```

## 실행
`BasicUsage.unity`를 열고 Play. MainMenu가 표시되고 버튼으로 Popup/Overlay를 띄울 수 있다.
