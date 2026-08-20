using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 직렬화 가능한 유사 enum. 실제 값은 에디터가 생성하는 Track_Generated.cs에 partial로 추가된다.
    /// </summary>
    [Serializable]
    public partial struct Track : IEquatable<Track>
    {
        [SerializeField] private string value;

        internal const string NULL_TAG = "__NULL__";

        public static readonly Track Null = new Track(NULL_TAG);

        internal Track(string value) => this.value = string.IsNullOrEmpty(value) ? NULL_TAG : value;

        /// <summary>생성된 상수 대신 문자열 태그로 값을 만든다.</summary>
        public static Track FromTag(string tag) => new Track(tag);

        public bool IsNull => string.IsNullOrEmpty(value) || value == NULL_TAG;

        public override string ToString() => string.IsNullOrEmpty(value) ? NULL_TAG : value;

        public bool Equals(Track other) => value == other.value;

        public override bool Equals(object obj) => obj is Track other && Equals(other);

        public override int GetHashCode() => value?.GetHashCode() ?? 0;

        public static bool operator ==(Track left, Track right) => left.Equals(right);

        public static bool operator !=(Track left, Track right) => !left.Equals(right);

        public static implicit operator string(Track s) => string.IsNullOrEmpty(s.value) ? NULL_TAG : s.value;
    }
}
