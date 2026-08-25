using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Behavior;
using Unity.Behavior.GraphFramework;
using UnityEditor;
using UnityEngine;

// 보스 Behavior 그래프를 코드로 조립한다.
//
// 그래프 에디터에서 손으로 노드를 20개 놓고 조건과 변수를 다 연결하는 것보다,
// 설계를 코드로 남겨 두면 구조를 바꿀 때 다시 만들기가 쉽다.
// 만든 뒤에는 평범한 그래프 자산이라 에디터에서 그대로 열어 고칠 수 있다.
//
// 주의: 다시 실행하면 그래프의 노드를 전부 지우고 새로 만든다. 에디터에서 손으로 고친 내용은 사라진다.
public static class BossBehaviorGraphBuilder
{
    private const string GraphPath = "Assets/Behavior/BossBehavior.asset";

    // Blackboard 변수 이름. 노드 연결과 이름이 어긋나면 조용히 실패하므로 상수로 묶어 둔다.
    // BossController 의 같은 이름 필드와 맞아야 런타임에 값이 들어온다.
    private const string VarTargetSurvivor = "Target Survivor";
    private const string VarNoisePosition = "Noise Position";
    private const string VarHearingFactor = "Hearing Factor";
    private const string VarPatrolWaypoints = "Patrol Waypoints";
    private const string VarSearchPosition = "Search Position";

    // 이동 노드는 속도를 float 파라미터 하나로 애니메이터에 넘기는데, 이 프로젝트 애니메이터는
    // 걷기/달리기 bool 두 개를 쓴다. 비워 두면 노드가 애니메이터를 건드리지 않고,
    // BossController 가 대신 두 bool 을 채운다.
    private const string NoAnimatorSpeedParam = "";

    // 순간이동 주기(초). 너무 짧으면 어디 있는지 영원히 모르고, 너무 길면 한쪽 구석이 안전지대가 된다.
    private const float TeleportIntervalMin = 60f;
    private const float TeleportIntervalMax = 180f;

    // 이동 노드의 도착 판정 거리. BossAttack 의 사거리(기본 2)보다 좁아야 멈춘 자리에서 바로 때린다.
    private const float AttackApproachDistance = 1.5f;

    // 교전이 끝났는지 다시 확인하는 간격(초).
    private const float EngagedRecheckInterval = 1f;

    // 잠든 동안 깨울 조건을 다시 확인하는 간격(초). 짧으면 반응이 빠르고 길면 검사가 싸다.
    private const float DormantCheckInterval = 0.25f;

    // 수색 이동 속도. 추격보다 느려야 "찾고 있다"로 보이고, 도망칠 틈도 생긴다.
    // BossController 의 달리기 판정(3.2)보다 낮아야 걷기 모션이 나온다.
    private const float SearchSpeed = 2.8f;

    // 소리 지점에 도착했을 때의 짧은 멈춤(초). 다음 소리를 기다리는 한 박자다.
    private const float SearchLookDuration = 0.4f;

    // 수색 지점에 도착했을 때의 아주 짧은 멈춤(초). 0으로 두면 노드가 실패한다.
    private const float SearchPassDuration = 0.2f;

    // 소리를 따라온 지점에서의 짧은 멈춤(초).
    private const float NoiseLookDuration = 0.5f;

