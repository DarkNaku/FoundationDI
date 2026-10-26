using UnityEngine.Scripting;

// 코어(FoundationDI)는 이 옵셔널 어셈블리를 참조할 수 없어(순환 참조) IL2CPP 링커가 통째로 걷어낸다.
// 그러면 MobileNotificationsInstaller의 [RuntimeInitializeOnLoadMethod]가 실기에서 돌지 않아
// 알림이 조용히 UnsupportedLocalNotifier로 떨어진다. FoundationDILinkXmlGenerator와 목적이 겹치지만
// 일부러 둘 다 둔다(iOSSupport와 같은 이유).
[assembly: AlwaysLinkAssembly]
