using UnityEngine;

// NPC 외형, 몽타주 조립, 단서 캡쳐가 전부 공유하는 옷 하나의 데이터.
// Id는 부위별로 고유하며, NpcFeature/MontageState가 그대로 이 Id를 저장한다.
[CreateAssetMenu(fileName = "ClothData", menuName = "Undercover/Cloth Data")]
public class ClothData : ScriptableObject {
	public int Id;
	public ClothPart Part;

	// 꺼두면 라운드 로테이션 대상에서 완전히 제외된다 (깨졌거나 못 쓰게 된 항목 영구 차단용).
	public bool IsEnabled = true;

	[Header("=== NPC 착용용 (스켈레톤 본 바인딩) ===")]
	public GameObject NpcPrefab;

	// Arm 전용: 반대쪽 손 프리팹. 그 외 부위는 비워둔다.
	public GameObject NpcPrefabSecondary;

	[Header("=== 몽타주 조립용 ===")]
	public GameObject MontagePrefab;
	public Sprite Thumbnail;
}
