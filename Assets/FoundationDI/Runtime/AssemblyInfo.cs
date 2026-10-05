using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FoundationDI.Editor")]
[assembly: InternalsVisibleTo("FoundationDI.Tests")]
[assembly: InternalsVisibleTo("FoundationDI.Tests.Editor")]
[assembly: InternalsVisibleTo("FoundationDI.Tests.Runtime")]
[assembly: InternalsVisibleTo("FoundationDI.UnityIAP.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

// 임시. 옛 IAPProviderFactory가 ProviderDiagnostics를 쓴다. provider 계층을 지울 때 함께 지운다.
[assembly: InternalsVisibleTo("FoundationDI.UnityIAP")]