    [MenuItem("Tools/Undercover/보스 Behavior 그래프 생성")]
    public static void Build()
    {
        BehaviorAuthoringGraph graph = AssetDatabase.LoadAssetAtPath<BehaviorAuthoringGraph>(GraphPath);
        if (graph == null)
        {
            Debug.LogError($"[보스 그래프] '{GraphPath}' 에 Behavior 그래프 자산이 없습니다.");
            return;
        }

        graph.Nodes.Clear();
        graph.EnsureAssetHasBlackboard();

        BlackboardAsset blackboard = graph.Blackboard;
        blackboard.Variables.Clear();
        GraphAssetProcessor.EnsureBlackboardGraphOwnerVariable(blackboard);

        VariableModel self = blackboard.Variables.First();
        VariableModel targetSurvivor = AddVariable<GameObject>(blackboard, VarTargetSurvivor, null);
        VariableModel noisePosition = AddVariable<Vector3>(blackboard, VarNoisePosition, Vector3.zero);
        VariableModel hearingFactor = AddVariable<float>(blackboard, VarHearingFactor, 1f);
        VariableModel waypoints = AddVariable<List<GameObject>>(blackboard, VarPatrolWaypoints, new List<GameObject>());
        VariableModel searchPosition = AddVariable<Vector3>(blackboard, VarSearchPosition, Vector3.zero);

        // 반응 트리와 순간이동 타이머는 서로를 기다릴 이유가 없어서 나란히 돌린다.
        BehaviorGraphNodeModel start = CreateNode(graph, "On Start", new Vector2(0f, -320f));
        BehaviorGraphNodeModel parallel = CreateNode(graph, "Run In Parallel", new Vector2(0f, -160f));
        Connect(start, parallel);

        BuildReactionTree(graph, parallel, self, targetSurvivor, noisePosition, searchPosition, hearingFactor, waypoints);
        BuildTeleportLoop(graph, parallel, self, hearingFactor);

        graph.SetAssetDirty(true);
        graph.ValidateAsset();
        graph.RebuildAndSave();
        AssetDatabase.SaveAssets();

        Debug.Log($"[보스 그래프] 노드 {graph.Nodes.Count}개, 변수 {blackboard.Variables.Count}개로 다시 만들었습니다.", graph);
        Selection.activeObject = graph;
    }

