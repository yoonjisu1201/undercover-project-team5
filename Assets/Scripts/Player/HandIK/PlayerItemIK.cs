using UnityEngine;

public class PlayerItemIK : HandIKBase {
	[Header("=== 왼손 아이템 드는 슬롯 ===")]
	[SerializeField] private Transform _leftHandParent;
	[Header("=== 왼손 목표 위치 ===")]
	[SerializeField] private Transform _leftHandTarget;

	[Header("=== 오른손 아이템 드는 슬롯 ===")]
	[SerializeField] private Transform _rightHandParent;
	[Header("=== 오른손 목표 위치 ===")]
	[SerializeField] private Transform _rightHandTarget;

	[Header("=== 손전등 오브젝트 ===")]
	[SerializeField] private GameObject _flashLightPrefab;

	[Header("=== 손전등 꺼질 때 같이 끌 Light오브젝트 ===")] 
	[SerializeField] private Light _lightInCamera;

	[Header("=== 머리 피벗(참고용) ===")]
	[SerializeField] private GameObject _headPivot;

	private GameObject _itemOnLeftHand;
	private GameObject _itemOnRightHand;

	public override bool IsActive => true;

	// 왼손에는 항상 플래시라이트 있어야 함. 고정으로 leftHand에 스폰
	protected override void Awake() {
		base.Awake();
		
		// 불빛도 켜져있도록
		_lightInCamera.enabled = true;
		_itemOnLeftHand = Instantiate(_flashLightPrefab, _leftHandParent);
	}

	// 다른 걸 잡을 때(카트 잡을 때 등)에는 손에 있는 오브젝트 비활성화한다.
	public void DisableItems() {
		_lightInCamera.enabled = false;
		_itemOnLeftHand?.SetActive(false);
		_itemOnRightHand?.SetActive(false);
	}

	public override void ApplyIK(int layerIndex) {
		// 잡을 때 손에 있는 오브젝트 활성화
		_lightInCamera.enabled = true;
		_itemOnLeftHand?.SetActive(true);
		_itemOnRightHand?.SetActive(true);

		// 왼손에 아이템 있으면, 왼손 위치 옮기기
		if (_itemOnLeftHand != null) {
			_animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f);
			_animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 1f);
			_animator.SetIKPosition(AvatarIKGoal.LeftHand, _leftHandTarget.position);
			_animator.SetIKRotation(AvatarIKGoal.LeftHand, _leftHandTarget.rotation);
		}
		// 왼손 아이템 없으면, 왼손 IK 해제
		else {
			_animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
			_animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
		}

		// 오른손에 아이템 있으면, 오른손 위치 옮기기
		if (_itemOnRightHand != null) {
			_animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f);
			_animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 1f);
			_animator.SetIKPosition(AvatarIKGoal.RightHand, _rightHandTarget.position);
			_animator.SetIKRotation(AvatarIKGoal.RightHand, _rightHandTarget.rotation);
		}
		// 오른손 아이템 없으면, 오른손 IK 해제
		else {
			_animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
			_animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
		}
	}
}
