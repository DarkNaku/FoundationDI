namespace DarkNaku.FoundationDI
{
    // 수락한 약관 버전을 저장한다. 버전을 저장하는 이유: 약관이 바뀌면 설정의 Policy Version만
    // 올려 기존 유저에게 다시 물을 수 있다.
    public interface IPolicyAgreementStorage
    {
        // 저장한 적이 없으면 0.
        int LoadAcceptedVersion();

        void SaveAcceptedVersion(int version);
    }
}
