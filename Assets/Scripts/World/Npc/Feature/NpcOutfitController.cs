using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine;

public class NpcOutfitController : MonoBehaviour {
	[Header("=== 사용 가능한 Outfit List 모두 등록하기 ===")]
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
	[Range(0.0f, 1.0f)] [SerializeField] private float _glovePossibility = 0.3f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _maskPossibility = 0.5f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _pantPossibility = 1f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _shoePossibility = 1f;
	[Range(0.0f, 1.0f)] [SerializeField] private float _torsoPossibility = 1f;

	[Header("=== 헤드폰과 모자 동시 스폰 가능 여부 ===")]
	[SerializeField] private bool _headPhoneAndHatAtTheSameTime = false;

	public OutfitFeature CreateRandomOutfitFeature() {
		OutfitFeature feature = new OutfitFeature();

		feature.BeardNumber = GetRandomPartIndex(_beardList, _beardPossibility);
		feature.EyebrowsNumber = GetRandomPartIndex(_eyebrowList, _eyebrowPossibility);
		feature.GlassesNumber = GetRandomPartIndex(_glassesList, _glassesPossibility);
		feature.HairNumber = GetRandomPartIndex(_hairList, _hairPossibility);
		feature.HatNumber = GetRandomPartIndex(_hatList, _hatPossibility);
		feature.HeadphoneNumber = GetRandomPartIndex(_headPhoneList, _headphonePossibility);
		feature.ArmNumber = GetRandomPartIndex(_leftArmList, _glovePossibility);
		feature.MaskNumber = GetRandomPartIndex(_maskList, _maskPossibility);
		feature.PantsNumber = GetRandomPartIndex(_pantsList, _pantPossibility);
		feature.ShoesNumber = GetRandomPartIndex(_shoesList, _shoePossibility);
		feature.TorsoNumber = GetRandomPartIndex(_torsoList, _torsoPossibility);

		ResolveHatAndHeadPhoneConflict(feature);

		return feature;
	}

	private static void SetActivePart(GameObject[] partList, int activeIndex) {
		for (int index = 0; index < partList.Length; index++) {
			GameObject part = partList[index];

			if (part != null) {
				part.SetActive(index == activeIndex);
			}
		}
	}


	// Possibility기반으로 그 부위 입을지 말지
	private static bool ShouldEquip(float possibility) {
		return possibility >= 1f ||
		       possibility > 0f && Random.value < possibility;
	}

	// 특정 파트에 무언가를 입을지 말지, 입는다면 뭘 입을지 결정한다.
	private static int GetRandomPartIndex(GameObject[] partList, float possibility) {
		int partCount = partList.Length;

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

	public void GetEquippedModules(OutfitFeature feature, List<GameObject> results) {
		results.Clear();

		AddModuleAt(_beardList, feature.BeardNumber, results);
		AddModuleAt(_eyebrowList, feature.EyebrowsNumber, results);
		AddModuleAt(_glassesList, feature.GlassesNumber, results);
		AddModuleAt(_hairList, feature.HairNumber, results);
		AddModuleAt(_hatList, feature.HatNumber, results);
		AddModuleAt(_headPhoneList, feature.HeadphoneNumber, results);
		AddModuleAt(_leftArmList, feature.ArmNumber, results);
		AddModuleAt(_maskList, feature.MaskNumber, results);
		AddModuleAt(_pantsList, feature.PantsNumber, results);
		AddModuleAt(_shoesList, feature.ShoesNumber, results);
		AddModuleAt(_torsoList, feature.TorsoNumber, results);
	}

	private static void AddModuleAt(GameObject[] modules, int index, List<GameObject> results) {
		if (index >= 0 && index < modules.Length && modules[index] != null) {
			results.Add(modules[index]);
		}
	}
}
