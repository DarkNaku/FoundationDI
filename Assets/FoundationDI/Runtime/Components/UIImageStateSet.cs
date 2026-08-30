using System;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    /// <summary>한 상태가 덮어쓸 Image 필드.</summary>
    [Flags]
    public enum UIImageSwap
    {
        None = 0,
        Sprite = 1 << 0,
        Color = 1 << 1,
        Visible = 1 << 2,
    }

    /// <summary>한 상태의 Image 값 묶음. <see cref="Override"/>에 켜진 필드만 유효하다.</summary>
    [Serializable]
    public struct UIImageStateValue
    {
        public UIImageSwap Override;
        public Sprite Sprite;
        public Color Color;
        public bool Visible;
    }

    /// <summary>
    /// Image 하나의 상태별 스왑 정의. <see cref="Selectable"/>을 모르므로 단독으로 테스트된다.
    /// </summary>
    [Serializable]
    public class UIImageStateSet
    {
        public Image Target;

        public UIImageStateValue Normal;
        public UIImageStateValue Highlighted;
        public UIImageStateValue Pressed;
        public UIImageStateValue Selected;
        public UIImageStateValue Disabled;

        /// <summary>
        /// 상태를 적용한다. 필드마다 "그 상태 → Normal → 안 씀" 3단으로 값을 고른다.
        /// </summary>
        public void Apply(UIButtonState state)
        {
            if (Target == null) return;

            if (TryResolve(state, UIImageSwap.Sprite, out var sprite)) Target.sprite = sprite.Sprite;
            if (TryResolve(state, UIImageSwap.Color, out var color)) Target.color = color.Color;
            if (TryResolve(state, UIImageSwap.Visible, out var visible)) Target.enabled = visible.Visible;
        }

        private UIImageStateValue Get(UIButtonState state)
        {
            switch (state)
            {
                case UIButtonState.Highlighted: return Highlighted;
                case UIButtonState.Pressed: return Pressed;
                case UIButtonState.Selected: return Selected;
                case UIButtonState.Disabled: return Disabled;
                default: return Normal;
            }
        }

        // 폴백 대상이 Normal인 것이 핵심이다. "가장 가까운 상태"(Selected→Highlighted)로
        // 떨어뜨리면 모바일에서 탭한 버튼이 계속 하이라이트된 채 남는다.
        private bool TryResolve(UIButtonState state, UIImageSwap field, out UIImageStateValue value)
        {
            var current = Get(state);

            if ((current.Override & field) != 0)
            {
                value = current;
                return true;
            }

            if ((Normal.Override & field) != 0)
            {
                value = Normal;
                return true;
            }

            value = default;
            return false;
        }
    }
}
