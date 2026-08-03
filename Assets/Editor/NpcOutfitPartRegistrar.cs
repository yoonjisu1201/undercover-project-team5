#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// NPC 베이스 프리팹에 새 옷 파츠를 영구 등록하는 에디터 도구.
/// SkinnedMeshRenderer 파츠는 본을 이름으로 매칭해 베이스 스켈레톤에 리바인딩하고, 자신이 들고 온 복제 스켈레톤은 버린다.
/// 등록은 에디터에서 한 번만 실행되고 결과가 프리팹 에셋에 저장되므로, 런타임(게임 실행/로딩)에는 비용이 없다.
/// 메뉴: Tools ▸ NPC ▸ Outfit Part Registrar
/// </summary>
public class NpcOutfitPartRegistrar : EditorWindow
{
    private enum OutfitCategory
    {
        Beard, Eyebrow, Glasses, Hair, Hat, Headphone, LeftArm, RightArm, Mask, Pants, Shoes, Torso
    }

    private static readonly Dictionary<OutfitCategory, string> FieldNameByCategory = new()
    {
        { OutfitCategory.Beard, "_beardList" },
        { OutfitCategory.Eyebrow, "_eyebrowList" },
        { OutfitCategory.Glasses, "_glassesList" },
        { OutfitCategory.Hair, "_hairList" },
        { OutfitCategory.Hat, "_hatList" },
        { OutfitCategory.Headphone, "_headPhoneList" },
        { OutfitCategory.LeftArm, "_leftArmList" },
        { OutfitCategory.RightArm, "_rightArmList" },
        { OutfitCategory.Mask, "_maskList" },
        { OutfitCategory.Pants, "_pantsList" },
        { OutfitCategory.Shoes, "_shoesList" },
        { OutfitCategory.Torso, "_torsoList" },
    };

    // 프리팹 이름을 '_'로 나눈 토큰 중 하나가 이 키워드와 일치하면 해당 카테고리로 분류한다.
    private static readonly (string keyword, OutfitCategory category)[] CategoryKeywords =
    {
        ("BEARD", OutfitCategory.Beard),
        ("EYEBROWS", OutfitCategory.Eyebrow),
        ("EYEBROW", OutfitCategory.Eyebrow),
        ("GLASSES", OutfitCategory.Glasses),
        ("HAIR", OutfitCategory.Hair),
        ("HAT", OutfitCategory.Hat),
        ("HEADPHONE", OutfitCategory.Headphone),
        ("HEADSET", OutfitCategory.Headphone),
        ("MASK", OutfitCategory.Mask),
        ("PANTS", OutfitCategory.Pants),
        ("SHOES", OutfitCategory.Shoes),
        ("TORSO", OutfitCategory.Torso),
    };

    private static readonly string[] ArmOrHandKeywords = { "ARM", "HAND", "GLOVE", "GLOVES" };
    private static readonly string[] LeftKeywords = { "LEFT", "L" };
    private static readonly string[] RightKeywords = { "RIGHT", "R" };

    private GameObject _baseNpcPrefab;
    private DefaultAsset _sourceFolder;
    private GameObject _sourcePartPrefab;
    private OutfitCategory _category;

    [MenuItem("Tools/NPC/Outfit Part Registrar")]
    private static void Open()
    {
        GetWindow<NpcOutfitPartRegistrar>("NPC 파츠 등록");
    }

