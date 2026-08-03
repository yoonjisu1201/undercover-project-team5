using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MapRenderingOptimizationEditor
{
    private const string PlayScenePath = "Assets/Scenes/PlayScene.unity";

    [MenuItem("Tools/Optimization/Apply Map Static Flags")]
    public static void ApplyMapStaticFlags()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != PlayScenePath)
        {
            Debug.LogError($"[Optimization] {PlayScenePath} 씬을 연 뒤 실행해야 합니다.");
            return;
        }

        GameObject map = GameObject.Find("Map");
        if (map == null)
        {
            Debug.LogError("[Optimization] Map 오브젝트를 찾지 못했습니다.");
            return;
        }

        ClearGeneratedStaticOverrides(map.transform.Find("B U I L D I N G S"));
        ClearGeneratedStaticOverrides(map.transform.Find("T I L E S "));
        ClearGeneratedStaticOverrides(map.transform.Find("T E R R A I N"));
        ClearGeneratedStaticOverrides(map.transform.Find("Plane"));

        int changedCount = ApplyLargeBuildingOccluders(map.transform.Find("B U I L D I N G S"));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Optimization] 대형 건물 Occlusion 플래그 적용 완료: {changedCount}개 Renderer 변경");
    }

    [MenuItem("Tools/Optimization/Bake Map Occlusion")]
    public static void BakeMapOcclusion()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != PlayScenePath)
        {
            Debug.LogError($"[Optimization] {PlayScenePath} 씬을 연 뒤 실행해야 합니다.");
            return;
        }

        if (!StaticOcclusionCulling.Compute())
        {
            Debug.LogError("[Optimization] Occlusion Culling Bake에 실패했습니다.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Optimization] Occlusion Culling Bake 완료");
    }

    private static int ApplyLargeBuildingOccluders(Transform root)
    {
        if (root == null)
        {
            return 0;
        }

        int changedCount = 0;
        StaticEditorFlags requiredFlags = StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (renderer.bounds.size.sqrMagnitude < 25f)
            {
                continue;
            }

            GameObject target = renderer.gameObject;
            StaticEditorFlags currentFlags = GameObjectUtility.GetStaticEditorFlags(target);
            StaticEditorFlags nextFlags = currentFlags | requiredFlags;
            if (currentFlags == nextFlags)
            {
                continue;
            }

            GameObjectUtility.SetStaticEditorFlags(target, nextFlags);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            changedCount++;
        }

        return changedCount;
    }

    private static void ClearGeneratedStaticOverrides(Transform root)
    {
        if (root == null)
        {
            return;
        }

        StaticEditorFlags generatedFlags =
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;

        foreach (Transform target in root.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            SerializedObject serializedObject = new(target.gameObject);
            SerializedProperty staticFlagsProperty = serializedObject.FindProperty("m_StaticEditorFlags");
            if (staticFlagsProperty != null && staticFlagsProperty.prefabOverride)
            {
                PrefabUtility.RevertPropertyOverride(staticFlagsProperty, InteractionMode.AutomatedAction);
                continue;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(target.gameObject))
            {
                continue;
            }

            StaticEditorFlags currentFlags = GameObjectUtility.GetStaticEditorFlags(target.gameObject);
            StaticEditorFlags nextFlags = currentFlags & ~generatedFlags;
            if (currentFlags != nextFlags)
            {
                GameObjectUtility.SetStaticEditorFlags(target.gameObject, nextFlags);
            }
        }
    }
}
