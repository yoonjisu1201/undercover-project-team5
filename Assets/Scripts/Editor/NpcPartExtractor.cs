using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// NPC 프리팹이 파츠 915개를 자식으로 들고 있어서, NPC 한 마리를 만들 때마다 916개
// SkinnedMeshRenderer를 생성한 뒤 안 쓰는 905개를 지우고 있었다.
// 이 도구가 파츠를 개별 프리팹으로 분리하고 NpcPartCatalog를 만들어, 런타임에 뽑힌 파츠만
// 생성할 수 있게 한다.
//
// 메뉴를 1 → 2 → 3 순서로 실행한다. 3번은 프리팹에서 파츠 자식을 지우는 되돌릴 수 없는 단계라,
// 2번 검증과 실제 플레이 확인을 통과한 뒤에 실행한다.
public static class NpcPartExtractor {
	private const string TargetPrefabPath = "Assets/Prefabs/Npc/NPC_main_BugTest.prefab";
	private const string PartsFolder = "Assets/Imported/Modular 3D Characters Vol.1/Prefabs/NPC_Parts";
	private const string CatalogPath = "Assets/Prefabs/Npc/NpcPartCatalog.asset";
	private const string PartRootName = "HumanVisual";
	private const string SkeletonRootName = "Armature";

	// 파츠 자식이 지워진 뒤에도 남아야 하는 오브젝트.
	private static readonly string[] KeepAlways = { SkeletonRootName, "M3CPCV2_HEAD_01" };

	private static readonly NpcPartSlot[] Slots = (NpcPartSlot[])Enum.GetValues(typeof(NpcPartSlot));

