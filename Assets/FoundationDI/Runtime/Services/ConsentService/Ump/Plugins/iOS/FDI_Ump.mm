// FoundationDI Google UMP 네이티브 브리지.
//
// GoogleUserMessagingPlatform 포드가 없으면(Use UMP를 끈 프로젝트) 스텁으로 컴파일되고
// FDI_Ump_IsAvailable이 false를 돌려준다. C# 쪽은 그때 UMP 없이 진행한다.
#import <UIKit/UIKit.h>

#if __has_include(<UserMessagingPlatform/UserMessagingPlatform.h>)
#import <UserMessagingPlatform/UserMessagingPlatform.h>
#define FDI_UMP_AVAILABLE 1
#else
#define FDI_UMP_AVAILABLE 0
#endif

extern UIViewController* UnityGetGLViewController();

typedef void (*FDI_UmpCallback)(int requestId, bool success, const char* message);

static char* FDI_Ump_CopyString(NSString* value)
{
    if (value == nil) return NULL;

    const char* utf8 = [value UTF8String];
    char* copy = (char*)malloc(strlen(utf8) + 1);
    strcpy(copy, utf8);
    return copy;
}

extern "C"
{
    bool FDI_Ump_IsAvailable(void)
    {
        return FDI_UMP_AVAILABLE == 1;
    }

    // iOS 15+는 Inactive일 때 띄운 모달을 버린다(ATT와 같은 문제). C#이 Active를 기다린 뒤 폼을 띄운다.
    bool FDI_Ump_IsApplicationActive(void)
    {
        return UIApplication.sharedApplication.applicationState == UIApplicationStateActive;
    }

    // IABTCF_gdprApplies. 키가 없으면 -1.
    int FDI_Ump_GdprApplies(void)
    {
        id value = [[NSUserDefaults standardUserDefaults] objectForKey:@"IABTCF_gdprApplies"];
        if (value == nil || ![value respondsToSelector:@selector(intValue)]) return -1;
        return [value intValue];
    }

    // 호출자(IL2CPP 마샬러)가 free한다.
    char* FDI_Ump_PurposeConsents(void)
    {
        id value = [[NSUserDefaults standardUserDefaults] objectForKey:@"IABTCF_PurposeConsents"];
        if (![value isKindOfClass:[NSString class]]) return NULL;
        return FDI_Ump_CopyString((NSString*)value);
    }

#if FDI_UMP_AVAILABLE
    int FDI_Ump_ConsentStatus(void)
    {
        return (int)UMPConsentInformation.sharedInstance.consentStatus;
    }

    bool FDI_Ump_CanRequestAds(void)
    {
        return UMPConsentInformation.sharedInstance.canRequestAds;
    }

    bool FDI_Ump_IsPrivacyOptionsRequired(void)
    {
        return UMPConsentInformation.sharedInstance.privacyOptionsRequirementStatus ==
               UMPPrivacyOptionsRequirementStatusRequired;
    }

    void FDI_Ump_RequestUpdate(int requestId, int debugGeography, const char* testDeviceIdsCsv,
                               bool tagForUnderAgeOfConsent, FDI_UmpCallback callback)
    {
        UMPRequestParameters* parameters = [[UMPRequestParameters alloc] init];
        parameters.tagForUnderAgeOfConsent = tagForUnderAgeOfConsent;

        bool hasIds = testDeviceIdsCsv != NULL && strlen(testDeviceIdsCsv) > 0;

        if (debugGeography != 0 || hasIds)
        {
            UMPDebugSettings* debug = [[UMPDebugSettings alloc] init];
            debug.geography = (UMPDebugGeography)debugGeography;

            if (hasIds)
            {
                NSString* csv = [NSString stringWithUTF8String:testDeviceIdsCsv];
                debug.testDeviceIdentifiers = [csv componentsSeparatedByString:@","];
            }

            parameters.debugSettings = debug;
        }

        [UMPConsentInformation.sharedInstance
            requestConsentInfoUpdateWithParameters:parameters
                                 completionHandler:^(NSError* error) {
                                     dispatch_async(dispatch_get_main_queue(), ^{
                                         callback(requestId, error == nil,
                                                  error ? [error.localizedDescription UTF8String] : NULL);
                                     });
                                 }];
    }

    void FDI_Ump_LoadAndShowIfRequired(int requestId, FDI_UmpCallback callback)
    {
        [UMPConsentForm loadAndPresentIfRequiredFromViewController:UnityGetGLViewController()
                                                  completionHandler:^(NSError* error) {
                                                      dispatch_async(dispatch_get_main_queue(), ^{
                                                          callback(requestId, error == nil,
                                                                   error ? [error.localizedDescription UTF8String] : NULL);
                                                      });
                                                  }];
    }

    void FDI_Ump_ShowPrivacyOptions(int requestId, FDI_UmpCallback callback)
    {
        [UMPConsentForm presentPrivacyOptionsFormFromViewController:UnityGetGLViewController()
                                                   completionHandler:^(NSError* error) {
                                                       dispatch_async(dispatch_get_main_queue(), ^{
                                                           callback(requestId, error == nil,
                                                                    error ? [error.localizedDescription UTF8String] : NULL);
                                                       });
                                                   }];
    }
#else
    int FDI_Ump_ConsentStatus(void) { return 0; }
    bool FDI_Ump_CanRequestAds(void) { return true; }
    bool FDI_Ump_IsPrivacyOptionsRequired(void) { return false; }

    void FDI_Ump_RequestUpdate(int requestId, int debugGeography, const char* testDeviceIdsCsv,
                               bool tagForUnderAgeOfConsent, FDI_UmpCallback callback)
    {
        callback(requestId, false, "UMP SDK not linked");
    }

    void FDI_Ump_LoadAndShowIfRequired(int requestId, FDI_UmpCallback callback)
    {
        callback(requestId, false, "UMP SDK not linked");
    }

    void FDI_Ump_ShowPrivacyOptions(int requestId, FDI_UmpCallback callback)
    {
        callback(requestId, false, "UMP SDK not linked");
    }
#endif
}
