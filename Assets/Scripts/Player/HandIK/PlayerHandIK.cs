using UnityEngine;

// 손 IK를 요구하는 기능(조준, 카트 밀기 등)이 여러 개라서, OnAnimatorIK를 여기 한 곳에서만 갖고
// 우선순위에 따라 어느 쪽을 적용할지 결정한다. PlayerAimIK/PlayerCartIK는 각자 OnAnimatorIK 없이
// IsActive/ApplyIK만 제공한다.
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(PlayerAimIK))]
[RequireComponent(typeof(PlayerCartIK))]
[RequireComponent(typeof(PlayerItemIK))]
public class PlayerHandIK : MonoBehaviour
{
    private Animator _animator;
    private PlayerAimIK _aimIK;
    private PlayerCartIK _cartIK;
    private PlayerItemIK _itemIK;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _aimIK = GetComponent<PlayerAimIK>();
        _cartIK = GetComponent<PlayerCartIK>();
        _itemIK = GetComponent<PlayerItemIK>();
    }

    private void OnAnimatorIK(int layerIndex)
    { 
        if (_cartIK.IsActive)
        {
            _cartIK.ApplyIK(layerIndex);
            // 카트 사용중 손 아이템 비활성화
            _itemIK.DisableItems();
            return;
        }

        if (_aimIK.IsActive)
        {
            _aimIK.ApplyIK(layerIndex);
            // 총 사용중 손 아이템 비활성화
            _itemIK.DisableItems();
            return;
        }
        
        if (_itemIK.IsActive) {
            _itemIK.ApplyIK(layerIndex);
            return;
        }
        
        _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
        _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
        _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
        _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
    }
}
