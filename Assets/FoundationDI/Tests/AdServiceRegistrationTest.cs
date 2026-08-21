using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using VContainer;

public class AdServiceRegistrationTest
{
    // 등록 그래프 전체를 검증하지는 않는다(그건 VContainer 몫). 여기서는 컨테이너를 실제로
    // 빌드해서 IAdService가 해석되는지, Dispose가 예외 없이 끝나는지만 확인하는 스모크 테스트다.
    [Test]
    public void RegisterAdService로_등록하면_IAdService를_해석할_수_있고_컨테이너_Dispose가_예외를_던지지_않는다()
    {
        var settings = ScriptableObject.CreateInstance<AdServiceSettings>();

        try
        {
            var builder = new ContainerBuilder();
            builder.RegisterAdService(settings);

            // 진짜 UnityAdDispatcher는 러너 GameObject를 만들며 DontDestroyOnLoad를 호출한다.
            // 플레이 모드 밖(EditMode 테스트)에서는 Unity가 이를 예외로 막는다. 여기서 검증하려는
            // 것은 그 MonoBehaviour 생명주기가 아니라 RegisterAdService의 등록 그래프 자체이므로,
            // 뒤에 다시 등록해서 덮어쓴다 — VContainer는 같은 인터페이스가 여러 번 등록되면
            // Resolve<T>() 단일 해석에서 나중 등록이 이긴다(Registry.Build의 buildBuffer 덮어쓰기).
            builder.Register<IAdDispatcher, FakeAdDispatcher>(Lifetime.Singleton);

            var container = builder.Build();

            var service = container.Resolve<IAdService>();

            Assert.IsNotNull(service);
            Assert.IsInstanceOf<AdService>(service);

            Assert.DoesNotThrow(() => container.Dispose());
        }
        finally
        {
            ScriptableObject.DestroyImmediate(settings);
        }
    }
}
