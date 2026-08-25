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

        // 그래프의 이동 노드가 상황별 속도(배회 2.2 / 소리 3.0 / 조명 3.5 / 추격 4.5)를 직접 넣으므로
        // 여기 speed 는 상한 역할만 한다.
        agent.speed = 4.5f;

        // 회전이 빠르면 코너에서 순간적으로 꺾여 "봤다"는 느낌 없이 붙는다. 느리게 둬서 돌아서는 게 보이게 한다.
        agent.angularSpeed = 200f;
        agent.acceleration = 10f;

        // 문틀을 지나야 하므로 반경을 넉넉히 잡으면 경로가 끊긴다.
        agent.radius = 0.4f;
        agent.stoppingDistance = 1.2f;

        // 다른 NPC와 서로 밀어내며 춤추는 것을 막는다. 보스는 한 마리라 회피가 필요 없다.
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

        NetworkTransform networkTransform = root.GetComponent<NetworkTransform>();
        if (networkTransform != null)
        {
            // 보스 위치 판정은 전부 서버에서 하므로 클라이언트가 트랜스폼을 쓰지 않게 한다.
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
        }
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
            brain.Graph = authoring.BuildRuntimeGraph(false);
        }

        // 서버가 스폰한 오브젝트의 소유자는 서버다. 이 옵션을 켜면 그래프가 서버에서만 돌아서
        // 클라이언트가 각자 다른 판단을 내리는 일이 없다.
        brain.NetcodeRunOnlyOnOwner = true;
    }

    private static void ConfigurePerception(GameObject root)
    {
        BossPerception perception = Require<BossPerception>(root);

        // 지하 모듈은 좁아서 시야를 길게 주면 방 하나를 통째로 훑는다.
        SetPrivateField(perception, "_sightRange", 16f);

        // 넓게 잡는다. 제자리에서 고개를 돌리는 대신, 복도를 따라 걷다 방향이 꺾이는 것만으로
        // 시야가 훑어지게 하려는 것이다. 좁으면 결국 도리도리로 메워야 한다.
        SetPrivateField(perception, "_sightAngle", 150f);
        // 프리팹 루트 스케일이 1.5라 모델 눈높이도 그만큼 올라간다. 1.6으로 두면 허리에서
        // 레이를 쏘는 셈이어서 낮은 지형지물에 쉽게 막힌다.
        SetPrivateField(perception, "_eyeHeight", 2.4f);

        // 시야를 막는 레이어에서 플레이어와 NPC가 올라가는 레이어는 빼야 서로를 가리지 않는다.
        // 지형(Ground)과 기본(Default) 레이어만 벽으로 취급한다.
        int blockers = (1 << LayerMask.NameToLayer("Default")) | (1 << LayerMask.NameToLayer("Ground"));
        SetPrivateField(perception, "_sightBlockers", (LayerMask)blockers);

        // 시야각 밖이라도 알아채는 거리. 시야(16)보다 짧게 둬서, 멀리서는 보고 있어야만 걸리고
        // 가까이서는 방향과 무관하게 걸리게 한다.
        SetPrivateField(perception, "_senseRadius", 14f);
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
