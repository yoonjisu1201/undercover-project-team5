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

	[Header("=== 모듈러 파츠 (지정하면 위 리스트 대신 뽑힌 파츠만 런타임 생성) ===")]
	[SerializeField] private NpcPartCatalog _partCatalog;
	[SerializeField] private Transform _partRoot;
	[SerializeField] private Transform _skeletonRoot;

	// 카탈로그에서 생성한 파츠. 슬롯 하나당 하나씩 보관해 몽타주 촬영 시 그대로 넘긴다.
	private readonly GameObject[] _equippedParts = new GameObject[NpcPartCatalog.SlotCount];
	private Transform[] _bones;
	private Transform _rootBone;

	private bool UseCatalog => _partCatalog != null;

	public OutfitFeature CreateRandomOutfitFeature() {
		OutfitFeature feature = new OutfitFeature();

		feature.BeardNumber = GetRandomPartIndex(GetPartList(NpcPartSlot.Beard), _beardPossibility);
		feature.EyebrowsNumber = GetRandomPartIndex(GetPartList(NpcPartSlot.Eyebrow), _eyebrowPossibility);
		feature.GlassesNumber = GetRandomPartIndex(GetPartList(NpcPartSlot.Glasses), _glassesPossibility);
		feature.HairNumber = GetRandomPartIndex(GetPartList(NpcPartSlot.Hair), _hairPossibility);
		feature.HatNumber = GetRandomPartIndex(GetPartList(NpcPartSlot.Hat), _hatPossibility);
		feature.HeadphoneNumber = GetRandomPartIndex(GetPartList(NpcPartSlot.Headphone), _headphonePossibility);
		feature.ArmNumber = GetRandomPartIndex(GetPartList(NpcPartSlot.LeftArm), _glovePossibility);
		feature.MaskNumber = GetRandomPartIndex(GetPartList(NpcPartSlot.Mask), _maskPossibility);
		feature.PantsNumber = GetRandomPartIndex(GetPartList(NpcPartSlot.Pants), _pantPossibility);
		feature.ShoesNumber = GetRandomPartIndex(GetPartList(NpcPartSlot.Shoes), _shoePossibility);
		feature.TorsoNumber = GetRandomPartIndex(GetPartList(NpcPartSlot.Torso), _torsoPossibility);

		ResolveHatAndHeadPhoneConflict(feature);

		return feature;
	}

	// 카탈로그가 지정되면 카탈로그의 파츠 프리팹 목록을, 아니면 프리팹 자식 목록을 쓴다.
	// 어느 쪽이든 인덱스는 동일하므로 이미 뽑힌 OutfitFeature 번호를 그대로 쓸 수 있다.
	private GameObject[] GetPartList(NpcPartSlot slot) {
		if (UseCatalog) {
			return _partCatalog.GetParts(slot);
		}

		return slot switch {
			NpcPartSlot.Beard => _beardList,
			NpcPartSlot.Eyebrow => _eyebrowList,
			NpcPartSlot.Glasses => _glassesList,
			NpcPartSlot.Hair => _hairList,
			NpcPartSlot.Hat => _hatList,
			NpcPartSlot.Headphone => _headPhoneList,
			NpcPartSlot.LeftArm => _leftArmList,
			NpcPartSlot.RightArm => _rightArmList,
			NpcPartSlot.Mask => _maskList,
			NpcPartSlot.Pants => _pantsList,
			NpcPartSlot.Shoes => _shoesList,
			NpcPartSlot.Torso => _torsoList,
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

		EquipPart(NpcPartSlot.Beard, feature.BeardNumber);
		EquipPart(NpcPartSlot.Eyebrow, feature.EyebrowsNumber);
		EquipPart(NpcPartSlot.Glasses, feature.GlassesNumber);
		EquipPart(NpcPartSlot.Hair, feature.HairNumber);
		EquipPart(NpcPartSlot.Hat, feature.HatNumber);
		EquipPart(NpcPartSlot.Headphone, feature.HeadphoneNumber);
		EquipPart(NpcPartSlot.LeftArm, feature.ArmNumber);
		EquipPart(NpcPartSlot.RightArm, feature.ArmNumber);
		EquipPart(NpcPartSlot.Mask, feature.MaskNumber);
		EquipPart(NpcPartSlot.Pants, feature.PantsNumber);
		EquipPart(NpcPartSlot.Shoes, feature.ShoesNumber);
		EquipPart(NpcPartSlot.Torso, feature.TorsoNumber);

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

	private void EquipPart(NpcPartSlot slot, int partIndex) {
		GameObject[] parts = _partCatalog.GetParts(slot);

		if (partIndex < 0 || partIndex >= parts.Length || parts[partIndex] == null) {
			return;
		}

		GameObject part = Instantiate(parts[partIndex], _partRoot);

		// Instantiate는 부모 레이어를 물려받지 않고 프리팹 자신의 레이어를 그대로 쓴다.
		// 파츠 프리팹은 전부 Default(0)로 추출돼 있어서, 그대로 두면 NPC를 미니맵/CCTV
		// 카메라에서 제외한 레이어 설정(#411)이 파츠에는 적용되지 않는다. NPC 레이어를 물려준다.
		part.layer = gameObject.layer;

		// 파츠 프리팹의 SMR은 본 참조 없이 저장돼 있으므로, 이 NPC의 스켈레톤으로 바인딩해준다.
		if (part.TryGetComponent(out SkinnedMeshRenderer partRenderer)) {
			partRenderer.bones = _bones;
			partRenderer.rootBone = _rootBone;
		}

		_equippedParts[(int)slot] = part;
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

	public void GetEquippedModules(OutfitFeature feature, List<GameObject> results, List<MontageParts> partResults) {
		results.Clear();
		partResults.Clear();

		if (UseCatalog) {
			AddEquippedPart(NpcPartSlot.Beard, MontageParts.Beard, results, partResults);
			AddEquippedPart(NpcPartSlot.Eyebrow, MontageParts.Eyebrows, results, partResults);
			AddEquippedPart(NpcPartSlot.Glasses, MontageParts.Glasses, results, partResults);
			AddEquippedPart(NpcPartSlot.Hair, MontageParts.Hair, results, partResults);
			AddEquippedPart(NpcPartSlot.Hat, MontageParts.Hats, results, partResults);
			AddEquippedPart(NpcPartSlot.Headphone, MontageParts.Headphones, results, partResults);
			AddEquippedPart(NpcPartSlot.LeftArm, MontageParts.Arms, results, partResults);
			AddEquippedPart(NpcPartSlot.Mask, MontageParts.Masks, results, partResults);
			AddEquippedPart(NpcPartSlot.Pants, MontageParts.Pants, results, partResults);
			AddEquippedPart(NpcPartSlot.Shoes, MontageParts.Shoes, results, partResults);
			AddEquippedPart(NpcPartSlot.Torso, MontageParts.Torso, results, partResults);
			return;
		}

		AddModuleAt(_beardList, feature.BeardNumber, MontageParts.Beard, results, partResults);
		AddModuleAt(_eyebrowList, feature.EyebrowsNumber, MontageParts.Eyebrows, results, partResults);
		AddModuleAt(_glassesList, feature.GlassesNumber, MontageParts.Glasses, results, partResults);
		AddModuleAt(_hairList, feature.HairNumber, MontageParts.Hair, results, partResults);
		AddModuleAt(_hatList, feature.HatNumber, MontageParts.Hats, results, partResults);
		AddModuleAt(_headPhoneList, feature.HeadphoneNumber, MontageParts.Headphones, results, partResults);
		AddModuleAt(_leftArmList, feature.ArmNumber, MontageParts.Arms, results, partResults);
		AddModuleAt(_maskList, feature.MaskNumber, MontageParts.Masks, results, partResults);
		AddModuleAt(_pantsList, feature.PantsNumber, MontageParts.Pants, results, partResults);
		AddModuleAt(_shoesList, feature.ShoesNumber, MontageParts.Shoes, results, partResults);
		AddModuleAt(_torsoList, feature.TorsoNumber, MontageParts.Torso, results, partResults);
	}

	private static void AddModuleAt(GameObject[] modules, int index, MontageParts part, List<GameObject> results, List<MontageParts> partResults) {
		if (modules != null && index >= 0 && index < modules.Length && modules[index] != null) {
			results.Add(modules[index]);
			partResults.Add(part);
		}
	}

	private void AddEquippedPart(NpcPartSlot slot, MontageParts part, List<GameObject> results, List<MontageParts> partResults) {
		GameObject equipped = _equippedParts[(int)slot];

		if (equipped != null) {
			results.Add(equipped);
			partResults.Add(part);
		}
	}
}
