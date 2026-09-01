using Unity.Behavior;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// 보스 프리팹의 컴포넌트 구성과 수치를 코드로 맞춘다.
//
// 인스펙터에서 손으로 채워도 되지만, 값이 서로 맞물려 있어서(추격 속도 ↔ 달리기 판정 임계값 등)
// 한곳에 모아 두는 편이 어긋날 여지가 적다. 실행 후에는 평범한 프리팹이라 인스펙터에서 그대로 고칠 수 있다.
public static class BossPrefabBuilder
{
    private const string PrefabPath = "Assets/Prefabs/Npc/Boss/Boss_main.prefab";
    private const string GraphPath = "Assets/Behavior/BossBehavior.asset";
    private const string DefaultModelName = "Character_01_Model";

    // 라운드마다 골라 쓸 외형. 각 프리팹이 자기 Animator(AlienAnimator)와 Avatar를 들고 있어서
    // 리그가 서로 달라도 그대로 재생된다.
    private static readonly string[] VisualPrefabPaths =
    {
        "Assets/Prefabs/Npc/Alien/AlienVisual_01.prefab",
        "Assets/Prefabs/Npc/Alien/AlienVisual_02.prefab",
        "Assets/Prefabs/Npc/Alien/AlienVisual_03.prefab",
        "Assets/Prefabs/Npc/Alien/AlienVisual_04.prefab",
        "Assets/Prefabs/Npc/Alien/AlienVisual_05.prefab",
    };

