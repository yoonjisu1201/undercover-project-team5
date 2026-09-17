using System.Collections.Generic;
using EPOOutline;
using UnityEngine;
using Random = UnityEngine.Random;

public class NpcOutfitController : MonoBehaviour {
	// 손은 랜덤 대상에서 빠졌지만, 스켈레톤에 기본 손 메시가 없어 뭐라도 달아줘야 한다. id 0은 맨손(M3CPCV2_HAND_01).
	private const int DefaultArmClothId = 0;

	[Header("=== 외형 생성 시에 사용할 각종 변수들 ===")]
	[Range(0.0f, 1.0f)] [SerializeField] private float _beardPossibility = 0.5f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _eyebrowPossibility = 1.0f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _glassesPossibility = 0.5f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _hairPossibility = 0.8f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _hatPossibility = 0.5f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _headphonePossibility = 0.5f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _maskPossibility = 0.5f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _pantPossibility = 1f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _shoePossibility = 1f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _torsoPossibility = 1f;

	[Header("=== 헤드폰과 모자 동시 스폰 가능 여부 ===")]
	[SerializeField] private bool _headPhoneAndHatAtTheSameTime = false;

	[Header("=== 모듈러 파츠 (공용 ClothCatalog에서 뽑힌 파츠만 런타임 생성) ===")]
	[SerializeField] private NpcPartCatalog _partCatalog;
	[SerializeField] private Transform _partRoot;
	[SerializeField] private Transform _skeletonRoot;

	private Transform[] _bones;
	private Transform _rootBone;

	// 공통 라운드 시드와 NPC NetworkObjectId에서 유도한 seed로 생성한다. 이 함수는 서버와
	// 모든 클라이언트에서 같은 순서로 같은 난수열을 소비해야 한다.
	public OutfitFeature CreateDeterministicOutfit(int seed) {
		NpcOutfitRandom random = new(seed);
		OutfitFeature feature = new OutfitFeature();

		feature.BeardNumber = GetRandomPartNumber(ClothPart.Beard, _beardPossibility, random);
		feature.EyebrowsNumber = GetRandomPartNumber(ClothPart.Eyebrow, _eyebrowPossibility, random);
		feature.GlassesNumber = GetRandomPartNumber(ClothPart.Glasses, _glassesPossibility, random);
		feature.HairNumber = GetRandomPartNumber(ClothPart.Hair, _hairPossibility, random);
		feature.HatNumber = GetRandomPartNumber(ClothPart.Hat, _hatPossibility, random);
		feature.HeadphoneNumber = GetRandomPartNumber(ClothPart.Headphone, _headphonePossibility, random);
		feature.ArmNumber = DefaultArmClothId; // 손은 더 이상 랜덤/장식 대상이 아니라 항상 기본 손을 단다
		feature.MaskNumber = GetRandomPartNumber(ClothPart.Mask, _maskPossibility, random);
		feature.PantsNumber = GetRandomPartNumber(ClothPart.Pants, _pantPossibility, random);
		feature.ShoesNumber = GetRandomPartNumber(ClothPart.Shoes, _shoePossibility, random);
		feature.TorsoNumber = GetRandomPartNumber(ClothPart.Torso, _torsoPossibility, random);

		ResolveHatAndHeadPhoneConflict(feature, random);

		return feature;
	}
	
	private static int GetRandomPartNumber(ClothPart part, float possibility, NpcOutfitRandom random) {
		// 해당 NPC가 이 부위 장비 입을지 말지 Possibility기반으로 먼저 결정.
		// 입지 않기로 했다면, -1 반환
		if (!random.NextBool(possibility)) {
			return -1;
		}

		// 입기로 했다면, Index골라서 반환
		return GetRandomNpcClothId(part, random);
	}

	// 카탈로그에는 몽타주 전용 항목(NpcPrefab 없음)도 섞여 있을 수 있으므로, NPC가 실제로
	// 입을 수 있는 항목 중에서만 뽑는다. (예: Arm은 몽타주에만 있는 항목이 6개 있다.)
	private static int GetRandomNpcClothId(ClothPart part, NpcOutfitRandom random) {
		IReadOnlyList<ClothData> options = ClothCatalog.GetAll(part);
		List<ClothData> usable = new List<ClothData>(options.Count);

		// NpcPrefab 없는 데이터는 거르는 부분.
		// 사실 이런 데이터 자체가 없어야 하긴 한다. 당장 수정하기 시간이 없어 놔둠
		foreach (ClothData data in options) {
			if (data.NpcPrefab != null) {
				usable.Add(data);
			}
		}

		if (usable.Count == 0) { return -1; }

		return usable[random.NextInt(usable.Count)].Id;
	}


	// 헤드폰, 모자 동시에 낄 수 없으니 확률로 둘 중 하나만 남기기
	private void ResolveHatAndHeadPhoneConflict(OutfitFeature feature, NpcOutfitRandom random) {
		if (_headPhoneAndHatAtTheSameTime ||
		    feature.HatNumber < 0 ||
		    feature.HeadphoneNumber < 0) {
			return;
		}

		if (random.NextBool(0.5f)) {
			feature.HatNumber = -1;
			return;
		}

		feature.HeadphoneNumber = -1;
	}

	// 뽑힌 파츠만 생성해서 본체 스켈레톤에 다시 바인딩한다.
	public void ApplyOutfit(OutfitFeature feature) {
		if (feature == null) {
			Debug.LogError("[NPC] 적용할 OutfitFeature가 없습니다.", this);
			return;
		}

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
