using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteStretcher : MonoBehaviour {
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool maintainAspectRatio = true;

    // 마지막으로 채운 화면(월드)의 크기. aspect가 아니라 보이는 영역 자체를 캐싱한다.
    // CameraFitter가 보드 핏을 위해 orthographicSize만 바꿔도(aspect 불변) 갱신되도록 한다.
    private float prevScreenWidth = 0f;
    private float prevScreenHeight = 0f;

    private void OnValidate() {
        spriteRenderer ??= GetComponent<SpriteRenderer>();
    }

    private void LateUpdate() {
        Stretch();
    }

    private void Stretch() {
        if (spriteRenderer == null) return;
        if (targetCamera == null) return;

        var sprite = spriteRenderer.sprite;
        if (sprite == null) return;

        // 카메라가 실제로 비추는 화면 영역의 월드 크기. orthographicSize가 매 프레임 바뀌어도
        // 항상 현재 화면을 기준으로 다시 계산하므로 dynamic ortho에서도 스크린에 가득 찬다.
        var screenHeight = targetCamera.orthographicSize * 2f;
        var screenWidth = screenHeight * targetCamera.aspect;

        // 보이는 영역이 그대로면 재계산하지 않는다(ortho/aspect 어느 쪽이 바뀌든 감지).
        if (Mathf.Approximately(screenWidth, prevScreenWidth) &&
            Mathf.Approximately(screenHeight, prevScreenHeight)) return;

        transform.localScale = Vector3.one;

        var pixelsPerUnit = sprite.pixelsPerUnit;
        var unitWidth = sprite.texture.width / pixelsPerUnit;
        var unitHeight = sprite.texture.height / pixelsPerUnit;
        var scaleX = screenWidth / unitWidth;
        var scaleY = screenHeight / unitHeight;

		switch (spriteRenderer.drawMode) {
            case SpriteDrawMode.Sliced:
            case SpriteDrawMode.Tiled:
                spriteRenderer.size = new Vector2(screenWidth, screenHeight);
                break;
            default:
                if (maintainAspectRatio) {
                    var scale = Mathf.Max(scaleX, scaleY);
                    transform.localScale = new Vector3(scale, scale, 1F);
                } else {
                    transform.localScale = new Vector3(scaleX, scaleY, 1F);
                }
                break;
        }

        prevScreenWidth = screenWidth;
        prevScreenHeight = screenHeight;
    }
}
