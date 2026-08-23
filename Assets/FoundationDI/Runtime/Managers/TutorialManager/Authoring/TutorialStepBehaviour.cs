using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 인스펙터에서 채운 데이터를 TutorialStep으로 옮기기만 하는 껍데기.
    /// 진행 규칙은 여기에 없다 — 순수 C# 엔진이 갖는다.
    /// </summary>
    public sealed class TutorialStepBehaviour : MonoBehaviour
    {
        [SerializeField] private string _stepId;
        [SerializeField] private float _startDelay;
        [SerializeField] private float _endDelay;

        [Tooltip("모듈이 가리킬 대상. 씬 오브젝트는 드래그하고, 런타임 생성 UI는 키를 적는다.")]
        [SerializeField] private TutorialTargetRef _target;

        [SerializeReference] private ITutorialTrigger _startTrigger = new AutoTrigger();
        [SerializeReference] private ITutorialTrigger _endTrigger = new AutoTrigger();

        [SerializeField] private TutorialModuleBehaviour[] _modules;

        public string StepId => string.IsNullOrWhiteSpace(_stepId) ? name : _stepId;

        public TutorialStep Build()
        {
            var modules = new List<ITutorialModule>();

            if (_modules != null)
            {
                foreach (var module in _modules)
                {
                    if (module == null) continue;

                    // Step이 시작되기 전까지는 연출이 보이면 안 된다.
                    module.gameObject.SetActive(false);
                    modules.Add(module);
                }
            }

            return new TutorialStep(StepId, _startTrigger, _endTrigger, modules, _target,
                                    _startDelay, _endDelay);
        }
    }
}
