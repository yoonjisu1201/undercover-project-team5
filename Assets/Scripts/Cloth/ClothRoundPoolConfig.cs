using UnityEngine;

// 라운드마다 부위별로 몇 종류까지만 뽑을지 정하는 설정. Resources 루트에 하나만 둔다.
// (#484) 실제 사용 가능한 의상 수를 줄이기 위해 만들었다 — 부위별 활성 목록 전체가 아니라
// 이 설정에 적힌 개수만큼만 라운드마다 새로 골라 쓰게 한다.
[CreateAssetMenu(menuName = "Undercover/Cloth Round Pool Config", fileName = "ClothRoundPoolConfig")]
public class ClothRoundPoolConfig : ScriptableObject {
	[Header("=== 부위별 라운드당 사용 개수 (그 부위 활성 항목 수보다 크면 전체를 그대로 씀) ===")]
	[SerializeField] private int _beardCount = 10;
	[SerializeField] private int _eyebrowCount = 10;
	[SerializeField] private int _glassesCount = 10;
	[SerializeField] private int _hairCount = 10;
	[SerializeField] private int _hatCount = 10;
	[SerializeField] private int _headphoneCount = 10;
	[SerializeField] private int _armCount = 10;
	[SerializeField] private int _maskCount = 10;
	[SerializeField] private int _pantsCount = 10;
	[SerializeField] private int _shoesCount = 10;
	[SerializeField] private int _torsoCount = 10;

	public int GetCount(ClothPart part) {
		return part switch {
			ClothPart.Beard => _beardCount,
			ClothPart.Eyebrow => _eyebrowCount,
			ClothPart.Glasses => _glassesCount,
			ClothPart.Hair => _hairCount,
			ClothPart.Hat => _hatCount,
			ClothPart.Headphone => _headphoneCount,
			ClothPart.Arm => _armCount,
			ClothPart.Mask => _maskCount,
			ClothPart.Pants => _pantsCount,
			ClothPart.Shoes => _shoesCount,
			ClothPart.Torso => _torsoCount,
			_ => int.MaxValue
		};
	}
}
