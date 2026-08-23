using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

/// <summary>
/// 씬 오써링 → InjectorService 주입 → 등록 → 진행 → 영속화까지 실제 경로를 태우는 스모크.
/// EditMode 단위 테스트가 닿지 않는 부분(MonoBehaviour 생명주기와 주입 타이밍)만 본다.
///
/// InjectorService는 정적 컨테이너 참조 하나를 공유하는 단일 컨테이너 모델이다.
/// 에디터에서 도메인 리로드 없이 플레이 모드를 반복하면 그 참조가 이전 실행의 컨테이너를
/// 가리킨 채 남아 테스트가 엉뚱한 인스턴스를 주입받는다. 그래서 매 테스트마다 초기화한다.
/// </summary>
public class TutorialManagerSmokeTests
{
    private const string SaveKey = "playmode_smoke";
    private const string SequenceId = "smoke";

    private readonly List<GameObject> _spawned = new();

    private LifetimeScope _scope;

    [SetUp]
    public void SetUp()
    {
        ResetInjector();
        new PlayerPrefsTutorialProgressStorage(SaveKey).Clear();
    }

    [TearDown]
    public void TearDown()
    {
        DespawnAll();
        DestroyScope();
        ResetInjector();

        new PlayerPrefsTutorialProgressStorage(SaveKey).Clear();
    }

    private static void ResetInjector()
    {
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;

        var type = typeof(InjectorService);

        type.GetField("_resolver", Flags)?.SetValue(null, null);

        if (type.GetField("_pending", Flags)?.GetValue(null) is System.Collections.IList pending)
        {
            pending.Clear();
        }
    }

    private ITutorialManager BuildScope()
    {
        _scope = LifetimeScope.Create(builder =>
        {
            builder.RegisterMessageService();
            builder.RegisterInjector();
            builder.RegisterTutorialManager(SaveKey);
        });

        return _scope.Container.Resolve<ITutorialManager>();
    }

    private void DestroyScope()
    {
        if (_scope == null) return;

        var go = _scope.gameObject;

        _scope = null;

        if (go != null) Object.DestroyImmediate(go);
    }

    private void DespawnAll()
    {
        foreach (var go in _spawned)
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        _spawned.Clear();
    }

    // 트리거를 지정하지 않으면 시작·종료 모두 AutoTrigger라 시퀀스가 곧바로 끝까지 흐른다.
    // 스모크가 보려는 건 연출이 아니라 "씬에 놓기만 하면 돌아가는가"이므로 이걸로 충분하다.
    private void SpawnSequence()
    {
        var sequence = new GameObject(SequenceId, typeof(TutorialSequenceBehaviour));
        _spawned.Add(sequence);

        var step = new GameObject("Step 1", typeof(TutorialStepBehaviour));
        step.transform.SetParent(sequence.transform);
    }

    [UnityTest]
    public IEnumerator 씬에_배치한_시퀀스가_주입받아_스스로_등록되고_완료된다()
    {
        var manager = BuildScope();
        var completed = new List<string>();

        manager.SequenceCompleted += id => completed.Add(id);

        SpawnSequence();

        // InjectorService의 EntryPoint가 먼저 뜨고, 그 다음 Start/Update가 돌아야 등록된다.
        for (var i = 0; i < 120 && completed.Count == 0; i++) yield return null;

        Assert.AreEqual(new[] { SequenceId }, completed.ToArray());
        Assert.IsTrue(manager.IsCompleted(SequenceId));
        Assert.AreEqual(TutorialState.Completed,
                        new PlayerPrefsTutorialProgressStorage(SaveKey).GetState(SequenceId));
    }

    [UnityTest]
    public IEnumerator 완료된_시퀀스는_앱을_다시_켜도_시작하지_않는다()
    {
        var manager = BuildScope();
        var completed = new List<string>();

        manager.SequenceCompleted += id => completed.Add(id);

        SpawnSequence();

        for (var i = 0; i < 120 && completed.Count == 0; i++) yield return null;

        Assert.AreEqual(1, completed.Count, "1회차에서 완료되지 않으면 2회차 검증이 무의미하다.");

        // 앱을 껐다 켠 상황을 흉내낸다 — 씬 오브젝트와 스코프를 버리고 같은 저장 키로 다시 만든다.
        DespawnAll();
        DestroyScope();
        ResetInjector();

        var restarted = BuildScope();
        var started = new List<string>();

        restarted.SequenceStarted += id => started.Add(id);

        Assert.IsTrue(restarted.IsCompleted(SequenceId),
                      "완료는 PlayerPrefs에 남아 새 컨테이너에서도 읽혀야 한다.");

        SpawnSequence();

        for (var i = 0; i < 120; i++) yield return null;

        Assert.IsEmpty(started, "완료된 시퀀스는 다시 시작되면 안 된다.");
    }
}
