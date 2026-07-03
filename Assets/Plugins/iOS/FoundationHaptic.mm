#import <UIKit/UIKit.h>

// style: 0=Light 1=Medium 2=Heavy 3=Soft 4=Rigid (HapticImpact enum 순서와 일치)
extern "C" void FDI_HapticImpact(int style)
{
    if (@available(iOS 10.0, *))
    {
        UIImpactFeedbackStyle mapped = UIImpactFeedbackStyleMedium;
        switch (style)
        {
            case 0: mapped = UIImpactFeedbackStyleLight; break;
            case 1: mapped = UIImpactFeedbackStyleMedium; break;
            case 2: mapped = UIImpactFeedbackStyleHeavy; break;
            case 3: if (@available(iOS 13.0, *)) { mapped = UIImpactFeedbackStyleSoft; } break;
            case 4: if (@available(iOS 13.0, *)) { mapped = UIImpactFeedbackStyleRigid; } break;
        }
        UIImpactFeedbackGenerator *generator = [[UIImpactFeedbackGenerator alloc] initWithStyle:mapped];
        [generator prepare];
        [generator impactOccurred];
    }
}

// type: 0=Success 1=Warning 2=Error (HapticNotification enum 순서와 일치)
extern "C" void FDI_HapticNotification(int type)
{
    if (@available(iOS 10.0, *))
    {
        UINotificationFeedbackType mapped = UINotificationFeedbackTypeSuccess;
        switch (type)
        {
            case 0: mapped = UINotificationFeedbackTypeSuccess; break;
            case 1: mapped = UINotificationFeedbackTypeWarning; break;
            case 2: mapped = UINotificationFeedbackTypeError; break;
        }
        UINotificationFeedbackGenerator *generator = [[UINotificationFeedbackGenerator alloc] init];
        [generator prepare];
        [generator notificationOccurred:mapped];
    }
}

extern "C" void FDI_HapticSelection()
{
    if (@available(iOS 10.0, *))
    {
        UISelectionFeedbackGenerator *generator = [[UISelectionFeedbackGenerator alloc] init];
        [generator prepare];
        [generator selectionChanged];
    }
}