	[MenuItem("Tools/Undercover/NPC 파츠 1. 추출 + 카탈로그 생성")]
	private static void Extract() {
		Dictionary<NpcPartSlot, List<string>> partPathsBySlot = new();
		string[] boneNames = null;
		string rootBoneName = null;

		// 폴더는 에셋 편집을 멈추기 전에 만들어야 한다. StartAssetEditing 중에는 생성이 지연돼
		// 아직 없는 폴더에 프리팹을 저장하려다 실패할 수 있다.
		Dictionary<NpcPartSlot, string> folderBySlot = new();
		foreach (NpcPartSlot slot in Slots) {
			folderBySlot[slot] = CreateFolder(PartsFolder, slot.ToString());
		}

		// 1단계: 파츠를 개별 프리팹으로 저장한다.
		GameObject prefabRoot = PrefabUtility.LoadPrefabContents(TargetPrefabPath);

		try {
			if (!TryResolvePrefab(prefabRoot, out NpcOutfitController controller, out _, out _)) {
				return;
			}

			if (!TryCaptureSkeleton(controller, out boneNames, out rootBoneName)) {
				return;
			}

			int savedCount = 0;
			int totalCount = CountParts(controller);

			AssetDatabase.StartAssetEditing();

			try {
				foreach (NpcPartSlot slot in Slots) {
					string slotFolder = folderBySlot[slot];
					List<string> paths = new();

					foreach (GameObject source in ReadPartList(controller, slot)) {
						EditorUtility.DisplayProgressBar("NPC 파츠 추출", $"{slot} / {source.name}", ++savedCount / (float)totalCount);
						paths.Add(SavePartPrefab(source, slotFolder));

						// 카탈로그 경로로 동작하는 중간 상태에서 원본 자식이 겹쳐 보이지 않게 끈다.
						source.SetActive(false);
					}

					partPathsBySlot[slot] = paths;
				}
			}
			finally {
				AssetDatabase.StopAssetEditing();
				EditorUtility.ClearProgressBar();
			}

			PrefabUtility.SaveAsPrefabAsset(prefabRoot, TargetPrefabPath);
		}
		finally {
			PrefabUtility.UnloadPrefabContents(prefabRoot);
		}

		AssetDatabase.Refresh();

		// 2단계: 저장된 파츠 프리팹을 불러와 카탈로그를 채운다.
		NpcPartCatalog catalog = LoadOrCreateCatalog();

		foreach (NpcPartSlot slot in Slots) {
			List<string> paths = partPathsBySlot[slot];
			GameObject[] parts = new GameObject[paths.Count];

			for (int index = 0; index < paths.Count; index++) {
				parts[index] = AssetDatabase.LoadAssetAtPath<GameObject>(paths[index]);

				if (parts[index] == null) {
					Debug.LogError($"[NpcPartExtractor] 저장한 파츠 프리팹을 다시 불러오지 못했습니다: {paths[index]}");
					return;
				}
			}

			WriteField(catalog, NpcPartCatalog.GetPartListFieldName(slot), parts);
		}

		WriteField(catalog, NpcPartCatalog.BoneNamesFieldName, boneNames);
		WriteField(catalog, NpcPartCatalog.RootBoneNameFieldName, rootBoneName);
		EditorUtility.SetDirty(catalog);

		// 3단계: 프리팹이 카탈로그를 쓰도록 연결한다.
		GameObject reloaded = PrefabUtility.LoadPrefabContents(TargetPrefabPath);

		try {
			if (!TryResolvePrefab(reloaded, out NpcOutfitController controller, out Transform partRoot, out Transform skeletonRoot)) {
				return;
			}

			WriteField(controller, "_partCatalog", catalog);
			WriteField(controller, "_partRoot", partRoot);
			WriteField(controller, "_skeletonRoot", skeletonRoot);
			PrefabUtility.SaveAsPrefabAsset(reloaded, TargetPrefabPath);
		}
		finally {
			PrefabUtility.UnloadPrefabContents(reloaded);
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		int extracted = 0;
		foreach (NpcPartSlot slot in Slots) {
			extracted += partPathsBySlot[slot].Count;
		}

		Debug.Log($"[NpcPartExtractor] 파츠 {extracted}개를 '{PartsFolder}'에 추출하고 카탈로그를 만들었습니다.\n" +
		          $"본 {boneNames.Length}개 (루트 '{rootBoneName}')를 기록했고, '{TargetPrefabPath}'가 카탈로그를 쓰도록 연결했습니다.\n" +
		          "다음으로 '2. 검증'을 실행하세요.");
	}

	[MenuItem("Tools/Undercover/NPC 파츠 2. 검증")]
	private static void Verify() {
		NpcPartCatalog catalog = AssetDatabase.LoadAssetAtPath<NpcPartCatalog>(CatalogPath);

		if (catalog == null) {
			Debug.LogError($"[NpcPartExtractor] 카탈로그가 없습니다: {CatalogPath}. '1. 추출'을 먼저 실행하세요.");
			return;
		}

		GameObject prefabRoot = PrefabUtility.LoadPrefabContents(TargetPrefabPath);
		int errors = 0;

		try {
			if (!TryResolvePrefab(prefabRoot, out NpcOutfitController controller, out _, out Transform skeletonRoot)) {
				return;
			}

			// 스켈레톤 본 이름이 실제 스켈레톤에서 모두 찾아지는지.
			HashSet<string> boneNamesInSkeleton = new();
			foreach (Transform bone in skeletonRoot.GetComponentsInChildren<Transform>(true)) {
				boneNamesInSkeleton.Add(bone.name);
			}

			foreach (string boneName in catalog.BoneNames) {
				if (!boneNamesInSkeleton.Contains(boneName)) {
					Debug.LogError($"[검증] 스켈레톤에 본 '{boneName}'이 없습니다.");
					errors++;
				}
			}

			if (!boneNamesInSkeleton.Contains(catalog.RootBoneName)) {
				Debug.LogError($"[검증] 스켈레톤에 루트 본 '{catalog.RootBoneName}'이 없습니다.");
				errors++;
			}

			// 슬롯별 개수와 이름 순서가 원본 리스트와 일치하는지.
			foreach (NpcPartSlot slot in Slots) {
				GameObject[] sources = ReadPartList(controller, slot);
				GameObject[] parts = catalog.GetParts(slot);

				if (sources.Length == 0) {
					Debug.Log($"[검증] {slot}: 원본 리스트가 비어 있어 순서 대조를 건너뜁니다 (카탈로그 {parts.Length}개).");
					continue;
				}

				if (sources.Length != parts.Length) {
					Debug.LogError($"[검증] {slot}: 개수 불일치 — 원본 {sources.Length} vs 카탈로그 {parts.Length}");
					errors++;
					continue;
				}

				for (int index = 0; index < parts.Length; index++) {
					if (parts[index] == null) {
						Debug.LogError($"[검증] {slot}[{index}]: 카탈로그 항목이 비어 있습니다.");
						errors++;
						continue;
					}

					if (parts[index].name != sources[index].name) {
						Debug.LogError($"[검증] {slot}[{index}]: 순서 불일치 — 원본 '{sources[index].name}' vs 카탈로그 '{parts[index].name}'");
						errors++;
					}
				}
			}
		}
		finally {
			PrefabUtility.UnloadPrefabContents(prefabRoot);
		}

		// 파츠 프리팹 자체가 런타임에 쓸 수 있는 상태인지.
		int checkedParts = 0;
		foreach (NpcPartSlot slot in Slots) {
			foreach (GameObject part in catalog.GetParts(slot)) {
				if (part == null) {
					continue;
				}

				checkedParts++;

				if (!part.activeSelf) {
					Debug.LogError($"[검증] {part.name}: 비활성 상태로 저장돼 생성해도 보이지 않습니다.", part);
					errors++;
				}

				SkinnedMeshRenderer[] renderers = part.GetComponentsInChildren<SkinnedMeshRenderer>(true);

				if (renderers.Length != 1) {
					Debug.LogError($"[검증] {part.name}: SkinnedMeshRenderer가 {renderers.Length}개입니다 (1개여야 함).", part);
					errors++;
					continue;
				}

				if (renderers[0].sharedMesh == null) {
					Debug.LogError($"[검증] {part.name}: sharedMesh가 없습니다.", part);
					errors++;
				}

				if (renderers[0].bones.Length != 0) {
					Debug.LogError($"[검증] {part.name}: 본 참조가 남아 있습니다 (런타임 바인딩과 충돌).", part);
					errors++;
				}
			}
		}

		if (errors > 0) {
			Debug.LogError($"[NpcPartExtractor] 검증 실패 — 오류 {errors}건. 파츠 자식 삭제를 진행하지 마세요.");
			return;
		}

		Debug.Log($"[NpcPartExtractor] 검증 통과 — 파츠 {checkedParts}개, 본 {catalog.BoneNames.Length}개.\n" +
		          "게임을 실행해 NPC 외형과 몽타주·단서 사진이 정상인지 확인한 뒤 '3. 프리팹에서 파츠 자식 삭제'를 실행하세요.");
	}

	[MenuItem("Tools/Undercover/NPC 파츠 3. 프리팹에서 파츠 자식 삭제")]
	private static void DeletePartChildren() {
		bool confirmed = EditorUtility.DisplayDialog(
			"파츠 자식 삭제",
			$"'{TargetPrefabPath}'에서 파츠 자식을 모두 삭제합니다.\n\n" +
			"되돌릴 수 없습니다. '2. 검증'을 통과하고 실제 플레이로 외형을 확인했는지 확인하세요.",
			"삭제", "취소");

		if (!confirmed) {
			return;
		}

		DeletePartChildrenCore();
	}

	private static void DeletePartChildrenCore() {
		GameObject prefabRoot = PrefabUtility.LoadPrefabContents(TargetPrefabPath);

		try {
			if (!TryResolvePrefab(prefabRoot, out NpcOutfitController controller, out Transform partRoot, out _)) {
				return;
			}

			int deletedParts = 0;

			foreach (NpcPartSlot slot in Slots) {
				foreach (GameObject source in ReadPartList(controller, slot)) {
					UnityEngine.Object.DestroyImmediate(source);
					deletedParts++;
				}

				WriteField(controller, NpcPartCatalog.GetPartListFieldName(slot), Array.Empty<GameObject>());
			}

			// 파츠를 담고 있던 그룹 폴더가 비었으면 같이 정리한다.
			int deletedFolders = 0;

			for (int index = partRoot.childCount - 1; index >= 0; index--) {
				Transform child = partRoot.GetChild(index);

				if (child.childCount > 0 || Array.IndexOf(KeepAlways, child.name) >= 0) {
					continue;
				}

				if (child.GetComponents<Component>().Length > 1) {
					continue;
				}

				UnityEngine.Object.DestroyImmediate(child.gameObject);
				deletedFolders++;
			}

			// 파츠를 지우면 Outlinable의 타깃 목록에 끊어진 참조가 그 수만큼 남는다. 칸 수는 줄지 않으므로
			// NPC를 생성할 때마다 쓰이지 않는 OutlineTarget이 그만큼 할당된다. 남은 렌더러로 다시 모아준다.
			int outlineTargetsBefore = 0;
			int outlineTargetsAfter = 0;

			if (prefabRoot.TryGetComponent(out EPOOutline.Outlinable outlinable)) {
				outlineTargetsBefore = outlinable.OutlineTargetsCount;
				outlinable.AddAllChildRenderersToRenderingList(
					EPOOutline.RenderersAddingMode.SkinnedMeshRenderer | EPOOutline.RenderersAddingMode.MeshRenderer | EPOOutline.RenderersAddingMode.SpriteRenderer);
				outlineTargetsAfter = outlinable.OutlineTargetsCount;
			}

			PrefabUtility.SaveAsPrefabAsset(prefabRoot, TargetPrefabPath);

			int remainingTransforms = prefabRoot.GetComponentsInChildren<Transform>(true).Length;
			int remainingRenderers = prefabRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;

			Debug.Log($"[NpcPartExtractor] 파츠 {deletedParts}개, 빈 그룹 {deletedFolders}개를 삭제했습니다.\n" +
			          $"아웃라인 타깃 {outlineTargetsBefore}칸 → {outlineTargetsAfter}칸으로 정리했습니다.\n" +
			          $"남은 Transform {remainingTransforms}개, SkinnedMeshRenderer {remainingRenderers}개.");
		}
		finally {
			PrefabUtility.UnloadPrefabContents(prefabRoot);
		}

		AssetDatabase.SaveAssets();
	}

	private static bool TryResolvePrefab(GameObject prefabRoot, out NpcOutfitController controller, out Transform partRoot, out Transform skeletonRoot) {
		controller = null;
		partRoot = null;
		skeletonRoot = null;

		if (prefabRoot == null) {
			Debug.LogError($"[NpcPartExtractor] 프리팹을 불러오지 못했습니다: {TargetPrefabPath}");
			return false;
		}

		controller = prefabRoot.GetComponent<NpcOutfitController>();

		if (controller == null) {
			Debug.LogError($"[NpcPartExtractor] '{prefabRoot.name}'에 NpcOutfitController가 없습니다.");
			return false;
		}

		partRoot = prefabRoot.transform.Find(PartRootName);

		if (partRoot == null) {
			Debug.LogError($"[NpcPartExtractor] '{PartRootName}'을 찾지 못했습니다.");
			return false;
		}

		skeletonRoot = partRoot.Find(SkeletonRootName);

		if (skeletonRoot == null) {
			Debug.LogError($"[NpcPartExtractor] '{PartRootName}/{SkeletonRootName}'을 찾지 못했습니다.");
			return false;
		}

		return true;
	}

	// 모든 파츠가 같은 본 배열을 같은 순서로 쓰는지 확인하고, 그 순서를 이름으로 기록한다.
	// 이 전제가 깨지면 파츠마다 본 매핑이 달라져 런타임 바인딩이 어긋난다.
	private static bool TryCaptureSkeleton(NpcOutfitController controller, out string[] boneNames, out string rootBoneName) {
		boneNames = null;
		rootBoneName = null;

		foreach (NpcPartSlot slot in Slots) {
			foreach (GameObject source in ReadPartList(controller, slot)) {
				if (!source.TryGetComponent(out SkinnedMeshRenderer renderer)) {
					Debug.LogError($"[NpcPartExtractor] '{source.name}'에 SkinnedMeshRenderer가 없습니다.", source);
					return false;
				}

				Transform[] bones = renderer.bones;

				if (boneNames == null) {
					boneNames = new string[bones.Length];
					for (int index = 0; index < bones.Length; index++) {
						boneNames[index] = bones[index].name;
					}

					rootBoneName = renderer.rootBone != null ? renderer.rootBone.name : null;

					if (string.IsNullOrEmpty(rootBoneName)) {
						Debug.LogError($"[NpcPartExtractor] '{source.name}'에 rootBone이 없습니다.", source);
						return false;
					}

					continue;
				}

				if (bones.Length != boneNames.Length) {
					Debug.LogError($"[NpcPartExtractor] '{source.name}'의 본 개수가 다릅니다 ({bones.Length} vs {boneNames.Length}). 파츠마다 스켈레톤이 달라 이 방식을 쓸 수 없습니다.", source);
					return false;
				}

				for (int index = 0; index < bones.Length; index++) {
					if (bones[index].name != boneNames[index]) {
						Debug.LogError($"[NpcPartExtractor] '{source.name}'의 본 순서가 다릅니다 (index {index}: '{bones[index].name}' vs '{boneNames[index]}').", source);
						return false;
					}
				}
			}
		}

		if (boneNames == null) {
			Debug.LogError("[NpcPartExtractor] 추출할 파츠가 없습니다. 이미 추출을 끝낸 프리팹일 수 있습니다.");
			return false;
		}

		return true;
	}

	private static string SavePartPrefab(GameObject source, string folder) {
		SkinnedMeshRenderer sourceRenderer = source.GetComponent<SkinnedMeshRenderer>();
		Bounds localBounds = sourceRenderer.localBounds;

		GameObject copy = UnityEngine.Object.Instantiate(source);

		try {
			copy.name = source.name;
			copy.SetActive(true);

			SkinnedMeshRenderer copyRenderer = copy.GetComponent<SkinnedMeshRenderer>();

			// 본은 원본 NPC의 스켈레톤을 가리키므로 프리팹에 남기지 않는다. 런타임에 다시 바인딩한다.
			copyRenderer.bones = Array.Empty<Transform>();
			copyRenderer.rootBone = null;

			// 본을 비우면서 컬링용 경계가 흐트러지지 않게 원본 값을 그대로 유지한다.
			copyRenderer.localBounds = localBounds;

			string path = $"{folder}/{source.name}.prefab";
			PrefabUtility.SaveAsPrefabAsset(copy, path);

			return path;
		}
		finally {
			UnityEngine.Object.DestroyImmediate(copy);
		}
	}

	private static NpcPartCatalog LoadOrCreateCatalog() {
		NpcPartCatalog catalog = AssetDatabase.LoadAssetAtPath<NpcPartCatalog>(CatalogPath);

		if (catalog != null) {
			return catalog;
		}

		catalog = ScriptableObject.CreateInstance<NpcPartCatalog>();
		AssetDatabase.CreateAsset(catalog, CatalogPath);

		return catalog;
	}

	private static string CreateFolder(string parent, string child) {
		string path = $"{parent}/{child}";

		if (!AssetDatabase.IsValidFolder(parent)) {
			AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(parent).Replace('\\', '/'), System.IO.Path.GetFileName(parent));
		}

		if (!AssetDatabase.IsValidFolder(path)) {
			AssetDatabase.CreateFolder(parent, child);
		}

		return path;
	}

	private static int CountParts(NpcOutfitController controller) {
		int count = 0;

		foreach (NpcPartSlot slot in Slots) {
			count += ReadPartList(controller, slot).Length;
		}

		return Mathf.Max(count, 1);
	}

	private static GameObject[] ReadPartList(NpcOutfitController controller, NpcPartSlot slot) {
		string fieldName = NpcPartCatalog.GetPartListFieldName(slot);
		FieldInfo field = typeof(NpcOutfitController).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

		if (field == null) {
			throw new InvalidOperationException($"[NpcPartExtractor] NpcOutfitController에 '{fieldName}' 필드가 없습니다. 필드명이 바뀌었으면 NpcPartCatalog.GetPartListFieldName도 함께 고쳐야 합니다.");
		}

		return field.GetValue(controller) as GameObject[] ?? Array.Empty<GameObject>();
	}

	private static void WriteField(object target, string fieldName, object value) {
		FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

		if (field == null) {
			throw new InvalidOperationException($"[NpcPartExtractor] {target.GetType().Name}에 '{fieldName}' 필드가 없습니다.");
		}

		field.SetValue(target, value);
	}
}
