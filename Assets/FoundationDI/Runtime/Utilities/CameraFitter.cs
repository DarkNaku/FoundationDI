using UnityEngine;

/// <summary>
/// CameraFitter가 언제 fit을 다시 적용할지 결정한다.
/// EveryFrame = 매 프레임, ResolutionChange = 화면 해상도/방향이 바뀔 때만(+수동 Refresh),
/// Manual = Refresh() 호출 시에만.
/// </summary>
public enum CameraFitRefreshMode
{
    EveryFrame,
    ResolutionChange,
    Manual,
}

/// <summary>
/// 패딩 값을 해석하는 단위.
/// WorldUnits = 월드 unit(해상도와 무관, 고정 물리 크기),
/// Pixels = 현재 렌더 해상도(Game 뷰/기기 화면)의 픽셀. 해상도에 종속된다.
/// </summary>
public enum CameraFitPaddingUnit
{
    WorldUnits,
    Pixels,
}

/// <summary>
/// 대상(IWorldBoundsProvider)의 월드 경계 + 패딩이 카메라 화면에 가득 차도록 카메라의
/// orthographicSize를 조정하고, 대상 중심을 화면 중앙에 맞춘다. 화면 가로/세로 중 더 좁은
/// 비율에 맞추는 uniform fit (contain) 패턴.
///
/// 화면 가로/세로 축을 고정 월드 축이 아니라 <b>카메라의 실제 right/up 방향</b>으로 계산하므로
/// 카메라 회전(탑다운/정면/임의 yaw 등)을 자동으로 반영한다. 별도의 평면 선택이 필요 없다.
/// (orthographic 카메라 전용.)
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

    [Header("패딩 — 보드와 화면 가장자리 사이 여백")]
    [Tooltip("패딩 단위. WorldUnits=월드 unit(해상도 무관), Pixels=현재 렌더 해상도의 픽셀(해상도 종속).")]
    [SerializeField] private CameraFitPaddingUnit _paddingUnit = CameraFitPaddingUnit.WorldUnits;

    [Tooltip("왼쪽(-X, 화면 왼쪽) 여백. 단위는 Padding Unit을 따른다.")]
    [SerializeField] private float _paddingLeft = 0f;

    [Tooltip("오른쪽(+X, 화면 오른쪽) 여백. 단위는 Padding Unit을 따른다.")]
    [SerializeField] private float _paddingRight = 0f;

    [Tooltip("위쪽(화면 위, 카메라 up 방향) 여백. 단위는 Padding Unit을 따른다.")]
    [SerializeField] private float _paddingTop = 0f;

    [Tooltip("아래쪽(화면 아래, 카메라 -up 방향) 여백. 단위는 Padding Unit을 따른다.")]
    [SerializeField] private float _paddingBottom = 0f;

    [Tooltip("true면 대상 bounds의 center가 화면 중앙에 오도록 카메라 위치를 정렬한다. " +
             "카메라가 바라보는 깊이(forward 방향 거리)는 보존.")]
    [SerializeField] private bool _alignCameraToCenter = true;

    [Tooltip("fit 재적용 시점. ResolutionChange=화면 크기/방향 변경 시에만(+수동 Refresh), " +
             "EveryFrame=매 프레임, Manual=Refresh() 호출 시에만.")]
    [SerializeField] private CameraFitRefreshMode _refreshMode = CameraFitRefreshMode.EveryFrame;

    private IWorldBoundsProvider _boundsProvider;
    private Vector2Int _lastScreenSize = new Vector2Int(-1, -1);

    private void Awake()
    {
        Resolve();
    }

    private void Update()
    {
        switch (_refreshMode)
        {
            case CameraFitRefreshMode.EveryFrame:
                Apply();
                break;
            case CameraFitRefreshMode.ResolutionChange:
                var screenSize = new Vector2Int(Screen.width, Screen.height);
                if (screenSize != _lastScreenSize)
                {
                    _lastScreenSize = screenSize;
                    Apply();
                }
                break;
            case CameraFitRefreshMode.Manual:
                break;
        }
    }

    public void Refresh()
    {
        _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        Apply();
    }

