using System;
using System.Reflection;
using UnityEngine;

// 전역 네임스페이스(기존 PlayMode 테스트 관례와 일치)
public static class TransitionTestHelpers
{
    // private [SerializeField] 필드 주입
    public static void SetPrivate(object target, string field, object value)
    {
        var f = target.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (f == null) throw new ArgumentException($"field not found: {field}");
        f.SetValue(target, value);
    }

    // RectTransform 노드 생성 (+ 추가 컴포넌트)
    public static GameObject NewUINode(string name, params Type[] extra)
    {
        var go = new GameObject(name, typeof(RectTransform));
        foreach (var t in extra) go.AddComponent(t);
        return go;
    }
}
