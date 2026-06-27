# 02 Page Navigation

다단계 Page 전환 + 파라미터 전달 + 라이프사이클 콜백.

## 시연 내용
- **Page 교체**: Title → CharacterList → CharacterDetail. 새 Page를 표시하면 이전 Page는 자동으로 Hide된다.
- **파라미터 전달**: `IConfigurable<CharacterDetailParams>`를 구현하고 `.With(params)`로 CharacterId 주입.
- **라이프사이클 콜백 4종**: `OnBeforeShow`/`OnAfterShow`/`OnBeforeHide`/`OnAfterHide`를 빌더로 구독해 Console에 로그.

## 핵심 코드
```csharp
_ui.Page<CharacterDetailPage>()
   .With(new CharacterDetailParams { CharacterId = id })
   .OnBeforeShow(p => Debug.Log("[Lifecycle] OnBeforeShow"))
   .OnAfterShow(p => Debug.Log("[Lifecycle] OnAfterShow"));

[UIPrefab("CharacterDetail")]
public class CharacterDetailPage : UIPagePresenter<CharacterDetailView>, IConfigurable<CharacterDetailParams>
{
    private CharacterDetailParams _params;
    public void Configure(CharacterDetailParams p) => _params = p;
    protected override void OnBeforeShow() => View.idLabel.text = $"Character #{_params.CharacterId}";
}
```

## 실행
`PageNavigation.unity`를 열고 Play. Title → 캐릭터 선택 → Detail로 전환되며 Console에 라이프사이클 로그가 출력된다.