    // 우선순위 반응 트리. Try In Order 는 위 가지가 실패할 때만 아래로 내려가므로 왼쪽부터 급한 반응이 온다.
    private static void BuildReactionTree(
        BehaviorAuthoringGraph graph,
        BehaviorGraphNodeModel parallel,
        VariableModel self,
        VariableModel targetSurvivor,
        VariableModel noisePosition,
        VariableModel searchPosition,
        VariableModel hearingFactor,
        VariableModel waypoints)
    {
        BehaviorGraphNodeModel repeat = CreateNode(graph, "Repeat", new Vector2(-220f, 0f));
        BehaviorGraphNodeModel selector = CreateNode(graph, "Try In Order", new Vector2(-220f, 160f));
        Connect(parallel, repeat);
        Connect(repeat, selector);

        // 0순위: 아직 잠들어 있으면 아무것도 하지 않는다. 감지보다 위에 둬야
        // 잠든 보스가 멀리 있는 사람을 보고 일어나 버리는 일이 없다.
        BuildDormantBranch(graph, selector, self, new Vector2(-1320f, 340f));

        // 1순위: 눈으로 봤다 → 시야에서 사라질 때까지 쫓는다.
        BuildChaseBranch(graph, selector, self, targetSurvivor, searchPosition, noisePosition, hearingFactor,
            new Vector2(-880f, 340f), "Sees Survivor", 4.5f);

        // 2순위: 시야각 밖이라도 가까이 있으면 알아챈다. 등 뒤를 스쳐 지나가도 걸리게 하는 가지다.
        // 봤을 때보다 조금 느려서 도망칠 틈이 있다.
        BuildChaseBranch(graph, selector, self, targetSurvivor, searchPosition, noisePosition, hearingFactor,
            new Vector2(-440f, 340f), "Survivor Is Near", 3.5f);

        // 3순위: 소리를 들었다 → 그 지점까지 가서 두리번거린다. 사람이 아니라 위치를 쫓는다.
        // 소리에는 쿨다운을 걸지 않는다. 조우 뒤 쉬는 동안에도 소음은 따라와야 숨어 있는 의미가 생긴다.
        BehaviorGraphNodeModel noiseGuard = CreateGuard(graph, new Vector2(0f, 340f));
        ConditionModel hears = AddCondition(noiseGuard, "Hears Noise");
        hears.SetField("Agent", self, typeof(GameObject));
        hears.SetField("Radius", hearingFactor, typeof(float));
        hears.SetField("NoisePosition", noisePosition, typeof(Vector3));
        Connect(selector, noiseGuard);
        EnableLowerPriorityAbort(noiseGuard);

        BehaviorGraphNodeModel noiseSequence = CreateNode(graph, "Sequence", new Vector2(0f, 500f));
        Connect(noiseGuard, noiseSequence);

        BehaviorGraphNodeModel noiseNav = CreateNode(graph, "Navigate To Location", new Vector2(0f, 660f));
        noiseNav.SetField("Agent", self, typeof(GameObject));
        noiseNav.SetField("Location", noisePosition, typeof(Vector3));
        noiseNav.SetField("Speed", 3f);
        noiseNav.SetField("AnimatorSpeedParam", NoAnimatorSpeedParam);
        Connect(noiseSequence, noiseNav);

        // 도착해서 고개를 돌리지 않는다. 제자리 회전은 "찾는 중"이 아니라 "고장난 것"으로 보이고,
        // 소리가 계속 나면 이동이 즉시 끝나 회전만 반복된다. 대신 다음 소리로 계속 걸어간다.
        // 시야각을 150°로 넓혀둔 것이 둘러보는 역할을 대신한다.
        BehaviorGraphNodeModel noiseLook = CreateNode(graph, "Wait (Seconds)", new Vector2(0f, 820f));
        noiseLook.SetField("SecondsToWait", NoiseLookDuration);
        Connect(noiseSequence, noiseLook);

        // 4순위: 아무 단서도 없으면 배회한다. 이 가지는 조건이 없어서 항상 성공한다.
        BehaviorGraphNodeModel patrol = CreateNode(graph, "Patrol", new Vector2(300f, 340f));
        patrol.SetField("Agent", self, typeof(GameObject));
        patrol.SetField("Waypoints", waypoints, typeof(List<GameObject>));
        patrol.SetField("Speed", 2.2f);
        patrol.SetField("WaypointWaitTime", 2f);
        patrol.SetField("AnimatorSpeedParam", NoAnimatorSpeedParam);
        Connect(selector, patrol);
    }

    // 잠복. 짧게 기다리기만 해서 제자리에 서 있고, 매번 성공으로 끝나 조건을 다시 평가한다.
    // 잠든 동안 깨울 조건을 계속 확인해야 하므로 여기서 붙잡고 있으면 안 된다.
    //
    // Observer Abort 를 켜지 않는다. 이 가지는 이미 최우선이라 끊을 아래 가지가 없고,
    // 깨어나면 조건이 거짓이 되어 자연히 다음 가지로 내려간다.
    private static void BuildDormantBranch(
        BehaviorAuthoringGraph graph,
        BehaviorGraphNodeModel selector,
        VariableModel self,
        Vector2 position)
    {
        BehaviorGraphNodeModel guard = CreateGuard(graph, position);
        ConditionModel dormant = AddCondition(guard, "Is Dormant");
        dormant.SetField("Agent", self, typeof(GameObject));
        Connect(selector, guard);

        BehaviorGraphNodeModel wait = CreateNode(graph, "Wait (Seconds)", position + new Vector2(0f, 160f));
        wait.SetField("SecondsToWait", DormantCheckInterval);
        Connect(guard, wait);
    }

