using UnityEngine;
using UnityEngine.Audio;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// SoundService의 단일 설정 에셋. 데이터 컬렉션 참조와 오클루전 파라미터를 모두 보유한다.
    /// 런타임에는 <c>builder.RegisterSoundService(settings)</c>로 DI 주입되며,
    /// 에디터 도구는 <c>AssetDatabase</c>로 이 에셋을 찾아 사용한다(Resources 의존 없음).
    /// </summary>
    [CreateAssetMenu(fileName = "SoundServiceSettings", menuName = "DarkNaku/FoundationDI/Sound Service Settings")]
    public class SoundServiceSettings : ScriptableObject
    {
        [field: Header("Data")]
        [field: Tooltip("SFX 태그 데이터베이스.")]
        [field: SerializeField]
        public SoundDataCollection SoundDataCollection { get; set; }

        [field: Tooltip("음악 태그 데이터베이스.")]
        [field: SerializeField]
        public MusicDataCollection MusicDataCollection { get; set; }

        [field: Tooltip("AudioMixer Output 데이터베이스.")]
        [field: SerializeField]
        public OutputDataCollection OutputDataCollection { get; set; }

        [field: Tooltip("Output 볼륨 파라미터가 노출된 마스터 AudioMixer.")]
        [field: SerializeField]
        public AudioMixer MasterAudioMixer { get; set; }

        [field: Tooltip("에디터 도구가 생성한 데이터/코드를 저장할 프로젝트 상대 경로. 예: 'Assets/FoundationDI.Data/SoundService/'")]
        [field: SerializeField]
        public string DataRootPath { get; set; } = "Assets/FoundationDI.Data/SoundService/";

        [field: Header("Occlusion")]
        [field: Tooltip("오클루전 전역 스위치. 끄면 레이캐스트와 로우패스 필터를 전혀 적용하지 않는다.")]
        [field: SerializeField]
        public bool EnableOcclusion { get; set; } = true;

        [field: Tooltip("오클루전 장애물로 취급할 레이어(벽, 문, 소품 등).")]
        [field: SerializeField]
        public LayerMask OcclusionLayers { get; set; } = ~0;

        [field: Tooltip("오클루전을 계산할 최대 거리. 이보다 멀면 계산하지 않는다.")]
        [field: SerializeField, Min(0f)]
        public float MaxDistance { get; set; } = 50f;

        [field: Tooltip("완전히 가려졌을 때의 로우패스 컷오프 주파수(최솟값).")]
        [field: SerializeField, Min(10f)]
        public float MinCutoff { get; set; } = 1200f;

        [field: Tooltip("전혀 가려지지 않았을 때의 로우패스 컷오프 주파수(최댓값).")]
        [field: SerializeField, Min(10f)]
        public float MaxCutoff { get; set; } = 22000f;

        [field: Tooltip("완전히 가려졌을 때의 볼륨 배수. 0이면 완전 무음, 1이면 감쇠 없음.")]
        [field: SerializeField, Range(0f, 1f)]
        public float MinVolumeMultiplier { get; set; } = 0.25f;

        [field: Tooltip("간접(회절 근사) 레이 바운스 횟수. 0이면 직선 오클루전만 계산한다.")]
        [field: SerializeField, Min(0)]
        public int MaxBounces { get; set; } = 1;

        [field: Tooltip("모서리 회절을 근사하기 위한 리스너 주변 바운스 링의 최소 반지름.")]
        [field: SerializeField, Min(0f)]
        public float BounceRadiusMin { get; set; } = 1.0f;

        [field: Tooltip("바운스 링에 사용할 레이 개수. 클수록 부드럽지만 CPU 비용이 늘어난다.")]
        [field: SerializeField, Min(4)]
        public int BounceRaysPerCircle { get; set; } = 8;

        [field: Tooltip("각 소스가 오클루전을 재계산하는 간격(초).")]
        [field: SerializeField, Min(0.01f)]
        public float CheckInterval { get; set; } = 0.1f;

        [field: Tooltip("오클루전 변화에 반응하는 속도(보간 계수).")]
        [field: SerializeField, Min(0.01f)]
        public float LerpSpeed { get; set; } = 10f;

        /// <summary>DataRootPath를 'Assets/'로 시작하고 '/'로 끝나는 형태로 정규화한다.</summary>
        public string GetNormalizedDataRootPath()
        {
            const string fallback = "Assets/FoundationDI.Data/SoundService/";

            if (string.IsNullOrWhiteSpace(DataRootPath)) return fallback;

            var path = DataRootPath.Trim().Replace("\\", "/");

            if (!path.StartsWith("Assets/") && !path.Equals("Assets"))
            {
                path = "Assets/" + path.TrimStart('/');
            }

            if (!path.EndsWith("/"))
            {
                path += "/";
            }

            return path;
        }

        public void ResetToDefaults()
        {
            DataRootPath = "Assets/FoundationDI.Data/SoundService/";

            EnableOcclusion = true;
            OcclusionLayers = ~0;

            MaxDistance = 50f;
            MinCutoff = 1200f;
            MaxCutoff = 22000f;
            MinVolumeMultiplier = 0.25f;

            MaxBounces = 1;
            BounceRadiusMin = 1.0f;
            BounceRaysPerCircle = 8;

            CheckInterval = 0.1f;
            LerpSpeed = 10f;
        }
    }
}
