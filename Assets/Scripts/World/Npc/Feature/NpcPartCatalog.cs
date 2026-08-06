using System;
using UnityEngine;

// OutfitFeature의 부위별 번호와 대응하는 파츠 슬롯.
public enum NpcPartSlot {
	Beard,
	Eyebrow,
	Glasses,
	Hair,
	Hat,
	Headphone,
	LeftArm,
	RightArm,
	Mask,
	Pants,
	Shoes,
	Torso
}

// NPC가 착용할 수 있는 파츠 프리팹 모음. NpcPartExtractor가 NPC 프리팹의 파츠 자식들을 추출해 채운다.
//
// 파츠를 프리팹 자식으로 모두 들고 있으면 NPC 한 마리를 만들 때마다 916개 SkinnedMeshRenderer를
// 생성한 뒤 안 쓰는 905개를 지우게 된다. 그 비용을 없애기 위해 파츠를 별도 프리팹으로 분리하고,
// 뽑힌 파츠만 런타임에 생성한다.
//
// 배열 인덱스가 OutfitFeature의 부위별 번호와 그대로 대응한다. 순서를 바꾸면 이미 동기화된
// 외형 번호와 몽타주가 어긋나므로 순서를 건드리면 안 된다.
[CreateAssetMenu(menuName = "Undercover/Npc Part Catalog", fileName = "NpcPartCatalog")]
public class NpcPartCatalog : ScriptableObject {
	public const int SlotCount = 12;

	[Header("=== 부위별 파츠 프리팹 (순서 변경 금지) ===")]
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

	[Header("=== 스켈레톤 (모든 파츠 SMR이 이 순서 그대로 본에 바인딩된다) ===")]
	[SerializeField] private string[] _boneNames;
	[SerializeField] private string _rootBoneName;

	public string[] BoneNames => _boneNames;
	public string RootBoneName => _rootBoneName;

	public GameObject[] GetParts(NpcPartSlot slot) {
		GameObject[] parts = slot switch {
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

		return parts ?? Array.Empty<GameObject>();
	}
}
