using UnityEngine.Scripting;

// 코어(FoundationDI)는 이 옵셔널 어셈블리를 참조할 수 없다(순환 참조). 그래서 이 어셈블리는
// 참조 그래프상 어디서도 닿지 않는 섬이 되고, IL2CPP 링커는 닿지 않는 어셈블리를 통째로
// 걷어낸다. 그러면 IosSupportInstaller의 [RuntimeInitializeOnLoadMethod]가 실기 빌드에서
// 아예 실행되지 않아 레지스트리가 비고, ATT 동의를 묻지 않은 채 광고가 초기화된다 —
// 에디터에서는 재현되지 않고 IDFA가 빈 값으로 나가는 것으로만 드러나는 실패다.
//
// FoundationDILinkXmlGenerator가 만드는 link.xml과 목적이 겹치지만 일부러 둘 다 둔다.
[assembly: AlwaysLinkAssembly]