    private void OnGUI()
    {
        _baseNpcPrefab = (GameObject)EditorGUILayout.ObjectField("베이스 NPC 프리팹", _baseNpcPrefab, typeof(GameObject), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("폴더 전체 일괄 등록", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "폴더 안의 모든 프리팹을 스캔해서, 이름에 들어간 키워드(BEARD/EYEBROWS/TORSO 등)로 카테고리를 자동 판별해 한 번에 등록합니다.\n" +
            "카테고리를 못 알아낸 프리팹은 건너뛰고 목록으로 알려줍니다.",
            MessageType.Info);
        _sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField("파츠가 들어있는 폴더", _sourceFolder, typeof(DefaultAsset), false);

        using (new EditorGUI.DisabledScope(_baseNpcPrefab == null || _sourceFolder == null))
        {
            if (GUILayout.Button("폴더 전체 스캔해서 일괄 등록"))
            {
                RegisterFolder(_baseNpcPrefab, AssetDatabase.GetAssetPath(_sourceFolder));
            }
        }

        EditorGUILayout.Space(20);
        EditorGUILayout.LabelField("파츠 하나만 수동 등록 (카테고리 자동 판별이 틀렸을 때)", EditorStyles.boldLabel);
        _sourcePartPrefab = (GameObject)EditorGUILayout.ObjectField("등록할 파츠 프리팹", _sourcePartPrefab, typeof(GameObject), false);
        _category = (OutfitCategory)EditorGUILayout.EnumPopup("카테고리", _category);

        using (new EditorGUI.DisabledScope(_baseNpcPrefab == null || _sourcePartPrefab == null))
        {
            if (GUILayout.Button("이 파츠만 등록"))
            {
                GameObject contentsRoot = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(_baseNpcPrefab));
                try
                {
                    RegisterOne(contentsRoot, _sourcePartPrefab, _category);
                    PrefabUtility.SaveAsPrefabAsset(contentsRoot, AssetDatabase.GetAssetPath(_baseNpcPrefab));
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contentsRoot);
                }
            }
        }
    }

    private static void RegisterFolder(GameObject baseNpcPrefab, string folderPath)
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
        string basePrefabPath = AssetDatabase.GetAssetPath(baseNpcPrefab);
        GameObject contentsRoot = PrefabUtility.LoadPrefabContents(basePrefabPath);

        int registeredCount = 0;
        int duplicateCount = 0;
        List<string> unrecognizedNames = new();

        try
        {
            foreach (string guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                if (sourcePrefab == null || !TryDetectCategory(sourcePrefab.name, out OutfitCategory category))
                {
                    unrecognizedNames.Add(sourcePrefab != null ? sourcePrefab.name : prefabPath);
                    continue;
                }

                if (IsAlreadyRegistered(contentsRoot, category, sourcePrefab.name))
                {
                    duplicateCount++;
                    continue;
                }

                if (RegisterOne(contentsRoot, sourcePrefab, category))
                {
                    registeredCount++;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(contentsRoot, basePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contentsRoot);
        }

        Debug.Log($"[NpcOutfitPartRegistrar] 일괄 등록 완료 | 등록 {registeredCount}개, 중복 스킵 {duplicateCount}개, 카테고리 인식 실패 {unrecognizedNames.Count}개");
        if (unrecognizedNames.Count > 0)
        {
            Debug.LogWarning($"[NpcOutfitPartRegistrar] 카테고리를 못 알아낸 프리팹들(수동 등록 필요):\n{string.Join("\n", unrecognizedNames)}");
        }
    }

    // 프리팹 하나를 로드된 프리팹 콘텐츠(contentsRoot)에 붙인다. 저장은 호출한 쪽에서 한다.
    private static bool RegisterOne(GameObject contentsRoot, GameObject sourcePartPrefab, OutfitCategory category)
    {
        NpcOutfitController outfitController = contentsRoot.GetComponent<NpcOutfitController>();
        if (outfitController == null)
        {
            Debug.LogError("[NpcOutfitPartRegistrar] 베이스 프리팹 루트에서 NpcOutfitController를 찾지 못했습니다.");
            return false;
        }

        Transform skeletonRoot = FindSkeletonRoot(contentsRoot.transform);
        if (skeletonRoot == null)
        {
            Debug.LogError("[NpcOutfitPartRegistrar] 베이스 프리팹에서 'Armature'를 찾지 못했습니다.");
            return false;
        }

        Dictionary<string, Transform> boneByName = BuildBoneMap(skeletonRoot);
        Transform attachParent = FindAttachParent(outfitController, contentsRoot.transform, category);

        GameObject registeredObject = InstantiateAndAttach(sourcePartPrefab, boneByName, attachParent);
        if (registeredObject == null)
        {
            return false;
        }

        registeredObject.name = sourcePartPrefab.name;
        registeredObject.SetActive(false);
        AppendToArrayField(outfitController, category, registeredObject);

        Debug.Log($"[NpcOutfitPartRegistrar] '{sourcePartPrefab.name}'을(를) {category} 카테고리에 등록했습니다.");
        return true;
    }

    // 이름의 '_' 토큰들을 보고 카테고리를 추측한다. 팔/손 계열은 LEFT/RIGHT(or L/R) 토큰으로 좌우를 구분한다.
    private static bool TryDetectCategory(string prefabName, out OutfitCategory category)
    {
        string[] tokens = prefabName.ToUpperInvariant().Split('_');

        if (tokens.Any(t => ArmOrHandKeywords.Contains(t)))
        {
            if (tokens.Any(t => RightKeywords.Contains(t)))
            {
                category = OutfitCategory.RightArm;
                return true;
            }

            // 좌우 표기가 없으면 왼팔로 취급한다 (필요하면 수동 등록으로 오른팔에 따로 등록).
            category = OutfitCategory.LeftArm;
            return true;
        }

        foreach ((string keyword, OutfitCategory mappedCategory) in CategoryKeywords)
        {
            if (tokens.Contains(keyword))
            {
                category = mappedCategory;
                return true;
            }
        }

        category = default;
        return false;
    }

    private static bool IsAlreadyRegistered(GameObject contentsRoot, OutfitCategory category, string sourceName)
    {
        NpcOutfitController outfitController = contentsRoot.GetComponent<NpcOutfitController>();
        SerializedObject serializedController = new(outfitController);
        SerializedProperty arrayProperty = serializedController.FindProperty(FieldNameByCategory[category]);

        if (arrayProperty == null)
        {
            return false;
        }

        for (int index = 0; index < arrayProperty.arraySize; index++)
        {
            GameObject existing = arrayProperty.GetArrayElementAtIndex(index).objectReferenceValue as GameObject;
            if (existing != null && existing.name == sourceName)
            {
                return true;
            }
        }

        return false;
    }

    private static Transform FindSkeletonRoot(Transform root)
    {
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == "Armature")
            {
                return candidate;
            }
        }

        return null;
    }

    private static Dictionary<string, Transform> BuildBoneMap(Transform skeletonRoot)
    {
        Dictionary<string, Transform> boneByName = new();

        foreach (Transform bone in skeletonRoot.GetComponentsInChildren<Transform>(true))
        {
            boneByName.TryAdd(bone.name, bone);
        }

        return boneByName;
    }

    // 같은 카테고리의 기존 파츠가 있으면 그 부모 밑에, 없으면 NPC 루트 밑에 붙인다.
    private static Transform FindAttachParent(NpcOutfitController outfitController, Transform npcRoot, OutfitCategory category)
    {
        SerializedObject serializedController = new(outfitController);
        SerializedProperty arrayProperty = serializedController.FindProperty(FieldNameByCategory[category]);

        if (arrayProperty != null && arrayProperty.arraySize > 0)
        {
            GameObject existing = arrayProperty.GetArrayElementAtIndex(0).objectReferenceValue as GameObject;
            if (existing != null && existing.transform.parent != null)
            {
                return existing.transform.parent;
            }
        }

        return npcRoot;
    }

    // 소스 파츠를 임시로 생성해서, 스킨 파츠는 본을 리바인딩한 뒤 렌더러만 떼어 붙이고 복제 스켈레톤은 버린다.
    // 고정 소품(MeshRenderer만 있는 파츠)은 그대로 붙인다.
    private static GameObject InstantiateAndAttach(GameObject sourcePrefab, Dictionary<string, Transform> boneByName, Transform attachParent)
    {
        GameObject spawnedRoot = Instantiate(sourcePrefab);
        SkinnedMeshRenderer skinnedRenderer = spawnedRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (skinnedRenderer != null)
        {
            GameObject rendererObject = skinnedRenderer.gameObject;
            rendererObject.transform.SetParent(attachParent, false);
            RebindBones(skinnedRenderer, boneByName);
            DestroyImmediate(spawnedRoot);
            return rendererObject;
        }

        MeshFilter meshFilter = spawnedRoot.GetComponentInChildren<MeshFilter>(true);
        if (meshFilter == null)
        {
            Debug.LogError($"[NpcOutfitPartRegistrar] '{sourcePrefab.name}'에서 Renderer를 찾지 못했습니다.");
            DestroyImmediate(spawnedRoot);
            return null;
        }

        spawnedRoot.transform.SetParent(attachParent, false);
        return spawnedRoot;
    }

    private static void RebindBones(SkinnedMeshRenderer skinnedRenderer, Dictionary<string, Transform> boneByName)
    {
        Transform[] originalBones = skinnedRenderer.bones;
        Transform[] reboundBones = new Transform[originalBones.Length];

        for (int index = 0; index < originalBones.Length; index++)
        {
            Transform originalBone = originalBones[index];

            if (originalBone != null && boneByName.TryGetValue(originalBone.name, out Transform matchedBone))
            {
                reboundBones[index] = matchedBone;
            }
            else
            {
                Debug.LogWarning($"[NpcOutfitPartRegistrar] '{originalBone?.name}' 본을 베이스 스켈레톤에서 찾지 못했습니다.");
            }
        }

        skinnedRenderer.bones = reboundBones;

        if (skinnedRenderer.rootBone != null && boneByName.TryGetValue(skinnedRenderer.rootBone.name, out Transform matchedRoot))
        {
            skinnedRenderer.rootBone = matchedRoot;
        }
    }

    private static void AppendToArrayField(NpcOutfitController outfitController, OutfitCategory category, GameObject newElement)
    {
        SerializedObject serializedController = new(outfitController);
        SerializedProperty arrayProperty = serializedController.FindProperty(FieldNameByCategory[category]);

        if (arrayProperty == null)
        {
            Debug.LogError($"[NpcOutfitPartRegistrar] NpcOutfitController에서 '{FieldNameByCategory[category]}' 필드를 찾지 못했습니다.");
            return;
        }

        int newIndex = arrayProperty.arraySize;
        arrayProperty.arraySize++;
        arrayProperty.GetArrayElementAtIndex(newIndex).objectReferenceValue = newElement;
        serializedController.ApplyModifiedProperties();
    }
}
#endif
