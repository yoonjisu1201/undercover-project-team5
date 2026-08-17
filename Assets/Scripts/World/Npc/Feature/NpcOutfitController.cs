using System.Collections.Generic;
using EPOOutline;
using UnityEngine;
using Random = UnityEngine.Random;

public class NpcOutfitController : MonoBehaviour {
	[Header("=== 사용 가능한 Outfit List 모두 등록하기 (본체와 같은 스켈레톤을 공유하는 자식들) ===")]
	[SerializeField] private GameObject[] _beardList;
	[SerializeField] private GameObject[] _eyebrowList;
	[SerializeField] private GameObject[] _glassesList;
	[SerializeField] private GameObject[] _hairList;
	[SerializeField] private GameObject[] _hatList;
	[SerializeField] private GameObject[] _headPhoneList;
	[SerializeField] private GameObject[] _leftArmList;
	[SerializeField] private GameObject[] _rightArmList;
	[SerializeField] private GameObject[] _maskList;
	[SerializeField] private GameObject[] _pantsList;
	[SerializeField] private GameObject[] _shoesList;
	[SerializeField] private GameObject[] _torsoList;

	[Header("=== 외형 생성 시에 사용할 각종 변수들 ===")]
	[Range(0.0f, 1.0f)] [SerializeField] private float _beardPossibility = 0.5f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _eyebrowPossibility = 1.0f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _glassesPossibility = 0.5f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _hairPossibility = 0.8f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _hatPossibility = 0.5f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _headphonePossibility = 0.5f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _glovePossibility = 1f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _maskPossibility = 0.5f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _pantPossibility = 1f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _shoePossibility = 1f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _torsoPossibility = 1f;

	[Header("=== 헤드폰과 모자 동시 스폰 가능 여부 ===")]
	[SerializeField] private bool _headPhoneAndHatAtTheSameTime = false;

	[Header("=== 모듈러 파츠 (지정하면 위 리스트 대신 공용 ClothCatalog에서 뽑힌 파츠만 런타임 생성) ===")]
	[SerializeField] private NpcPartCatalog _partCatalog;
	[SerializeField] private Transform _partRoot;
	[SerializeField] private Transform _skeletonRoot;

	private Transform[] _bones;
	private Transform _rootBone;

	private bool UseCatalog => _partCatalog != null;

	public OutfitFeature CreateRandomOutfitFeature() {
		OutfitFeature feature = new OutfitFeature();

		feature.BeardNumber = GetRandomPartNumber(ClothPart.Beard, _beardPossibility);
		feature.EyebrowsNumber = GetRandomPartNumber(ClothPart.Eyebrow, _eyebrowPossibility);
		feature.GlassesNumber = GetRandomPartNumber(ClothPart.Glasses, _glassesPossibility);
		feature.HairNumber = GetRandomPartNumber(ClothPart.Hair, _hairPossibility);
		feature.HatNumber = GetRandomPartNumber(ClothPart.Hat, _hatPossibility);
		feature.HeadphoneNumber = GetRandomPartNumber(ClothPart.Headphone, _headphonePossibility);
		feature.ArmNumber = GetRandomPartNumber(ClothPart.Arm, _glovePossibility);
		feature.MaskNumber = GetRandomPartNumber(ClothPart.Mask, _maskPossibility);
		feature.PantsNumber = GetRandomPartNumber(ClothPart.Pants, _pantPossibility);
		feature.ShoesNumber = GetRandomPartNumber(ClothPart.Shoes, _shoePossibility);
		feature.TorsoNumber = GetRandomPartNumber(ClothPart.Torso, _torsoPossibility);

		ResolveHatAndHeadPhoneConflict(feature);

		return feature;
	}

	// 카탈로그를 쓰면 ClothCatalog에서 뽑은 ClothData.Id를, 아니면 기존 프리팹 자식 배열의 인덱스를 돌려준다.
	// 어느 쪽이든 OutfitFeature에는 그대로 저장할 수 있다(카탈로그 쪽은 Id가, 레거시 쪽은 배열 인덱스가 곧 값).
	private int GetRandomPartNumber(ClothPart part, float possibility) {
		if (UseCatalog) {
			if (!ShouldEquip(possibility)) {
				return -1;
			}

			return GetRandomNpcClothId(part);
		}

		return GetRandomPartIndex(GetLegacyPartList(part), possibility);
	}