    // 시야/조명처럼 "대상을 특정했다"는 감지는 이후 동작이 같으므로 한 함수로 만든다.
    //
    // 추격을 "보이는 동안"이 아니라 "기억하는 동안"으로 묶는다. 시야가 끊기는 즉시 끝내면
    // 기둥 뒤로 한 발 비킨 것과 완전히 도망친 것이 같아져서, 잠깐 숨었다 나오면 보스가
    // 조우 쿨다운에 걸려 눈앞의 사람을 무시한다.
    //
    // 조우 쿨다운은 두지 않는다. 어떤 형태로 넣어도 "감지했지만 무시한다"는 구간이 생겨서,
    // 눈앞에 서 있는데 보스가 지나가 버리는 일이 났다.
    //
    // 겹 구조의 이유:
    //  - Repeat While [Remembers Survivor] 는 기억이 살아 있는 동안 반복하고 만료되면 종료한다.
    //  - 안쪽 Try In Order 가 매 주기 "보이나?"를 다시 물어서, 보이면 추격 / 안 보이면 수색으로 갈린다.
    private static void BuildChaseBranch(
        BehaviorAuthoringGraph graph,
        BehaviorGraphNodeModel selector,
        VariableModel self,
        VariableModel targetSurvivor,
        VariableModel searchPosition,
        VariableModel noisePosition,
        VariableModel hearingFactor,
        Vector2 position,
        string conditionName,
        float speed)
    {
        BehaviorGraphNodeModel guard = CreateGuard(graph, position);
        LinkDetectionCondition(AddCondition(guard, conditionName), self, targetSurvivor);
        Connect(selector, guard);
        EnableLowerPriorityAbort(guard);

        BehaviorGraphNodeModel repeatWhile = CreateRepeatWhile(graph, position + new Vector2(0f, 160f));
        ConditionModel remembers = AddCondition(repeatWhile, "Remembers Survivor");
        remembers.SetField("Agent", self, typeof(GameObject));
        remembers.SetField("Survivor", targetSurvivor, typeof(GameObject));
        remembers.SetField("SearchPosition", searchPosition, typeof(Vector3));
        Connect(guard, repeatWhile);

        BehaviorGraphNodeModel branch = CreateNode(graph, "Try In Order", position + new Vector2(0f, 320f));
        Connect(repeatWhile, branch);

        // 보이면 붙어서 때린다.
        BehaviorGraphNodeModel chaseGuard = CreateGuard(graph, position + new Vector2(-150f, 480f));
        LinkDetectionCondition(AddCondition(chaseGuard, conditionName), self, targetSurvivor);
        Connect(branch, chaseGuard);

        // 수색 중에 다시 보이면 그 자리에서 끊고 추격으로 돌아와야 한다.
        EnableLowerPriorityAbort(chaseGuard);

        BehaviorGraphNodeModel chase = CreateNode(graph, "Sequence", position + new Vector2(-150f, 640f));
        Connect(chaseGuard, chase);

        // 이동 노드는 표적이 움직이면 목적지를 스스로 다시 잡는다. 그래서 별도 재탐색 노드가 없다.
        // 도착 판정 거리를 공격 사거리보다 조금 좁게 둬야, 멈춘 자리에서 바로 때릴 수 있다.
        BehaviorGraphNodeModel navigate = CreateNode(graph, "Navigate To Target", position + new Vector2(-150f, 800f));
        navigate.SetField("Agent", self, typeof(GameObject));
        navigate.SetField("Target", targetSurvivor, typeof(GameObject));
        navigate.SetField("Speed", speed);
        navigate.SetField("DistanceThreshold", AttackApproachDistance);
        navigate.SetField("AnimatorSpeedParam", NoAnimatorSpeedParam);
        Connect(chase, navigate);

        // 붙었으면 때린다. 사거리 밖이거나 쿨다운이면 실패하고 위의 반복이 다시 돌면서 이동으로 돌아간다.
        BehaviorGraphNodeModel attack = CreateNode(graph, "Attack Survivor", position + new Vector2(-150f, 960f));
        attack.SetField("Agent", self, typeof(GameObject));
        attack.SetField("Survivor", targetSurvivor, typeof(GameObject));
        Connect(chase, attack);

        // 놓친 동안 소리가 나면 오래된 목격 지점보다 그쪽이 우선이다. 사람이 사라졌어도
        // 방금 난 소리가 지금 위치를 더 잘 알려준다.
        BehaviorGraphNodeModel chaseNoiseGuard = CreateGuard(graph, position + new Vector2(180f, 480f));
        ConditionModel chaseHears = AddCondition(chaseNoiseGuard, "Hears Noise");
        chaseHears.SetField("Agent", self, typeof(GameObject));
        chaseHears.SetField("Radius", hearingFactor, typeof(float));
        chaseHears.SetField("NoisePosition", noisePosition, typeof(Vector3));
        Connect(branch, chaseNoiseGuard);

        // 수색 중에 소리가 나면 그 자리에서 끊고 소리 쪽으로 돌린다.
        EnableLowerPriorityAbort(chaseNoiseGuard);

        BehaviorGraphNodeModel chaseNoise = CreateNode(graph, "Sequence", position + new Vector2(180f, 640f));
        Connect(chaseNoiseGuard, chaseNoise);

        BehaviorGraphNodeModel chaseNoiseNav = CreateNode(graph, "Navigate To Location", position + new Vector2(180f, 800f));
        chaseNoiseNav.SetField("Agent", self, typeof(GameObject));
        chaseNoiseNav.SetField("Location", noisePosition, typeof(Vector3));
        chaseNoiseNav.SetField("Speed", SearchSpeed);
        chaseNoiseNav.SetField("AnimatorSpeedParam", NoAnimatorSpeedParam);
        Connect(chaseNoise, chaseNoiseNav);

        BehaviorGraphNodeModel chaseNoiseLook = CreateNode(graph, "Wait (Seconds)", position + new Vector2(180f, 960f));
        chaseNoiseLook.SetField("SecondsToWait", SearchLookDuration);
        Connect(chaseNoise, chaseNoiseLook);

        // 아무 소리도 없으면 마지막으로 본 지점을 뒤진다. 조건 노드가 매 주기 지점을 갱신하고, 도착하면
        // 다음 지점이 새로 뽑히므로 이 두 노드의 반복만으로 주변을 돌아다니는 수색이 된다.
        // 지점마다 오래 서 있으면 수색이 아니라 멈춘 것으로 보이니 훑어보는 시간은 짧게 둔다.
        BehaviorGraphNodeModel search = CreateNode(graph, "Sequence", position + new Vector2(520f, 480f));
        Connect(branch, search);

        BehaviorGraphNodeModel searchNav = CreateNode(graph, "Navigate To Location", position + new Vector2(520f, 640f));
        searchNav.SetField("Agent", self, typeof(GameObject));
        searchNav.SetField("Location", searchPosition, typeof(Vector3));
        searchNav.SetField("Speed", SearchSpeed);
        searchNav.SetField("AnimatorSpeedParam", NoAnimatorSpeedParam);
        Connect(search, searchNav);

        // 그냥 기다리면 도착한 방향만 보고 서 있어서 등 뒤의 사람을 영원히 못 본다. 몸을 돌려 훑는다.
        // 지점마다 멈춰서 고개를 돌리면 도리도리가 과하게 보인다. 수색은 이동으로만 하고,
        // 시야 확인은 복도를 따라 걷다 방향이 꺾이는 것으로 자연히 이루어지게 한다.
        // 시야각을 넓게 잡아둔 것이 이 역할을 대신한다.
        BehaviorGraphNodeModel searchPause = CreateNode(graph, "Wait (Seconds)", position + new Vector2(520f, 800f));
        searchPause.SetField("SecondsToWait", SearchPassDuration);
        Connect(search, searchPause);
    }

