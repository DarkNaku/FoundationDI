// FoundationDI 네이티브 약관 창(iOS).
//
// UIAlertController는 본문 텍스트에 링크를 거는 공개 API가 없다(비공개 키 attributedMessage는 링크가
// 눌리지 않고 심사 위험이 있다). 그래서 시스템 알림창과 같은 모양의 뷰 컨트롤러를 직접 띄운다.
// iOS 26+는 UIGlassEffect(Liquid Glass), 그 아래는 시스템 머티리얼 블러다.
#import <UIKit/UIKit.h>

extern UIViewController* UnityGetGLViewController();

typedef void (*FDI_PolicyDialogCallback)(void);

static const CGFloat FDI_CardMaxWidth = 340.0;
static const CGFloat FDI_CardMargin = 36.0;
static const CGFloat FDI_CardRadius = 34.0;

@interface FDIPolicyDialogController : UIViewController <UITextViewDelegate>
@property (nonatomic, copy) NSString* titleText;
@property (nonatomic, copy) NSAttributedString* message;
@property (nonatomic, copy) NSString* acceptText;
@property (nonatomic, assign) FDI_PolicyDialogCallback callback;
@end

static FDIPolicyDialogController* FDI_PolicyDialog_Current = nil;

@implementation FDIPolicyDialogController

- (void)viewDidLoad
{
    [super viewDidLoad];

    self.view.backgroundColor = [UIColor colorWithWhite:0.0 alpha:0.3];

    UIVisualEffect* effect = nil;

#if defined(__IPHONE_26_0) && __IPHONE_OS_VERSION_MAX_ALLOWED >= __IPHONE_26_0
    if (@available(iOS 26.0, *))
    {
        effect = [UIGlassEffect effectWithStyle:UIGlassEffectStyleRegular];
    }
#endif

    if (effect == nil) effect = [UIBlurEffect effectWithStyle:UIBlurEffectStyleSystemMaterial];

    UIVisualEffectView* card = [[UIVisualEffectView alloc] initWithEffect:effect];
    card.translatesAutoresizingMaskIntoConstraints = NO;
    card.layer.cornerRadius = FDI_CardRadius;
    card.layer.cornerCurve = kCACornerCurveContinuous;
    card.clipsToBounds = YES;
    [self.view addSubview:card];

    UILabel* title = [[UILabel alloc] init];
    title.translatesAutoresizingMaskIntoConstraints = NO;
    title.text = self.titleText;
    title.font = [UIFont systemFontOfSize:17.0 weight:UIFontWeightSemibold];
    title.textColor = UIColor.labelColor;
    title.textAlignment = NSTextAlignmentCenter;
    title.numberOfLines = 0;
    title.hidden = self.titleText.length == 0;

    // 링크를 누를 수 있어야 하므로 UILabel이 아니라 편집 불가 UITextView다.
    UITextView* body = [[UITextView alloc] init];
    body.translatesAutoresizingMaskIntoConstraints = NO;
    body.editable = NO;
    body.scrollEnabled = NO;
    body.backgroundColor = UIColor.clearColor;
    body.textContainerInset = UIEdgeInsetsZero;
    body.textContainer.lineFragmentPadding = 0;
    body.linkTextAttributes = @{ NSForegroundColorAttributeName: UIColor.systemBlueColor };
    body.attributedText = self.message;
    body.delegate = self;

    UIButtonConfiguration* config = [UIButtonConfiguration grayButtonConfiguration];
    config.cornerStyle = UIButtonConfigurationCornerStyleCapsule;
    config.baseForegroundColor = UIColor.labelColor;
    config.attributedTitle = [[NSAttributedString alloc]
        initWithString:self.acceptText
            attributes:@{ NSFontAttributeName: [UIFont systemFontOfSize:17.0 weight:UIFontWeightMedium] }];

    UIButton* accept = [UIButton buttonWithConfiguration:config primaryAction:nil];
    accept.translatesAutoresizingMaskIntoConstraints = NO;
    [accept addTarget:self action:@selector(onAccept) forControlEvents:UIControlEventTouchUpInside];

    UIView* content = card.contentView;
    [content addSubview:title];
    [content addSubview:body];
    [content addSubview:accept];

    UIView* root = self.view;
    NSLayoutConstraint* preferredWidth = [card.widthAnchor constraintEqualToAnchor:root.widthAnchor
                                                                         constant:-FDI_CardMargin * 2];
    preferredWidth.priority = UILayoutPriorityDefaultHigh;

    // 제목이 비어 있으면 본문이 위로 붙는다.
    NSLayoutYAxisAnchor* bodyTop = title.hidden ? content.topAnchor : title.bottomAnchor;
    CGFloat bodyGap = title.hidden ? 24.0 : 12.0;

    [NSLayoutConstraint activateConstraints:@[
        [card.centerXAnchor constraintEqualToAnchor:root.centerXAnchor],
        [card.centerYAnchor constraintEqualToAnchor:root.centerYAnchor],
        [card.widthAnchor constraintLessThanOrEqualToConstant:FDI_CardMaxWidth],
        preferredWidth,
        [card.topAnchor constraintGreaterThanOrEqualToAnchor:root.safeAreaLayoutGuide.topAnchor constant:16.0],

        [title.topAnchor constraintEqualToAnchor:content.topAnchor constant:24.0],
        [title.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:24.0],
        [title.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-24.0],

        [body.topAnchor constraintEqualToAnchor:bodyTop constant:bodyGap],
        [body.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:24.0],
        [body.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-24.0],

        [accept.topAnchor constraintEqualToAnchor:body.bottomAnchor constant:20.0],
        [accept.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:16.0],
        [accept.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-16.0],
        [accept.heightAnchor constraintEqualToConstant:50.0],
        [accept.bottomAnchor constraintEqualToAnchor:content.bottomAnchor constant:-16.0],
    ]];
}

