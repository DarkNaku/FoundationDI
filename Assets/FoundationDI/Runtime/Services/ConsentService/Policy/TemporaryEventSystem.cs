using System;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    // 약관 창은 첫 씬의 UI보다 먼저 뜰 수 있다. 그때 씬에 EventSystem이 없으면 버튼이 눌리지 않아
    // 게임이 시작되지 않는다. 없을 때만 만들고, 창이 닫히면 지운다(씬의 것과 겹치지 않게).
    internal sealed class TemporaryEventSystem
    {
        private const string InputSystemModuleType =
            "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem";

        private GameObject _created;

        public void Acquire()
        {
            if (_created != null) return;
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;

            _created = new GameObject("[ConsentService] EventSystem", typeof(EventSystem));

            // Input System 전용 프로젝트에서 StandaloneInputModule은 매 프레임 예외를 낸다. 패키지가
            // Input System을 참조하지 않도록 리플렉션으로 찾는다.
            var inputSystemModule = Type.GetType(InputSystemModuleType);

            if (inputSystemModule != null) _created.AddComponent(inputSystemModule);
            else _created.AddComponent<StandaloneInputModule>();

            if (Application.isPlaying) Object.DontDestroyOnLoad(_created);
        }

        public void Release()
        {
            if (_created == null) return;

            if (Application.isPlaying) Object.Destroy(_created);
            else Object.DestroyImmediate(_created);

            _created = null;
        }
    }
}