    // 주기적으로 먼 모듈로 옮긴다. 반응 트리와 나란히 돌아서 추격 중에도 시간이 흐른다.
    //
    // 단, 교전 중에는 옮기지 않는다. 쫓기는 도중에 보스가 사라지면 쫓기는 쪽에서는 이유를 알 수
    // 없는 일이라 긴장이 풀린다. 시간이 됐어도 교전이 끝날 때까지 기다린 뒤에 옮긴다.
    // 순간이동 주기는 보스가 깨어난 뒤부터 센다. 잠복 중에 옮겨가면, 아직 아무도 만나지 못한
    // 보스가 스스로 자리를 바꿔서 스폰 위치를 입구에서 멀리 잡아둔 의미가 사라진다.
    private static void BuildTeleportLoop(
        BehaviorAuthoringGraph graph,
        BehaviorGraphNodeModel parallel,
        VariableModel self,
        VariableModel hearingFactor)
    {
        BehaviorGraphNodeModel repeat = CreateNode(graph, "Repeat", new Vector2(700f, 0f));
        Connect(parallel, repeat);

        BehaviorGraphNodeModel sequence = CreateNode(graph, "Sequence", new Vector2(700f, 160f));
        Connect(repeat, sequence);

        // 깨어날 때까지는 주기 자체를 시작하지 않는다.
        BehaviorGraphNodeModel waitForWake = CreateRepeatWhile(graph, new Vector2(700f, 320f));
        ConditionModel dormant = AddCondition(waitForWake, "Is Dormant");
        dormant.SetField("Agent", self, typeof(GameObject));
        Connect(sequence, waitForWake);

        BehaviorGraphNodeModel wakeTick = CreateNode(graph, "Wait (Seconds)", new Vector2(700f, 480f));
        wakeTick.SetField("SecondsToWait", DormantCheckInterval);
        Connect(waitForWake, wakeTick);

        BehaviorGraphNodeModel wait = CreateNode(graph, "Wait (Range) (Seconds)", new Vector2(700f, 640f));
        wait.SetField("Min", TeleportIntervalMin);
        wait.SetField("Max", TeleportIntervalMax);
        Connect(sequence, wait);

        // 교전이 끝날 때까지 1초씩 확인하며 기다린다. 조건이 거짓이면 즉시 통과한다.
        BehaviorGraphNodeModel waitForCalm = CreateRepeatWhile(graph, new Vector2(700f, 800f));
        ConditionModel engaged = AddCondition(waitForCalm, "Is Engaged");
        engaged.SetField("Agent", self, typeof(GameObject));
        engaged.SetField("Radius", hearingFactor, typeof(float));
        Connect(sequence, waitForCalm);

        BehaviorGraphNodeModel calmTick = CreateNode(graph, "Wait (Seconds)", new Vector2(700f, 960f));
        calmTick.SetField("SecondsToWait", EngagedRecheckInterval);
        Connect(waitForCalm, calmTick);

        BehaviorGraphNodeModel teleport = CreateNode(graph, "Teleport To Distant Module", new Vector2(700f, 1120f));
        teleport.SetField("Agent", self, typeof(GameObject));
        Connect(sequence, teleport);
    }

