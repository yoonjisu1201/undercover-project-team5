using UnityEngine;

// 손 IK를 요구하는 기능(조준, 카트 밀기 등)이 여러 개라서, OnAnimatorIK를 여기 한 곳에서만 갖고
// 우선순위에 따라 어느 쪽을 적용할지 결정한다. PlayerAimIK/PlayerCartIK는 각자 OnAnimatorIK 없이
// IsActive/ApplyIK만 제공한다.
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(PlayerAimIK))]
[RequireComponent(typeof(PlayerCartIK))]
[RequireComponent(typeof(PlayerItemIK))]
[RequireComponent(typeof(PlayerHealth))]
[RequireComponent(typeof(PlayerMoveSample))]
public class PlayerHandIK : MonoBehaviour
{
    private Animator _animator;
    private PlayerAimIK _aimIK;
    private PlayerCartIK _cartIK;
    private PlayerItemIK _itemIK;
    private PlayerHealth _playerHealth;
    private PlayerMoveSample _playerMove;
    private PlayerArrestInput _arrestInput;

    // 다운~기상(Getting Up) 애니메이션이 끝날 때까지 손 IK를 전부 애니메이션에 맡긴다.
    private bool _handsSuppressed;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _aimIK = GetComponent<PlayerAimIK>();
        _cartIK = GetComponent<PlayerCartIK>();
        _itemIK = GetComponent<PlayerItemIK>();
        _playerHealth = GetComponent<PlayerHealth>();
        _playerMove = GetComponent<PlayerMoveSample>();
        _arrestInput = GetComponent<PlayerArrestInput>();
    }

    private void OnEnable()
    {
        _playerHealth.DownedStateChanged += HandleDownedStateChanged;
        _playerMove.GettingUpFinished += HandleGettingUpFinished;
    }

    private void OnDisable()
    {
        _playerHealth.DownedStateChanged -= HandleDownedStateChanged;
        _playerMove.GettingUpFinished -= HandleGettingUpFinished;
    }

    private void HandleDownedStateChanged(bool previousValue, bool isDowned)
    {
        if (!isDowned) return; // 소생 시작 시점은 아직 Getting Up 중이므로 무시, GettingUpFinished에서 해제한다.

        _handsSuppressed = true;
        _itemIK.DisableItems();
    }

    private void HandleGettingUpFinished()
    {
        _handsSuppressed = false;
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (_handsSuppressed)
        {
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            return;
        }

        if (_cartIK.IsActive)
        {
            _cartIK.ApplyIK(layerIndex);
            // 카트 사용중 손 아이템 비활성화
            _itemIK.DisableItems();
            return;
        }

        if (_aimIK.IsActive)
        {
            // 섞이는 중에는 아이템 자세를 먼저 깔아 둔다. 조준 IK 가 그 자세에서 출발해 섞이므로
            // 손에 든 것이 있으면 빈손 자세를 거치지 않고 곧바로 이어진다.
            if (_aimIK.IsBlending && _itemIK.IsActive)
            {
                _itemIK.ApplyIK(layerIndex);
            }

            _aimIK.ApplyIK(layerIndex);

            // 총 사용중 손 아이템 비활성화. 다만 팔이 올라오는 동안에는 아직 들고 있어야 한다.
            // 좌클릭하자마자 내리면 총과 손전등이 사라진 빈손이 화면 중앙으로 올라간다.
            // 손전등은 왼손에 그대로 들고 있는다. 오른손 아이템만 내린다.
            if (_arrestInput == null || _arrestInput.IsToolVisualShown)
            {
                _itemIK.DisableItems(hideFlashlight: false);
            }
            else
            {
                // 손에 붙은 제압기가 꺼진 뒤에도 조준 자세가 풀릴 때까지는 IK 가 계속 돈다.
                // 그동안 아이템까지 숨겨 두면 손에 아무것도 없다가 끝에서 툭 나타난다.
                _itemIK.EnableItems();
            }

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
