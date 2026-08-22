using System;
using System.Reflection;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Purchasing.Security;
#endif

namespace DarkNaku.FoundationDI
{
    // 로컬 영수증 검증.
    //
    // 플랫폼별로 할 수 있는 일이 다르다.
    //  - Google Play: Tangle 데이터로 서명을 검증할 수 있다. 실제로 검증하는 유일한 경로다.
    //  - App Store: Unity IAP 5는 StoreKit 2를 쓴다. OS가 이미 서명을 검증한 뒤 넘겨주므로
    //    클라이언트가 다시 검증할 방법이 없고 필요도 없다. 더 강한 보증이 필요하면 서버 검증이다.
    //  - 에디터: 가짜 스토어의 영수증이라 검증할 대상이 없다.
    public sealed class CrossPlatformReceiptValidator : IReceiptValidator
    {
        private bool _warnedMissingTangle;

#if UNITY_ANDROID && !UNITY_EDITOR
        private CrossPlatformValidator _validator;
        private bool _initialized;
#endif

        public bool Validate(IapPurchase purchase, out IapError error)
        {
            error = default;

#if UNITY_ANDROID && !UNITY_EDITOR
            var validator = GetValidator();

            // Tangle이 없으면 검증할 열쇠가 없다. 여기서 막으면 Obfuscator를 돌리지 않은
            // 개발 빌드에서 모든 구매가 실패한다 — 경고만 남기고 통과시킨다.
            if (validator == null) return true;

            if (string.IsNullOrEmpty(purchase.Receipt))
            {
                error = new IapError(-2001, "영수증이 비어 있다");
                return false;
            }

            try
            {
                validator.Validate(purchase.Receipt);
                return true;
            }
            catch (IAPSecurityException e)
            {
                error = new IapError(-2002, $"영수증 검증에 실패했다: {e.Message}");
                return false;
            }
            catch (Exception e)
            {
                // 검증기 자체가 터진 경우다. 정품 구매를 막지 않도록 통과시키되 반드시 남긴다.
                Debug.LogError($"[IAPService] 영수증 검증기가 예외를 던졌다. 통과시킨다: {e}");
                return true;
            }
#else
            return true;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private CrossPlatformValidator GetValidator()
        {
            if (_initialized) return _validator;

            _initialized = true;

            var tangle = LoadGooglePlayTangle();

            if (tangle == null || tangle.Length == 0) return null;

            _validator = new CrossPlatformValidator(tangle, Application.identifier);
            return _validator;
        }
#endif

        // GooglePlayTangle은 IAP 에디터 도구(Receipt Validation Obfuscator)가
        // Assets/Plugins/UnityPurchasing/generated/에 만들어 주는 클래스다. 그 폴더는 asmdef가
        // 없어 Assembly-CSharp에 들어가므로 패키지 어셈블리에서 직접 참조할 수 없다 —
        // 그래서 리플렉션으로 찾는다. 없으면 아직 Obfuscator를 돌리지 않았다는 뜻이다.
        private byte[] LoadGooglePlayTangle()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("GooglePlayTangle", false);
                if (type == null) continue;

                var method = type.GetMethod("Data", BindingFlags.Public | BindingFlags.Static);
                if (method == null) continue;

                try
                {
                    return method.Invoke(null, null) as byte[];
                }
                catch (Exception e)
                {
                    Debug.LogError($"[IAPService] GooglePlayTangle.Data() 호출에 실패했다: {e}");
                    return null;
                }
            }

            if (!_warnedMissingTangle)
            {
                _warnedMissingTangle = true;
                Debug.LogWarning("[IAPService] GooglePlayTangle이 없어 영수증을 검증하지 않는다. " +
                                 "Services > In-App Purchasing > Receipt Validation Obfuscator를 실행할 것.");
            }

            return null;
        }
    }
}
