using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerCartIK : MonoBehaviour
{
	private PlayerInteraction _playerInteraction;
	private Animator _animator;

	private void Awake()
	{
		_animator = GetComponent<Animator>();
		_playerInteraction = GetComponent<PlayerInteraction>();
	}

	// PlayerHandIK가 이 값을 보고 지금 이 IK를 적용할지 판단한다.
	public bool IsActive => _playerInteraction.CarryingCart != null;

	// 카트를 잡고있으면 IK를 카트 손잡이에 붙인다. PlayerHandIK가 IsActive를 확인한 뒤 호출한다.
	public void ApplyIK(int layerIndex)
	{
		CartBase cart = _playerInteraction.CarryingCart;

		_animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f);
		_animator.SetIKPosition(AvatarIKGoal.LeftHand, cart.LeftHandle.position);
		_animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 1f);
		_animator.SetIKRotation(AvatarIKGoal.LeftHand, cart.LeftHandle.rotation);

		_animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f);
		_animator.SetIKPosition(AvatarIKGoal.RightHand, cart.RightHandle.position);
		_animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 1f);
		_animator.SetIKRotation(AvatarIKGoal.RightHand, cart.RightHandle.rotation);
	}
}