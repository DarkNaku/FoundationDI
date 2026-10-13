// FoundationDI ATT 요청 시점 판정용 네이티브 헬퍼.
//
// AppTrackingTransparency는 import하지 않는다. 요청과 상태 조회는 ios-support 바인딩
// (ATTrackingStatusBinding)이 맡고, 여기서는 "지금 요청해도 iOS가 받아 주는가"만 답한다.
// iOS 15부터 requestTrackingAuthorization은 UIApplicationStateActive일 때만 팝업을 띄우고,
// 그 외에는 팝업도 오류도 없이 notDetermined로 끝난다.
#import <UIKit/UIKit.h>

static int _resignActiveCount = 0;
static id _applicationObserver = nil;
static id _sceneObserver = nil;

// 시스템 팝업이 뜨면 앱이 Inactive로 내려간다. "요청 뒤 한 번이라도 내려갔는가"를 보면
// 팝업이 실제로 떴는지 알 수 있다. OnApplicationFocus는 일부 기기에서 누락 보고가 있어 쓰지 않는다.
//
// UIScene 라이프사이클에서도 UIApplication 알림은 오지만, 장면 알림도 함께 센다. 판정은
// "값이 바뀌었는가"뿐이라 한 번의 전이에 두 번 세어도 상관없다.
static void FDI_Att_EnsureObservers(void)
{
    if (_applicationObserver != nil) return;

    NSNotificationCenter* center = [NSNotificationCenter defaultCenter];

    _applicationObserver = [center addObserverForName:UIApplicationWillResignActiveNotification
                                               object:nil
                                                queue:nil
                                           usingBlock:^(__unused NSNotification* note) { _resignActiveCount++; }];

    if (@available(iOS 13.0, *))
    {
        _sceneObserver = [center addObserverForName:UISceneWillDeactivateNotification
                                             object:nil
                                              queue:nil
                                         usingBlock:^(__unused NSNotification* note) { _resignActiveCount++; }];
    }
}

// 첫 호출보다 앞선 전이도 세도록 이미지 로드 시점에 관찰을 시작한다.
@interface FDI_AttActivityObserver : NSObject
@end

@implementation FDI_AttActivityObserver
+ (void)load
{
    FDI_Att_EnsureObservers();
}
@end

extern "C"
{
    bool FDI_Att_IsApplicationActive(void)
    {
        return UIApplication.sharedApplication.applicationState == UIApplicationStateActive;
    }

    int FDI_Att_ResignActiveCount(void)
    {
        FDI_Att_EnsureObservers();
        return _resignActiveCount;
    }
}
