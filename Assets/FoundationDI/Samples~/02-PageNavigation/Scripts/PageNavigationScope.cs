using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    public class PageNavigationScope : SampleLifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            builder.RegisterEntryPoint<PageNavigationDemo>();
        }
    }
}
