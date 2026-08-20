using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    public class OverlayScope : SampleLifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            builder.RegisterEntryPoint<OverlayDemo>();
        }
    }
}
