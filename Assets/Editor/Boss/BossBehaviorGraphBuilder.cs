using System;
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
    private const string VarSearchPosition = "Search Position";
    private const string VarWanderPosition = "Wander Position";

    // 이동 노드는 속도를 float 파라미터 하나로 애니메이터에 넘기는데, 이 프로젝트 애니메이터는
    // 걷기/달리기 bool 두 개를 쓴다. 비워 두면 노드가 애니메이터를 건드리지 않고,
    // BossController 가 대신 두 bool 을 채운다.
    private const string NoAnimatorSpeedParam = "";

    // 순간이동 주기(초). 너무 짧으면 어디 있는지 영원히 모르고, 너무 길면 한쪽 구석이 안전지대가 된다.
    private const float TeleportIntervalMin = 60f;
    private const float TeleportIntervalMax = 180f;

    // 이동 노드의 도착 판정 거리. BossAttack 의 사거리(2.2)보다 좁아야 멈춘 자리에서 바로 때린다.
    //
    // 이동 노드는 여기에 보스 콜라이더 반경(약 0.45)을 더해서 멈추므로 실제 정지 거리는 이 값보다
    // 크다. 1.5로 두면 1.95에서 멈춰 사거리까지 여유가 0.25밖에 없어서, 상대가 반 발짝만 물러나도
    // 사거리를 벗어나 이동 노드가 다시 돌았다. 그게 때리고 다시 자리를 잡는 것처럼 보였다.
    private const float AttackApproachDistance = 1f;

    // 교전이 끝났는지 다시 확인하는 간격(초).
    private const float EngagedRecheckInterval = 1f;

    // 잠든 동안 깨울 조건을 다시 확인하는 간격(초). 짧으면 반응이 빠르고 길면 검사가 싸다.
    private const float DormantCheckInterval = 0.25f;

    // 그래프 배치. 가지 하나가 가로로 Column 만큼 자리를 차지하고, 아래로 Row 씩 내려간다.
    // 손으로 옮기지 않아도 겹치지 않게 하려는 것이라 값 자체에 의미는 없다.
    private const float Column = 440f;
    private const float Row = 170f;

    // 추격·수색 속도. 사람 걷기(5)보다 느려서 직선으로 걸으면 벗어날 수 있지만, 모퉁이에서
    // 속도가 줄거나 미션에 잠깐 멈추면 붙는다. 5를 넘기면 걸어서는 절대 못 벗어나게 되므로
    // 그 아래가 상한이다.
    //
    // BossController 의 달리기 판정(3.7)이 이 값들 사이를 가른다. 추격(4.2)만 달리고 나머지는
    // 걷는다. 여기를 고치면 그쪽도 같이 봐야
    // 추격 중에 걷기 모션만 나오거나 수색 중에 달리기 모션이 나오는 일이 없다.
    private const float ChaseSpeed = 4.6f;

    // 눈으로 본 게 아니라 기척으로 알아챈 경우. 봤을 때보다 느려서 도망칠 틈이 있다.
    private const float NearChaseSpeed = 3.5f;

    // 흔적 주변을 뒤질 때. 가장 느려야 "찾고 있다"로 보인다.
    private const float SearchSpeed = 3f;

    // 단서 없이 훑고 다니는 기본 수색 속도. 예전 순찰 속도를 그대로 쓴다.
    private const float WanderSpeed = 3f;

    // 수색 지점마다 멈춰 서는 시간(초). 여기서 시야가 한 번 정착해야 "확인했다"가 된다.
    // 추격 중 멈춤과 달리 이건 보여야 하는 동작이라 조금 길게 둔다.
    private const float SearchLookDuration = 0.4f;

    // 수색을 포기한 뒤 쉬는 동안 조건을 다시 확인하는 간격(초).
    // 휴식 시간 자체는 BossTargetMemory 가 들고 있다.
    private const float RestTickInterval = 0.5f;

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
        VariableModel searchPosition = AddVariable<Vector3>(blackboard, VarSearchPosition, Vector3.zero);
        VariableModel wanderPosition = AddVariable<Vector3>(blackboard, VarWanderPosition, Vector3.zero);

        // 반응 트리와 순간이동 타이머는 서로를 기다릴 이유가 없어서 나란히 돌린다.
        BehaviorGraphNodeModel start = CreateNode(graph, "On Start", new Vector2(0f, Row * -2f));
        BehaviorGraphNodeModel parallel = CreateNode(graph, "Run In Parallel", new Vector2(0f, -Row));
        Connect(start, parallel);

        BuildReactionTree(graph, parallel, self, targetSurvivor, searchPosition, wanderPosition);
        BuildTeleportLoop(graph, parallel, self);

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
        VariableModel searchPosition,
        VariableModel wanderPosition)
    {
        BehaviorGraphNodeModel repeat = CreateNode(graph, "Repeat", new Vector2(0f, 0f));
        BehaviorGraphNodeModel selector = CreateNode(graph, "Try In Order", new Vector2(0f, Row));
        Connect(parallel, repeat);
        Connect(repeat, selector);

        // 가지를 왼쪽부터 우선순위 순으로 늘어놓는다. 위에서 아래로 읽으면 그대로 판단 순서다.
        float top = Row * 2f;

        // 0순위: 잠복(기본 꺼짐). 켜면 라운드 초반에 제자리에 서 있는다.
        BuildDormantBranch(graph, selector, self, new Vector2(Column * -2.5f, top));

        // 1순위: 눈으로 봤다 → 붙어서 때린다. 안 보이게 되는 즉시 끊긴다(Self 감시).
        BuildChaseBranch(graph, selector, self, targetSurvivor,
            new Vector2(Column * -1.5f, top), "Sees Survivor", ChaseSpeed);

        // 2순위: 시야각 밖이라도 가까이 있으면 알아챈다. 등 뒤를 스쳐 지나가도 걸리게 하는 가지다.
        // 봤을 때보다 조금 느려서 도망칠 틈이 있다.
        BuildChaseBranch(graph, selector, self, targetSurvivor,
            new Vector2(Column * -0.5f, top), "Survivor Is Near", NearChaseSpeed);

        // 3순위: 단서를 쫓는다. 눈으로 본 자리든 소리가 난 자리든 흔적 하나로 모여 있다.
        //
        // 소리 전용 가지를 따로 두지 않는다. 예전에는 소리를 그래프에서 직접 받아 그 좌표로
        // 걸어갔는데, 그러면 단서가 두 곳(기억 / 소리)으로 갈려서 보스가 소리 쪽으로 갔다가
        // 오래된 목격 지점으로 되돌아가는 왕복이 생겼다. 지금은 BossTargetMemory 가 소리를
        // 받아 흔적으로 남기므로, 쫓을 대상이 언제나 하나다.
        BuildTraceBranch(graph, selector, self, searchPosition, new Vector2(Column * 0.6f, top));

        // 4순위: 포기한 직후면 한 박자 선다.
        BuildRestBranch(graph, selector, self, new Vector2(Column * 1.4f, top));

        // 5순위: 보스의 기본 상태. 단서가 없으면 여기로 떨어져 계속 훑고 다닌다.
        // 조건이 항상 참이라 위 가지가 전부 실패하면 반드시 여기가 돈다.
        BuildWanderBranch(graph, selector, self, wanderPosition, new Vector2(Column * 2.2f, top));
    }

    // 단서(흔적)를 쫓는 가지. 흔적까지 걸어갔다가 도착하면 그 주위를 뒤진다.
    //
    // 흔적이 어디서 왔는지는 여기서 따지지 않는다. 눈으로 본 자리든 소리가 난 자리든
    // BossTargetMemory 가 같은 흔적으로 들고 있고, 조건 노드가 지금 향할 지점을 내준다.
    // 도착 판정과 주변 수색 지점 선택도 전부 그쪽이 한다.
    private static void BuildTraceBranch(
        BehaviorAuthoringGraph graph,
        BehaviorGraphNodeModel selector,
        VariableModel self,
        VariableModel searchPosition,
        Vector2 position)
    {
        BehaviorGraphNodeModel guard = CreateGuard(graph, position);
        ConditionModel remembers = AddCondition(guard, "Remembers Trace");
        remembers.SetField("Agent", self, typeof(GameObject));
        remembers.SetField("SearchPosition", searchPosition, typeof(Vector3));
        Connect(selector, guard);

        // 양방향 감시(Self + LowerPriority)를 건다.
        //
        // LowerPriority: 훑고 다니는 도중에 단서가 생기면 그 자리에서 끊고 이쪽으로 넘어온다.
        //
        // Self: 조건이 매 틱 평가되게 하려는 것이다. Remembers Trace 는 평가될 때마다 지금 향할
        // 지점을 Blackboard 에 써 주는데, 이 감시가 없으면 가지에 들어갈 때 한 번만 평가된다.
        // 그러면 걸어가는 도중에 흔적이 반대편으로 옮겨가도 원래 목적지까지 간 뒤에야 알아챈다.
        // 흔적이 만료되는 즉시 가지가 끊기는 것도 같이 얻는다.
        EnableAbort(guard, ObserverAbortTarget.Both);

        BehaviorGraphNodeModel sequence = CreateNode(graph, "Sequence", position + new Vector2(0f, Row));
        Connect(guard, sequence);

        BehaviorGraphNodeModel nav = CreateNode(graph, "Navigate To Location", position + new Vector2(0f, Row * 2f));
        nav.SetField("Agent", self, typeof(GameObject));
        nav.SetField("Location", searchPosition, typeof(Vector3));
        nav.SetField("Speed", SearchSpeed);
        nav.SetField("AnimatorSpeedParam", NoAnimatorSpeedParam);
        Connect(sequence, nav);

        BehaviorGraphNodeModel pause = CreateNode(graph, "Wait (Seconds)", position + new Vector2(0f, Row * 3f));
        pause.SetField("SecondsToWait", SearchLookDuration);
        Connect(sequence, pause);
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

        BehaviorGraphNodeModel wait = CreateNode(graph, "Wait (Seconds)", position + new Vector2(0f, Row));
        wait.SetField("SecondsToWait", DormantCheckInterval);
        Connect(guard, wait);
    }

    // 흔적 주변을 다 뒤졌는데도 못 찾았을 때의 휴식. 짧게 기다리기만 해서 제자리에 서 있고,
    // 매번 성공으로 끝나 조건을 다시 평가한다. 휴식이 끝나면 조건이 거짓이 되어 기본 수색으로 내려간다.
    //
    // 포기한 자리에서 곧바로 기본 수색으로 넘어가면, 숨어 있던 쪽에서는 보스가 계속 돌아다니는 것과
    // 구분이 안 된다. 한 박자 멈췄다가 걸어 나가야 "포기하고 갔다"를 읽고 나올 틈이 생긴다.
    //
    // 이 가지가 서 있는 동안 BossController 의 굳음 감시에 걸리지 않도록,
    // 그쪽에서 휴식 상태를 예외로 빼 두었다.
    private static void BuildRestBranch(
        BehaviorAuthoringGraph graph,
        BehaviorGraphNodeModel selector,
        VariableModel self,
        Vector2 position)
    {
        BehaviorGraphNodeModel guard = CreateGuard(graph, position);
        ConditionModel resting = AddCondition(guard, "Is Resting");
        resting.SetField("Agent", self, typeof(GameObject));
        Connect(selector, guard);

        // 수색 가지는 계속 도는 가지라, 감시를 켜지 않으면 이미 그쪽으로 내려간 뒤에는
        // 이 가지로 올라올 기회가 없다. 그러면 포기 직후의 한 박자가 통째로 건너뛰어진다.
        EnableLowerPriorityAbort(guard);

        BehaviorGraphNodeModel wait = CreateNode(graph, "Wait (Seconds)", position + new Vector2(0f, Row));
        wait.SetField("SecondsToWait", RestTickInterval);
        Connect(guard, wait);
    }

    // 보스의 기본 상태(idle)인 수색. 한 걸음(5~12m)씩 갈 곳을 정해 걸어가는 것을 반복한다.
    //
    // 순찰(Patrol) 노드를 이걸로 대체했다. 순찰은 방마다 놓아둔 웨이포인트를 정해진 순서로 도는데,
    // 목적지가 미리 정해져 있다는 게 문제였다. 보스가 그 점까지 한 번에 걸어가 버려서 가는 길에
    // 아무것도 살피지 않고, 마침 그 직선이 숨은 사람 쪽이면 단서 없이 찾아오는 것처럼 보였다.
    //
    // 지점 선택은 BossTargetMemory 가 하고, 여기서는 그 지점으로 걸어갔다 아주 짧게 쉬는 것을
    // 반복할 뿐이다. 지점마다 서서 고개를 돌리지 않는다. 제자리 회전은 "찾는 중"이 아니라
    // "고장난 것"으로 보인다. 방향이 튀지 않게 하는 것도 지점을 뽑는 쪽에서 처리한다(진행 방향 ±60도).
    private static void BuildWanderBranch(
        BehaviorAuthoringGraph graph,
        BehaviorGraphNodeModel selector,
        VariableModel self,
        VariableModel wanderPosition,
        Vector2 position)
    {
        BehaviorGraphNodeModel guard = CreateGuard(graph, position);
        ConditionModel wander = AddCondition(guard, "Wander");
        wander.SetField("Agent", self, typeof(GameObject));
        wander.SetField("WanderPosition", wanderPosition, typeof(Vector3));
        Connect(selector, guard);

        BehaviorGraphNodeModel sequence = CreateNode(graph, "Sequence", position + new Vector2(0f, Row));
        Connect(guard, sequence);

        BehaviorGraphNodeModel nav = CreateNode(graph, "Navigate To Location", position + new Vector2(0f, Row * 2f));
        nav.SetField("Agent", self, typeof(GameObject));
        nav.SetField("Location", wanderPosition, typeof(Vector3));
        nav.SetField("Speed", WanderSpeed);
        nav.SetField("AnimatorSpeedParam", NoAnimatorSpeedParam);
        Connect(sequence, nav);

        BehaviorGraphNodeModel pause = CreateNode(graph, "Wait (Seconds)", position + new Vector2(0f, Row * 3f));
        pause.SetField("SecondsToWait", SearchLookDuration);
        Connect(sequence, pause);
    }

    // 시야/근접처럼 "대상을 특정했다"는 감지는 이후 동작이 같으므로 한 함수로 만든다.
    //
    // 이 가지는 "보이는 동안 붙어서 때린다"만 한다. 놓친 뒤의 처리(흔적까지 이동, 주변 수색)는
    // 위 단계의 흔적 가지가 맡는다. 예전에는 이 안에 흔적·소리 처리가 전부 중첩돼 있었는데,
    // 그러면 사람을 한 번도 본 적 없을 때(소리만 들은 경우) 흔적을 쫓을 방법이 없었다.
    private static void BuildChaseBranch(
        BehaviorAuthoringGraph graph,
        BehaviorGraphNodeModel selector,
        VariableModel self,
        VariableModel targetSurvivor,
        Vector2 position,
        string conditionName,
        float speed)
    {
        BehaviorGraphNodeModel guard = CreateGuard(graph, position);
        LinkDetectionCondition(AddCondition(guard, conditionName), self, targetSurvivor);
        Connect(selector, guard);

        // 양방향 감시(Self + LowerPriority)를 건다.
        //
        // LowerPriority: 흔적을 쫓거나 훑고 다니는 중에 사람이 보이면 그 자리에서 끊고 넘어온다.
        //
        // Self: 안 보이게 되는 즉시 추격을 끊는다. 이게 없으면 안쪽의 Navigate To Target 이
        // 살아남는다. 그 노드는 매 프레임 표적의 '현재' 트랜스폼을 읽어 SetDestination 을 다시
        // 거는데, 보이는지 들리는지는 전혀 보지 않고 사거리에 닿아야만 끝난다. 그래서 한 번
        // 걸리면 시야가 끊겨도 숨은 자리까지 그대로 걸어온다.
        EnableAbort(guard, ObserverAbortTarget.Both);

        // 보고 있는 동안은 이 가지를 놓지 않는다. 안쪽 Sequence 가 한 번 실패할 때마다
        // (공격 쿨다운 등) 아래 가지로 내려갔다 감시에 걸려 다시 올라오는 왕복을 막는다.
        BehaviorGraphNodeModel repeatWhile = CreateRepeatWhile(graph, position + new Vector2(0f, Row));
        LinkDetectionCondition(AddCondition(repeatWhile, conditionName), self, targetSurvivor);
        Connect(guard, repeatWhile);

        BehaviorGraphNodeModel chase = CreateNode(graph, "Sequence", position + new Vector2(0f, Row * 2f));
        Connect(repeatWhile, chase);

        // 이동 노드는 표적이 움직이면 목적지를 스스로 다시 잡는다. 그래서 별도 재탐색 노드가 없다.
        // 도착 판정 거리를 공격 사거리보다 좁게 둬야, 멈춘 자리에서 바로 때릴 수 있다.
        BehaviorGraphNodeModel navigate = CreateNode(graph, "Navigate To Target", position + new Vector2(0f, Row * 3f));
        navigate.SetField("Agent", self, typeof(GameObject));
        navigate.SetField("Target", targetSurvivor, typeof(GameObject));
        navigate.SetField("Speed", speed);
        navigate.SetField("DistanceThreshold", AttackApproachDistance);
        navigate.SetField("AnimatorSpeedParam", NoAnimatorSpeedParam);
        Connect(chase, navigate);

        // 붙은 다음에 "때릴까 놓아줄까"를 고른다. 이동 노드 뒤에 있어야 하는 이유는,
        // 사거리에 들어온 뒤라야 "몰렸다"가 확정되기 때문이다. 그 전에 물어보면 아직
        // 빠져나갈 수 있는 사람까지 놓아주게 된다.
        BehaviorGraphNodeModel decide = CreateNode(graph, "Try In Order", position + new Vector2(0f, Row * 4f));
        Connect(chase, decide);

        // 막다른 곳에 몰아넣은 참이면 낮은 확률로 그냥 지나친다.
        BehaviorGraphNodeModel spareGuard = CreateGuard(graph, position + new Vector2(-Column * 0.35f, Row * 5f));
        ConditionModel spare = AddCondition(spareGuard, "Should Spare");
        spare.SetField("Agent", self, typeof(GameObject));
        spare.SetField("Survivor", targetSurvivor, typeof(GameObject));
        Connect(decide, spareGuard);

        BehaviorGraphNodeModel giveUp = CreateNode(graph, "Give Up Chase", position + new Vector2(-Column * 0.35f, Row * 6f));
        giveUp.SetField("Agent", self, typeof(GameObject));
        Connect(spareGuard, giveUp);

        // 그 외에는 때린다. 쿨다운 중에는 붙어 있는 동안 기다리고, 사거리를 벗어나면 끝난다.
        BehaviorGraphNodeModel attack = CreateNode(graph, "Attack Survivor", position + new Vector2(Column * 0.35f, Row * 5f));
        attack.SetField("Agent", self, typeof(GameObject));
        attack.SetField("Survivor", targetSurvivor, typeof(GameObject));
        Connect(decide, attack);
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
        VariableModel self)
    {
        BehaviorGraphNodeModel repeat = CreateNode(graph, "Repeat", new Vector2(Column * 3.4f, 0f));
        Connect(parallel, repeat);

        BehaviorGraphNodeModel sequence = CreateNode(graph, "Sequence", new Vector2(Column * 3.4f, Row));
        Connect(repeat, sequence);

        // 깨어날 때까지는 주기 자체를 시작하지 않는다.
        BehaviorGraphNodeModel waitForWake = CreateRepeatWhile(graph, new Vector2(Column * 3.4f, Row * 2f));
        ConditionModel dormant = AddCondition(waitForWake, "Is Dormant");
        dormant.SetField("Agent", self, typeof(GameObject));
        Connect(sequence, waitForWake);

        BehaviorGraphNodeModel wakeTick = CreateNode(graph, "Wait (Seconds)", new Vector2(Column * 3.4f, Row * 3f));
        wakeTick.SetField("SecondsToWait", DormantCheckInterval);
        Connect(waitForWake, wakeTick);

        BehaviorGraphNodeModel wait = CreateNode(graph, "Wait (Range) (Seconds)", new Vector2(Column * 3.4f, Row * 4f));
        wait.SetField("Min", TeleportIntervalMin);
        wait.SetField("Max", TeleportIntervalMax);
        Connect(sequence, wait);

        // 교전이 끝날 때까지 1초씩 확인하며 기다린다. 조건이 거짓이면 즉시 통과한다.
        BehaviorGraphNodeModel waitForCalm = CreateRepeatWhile(graph, new Vector2(Column * 3.4f, Row * 5f));
        ConditionModel engaged = AddCondition(waitForCalm, "Is Engaged");
        engaged.SetField("Agent", self, typeof(GameObject));
        Connect(sequence, waitForCalm);

        BehaviorGraphNodeModel calmTick = CreateNode(graph, "Wait (Seconds)", new Vector2(Column * 3.4f, Row * 6f));
        calmTick.SetField("SecondsToWait", EngagedRecheckInterval);
        Connect(waitForCalm, calmTick);

        BehaviorGraphNodeModel teleport = CreateNode(graph, "Teleport To Distant Module", new Vector2(Column * 3.4f, Row * 7f));
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
    // 이게 없으면 보스가 사람을 무시한다. 수색 가지는 끝나지 않고 계속 Running 이라서,
    // Try In Order 가 한 번 수색으로 내려가면 위쪽 감지 가지를 다시 평가할 기회가 없다.
    //
    // Guard 가 Try In Order 에 이어진 뒤에 호출해야 한다. 연결 전에는 Modifier 모드로 확정되지 않아
    // OnValidate 가 ObserverType 을 None 으로 되돌린다.
    private static void EnableLowerPriorityAbort(BehaviorGraphNodeModel node)
        => EnableAbort(node, ObserverAbortTarget.LowerPriority);

    private static void EnableAbort(BehaviorGraphNodeModel node, ObserverAbortTarget target)
    {
        var guard = (ConditionalGuardNodeModel)node;
        if (!guard.CanUseObserverAbort())
        {
            throw new InvalidOperationException("Guard 가 Try In Order 에 연결되기 전에 감시를 켤 수 없습니다.");
        }

        guard.ObserverType = target;
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
