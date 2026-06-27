# 03 Popup Modal

모달 팝업의 **입력 차단**과 **결과 반환**.

## 시연 내용
- **모달 입력 차단**: `ConfirmDialog`가 뜨면 하위 `ModalHost` Page의 입력이 죽는다(`CanvasGroup.interactable=false`). Dummy 버튼이 무반응이 되는 것으로 확인.
- **결과 반환**: 우리 시스템엔 결과 await API가 없으므로, Presenter의 `Confirmed` 프로퍼티를 `OnAfterHide` 콜백에서 읽는 패턴을 쓴다.
- **명시적 닫기**: 확인/취소 버튼이 `presenter.Hide()`를 호출한다. (우리 시스템엔 바깥클릭 dismiss가 없다)

## 핵심 코드
```csharp
[UIPrefab("ConfirmDialog")]
public class ConfirmDialog : UIPopupPresenter<ConfirmDialogView>
{
    public bool Confirmed { get; private set; }
    protected override void OnInitialize()
    {
        View.yesButton.onClick.AddListener(() => { Confirmed = true; Hide(); });
        View.noButton.onClick.AddListener(() => { Confirmed = false; Hide(); });
    }
}

// 결과 반환: OnAfterHide에서 Confirmed 읽기
var dialog = _ui.Popup<ConfirmDialog>();
dialog.OnAfterHide(_ => View.resultLabel.text = dialog.Confirmed ? "결과: 확인" : "결과: 취소");
```

## 실행
`PopupModal.unity`를 열고 Play. Ask Confirm → 다이얼로그 표시(이때 Dummy 버튼 비활성), 확인/취소 시 결과가 갱신된다.