    [MenuItem("Tools/Undercover/보스 프리팹 구성")]
    public static void Configure()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (asset == null)
        {
            Debug.LogError($"[보스 프리팹] '{PrefabPath}' 가 없습니다.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ConfigureNavAgent(root);
            ConfigureBrain(root);
            ConfigurePerception(root);
            Require<BossController>(root);
            ConfigureVisual(root);
            RemoveUnusedNetworkAnimator(root);
            ConfigureAttack(root);
            Require<BossDormancy>(root);
            Require<BossTargetMemory>(root);
            Require<BossDebugView>(root);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.Refresh();
        Debug.Log("[보스 프리팹] 구성을 마쳤습니다.", AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
    }

    private static void ConfigureNavAgent(GameObject root)
    {
        NavMeshAgent agent = Require<NavMeshAgent>(root);

        // 그래프의 이동 노드가 상황별 속도(수색 2.2 / 수색 2.8 / 기척 3.2 / 추격 4.2)를
        // 직접 넣으므로 여기 speed 는 상한 역할만 한다.
        agent.speed = 4.2f;

        // 큰 덩치가 갖는 관성. 이 두 값이 곧 "무겁게 움직인다"의 전부다.
        //
        // 회전(도/초): 표적이 지나쳐 가면 몸을 돌리는 데 시간이 걸려야 한다. 200 이면 한 바퀴를
        // 1.8초에 돌아서 사실상 즉시 꺾이고, 어떻게 피해도 똑같이 붙어 온다. 120 이면 180도
        // 돌아서는 데 1.5초가 걸려서, 지나쳐 달리는 것이 실제로 거리를 버는 수가 된다.
        //
        // 가속(m/s^2): 멈추고 다시 붙는 데 걸리는 시간이자 미끄러지는 거리다. 추격 속도 4.2 기준
        // 10 이면 0.4초에 멈춰서 브레이크가 없는 것과 같고, 4 면 약 1초에 걸쳐 2.2m 를 미끄러진다.
        // 방향을 바꿀 때도 같은 만큼 굼떠지므로 급회전이 저절로 무거워진다.
        agent.angularSpeed = 120f;
        agent.acceleration = 4f;

        // 문틀을 지나야 하므로 반경을 넉넉히 잡으면 경로가 끊긴다.
        agent.radius = 0.4f;
        agent.stoppingDistance = 1.2f;

        // 다른 NPC와 서로 밀어내며 춤추는 것을 막는다. 보스는 한 마리라 회피가 필요 없다.
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

        ConfigureBody(root);

        NetworkTransform networkTransform = root.GetComponent<NetworkTransform>();
        if (networkTransform != null)
        {
            // 보스 위치 판정은 전부 서버에서 하므로 클라이언트가 트랜스폼을 쓰지 않게 한다.
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
        }
    }

    // 콜라이더만 들고 있으면 물리 엔진이 보스를 정적 지형으로 다룬다.
    //
    // 그 지형을 NavMeshAgent 가 매 프레임 순간이동시키는 꼴이라, 플레이어와 겹치는 순간을
    // 접촉으로 풀지 못하고 겹침 해소로 밀어낸다. 겹침 해소는 이동이 아니라 위치를 직접
    // 보정하는 것이라, 플레이어가 연속 충돌 판정을 켜 두어도 벽을 그대로 통과한다.
    //
    // 운동학 Rigidbody 를 붙이면 움직이는 물체로 잡혀서 정상 접촉으로 밀어낸다.
    // 다운 상태에서는 PlayerMoveSample 이 IgnoreCollision 으로 접촉을 끊으므로 시신에는
    // 영향이 없다.
    private static void ConfigureBody(GameObject root)
    {
        Rigidbody body = Require<Rigidbody>(root);

        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;

        // 운동학 물체가 쓸 수 있는 유일한 연속 판정. 보스가 빠르게 지나갈 때도 접촉을 놓치지 않는다.
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private static void ConfigureBrain(GameObject root)
    {
        BehaviorGraphAgent brain = Require<BehaviorGraphAgent>(root);

        BehaviorAuthoringGraph authoring = AssetDatabase.LoadAssetAtPath<BehaviorAuthoringGraph>(GraphPath);
        if (authoring == null)
        {
            Debug.LogError($"[보스 프리팹] '{GraphPath}' 그래프를 찾지 못해 연결하지 못했습니다.");
        }
        else
        {
            // 반드시 강제로 다시 만든다(true).
            //
            // false 면 "바뀐 게 없다"고 판단될 때 예전 런타임 그래프를 그대로 돌려준다. 그래프를
            // 새로 만든 직후에는 변경 플래그가 이미 정리돼 있어서 이 경우에 걸리고, 결과적으로
            // 프리팹에는 옛 노드가 붙은 그래프가 그대로 남는다. 실제로 순찰 노드를 지웠는데도
            // 프리팹에는 계속 남아 있었다.
            brain.Graph = authoring.BuildRuntimeGraph(true);
        }

        // 서버가 스폰한 오브젝트의 소유자는 서버다. 이 옵션을 켜면 그래프가 서버에서만 돌아서
        // 클라이언트가 각자 다른 판단을 내리는 일이 없다.
        brain.NetcodeRunOnlyOnOwner = true;
    }

    private static void ConfigurePerception(GameObject root)
    {
        BossPerception perception = Require<BossPerception>(root);

        // 지하 모듈은 좁아서 시야를 길게 주면 방 하나를 통째로 훑는다.
        // 방 하나를 다 보지는 못하는 거리라야 엄폐물과 조명이 의미를 가진다.
        //
        // 이 값이 곧 '얼마나 달려야 시야를 끊을 수 있는가'다. 길게 잡으면 곧은 복도에서
        // 계속 보이는 채로 달리게 되고, 보이는 동안에는 기억이 매 틱 갱신돼 추격 제한 시간이
        // 아예 시작되지 않는다. 아무리 도망쳐도 떨어지지 않는 느낌이 여기서 나온다.
        SetPrivateField(perception, "_sightRange", 7f);

        // 넓게 잡는다. 제자리에서 고개를 돌리는 대신, 복도를 따라 걷다 방향이 꺾이는 것만으로
        // 시야가 훑어지게 하려는 것이다. 좁으면 결국 도리도리로 메워야 한다.
        // 다만 150도는 등 뒤만 빼고 다 보는 것과 같아서, 옆으로 비켜서는 것이 통하지 않았다.
        // 90도(좌우 45도씩)면 정면은 확실히 보되 옆으로 파고들어 벗어날 여지가 남는다.
        SetPrivateField(perception, "_sightAngle", 90f);
        // 프리팹 루트 스케일이 1.5라 모델 눈높이도 그만큼 올라간다. 1.6으로 두면 허리에서
        // 레이를 쏘는 셈이어서 낮은 지형지물에 쉽게 막힌다.
        SetPrivateField(perception, "_eyeHeight", 2.4f);

        // 시야를 막는 레이어에서 플레이어와 NPC가 올라가는 레이어는 빼야 서로를 가리지 않는다.
        // 지형(Ground)과 기본(Default) 레이어만 벽으로 취급한다.
        int blockers = (1 << LayerMask.NameToLayer("Default")) | (1 << LayerMask.NameToLayer("Ground"));
        SetPrivateField(perception, "_sightBlockers", (LayerMask)blockers);

        // 시야각 밖이라도 알아채는 거리. 시야(7)보다 짧게 둬서, 멀리서는 보고 있어야만 걸리고
        // 가까이서는 방향과 무관하게 걸리게 한다.
        // 등 뒤로 도는 것이 통해야 하므로 "바로 옆"일 때만 걸리는 거리까지 줄인다.
        SetPrivateField(perception, "_senseRadius", 4.5f);
    }

    // 외형마다 Animator가 달라져서 루트에 묶인 NetworkAnimator는 아무것도 동기화하지 않는다.
    // 남겨두면 기본 모델 리그만 동기화하려 들어 혼란만 준다.
    private static void RemoveUnusedNetworkAnimator(GameObject root)
    {
        NetworkAnimator networkAnimator = root.GetComponent<NetworkAnimator>();
        if (networkAnimator != null)
        {
            Object.DestroyImmediate(networkAnimator, true);
        }
    }

    private static void ConfigureAttack(GameObject root)
    {
        BossAttack attack = Require<BossAttack>(root);

        // 그래프의 이동 노드가 1.5까지 붙으므로 사거리를 그보다 넉넉히 둬야 붙은 자리에서 판정이 선다.
        SetPrivateField(attack, "_attackRange", 2.2f);

        // 에일리언보다 세게, 대신 느리게. 한 대 맞고 도망칠 여지는 남겨야 한다.
        SetPrivateField(attack, "_damage", 40f);
        SetPrivateField(attack, "_attackCooldown", 2f);

        // 플레이어는 Default 레이어에 있다. 여기서 좁혀도 PlayerHealth 로 한 번 더 걸러낸다.
        SetPrivateField(attack, "_hitLayers", (LayerMask)(1 << LayerMask.NameToLayer("Default")));
    }

    // 라운드별 외형 교체. 외형 프리팹 목록은 비워 두고 사용자가 인스펙터에서 채운다.
    // 목록이 비어 있으면 프리팹에 들어 있는 기본 모델을 그대로 쓴다.
    private static void ConfigureVisual(GameObject root)
    {
        BossVisual visual = Require<BossVisual>(root);

        Transform defaultModel = root.transform.Find(DefaultModelName);
        if (defaultModel == null)
        {
            Debug.LogError($"[보스 프리팹] 기본 모델 '{DefaultModelName}' 자식을 찾지 못했습니다.");
            return;
        }

        SetPrivateField(visual, "_defaultModel", defaultModel.gameObject);

        var serialized = new SerializedObject(visual);
        SerializedProperty models = serialized.FindProperty("_modelPrefabs");
        models.arraySize = VisualPrefabPaths.Length;
        for (int i = 0; i < VisualPrefabPaths.Length; i++)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPaths[i]);
            if (prefab == null)
            {
                Debug.LogError($"[보스 프리팹] 외형 '{VisualPrefabPaths[i]}' 를 찾지 못했습니다.");
            }

            models.GetArrayElementAtIndex(i).objectReferenceValue = prefab;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static T Require<T>(GameObject root) where T : Component
    {
        T component = root.GetComponent<T>();
        return component != null ? component : root.AddComponent<T>();
    }

    // 튜닝 값은 인스펙터에 노출된 private 필드라 SerializedObject 로 넣는다.
    private static void SetPrivateField(Component component, string fieldName, object value)
    {
        var serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogError($"[보스 프리팹] {component.GetType().Name} 에 '{fieldName}' 필드가 없습니다.");
            return;
        }

        switch (value)
        {
            case float floatValue:
                property.floatValue = floatValue;
                break;
            case LayerMask layerMask:
                property.intValue = layerMask.value;
                break;
            case Object objectValue:
                property.objectReferenceValue = objectValue;
                break;
            default:
                Debug.LogError($"[보스 프리팹] '{fieldName}' 에 넣을 수 없는 값입니다: {value}");
                return;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
