using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 버튼 클릭 시 카탈로그에서 고른 사운드를 재생하는 컴포넌트.
    /// _catalog는 에디터 키 드롭다운 소스 전용이며, 런타임 재생은 주입된
    /// ISoundService가 자체 카탈로그로 처리한다(_key가 DI 카탈로그에 존재해야 함).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class SoundButton : InjectableBehaviour
    {
        [Inject] private ISoundService _sound;

        [Tooltip("에디터 키 드롭다운 전용. 런타임 미사용 — DI로 등록된 ISoundCatalog와 동일 에셋을 지정해야 _key가 유효하다.")]
        [SerializeField] private SoundCatalog _catalog;
        [SerializeField] private string _key;

        protected override void Awake()
        {
            base.Awake();
            GetComponent<Button>().onClick.AddListener(Play);
        }

        public void Play()
        {
            EnsureInjected();

            if (_sound == null)
            {
                Debug.LogError("[SoundButton] ISoundService가 주입되지 않았습니다.");
                return;
            }

            _sound.Play(_key);
        }
    }
}