    // 시야·조명 조건은 둘 다 "누구를 봤는지"를 같은 변수에 써 준다.
    private static void LinkDetectionCondition(
        ConditionModel condition, VariableModel self, VariableModel targetSurvivor)
    {
        condition.SetField("Agent", self, typeof(GameObject));
        condition.SetField("Survivor", targetSurvivor, typeof(GameObject));
    }

    private static BehaviorGraphNodeModel CreateGuard(BehaviorAuthoringGraph graph, Vector2 position)
    {
        BehaviorGraphNodeModel node = CreateNode(graph, "Conditional Guard (Modifier)", position);
        var guard = (ConditionalGuardNodeModel)node;

        // 생성자가 Action 모드로 두기 때문에 직접 바꿔야 한다.
        // Modifier 모드여야 조건이 통과할 때만 자식을 돌리고, 실패하면 Try In Order 가 다음 가지로 넘어간다.
        guard.Mode = ConditionalGuardNodeModel.GuardMode.Modifier;
        guard.RequiresAllConditionsTrue = true;
        guard.OnValidate();
        return node;
    }

    private static BehaviorGraphNodeModel CreateRepeatWhile(BehaviorAuthoringGraph graph, Vector2 position)
    {
        BehaviorGraphNodeModel node = CreateNode(graph, "Repeat While", position);
        var repeat = (RepeatNodeModel)node;
        repeat.Mode = RepeatNodeModel.RepeatMode.Condition;
        repeat.RequiresAllConditionsTrue = true;

        // 조건이 끊기는 것은 "놓쳤다"이지 실패가 아니다. 성공으로 끝내야 Cooldown 이 정상 종료로 받아
        // 그 시점부터 대기 시간을 잡는다.
        repeat.ReturnFailureOnConditionFail = false;
        repeat.OnValidate();
        return node;
    }

