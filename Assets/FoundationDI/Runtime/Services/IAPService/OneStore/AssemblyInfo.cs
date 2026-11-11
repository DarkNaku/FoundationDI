using System.Runtime.CompilerServices;
using UnityEngine.Scripting;

// FoundationDI.UnityIAP와 같은 이유다 — 게임이 IIAPService만 참조하면 이 어셈블리는 참조 그래프의 섬이 되어
// IL2CPP 링커가 걷어낸다.
[assembly: AlwaysLinkAssembly]

[assembly: InternalsVisibleTo("FoundationDI.OneStore.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
