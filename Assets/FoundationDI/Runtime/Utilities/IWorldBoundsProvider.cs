using UnityEngine;

/// <summary>
/// 자신의 월드 경계(중심 + 크기)를 제공하는 객체. CameraFitter 등 화면 fit 컴포넌트가
/// 대상에서 이 정보를 얻어 카메라 또는 스케일을 조정한다.
/// </summary>
public interface IWorldBoundsProvider
{
    Bounds WorldBounds { get; }
}
