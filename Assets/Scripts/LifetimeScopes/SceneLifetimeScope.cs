using DarkNaku.FoundationDI;
using FoundationDI.Host;   // TestHubBootstrap 이 이 네임스페이스에 있다 (TestHubPresenters.cs:6)
using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// 씬 수명 컴포넌트를 등록하는 스코프. UINavigator는 이 스코프가 소유하므로
/// 씬이 언로드되면 캔버스·풀·프리젠터가 함께 파괴된다.
/// IResourceService 등 앱 수명 서비스는 부모(RootLifetimeScope)에서 해결된다.
/// </summary>
public class SceneLifetimeScope : LifetimeScope
{
    // 인스펙터에서 Assets/Settings/UINavigatorSettings.asset 을 연결한다.
    [SerializeField] private UINavigatorSettings _uiNavigatorSettings;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterUINavigator(_uiNavigatorSettings);
        builder.RegisterEntryPoint<TestHubBootstrap>();
    }
}
