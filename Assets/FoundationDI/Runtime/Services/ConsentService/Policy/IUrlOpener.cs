using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 링크 열기 seam. 테스트가 실제 브라우저를 띄우지 않게 한다.
    public interface IUrlOpener
    {
        void Open(string url);
    }

    public sealed class UnityUrlOpener : IUrlOpener
    {
        public void Open(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            Application.OpenURL(url);
        }
    }
}
