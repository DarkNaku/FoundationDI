using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 씬에 배치해서 시퀀스 하나를 오써링한다. 자식의 TutorialStepBehaviour를 순서대로 모은다.
    /// 씬에 직접 배치된 컴포넌트라 생성자 주입이 안 되므로 InjectableBehaviour를 쓴다.
    /// </summary>
    public sealed class TutorialSequenceBehaviour : InjectableBehaviour
    {
        [Tooltip("진행도 저장 키. 비우면 GameObject 이름을 쓴다. 한 번 정하면 바꾸지 않는다.")]
        [SerializeField] private string _sequenceId;

        [Tooltip("여러 시퀀스가 동시에 발동하면 이 값이 낮은 쪽부터 실행된다.")]
        [SerializeField] private int _order;

        [SerializeField] private ResumeMode _resumeMode = ResumeMode.RestartSequence;

        [Tooltip("타깃을 기다리는 최대 시간(초). 0이면 무한.")]
        [SerializeField] private float _targetTimeout;

        [SerializeReference] private ITutorialTrigger _startTrigger = new AutoTrigger();

        [Inject] private ITutorialManager _tutorial;

        private bool _registered;

        public string SequenceId => string.IsNullOrWhiteSpace(_sequenceId) ? name : _sequenceId;

        // 주입은 컨테이너 준비 시점에 달려 있어 Awake/OnEnable보다 늦을 수 있다.
        // Start에서 한 번, 그래도 아직이면 등록될 때까지 Update에서 재시도한다.
        private void Start() => TryRegister();

        private void Update()
        {
            if (_registered) return;

            TryRegister();
        }

        private void OnDestroy()
        {
            if (!_registered) return;

            _registered = false;
            _tutorial?.Unregister(SequenceId);
        }

        private void TryRegister()
        {
            if (_registered) return;
            if (_tutorial == null) return;

            _registered = true;
            _tutorial.Register(BuildSequence());

            // 등록이 끝났으니 Update 폴링을 끈다. OnDisable이 없는 컴포넌트라 안전하다.
            enabled = false;
        }

        internal TutorialSequence BuildSequence()
        {
            var steps = new List<TutorialStep>();

            foreach (Transform child in transform)
            {
                if (!child.TryGetComponent<TutorialStepBehaviour>(out var behaviour)) continue;

                steps.Add(behaviour.Build());
            }

            return new TutorialSequence(SequenceId, _startTrigger, steps, _order, _resumeMode,
                                        _targetTimeout);
        }
    }
}
