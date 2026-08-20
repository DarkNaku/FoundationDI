using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 직렬화 가능한 유사 enum. 실제 값은 에디터가 생성하는 Output_Generated.cs에 partial로 추가된다.
    /// </summary>
    [Serializable]
    public partial struct Output : IEquatable<Output>
    {
        [SerializeField] private string value;

        internal const string NULL_TAG = "__NULL__";

        public static readonly Output Null = new Output(NULL_TAG);

        internal Output(string value) => this.value = string.IsNullOrEmpty(value) ? NULL_TAG : value;

        /// <summary>생성된 상수 대신 문자열 태그로 값을 만든다.</summary>
        public static Output FromTag(string tag) => new Output(tag);

        public bool IsNull => string.IsNullOrEmpty(value) || value == NULL_TAG;

        public override string ToString() => string.IsNullOrEmpty(value) ? NULL_TAG : value;

        public bool Equals(Output other) => value == other.value;

        public override bool Equals(object obj) => obj is Output other && Equals(other);

        public override int GetHashCode() => value?.GetHashCode() ?? 0;

        public static bool operator ==(Output left, Output right) => left.Equals(right);

        public static bool operator !=(Output left, Output right) => !left.Equals(right);

        public static implicit operator string(Output s) => string.IsNullOrEmpty(s.value) ? NULL_TAG : s.value;
    }
}
