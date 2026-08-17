// NPC 외형(NpcOutfitController)과 몽타주(MontageSyncBase 계열)가 공유하는 부위 구분.
// 예전에는 NpcPartSlot(팔 좌/우 분리)과 MontageParts(팔 통합)가 따로 있었으나,
// 팔은 항상 같은 인덱스로 좌우를 함께 착용하므로 하나로 합쳤다.
public enum ClothPart {
	Beard,
	Eyebrow,
	Glasses,
	Hair,
	Hat,
	Headphone,
	Arm,
	Mask,
	Pants,
	Shoes,
	Torso
}
