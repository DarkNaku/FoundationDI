using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Reflex.Core;
using Reflex.Injectors;

/// <summary>
/// 씬 오써링 → 주입 → 등록 → 진행 → 영속화까지 실제 경로를 태우는 스모크.
/// EditMode 단위 테스트가 닿지 않는 부분(MonoBehaviour 생명주기와 주입 타이밍)만 본다.
///
/// 실제 씬에서는 ContainerScope가 실행 순서 -1e9로 돌면서 SceneInjector가 씬 전체를
/// 재귀 주입한다. 여기서는 씬을 띄우지 않으므로 GameObjectInjector.InjectRecursive로
/// 같은 일을 직접 한다 - SceneInjector가 내부적으로 부르는 바로 그 메서드다.
/// </summary>
public class TutorialManagerSmokeTests
{
    private const string SaveKey = "playmode_smoke";
    private const string SequenceId = "smoke";

    private readonly List<GameObject> _spawned = new();

    private Container _container;

    [SetUp]
    public void SetUp()
    {
        new PlayerPrefsTutorialProgressStorage(SaveKey).Clear();
    }

    [TearDown]
    public void TearDown()
    {
        DespawnAll();
        DisposeContainer();

        new PlayerPrefsTutorialProgressStorage(SaveKey).Clear();
    }

    private ITutorialManager BuildContainer()
    {
        var builder = new ContainerBuilder();

        ServiceResolverBootstrap.Register(builder);
        builder.RegisterMessageService();
        builder.RegisterTutorialManager(SaveKey);

        _container = builder.Build();

        return _container.Resolve<ITutorialManager>();
    }

    private void DisposeContainer()
    {
        if (_container == null) return;

        var container = _container;
        _container = null;
        container.Dispose();
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

        // 씬 로드 시 SceneInjector가 하는 일. Start가 돌기 전에 끝나야 한다.
        GameObjectInjector.InjectRecursive(sequence, _container);
    }

    [UnityTest]
    public IEnumerator 씬에_배치한_시퀀스가_주입받아_스스로_등록되고_완료된다()
    {
        var manager = BuildContainer();
        var completed = new List<string>();

        manager.SequenceCompleted += id => completed.Add(id);

        SpawnSequence();

        // 주입은 끝났고, Start/Update가 돌아야 등록과 진행이 일어난다.
        for (var i = 0; i < 120 && completed.Count == 0; i++) yield return null;

        Assert.AreEqual(new[] { SequenceId }, completed.ToArray());
        Assert.IsTrue(manager.IsCompleted(SequenceId));
        Assert.AreEqual(TutorialState.Completed,
                        new PlayerPrefsTutorialProgressStorage(SaveKey).GetState(SequenceId));
    }

    [UnityTest]
    public IEnumerator 완료된_시퀀스는_앱을_다시_켜도_시작하지_않는다()
    {
        var manager = BuildContainer();
        var completed = new List<string>();

        manager.SequenceCompleted += id => completed.Add(id);

        SpawnSequence();

        for (var i = 0; i < 120 && completed.Count == 0; i++) yield return null;

        Assert.AreEqual(1, completed.Count, "1회차에서 완료되지 않으면 2회차 검증이 무의미하다.");

        // 앱을 껐다 켠 상황을 흉내낸다 - 씬 오브젝트와 컨테이너를 버리고 같은 저장 키로 다시 만든다.
        DespawnAll();
        DisposeContainer();

        var restarted = BuildContainer();
        var started = new List<string>();

        restarted.SequenceStarted += id => started.Add(id);

        Assert.IsTrue(restarted.IsCompleted(SequenceId),
                      "완료는 PlayerPrefs에 남아 새 컨테이너에서도 읽혀야 한다.");

        SpawnSequence();

        for (var i = 0; i < 120; i++) yield return null;

        Assert.IsEmpty(started, "완료된 시퀀스는 다시 시작되면 안 된다.");
    }
}