#if UNITY_EDITOR
    // 인스펙터에서 패딩 등 값을 바꾸면 에디터에서 즉시 다시 fit한다(빌드에는 포함 안 됨).
    // OnValidate 도중 카메라/트랜스폼을 직접 수정하면 경고가 날 수 있어 delayCall로 한 프레임 미룬다.
    private void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += RefreshFromValidate;
    }

    private void RefreshFromValidate()
    {
        if (this == null) return; // delayCall 사이에 컴포넌트가 파괴되었을 수 있음

        Resolve();
        if (_camera == null || _boundsProvider == null || !_camera.orthographic) return;

        // 맞출 보드가 아직 없으면(경계 크기 0, 예: 레벨 미로드 편집 모드) 카메라를 건드리지 않는다.
        if (_boundsProvider.WorldBounds.size.sqrMagnitude <= Mathf.Epsilon) return;

        Apply();
    }
#endif

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
        Vector3 extents = bounds.extents; // 월드 AABB 반-크기

        // 카메라 회전을 반영: 화면 가로=camera.right, 세로=camera.up 방향으로 보드 AABB를 투영해
        // 각 화면 축 기준 반-크기를 구한다. (AABB를 단위축 d에 투영한 반-크기 = Σ|d.k|*extents.k)
        Transform camTransform = _camera.transform;
        Vector3 right = camTransform.right;
        Vector3 up = camTransform.up;
        Vector3 forward = camTransform.forward;

        float halfWidth = Mathf.Abs(right.x) * extents.x + Mathf.Abs(right.y) * extents.y + Mathf.Abs(right.z) * extents.z;
        float halfHeight = Mathf.Abs(up.x) * extents.x + Mathf.Abs(up.y) * extents.y + Mathf.Abs(up.z) * extents.z;

        float orthographicSize;
        float offsetRight;  // 비대칭 패딩에 의한 카메라 이동(화면 오른쪽=+right 방향, 월드 스케일)
        float offsetUp;     // 비대칭 패딩에 의한 카메라 이동(화면 위=+up 방향, 월드 스케일)

        if (_paddingUnit == CameraFitPaddingUnit.Pixels)
        {
            // 픽셀 패딩: 보드가 "화면 - 패딩 픽셀"의 하위 영역에 들어가도록 world/pixel 배율(k)을 직접 구한다.
            // 패딩의 월드 크기가 최종 orthographicSize에 의존하는 순환 참조를 피할 수 있다.
            float pxW = _camera.pixelWidth;
            float pxH = _camera.pixelHeight;

            float availW = pxW - (_paddingLeft + _paddingRight);
            float availH = pxH - (_paddingTop + _paddingBottom);
            if (availW <= 0f || availH <= 0f || pxH <= 0f) return; // 패딩이 화면보다 큼

            // uniform fit (contain): 두 축 모두 영역 안에 들어가는 최소 배율.
            float worldPerPixel = Mathf.Max((halfWidth * 2f) / availW, (halfHeight * 2f) / availH);
            orthographicSize = worldPerPixel * pxH * 0.5f;

            offsetRight = (_paddingRight - _paddingLeft) * 0.5f * worldPerPixel;
            offsetUp = (_paddingTop - _paddingBottom) * 0.5f * worldPerPixel;
        }
        else
        {
            // 월드 unit 패딩: 좌/우, 상/하 패딩을 각 화면 축 반-크기에 합산.
            float paddedHalfWidth = halfWidth + (_paddingLeft + _paddingRight) * 0.5f;
            float paddedHalfHeight = halfHeight + (_paddingTop + _paddingBottom) * 0.5f;
            if (paddedHalfWidth <= 0f || paddedHalfHeight <= 0f) return;

            // uniform fit (contain): 가로 기준과 세로 기준 ortho 중 큰 값 → 양쪽 다 화면 안.
            float cameraAspect = _camera.aspect;
            float fitForHeight = paddedHalfHeight;
            float fitForWidth = cameraAspect > 0f ? paddedHalfWidth / cameraAspect : 0f;
            orthographicSize = Mathf.Max(fitForHeight, fitForWidth);

            offsetRight = (_paddingRight - _paddingLeft) * 0.5f;
            offsetUp = (_paddingTop - _paddingBottom) * 0.5f;
        }

        _camera.orthographicSize = orthographicSize;

        if (_alignCameraToCenter)
        {
            // 보드 중심(+비대칭 패딩 이동)이 화면 중앙에 오도록 카메라를 이동한다.
            // 카메라가 바라보는 깊이(forward 방향 거리)는 그대로 보존해 회전/거리를 유지한다.
            Vector3 camPos = camTransform.position;
            float forwardDist = Vector3.Dot(camPos - bounds.center, forward);
            camTransform.position = bounds.center + right * offsetRight + up * offsetUp + forward * forwardDist;
        }
    }
}
