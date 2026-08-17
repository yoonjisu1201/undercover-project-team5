using UnityEngine;

// NPC 파츠가 공유하는 스켈레톤 바인딩 정보.
// 예전에는 부위별 옷 프리팹 배열(GameObject[])도 여기서 들고 있었지만, 그 옷 데이터는
// ClothData/ClothCatalog로 옮기고(#592) 이 자산에는 스켈레톤 본 이름 목록만 남긴다.
[CreateAssetMenu(menuName = "Undercover/Npc Part Catalog", fileName = "NpcPartCatalog")]
public class NpcPartCatalog : ScriptableObject {
	[Header("=== 스켈레톤 (모든 파츠 SMR이 이 순서 그대로 본에 바인딩된다) ===")]
	[SerializeField] private string[] _boneNames;
	[SerializeField] private string _rootBoneName;

	public string[] BoneNames => _boneNames;
	public string RootBoneName => _rootBoneName;
}