    // 아래 우선순위 가지가 돌고 있어도 조건을 계속 감시해서, 참이 되면 그 가지를 끊고 이쪽으로 넘어온다.
    //
    // 이게 없으면 보스가 사람을 무시한다. 배회(Patrol) 노드는 끝나지 않고 계속 Running 이라서,
    // Try In Order 가 한 번 배회로 내려가면 위쪽 감지 가지를 다시 평가할 기회가 없다.
    //
    // Guard 가 Try In Order 에 이어진 뒤에 호출해야 한다. 연결 전에는 Modifier 모드로 확정되지 않아
    // OnValidate 가 ObserverType 을 None 으로 되돌린다.
    private static void EnableLowerPriorityAbort(BehaviorGraphNodeModel node)
    {
        var guard = (ConditionalGuardNodeModel)node;
        if (!guard.CanUseObserverAbort())
        {
            throw new InvalidOperationException("Guard 가 Try In Order 에 연결되기 전에 감시를 켤 수 없습니다.");
        }

        guard.ObserverType = ObserverAbortTarget.LowerPriority;
    }

    private static ConditionModel AddCondition(BehaviorGraphNodeModel node, string conditionName)
    {
        Type conditionType = ConditionUtility.GetConditionTypes()
            .FirstOrDefault(type => type.GetCustomAttributes(typeof(ConditionAttribute), false)
                .Cast<ConditionAttribute>()
                .Any(attribute => attribute.Name == conditionName));

        if (conditionType == null)
        {
            throw new InvalidOperationException($"'{conditionName}' 조건 노드를 찾을 수 없습니다.");
        }

        ConditionInfo info = ConditionUtility.GetInfoForConditionType(conditionType);
        var condition = (Condition)Activator.CreateInstance(conditionType);
        ConditionModel model = new ConditionModel(node, condition, info);
        ((IConditionalNodeModel)node).ConditionModels.Add(model);
        return model;
    }

    private static BehaviorGraphNodeModel CreateNode(BehaviorAuthoringGraph graph, string nodeName, Vector2 position)
    {
        NodeInfo info = Unity.Behavior.NodeRegistry.NodeInfos.FirstOrDefault(candidate => candidate.Name == nodeName);
        if (info == null)
        {
            throw new InvalidOperationException($"'{nodeName}' 노드를 찾을 수 없습니다.");
        }

        return (BehaviorGraphNodeModel)graph.CreateNode(info.ModelType, position, null, new object[] { info });
    }

    private static void Connect(NodeModel parent, NodeModel child)
    {
        if (!parent.TryDefaultOutputPortModel(out PortModel output) ||
            !child.TryDefaultInputPortModel(out PortModel input))
        {
            throw new InvalidOperationException("기본 포트가 없어 연결할 수 없습니다.");
        }

        output.ConnectTo(input);
    }

    private static VariableModel AddVariable<T>(BlackboardAsset blackboard, string name, T value)
    {
        var variable = new TypedVariableModel<T> { Name = name, m_Value = value };
        blackboard.Variables.Add(variable);
        return variable;
    }
}
