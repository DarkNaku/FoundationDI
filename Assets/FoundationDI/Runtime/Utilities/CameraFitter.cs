using UnityEngine;

/// <summary>
/// 대상 오브젝트가 놓인 평면. 화면 가로/세로에 어떤 월드 축을 매핑할지 결정한다.
/// XY = 정면(2D 기본), XZ = 탑다운(월드 X→화면 가로, 월드 Z→화면 세로).
/// </summary>
public enum CameraFitPlane
{
    XY,
    XZ,
}

/// <summary>
/// 대상(IWorldBoundsProvider)의 월드 경계 + 패딩이 카메라 화면에 가득 차도록 카메라의
/// orthographicSize를 매 프레임 조정한다. 화면 가로/세로 중 더 좁은 비율에 맞추는
/// uniform fit (contain) 패턴. 카메라 위치를 대상 중심으로 정렬하는 옵션 제공.
///
/// 대상이 놓인 평면(<see cref="CameraFitPlane.XY"/> 정면 / <see cref="CameraFitPlane.XZ"/> 탑다운)을
/// 인스펙터 드롭다운으로 선택할 수 있다. XZ는 카메라가 아래(-Y)를 내려다보고 up이 +Z인
/// 탑다운 구성을 전제한다.
///
/// 모듈화 의도: 어떤 객체든 IWorldBoundsProvider만 구현하면 동일하게 사용 가능.
/// </summary>
[DisallowMultipleComponent]
public sealed class CameraFitter : MonoBehaviour
{
    [Tooltip("비어 있으면 Camera.main을 사용한다.")]
    [SerializeField] private Camera _camera;

    [Tooltip("비어 있으면 이 컴포넌트가 붙어있는 transform을 사용한다. " +
             "해당 transform 또는 자식에 IWorldBoundsProvider 구현 컴포넌트가 있어야 한다.")]
    [SerializeField] private Transform _target;

    [Tooltip("대상이 놓인 평면. XY=정면(기본), XZ=탑다운(월드 X→화면 가로, 월드 Z→화면 세로).")]
    [SerializeField] private CameraFitPlane _plane = CameraFitPlane.XY;

    [Tooltip("월드 unit 단위 패딩. 선택한 평면의 (가로, 세로) 축에 각각 양쪽 절반씩 추가된다.")]
    [SerializeField] private Vector2 _padding = Vector2.zero;

    [Tooltip("true면 카메라 위치를 대상 bounds의 center로 매 프레임 정렬한다. " +
             "평면과 수직인 깊이축(XY→Z, XZ→Y)은 보존.")]
    [SerializeField] private bool _alignCameraToCenter = true;

    private IWorldBoundsProvider _boundsProvider;

    private void Awake()
    {
        Resolve();
    }

    private void Update()
    {
        Apply();
    }

    public void Refresh()
    {
        Apply();
    }

    private void Resolve()
    {
        if (_camera == null)
        {
            _camera = Camera.main;
        }
        if (_target == null)
        {
            _target = transform;
        }
        if (_target != null && _boundsProvider == null)
        {
            _boundsProvider = _target.GetComponent<IWorldBoundsProvider>();
        }
    }

    private void Apply()
    {
        if (_camera == null || _boundsProvider == null)
        {
            Resolve();
            if (_camera == null || _boundsProvider == null) return;
        }
        if (!_camera.orthographic) return;

        var bounds = _boundsProvider.WorldBounds;

        // 선택한 평면에 따라 화면 가로/세로에 매핑할 월드 축을 고른다.
        // XY: 화면 세로 = 월드 Y, XZ: 화면 세로 = 월드 Z. (가로는 두 경우 모두 월드 X)
        float worldWidth = bounds.size.x;
        float worldHeight = _plane == CameraFitPlane.XZ ? bounds.size.z : bounds.size.y;

        float paddedWidth = worldWidth + _padding.x * 2f;
        float paddedHeight = worldHeight + _padding.y * 2f;
        if (paddedWidth <= 0f || paddedHeight <= 0f) return;

        float halfWidth = paddedWidth * 0.5f;
        float halfHeight = paddedHeight * 0.5f;

        // uniform fit (contain): 가로 기준과 세로 기준 ortho 중 큰 값 → 양쪽 다 화면 안.
        float cameraAspect = _camera.aspect;
        float fitForHeight = halfHeight;
        float fitForWidth = cameraAspect > 0f ? halfWidth / cameraAspect : 0f;
        _camera.orthographicSize = Mathf.Max(fitForHeight, fitForWidth);

        if (_alignCameraToCenter)
        {
            var center = bounds.center;
            var camPos = _camera.transform.position;
            // 평면과 수직인 깊이축(카메라 거리)은 그대로 보존한다.
            _camera.transform.position = _plane == CameraFitPlane.XZ
                ? new Vector3(center.x, camPos.y, center.z)
                : new Vector3(center.x, center.y, camPos.z);
        }
    }
}
