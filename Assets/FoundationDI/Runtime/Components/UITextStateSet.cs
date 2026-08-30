using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    /// <summary>한 상태가 덮어쓸 텍스트 필드.</summary>
    [Flags]
    public enum UITextSwap
    {
        None = 0,
        Text = 1 << 0,
        Color = 1 << 1,
        Material = 1 << 2,
    }

    /// <summary>한 상태의 텍스트 값 묶음. <see cref="Override"/>에 켜진 필드만 유효하다.</summary>
    [Serializable]
    public struct UITextStateValue
    {
        public UITextSwap Override;
        public string Text;
        public Color Color;
        public Material Material;
    }

    /// <summary>
    /// 텍스트 하나의 상태별 스왑 정의.
    /// 타깃이 <see cref="Graphic"/>인 이유는 <see cref="TMP_Text"/>와
    /// <see cref="Text"/>의 유일한 공통 조상이기 때문이다.
    /// </summary>
    [Serializable]
    public class UITextStateSet
    {
        public Graphic Target;

        public UITextStateValue Normal;
        public UITextStateValue Highlighted;
        public UITextStateValue Pressed;
        public UITextStateValue Selected;
        public UITextStateValue Disabled;

        [NonSerialized] private bool _baselineCaptured;
        [NonSerialized] private UITextStateValue _baseline;

        /// <summary>
        /// 상태를 적용한다. 필드마다 "그 상태 → Normal → 기준값 → 안 씀" 순으로 값을 고른다.
        /// </summary>
        public void Apply(UIButtonState state)
        {
            if (Target == null) return;

            CaptureBaseline();

            if (TryResolve(state, UITextSwap.Text, out var text)) SetText(text.Text);
            if (TryResolve(state, UITextSwap.Color, out var color)) Target.color = color.Color;
            if (TryResolve(state, UITextSwap.Material, out var material)) SetMaterial(material.Material);
        }

        private void SetText(string value)
        {
            switch (Target)
            {
                case TMP_Text tmp: tmp.text = value; break;
                case Text legacy: legacy.text = value; break;
            }
        }

        // TMP에 Graphic.material을 그냥 대입하면 TMP 자체 머티리얼 관리와 충돌한다.
        // 아웃라인/글로우를 상태별로 바꾸는 TMP 표준 방식이 곧 Material Preset 교체다.
        private void SetMaterial(Material value)
        {
            if (Target is TMP_Text tmp) tmp.fontSharedMaterial = value;
            else Target.material = value;
        }

        private UITextStateValue Get(UIButtonState state)
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

        /// <summary>
        /// 첫 <see cref="Apply"/> 시점의 타깃 값을 복원 기준으로 잡는다.
        /// <see cref="UIImageStateSet"/>와 같은 규칙이다.
        /// </summary>
        private void CaptureBaseline()
        {
            if (_baselineCaptured) return;

            _baselineCaptured = true;
            _baseline = new UITextStateValue
            {
                Override = UITextSwap.Text | UITextSwap.Color | UITextSwap.Material,
                Text = GetText(),
                Color = Target.color,
                Material = GetMaterial(),
            };
        }

        private string GetText()
        {
            switch (Target)
            {
                case TMP_Text tmp: return tmp.text;
                case Text legacy: return legacy.text;
                default: return null;
            }
        }

        private Material GetMaterial() =>
            Target is TMP_Text tmp ? tmp.fontSharedMaterial : Target.material;

        private UITextSwap AllOverrides() =>
            Normal.Override | Highlighted.Override | Pressed.Override | Selected.Override | Disabled.Override;

        private bool TryResolve(UIButtonState state, UITextSwap field, out UITextStateValue value)
        {
            // 아무 상태도 이 필드를 관리하지 않으면 손대지 않는다.
            if ((AllOverrides() & field) == 0)
            {
                value = default;
                return false;
            }

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

            value = _baseline;
            return true;
        }
    }
}