- (void)onAccept
{
    FDI_PolicyDialogCallback callback = self.callback;
    self.callback = NULL;

    if (FDI_PolicyDialog_Current == self) FDI_PolicyDialog_Current = nil;

    [self.presentingViewController dismissViewControllerAnimated:YES completion:nil];

    if (callback != NULL) callback();
}

// iOS 16 이하. 17+는 기본 동작(primaryAction)이 URL을 연다.
- (BOOL)textView:(UITextView*)textView
    shouldInteractWithURL:(NSURL*)URL
                  inRange:(NSRange)characterRange
              interaction:(UITextItemInteraction)interaction
{
    [UIApplication.sharedApplication openURL:URL options:@{} completionHandler:nil];
    return NO;
}

@end

static NSString* FDI_PolicyDialog_String(const char* value)
{
    return value == NULL ? @"" : [NSString stringWithUTF8String:value];
}

extern "C"
{
    // iOS 15+는 Inactive일 때 띄운 모달을 버린다. C#이 Active를 기다린 뒤 띄운다.
    bool FDI_PolicyDialog_IsApplicationActive(void)
    {
        return UIApplication.sharedApplication.applicationState == UIApplicationStateActive;
    }

    // starts/lengths는 UTF-16 단위다(C# string과 NSString이 같다).
    void FDI_PolicyDialog_Show(const char* title, const char* message, const int* starts, const int* lengths,
                               const char** urls, int linkCount, const char* acceptLabel,
                               FDI_PolicyDialogCallback callback)
    {
        NSString* text = FDI_PolicyDialog_String(message);

        NSMutableParagraphStyle* paragraph = [[NSMutableParagraphStyle alloc] init];
        paragraph.alignment = NSTextAlignmentCenter;
        paragraph.lineSpacing = 2.0;

        NSMutableAttributedString* attributed = [[NSMutableAttributedString alloc]
            initWithString:text
                attributes:@{
                    NSFontAttributeName: [UIFont systemFontOfSize:15.0],
                    NSForegroundColorAttributeName: UIColor.labelColor,
                    NSParagraphStyleAttributeName: paragraph,
                }];

        for (int i = 0; i < linkCount; i++)
        {
            NSURL* url = [NSURL URLWithString:FDI_PolicyDialog_String(urls[i])];
            NSRange range = NSMakeRange((NSUInteger)starts[i], (NSUInteger)lengths[i]);

            if (url == nil || NSMaxRange(range) > text.length) continue;

            [attributed addAttribute:NSLinkAttributeName value:url range:range];
        }

        FDIPolicyDialogController* controller = [[FDIPolicyDialogController alloc] init];
        controller.titleText = FDI_PolicyDialog_String(title);
        controller.message = attributed;
        controller.acceptText = FDI_PolicyDialog_String(acceptLabel);
        controller.callback = callback;
        controller.modalPresentationStyle = UIModalPresentationOverFullScreen;
        controller.modalTransitionStyle = UIModalTransitionStyleCrossDissolve;
        controller.modalInPresentation = YES;

        // 다른 모달(광고 등)이 떠 있으면 그 위에 띄운다.
        UIViewController* presenter = UnityGetGLViewController();
        while (presenter.presentedViewController != nil) presenter = presenter.presentedViewController;

        FDI_PolicyDialog_Current = controller;
        [presenter presentViewController:controller animated:YES completion:nil];
    }

    void FDI_PolicyDialog_Close(void)
    {
        FDIPolicyDialogController* controller = FDI_PolicyDialog_Current;
        FDI_PolicyDialog_Current = nil;

        if (controller == nil) return;

        controller.callback = NULL;
        [controller.presentingViewController dismissViewControllerAnimated:NO completion:nil];
    }
}
