using UnityEngine.Scripting;

// 코어(FoundationDI)는 이 옵셔널 어셈블리를 참조할 수 없다(순환 참조). 그래서 이 어셈블리는
// 참조 그래프상 어디서도 닿지 않는 섬이 되고, IL2CPP 링커는 닿지 않는 어셈블리를 통째로
// 걷어낸다. 그러면 Installer의 [RuntimeInitializeOnLoadMethod]가 실기 빌드에서 아예 실행되지
// 않아 레지스트리가 비고, 서비스가 조용히 Dummy provider로 떨어진다.
//
// AlwaysLinkAssembly는 UnityLinker의 ResolveAssemblyDirectoryStep이 "참조가 없어도 버리지
// 않는다"로 처리한다. Unity IAP도 같은 이유로 Unity.Purchasing.Stores에 이 속성을 달아 둔다.
//
// FoundationDILinkXmlGenerator가 만드는 link.xml과 목적이 겹치지만 일부러 둘 다 둔다.
// 이 속성은 어댑터 폴더를 들어내면 함께 사라지고, 링커가 생성 link.xml을 못 받는 상황
// (예: 커스텀 빌드 파이프라인)에서도 어댑터 자신은 살아남는다.
[assembly: AlwaysLinkAssembly]