	// 카탈로그에는 몽타주 전용 항목(NpcPrefab 없음)도 섞여 있을 수 있으므로, NPC가 실제로
	// 입을 수 있는 항목 중에서만 뽑는다. (예: Arm은 몽타주에만 있는 항목이 6개 있다.)
	private static int GetRandomNpcClothId(ClothPart part) {
		IReadOnlyList<ClothData> options = ClothCatalog.GetAll(part);
		List<ClothData> usable = new List<ClothData>(options.Count);

		foreach (ClothData data in options) {
			if (data.NpcPrefab != null) {
				usable.Add(data);
			}
		}

		if (usable.Count == 0) {
			return -1;
		}

		return usable[Random.Range(0, usable.Count)].Id;
	}

	// 카탈로그를 쓰지 않을 때만 참조하는, 프리팹 자식으로 들고 있는 파츠 목록.
	private GameObject[] GetLegacyPartList(ClothPart part) {
		return part switch {
			ClothPart.Beard => _beardList,
			ClothPart.Eyebrow => _eyebrowList,
			ClothPart.Glasses => _glassesList,
			ClothPart.Hair => _hairList,
			ClothPart.Hat => _hatList,
			ClothPart.Headphone => _headPhoneList,
			ClothPart.Arm => _leftArmList,
			ClothPart.Mask => _maskList,
			ClothPart.Pants => _pantsList,
			ClothPart.Shoes => _shoesList,
			ClothPart.Torso => _torsoList,
			_ => null
		};
	}

	private static void SetActivePart(GameObject[] partList, int activeIndex) {
		for (int index = 0; index < partList.Length; index++) {
			GameObject part = partList[index];

			// 파츠 미사용시 그냥 삭제
			if (part != null) {
				if (index == activeIndex) {
					part.SetActive(index == activeIndex);
				} else {
					Destroy(part);
				}
			}
		}
	}


	// Possibility 기반으로 그 부위 입을지 말지
	private static bool ShouldEquip(float possibility) {
		return possibility >= 1f ||
		       possibility > 0f && Random.value < possibility;
	}

	// 특정 파트에 무언가를 입을지 말지, 입는다면 뭘 입을지 결정한다.
	private static int GetRandomPartIndex(GameObject[] partList, float possibility) {
		int partCount = partList?.Length ?? 0;

		if (partCount == 0 || !ShouldEquip(possibility)) {
			return -1;
		}

		return Random.Range(0, partCount);
	}


	// 헤드폰, 모자 동시에 낄 수 없으니 확률로 둘 중 하나만 남기기
	private void ResolveHatAndHeadPhoneConflict(OutfitFeature feature) {
		if (_headPhoneAndHatAtTheSameTime ||
		    feature.HatNumber < 0 ||
		    feature.HeadphoneNumber < 0) {
			return;
		}

		if (Random.value < 0.5f) {
			feature.HatNumber = -1;
			return;
		}

		feature.HeadphoneNumber = -1;
	}


	public void ApplyOutfit(OutfitFeature feature) {
		if (feature == null) {
			Debug.LogError("[NPC] 적용할 NpcFeature가 없습니다.", this);
			return;
		}

		if (UseCatalog) {
			ApplyOutfitFromCatalog(feature);
			return;
		}

		SetActivePart(_beardList, feature.BeardNumber);
		SetActivePart(_eyebrowList, feature.EyebrowsNumber);
		SetActivePart(_glassesList, feature.GlassesNumber);
		SetActivePart(_hairList, feature.HairNumber);
		SetActivePart(_hatList, feature.HatNumber);
		SetActivePart(_headPhoneList, feature.HeadphoneNumber);
		SetActivePart(_leftArmList, feature.ArmNumber);
		SetActivePart(_rightArmList, feature.ArmNumber);
		SetActivePart(_maskList, feature.MaskNumber);
		SetActivePart(_pantsList, feature.PantsNumber);
		SetActivePart(_shoesList, feature.ShoesNumber);
		SetActivePart(_torsoList, feature.TorsoNumber);
	}

