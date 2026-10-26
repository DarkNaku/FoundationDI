using System;
using System.Reflection;

namespace DarkNaku.FoundationDI
{
    // GooglePlayTangle은 IAP 에디터 도구(Receipt Validation Obfuscator)가 만들어 주는 클래스다.
    // 생성 폴더는 asmdef가 없어 Assembly-CSharp에 들어가므로 패키지 어셈블리에서 직접 참조할 수
    // 없다 — 그래서 이름으로 찾는다.
    //
    // Unity IAP 5(5.4.4 확인)는 Assets/Scripts/UnityPurchasing/generated/에
    // UnityEngine.Purchasing.Security.GooglePlayTangle을 만든다. 예전 버전은 전역 이름이었으므로
    // 둘 다 본다. 전역 이름만 찾으면 Obfuscator를 돌렸는데도 검증 없이 통과했다.
    internal static class GooglePlayTangleLocator
    {
        // 찾는 순서다. 앞의 것이 있으면 뒤는 보지 않는다.
        internal static readonly string[] TypeNames =
        {
            "UnityEngine.Purchasing.Security.GooglePlayTangle",
            "GooglePlayTangle"
        };

        public static Type Find() => Find(FindInLoadedAssemblies);

        // resolve는 타입 전체 이름으로 타입을 찾아 준다(없으면 null). 테스트가 대체한다.
        internal static Type Find(Func<string, Type> resolve)
        {
            foreach (var name in TypeNames)
            {
                var type = resolve(name);
                if (type != null) return type;
            }

            return null;
        }

        internal static Type FindInLoadedAssemblies(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }

            return null;
        }

        // 생성 클래스의 정적 Data()가 Tangle 바이트를 돌려준다.
        internal static MethodInfo GetDataMethod(Type type)
        {
            return type?.GetMethod("Data", BindingFlags.Public | BindingFlags.Static);
        }
    }
}
