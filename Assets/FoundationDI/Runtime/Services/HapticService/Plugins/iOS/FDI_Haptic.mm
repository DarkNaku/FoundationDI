// FoundationDI iOS 햅틱 네이티브 브리지 (clean-room, 공개 API만 사용).
// enum 정렬: HapticImpact{Light=0,Medium=1,Heavy=2,Soft=3,Rigid=4}==UIImpactFeedbackStyle
//           HapticNotification{Success=0,Warning=1,Error=2}==UINotificationFeedbackType
#import <UIKit/UIKit.h>
#import <math.h>
#if __has_feature(modules)
@import CoreHaptics;
#else
#import <CoreHaptics/CoreHaptics.h>
#endif

static UISelectionFeedbackGenerator *gSelection = nil;
static UINotificationFeedbackGenerator *gNotif = nil;
static UIImpactFeedbackGenerator *gImpact[5] = { nil, nil, nil, nil, nil };

static CHHapticEngine *gEngine = nil;
static id<CHHapticPatternPlayer> gCurvePlayer = nil;

static float FDI_Clamp(float v, float lo, float hi) {
    if (!isfinite(v)) return lo;
    return v < lo ? lo : (v > hi ? hi : v);
}

static void FDI_EnsureGenerators(void) {
    if (@available(iOS 10.0, *)) {
        if (gSelection) return;
        gSelection = [UISelectionFeedbackGenerator new];
        gNotif = [UINotificationFeedbackGenerator new];
        gImpact[0] = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
        gImpact[1] = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
        gImpact[2] = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
        if (@available(iOS 13.0, *)) {
            gImpact[3] = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleSoft];
            gImpact[4] = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleRigid];
        } else {
            gImpact[3] = gImpact[0]; // Soft→Light 폴백
            gImpact[4] = gImpact[2]; // Rigid→Heavy 폴백
        }
    }
}

static BOOL FDI_EnsureEngine(void) {
    if (@available(iOS 13.0, *)) {
        id<CHHapticDeviceCapability> caps = [CHHapticEngine capabilitiesForHardware];
        if (![caps supportsHaptics]) return NO;
        NSError *err = nil;
        if (!gEngine) {
            gEngine = [[CHHapticEngine alloc] initAndReturnError:&err];
            if (err || !gEngine) { NSLog(@"[FDI_Haptic] engine init: %@", err); return NO; }
            __weak CHHapticEngine *weakEngine = gEngine;
            gEngine.resetHandler = ^{ NSError *e = nil; [weakEngine startAndReturnError:&e]; };
            gEngine.stoppedHandler = ^(CHHapticEngineStoppedReason r) { };
        }
        if (![gEngine startAndReturnError:&err]) { NSLog(@"[FDI_Haptic] engine start: %@", err); return NO; }
        return YES;
    }
    return NO;
}

extern "C" {

void FDI_HapticImpact(int style) {
    if (@available(iOS 10.0, *)) {
        dispatch_async(dispatch_get_main_queue(), ^{
            FDI_EnsureGenerators();
            int s = (style < 0 || style > 4) ? 1 : style;
            [gImpact[s] prepare];
            [gImpact[s] impactOccurred];
        });
    }
}

void FDI_HapticNotification(int type) {
    if (@available(iOS 10.0, *)) {
        dispatch_async(dispatch_get_main_queue(), ^{
            FDI_EnsureGenerators();
            [gNotif prepare];
            [gNotif notificationOccurred:(UINotificationFeedbackType)type];
        });
    }
}

void FDI_HapticSelection(void) {
    if (@available(iOS 10.0, *)) {
        dispatch_async(dispatch_get_main_queue(), ^{
            FDI_EnsureGenerators();
            [gSelection prepare];
            [gSelection selectionChanged];
        });
    }
}

void FDI_HapticPrewarm(void) {
    if (@available(iOS 10.0, *)) {
        dispatch_async(dispatch_get_main_queue(), ^{
            FDI_EnsureGenerators();
            [gSelection prepare];
            [gNotif prepare];
            for (int i = 0; i < 5; i++) [gImpact[i] prepare];
            if (@available(iOS 13.0, *)) FDI_EnsureEngine();
        });
    }
}

bool FDI_HapticSupportsCore(void) {
    if (@available(iOS 13.0, *)) {
        return [[CHHapticEngine capabilitiesForHardware] supportsHaptics] ? true : false;
    }
    return false;
}

void FDI_HapticStopCurve(void) {
    if (@available(iOS 13.0, *)) {
        dispatch_async(dispatch_get_main_queue(), ^{
            if (!gCurvePlayer) return;
            NSError *e = nil;
            [gCurvePlayer stopAtTime:CHHapticTimeImmediate error:&e];
            gCurvePlayer = nil;
        });
    }
}

void FDI_HapticPlayCurve(float durationSeconds, float sharpness,
                         const float *times, const float *intensities, int count) {
    if (count < 2 || times == NULL || intensities == NULL) return;
    float dur = FDI_Clamp(durationSeconds, 0.01f, 30.0f);
    float shp = FDI_Clamp(sharpness, 0.0f, 1.0f);

    NSMutableArray<NSNumber *> *t = [NSMutableArray arrayWithCapacity:count];
    NSMutableArray<NSNumber *> *v = [NSMutableArray arrayWithCapacity:count];
    for (int i = 0; i < count; i++) {
        [t addObject:@(FDI_Clamp(times[i], 0.0f, dur))];
        [v addObject:@(FDI_Clamp(intensities[i], 0.0f, 1.0f))];
    }

    dispatch_async(dispatch_get_main_queue(), ^{
        if (@available(iOS 13.0, *)) {
            if (!FDI_EnsureEngine()) return;
            if (gCurvePlayer) { NSError *se = nil; [gCurvePlayer stopAtTime:CHHapticTimeImmediate error:&se]; gCurvePlayer = nil; }

            NSMutableArray<CHHapticParameterCurveControlPoint *> *points = [NSMutableArray arrayWithCapacity:count];
            for (int i = 0; i < count; i++) {
                [points addObject:[[CHHapticParameterCurveControlPoint alloc]
                    initWithRelativeTime:[t[i] floatValue] value:[v[i] floatValue]]];
            }

            CHHapticEventParameter *baseIntensity = [[CHHapticEventParameter alloc]
                initWithParameterID:CHHapticEventParameterIDHapticIntensity value:1.0f];
            CHHapticEventParameter *sharpnessParam = [[CHHapticEventParameter alloc]
                initWithParameterID:CHHapticEventParameterIDHapticSharpness value:shp];
            CHHapticEvent *event = [[CHHapticEvent alloc]
                initWithEventType:CHHapticEventTypeHapticContinuous
                parameters:@[baseIntensity, sharpnessParam] relativeTime:0.0 duration:dur];
            CHHapticParameterCurve *curve = [[CHHapticParameterCurve alloc]
                initWithParameterID:CHHapticDynamicParameterIDHapticIntensityControl
                controlPoints:points relativeTime:0.0];

            NSError *err = nil;
            CHHapticPattern *pattern = [[CHHapticPattern alloc]
                initWithEvents:@[event] parameterCurves:@[curve] error:&err];
            if (err || !pattern) { NSLog(@"[FDI_Haptic] pattern: %@", err); return; }

            gCurvePlayer = [gEngine createPlayerWithPattern:pattern error:&err];
            if (err || !gCurvePlayer) { NSLog(@"[FDI_Haptic] player: %@", err); return; }
            [gCurvePlayer startAtTime:CHHapticTimeImmediate error:&err];
        }
    });
}

}