	// 뽑힌 파츠만 생성해서 본체 스켈레톤에 다시 바인딩한다.
	private void ApplyOutfitFromCatalog(OutfitFeature feature) {
		if (!TryCacheBones()) {
			return;
		}

		EquipPart(ClothPart.Beard, feature.BeardNumber);
		EquipPart(ClothPart.Eyebrow, feature.EyebrowsNumber);
		EquipPart(ClothPart.Glasses, feature.GlassesNumber);
		EquipPart(ClothPart.Hair, feature.HairNumber);
		EquipPart(ClothPart.Hat, feature.HatNumber);
		EquipPart(ClothPart.Headphone, feature.HeadphoneNumber);
		EquipPart(ClothPart.Arm, feature.ArmNumber);
		EquipPart(ClothPart.Mask, feature.MaskNumber);
		EquipPart(ClothPart.Pants, feature.PantsNumber);
		EquipPart(ClothPart.Shoes, feature.ShoesNumber);
		EquipPart(ClothPart.Torso, feature.TorsoNumber);

		RebuildOutline();
	}

	// 파츠를 런타임에 만들기 때문에 아웃라인 대상 목록도 이 시점에 다시 모아야 한다.
	// 프리팹에 저작된 목록에는 기본 머리만 들어 있어서, 그냥 두면 머리에만 외곽선이 나온다.
	// AddAllChildRenderersToRenderingList는 대상 수에 대해 O(n^2)이므로 파츠가 10여 개인 지금만 쓸 수 있다.
	private void RebuildOutline() {
		if (!TryGetComponent(out Outlinable outlinable)) {
			return;
		}

		outlinable.AddAllChildRenderersToRenderingList(
			RenderersAddingMode.SkinnedMeshRenderer | RenderersAddingMode.MeshRenderer | RenderersAddingMode.SpriteRenderer);
	}

	private void EquipPart(ClothPart part, int clothId) {
		if (clothId < 0) {
			return;
		}

		ClothData data = ClothCatalog.Find(part, clothId);
		if (data == null) {
			return;
		}

		InstantiatePart(data.NpcPrefab);

		// Arm은 왼손/오른손 프리팹을 함께 착용한다.
		if (part == ClothPart.Arm) {
			InstantiatePart(data.NpcPrefabSecondary);
		}
	}

	private void InstantiatePart(GameObject prefab) {
		if (prefab == null) {
			return;
		}

		GameObject instance = Instantiate(prefab, _partRoot);

		// Instantiate는 부모 레이어를 물려받지 않고 프리팹 자신의 레이어를 그대로 쓴다.
		// 파츠 프리팹은 전부 Default(0)로 추출돼 있어서, 그대로 두면 NPC를 미니맵/CCTV
		// 카메라에서 제외한 레이어 설정(#411)이 파츠에는 적용되지 않는다. NPC 레이어를 물려준다.
		instance.layer = gameObject.layer;

		// 파츠 프리팹의 SMR은 본 참조 없이 저장돼 있으므로, 이 NPC의 스켈레톤으로 바인딩해준다.
		if (instance.TryGetComponent(out SkinnedMeshRenderer partRenderer)) {
			partRenderer.bones = _bones;
			partRenderer.rootBone = _rootBone;
		}
	}

	// 스켈레톤 본을 카탈로그의 이름 순서대로 한 번만 찾아 캐시한다.
	// 모든 파츠 SMR이 같은 순서로 같은 본을 쓰므로 배열 하나를 그대로 공유한다.
	private bool TryCacheBones() {
		if (_bones != null) {
			return true;
		}

		if (_partRoot == null || _skeletonRoot == null) {
			Debug.LogError("[NPC] 모듈러 파츠를 쓰려면 _partRoot와 _skeletonRoot를 지정해야 합니다.", this);
			return false;
		}

		string[] boneNames = _partCatalog.BoneNames;
		Dictionary<string, Transform> boneByName = new(boneNames.Length);

		foreach (Transform bone in _skeletonRoot.GetComponentsInChildren<Transform>(true)) {
			boneByName[bone.name] = bone;
		}

		Transform[] bones = new Transform[boneNames.Length];

		for (int index = 0; index < boneNames.Length; index++) {
			if (!boneByName.TryGetValue(boneNames[index], out bones[index])) {
				Debug.LogError($"[NPC] 스켈레톤에서 본 '{boneNames[index]}'을 찾지 못했습니다.", this);
				return false;
			}
		}

		if (!boneByName.TryGetValue(_partCatalog.RootBoneName, out _rootBone)) {
			Debug.LogError($"[NPC] 스켈레톤에서 루트 본 '{_partCatalog.RootBoneName}'을 찾지 못했습니다.", this);
			return false;
		}

		_bones = bones;

		return true;
	}
}
