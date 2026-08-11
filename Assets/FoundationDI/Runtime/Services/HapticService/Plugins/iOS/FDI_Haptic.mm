// iOS native implementation for iOSHapticProvider (DllImport("__Internal")).
// Enum values are aligned 1:1 with UIKit:
//   HapticImpact { Light=0, Medium=1, Heavy=2, Soft=3, Rigid=4 } == UIImpactFeedbackStyle
//   HapticNotification { Success=0, Warning=1, Error=2 }         == UINotificationFeedbackType
#import <UIKit/UIKit.h>

extern "C" {

void FDI_HapticImpact(int style) {
    if (@available(iOS 10.0, *)) {
        UIImpactFeedbackGenerator *generator =
            [[UIImpactFeedbackGenerator alloc] initWithStyle:(UIImpactFeedbackStyle)style];
        [generator prepare];
        [generator impactOccurred];
    }
}

void FDI_HapticNotification(int type) {
    if (@available(iOS 10.0, *)) {
        UINotificationFeedbackGenerator *generator =
            [[UINotificationFeedbackGenerator alloc] init];
        [generator prepare];
        [generator notificationOccurred:(UINotificationFeedbackType)type];
    }
}

void FDI_HapticSelection() {
    if (@available(iOS 10.0, *)) {
        UISelectionFeedbackGenerator *generator =
            [[UISelectionFeedbackGenerator alloc] init];
        [generator prepare];
        [generator selectionChanged];
    }
}

}
