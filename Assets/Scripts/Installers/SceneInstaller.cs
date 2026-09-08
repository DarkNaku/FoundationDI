using DarkNaku.FoundationDI;
using Reflex.Core;
using UnityEngine;

/// <summary>
/// 씬 수명 컴포넌트를 등록한다. UINavigator는 씬 컨테이너가 소유하므로
/// 씬이 언로드되면 캔버스·풀·프리젠터가 함께 파괴된다.
/// IResourceService 등 앱 수명 서비스는 부모(루트) 컨테이너에서 해결된다.
/// </summary>
public class SceneInstaller : MonoBehaviour, IInstaller
{
    // 인스펙터에서 Assets/Settings/UINavigatorSettings.asset 을 연결한다.
    [SerializeField] private UINavigatorSettings _uiNavigatorSettings;

    public void InstallBindings(ContainerBuilder builder)
    {
        builder.RegisterUINavigator(_uiNavigatorSettings);

        // 루트에서 내려왔다. 씬 수명이 원래 자리다.
        builder.RegisterTutorialManager();
    }
}
