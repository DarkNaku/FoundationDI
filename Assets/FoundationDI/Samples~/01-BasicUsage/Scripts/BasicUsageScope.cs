using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    public class BasicUsageScope : SampleLifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            builder.RegisterEntryPoint<BasicUsageDemo>();
        }
    }
}
