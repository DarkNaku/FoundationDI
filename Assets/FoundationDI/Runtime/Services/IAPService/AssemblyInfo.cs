using System.Runtime.CompilerServices;
using UnityEngine.Scripting;

// 코어(FoundationDI)는 이 옵셔널 어셈블리를 참조할 수 없다(순환 참조). 게임 코드가 IIAPService를
// 직접 쓰지 않는 빌드에서는 이 어셈블리가 참조 그래프상 어디서도 닿지 않는 섬이 되고, IL2CPP 링커는
// 닿지 않는 어셈블리를 통째로 걷어낸다.
//
// AlwaysLinkAssembly는 UnityLinker의 ResolveAssemblyDirectoryStep이 "참조가 없어도 버리지
// 않는다"로 처리한다. Unity IAP도 같은 이유로 Unity.Purchasing.Stores에 이 속성을 달아 둔다.
//
// FoundationDILinkXmlGenerator가 만드는 link.xml과 목적이 겹치지만 일부러 둘 다 둔다.
// 이 속성은 어댑터 폴더를 들어내면 함께 사라지고, 링커가 생성 link.xml을 못 받는 상황
// (예: 커스텀 빌드 파이프라인)에서도 어댑터 자신은 살아남는다.
[assembly: AlwaysLinkAssembly]

[assembly: InternalsVisibleTo("FoundationDI.UnityIAP.Editor")]
[assembly: InternalsVisibleTo("FoundationDI.UnityIAP.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
