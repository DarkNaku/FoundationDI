namespace DarkNaku.FoundationDI
{
    // ConsentServiceSettings(SO)가 아니라 이 구조체를 받는다. 서비스가 에셋 형식에 묶이지 않고
    // EditMode 테스트가 SO를 만들 필요가 없다(AdServiceOptions와 같은 이유).
    public readonly struct ConsentServiceOptions
    {
        // 0 이하면 약관 단계를 쓰지 않는다(항상 수락으로 본다).
        public int PolicyVersion { get; }
        public PolicyAgreementContent PolicyContent { get; }
        public bool UseUmp { get; }
        public UmpRequestOptions UmpRequest { get; }
        public bool RequestTrackingAuthorization { get; }

        // UMP 정보 갱신(네트워크)에만 건다. 폼 표시 중에는 사용자가 읽는 시간이라 걸지 않는다.
        public float UmpUpdateTimeoutSeconds { get; }

        public ConsentServiceOptions(int policyVersion, PolicyAgreementContent policyContent, bool useUmp,
                                     UmpRequestOptions umpRequest, bool requestTrackingAuthorization,
                                     float umpUpdateTimeoutSeconds = 10f)
        {
            PolicyVersion = policyVersion;
            PolicyContent = policyContent;
            UseUmp = useUmp;
            UmpRequest = umpRequest;
            RequestTrackingAuthorization = requestTrackingAuthorization;
            UmpUpdateTimeoutSeconds = umpUpdateTimeoutSeconds;
        }
    }
}
