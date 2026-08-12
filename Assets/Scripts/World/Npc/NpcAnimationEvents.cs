using System;
using UnityEngine;

// Animator 클립에서 호출하는 행동 완료 이벤트와 휴대폰 소품을 관리합니다.
[RequireComponent(typeof(Animator))]
public sealed class NpcAnimationEvents : MonoBehaviour
{
    // 휴대폰 애니메이션을 벗어난 클라이언트에서 소품을 정리할 때 사용합니다.
    private const string PhoneActionTag = "PhoneAction";

    // Walking While Texting에서만 전용 휴대폰 Transform을 적용하기 위한 State Hash입니다.
    private static readonly int WalkingWhileTextingStartHash = Animator.StringToHash("Walking While Texting Start");
    // 한 회성 애니메이션 종료 후 기본 Idle 또는 Walk에 머물도록 선택값을 초기화합니다.
    private static readonly int AnimationVariantHash = Animator.StringToHash("AnimationVariant");

    [Header("Phone Prop")]
    // 손 아래에 배치한 휴대폰 오브젝트를 Animation Event 시점에 표시합니다.
    [SerializeField] private GameObject _phoneProp;
    // 직접 조정한 Walking While Texting 전용 휴대폰 위치를 그대로 사용합니다.
    [SerializeField] private Vector3 _walkingWhileTextingPhoneLocalPosition = new(0.000f, 0.117f, 0.086f);
    // Walking While Texting 전용 휴대폰 회전값입니다.
    [SerializeField] private Vector3 _walkingWhileTextingPhoneLocalEulerAngles = new(150.219f, 107.609f, 93.796f);

    // Animation Event가 실행되는 HumanVisual의 Animator를 보관합니다.
    private Animator _animator;
    // 전용 Transform 적용 후 되돌릴 휴대폰의 기본 위치와 회전을 보관합니다.
    private Vector3 _defaultPhoneLocalPosition;
    private Quaternion _defaultPhoneLocalRotation;
    // 현재 전용 Transform이 적용됐는지 기록해 중복 복원을 막습니다.
    private bool _isUsingWalkingWhileTextingPhoneTransform;

    // Standard Idle 또는 Arm Stretching 애니메이션이 끝났음을 현재 Idle State에 알립니다.
    public event Action IdleAnimationCompleted;
    // Idle Phone Loop가 정해진 시점에 도달해 수납 동작을 시작해야 함을 알립니다.
    public event Action PhoneEndingRequested;
    // 휴대폰을 꺼내는 Start 동작이 끝났음을 현재 Walk State에 알립니다.
    public event Action PhoneStartingCompleted;
    // 휴대폰을 넣는 End 동작이 끝났음을 현재 Idle 또는 Walk State에 알립니다.
    public event Action PhoneEndingCompleted;

    private void Awake()
    {
        // Animation Event를 받는 같은 GameObject의 Animator를 가져옵니다.
        _animator = GetComponent<Animator>();

        // Walking While Texting 종료 후 복원할 프리팹 기본 Transform을 저장합니다.
        _defaultPhoneLocalPosition = _phoneProp.transform.localPosition;
        _defaultPhoneLocalRotation = _phoneProp.transform.localRotation;
    }

    private void LateUpdate()
    {
        // 휴대폰이 꺼져 있거나 PhoneAction 상태가 계속되면 별도 정리가 필요 없습니다.
        if (!_phoneProp.activeSelf || IsPlayingPhoneAction())
        {
            return;
        }

        // Run처럼 End 동작을 건너뛴 전환에서는 Tag 확인 후 남은 소품을 정리합니다.
        HidePhone();
    }

    // Animation Event: Standard Idle 또는 Arm Stretching 애니메이션의 마지막 프레임에서 호출됩니다.
    public void CompleteIdleAnimation()
    {
        // Walk나 Run으로 전환된 뒤 도착한 Idle Event가 새 Variant를 덮어쓰지 않게 합니다.
        if (_animator.GetInteger("NpcState") == 0)
        {
            _animator.SetInteger(AnimationVariantHash, 0);
        }

        // 현재 활성 Idle State에 애니메이션 완료 시점을 전달합니다.
        IdleAnimationCompleted?.Invoke();
    }

    // Animation Event: Idle Phone Loop에서 최대 행동 시간 안에 End를 시작할 시점에 호출됩니다.
    public void RequestPhoneEnding()
    {
        // 현재 활성 Idle State가 같은 Phone 묶음의 End 전환을 요청하도록 이벤트를 보냅니다.
        PhoneEndingRequested?.Invoke();
    }

    // Animation Event: 손이 휴대폰을 꺼내는 프레임에서 호출됩니다.
    public void ShowPhone()
    {
        // Walking While Texting에서만 직접 조정한 전용 Transform을 적용합니다.
        if (IsAnimatorStateActive(WalkingWhileTextingStartHash))
        {
            Transform phoneTransform = _phoneProp.transform;
            phoneTransform.localPosition = _walkingWhileTextingPhoneLocalPosition;
            phoneTransform.localRotation = Quaternion.Euler(_walkingWhileTextingPhoneLocalEulerAngles);
            _isUsingWalkingWhileTextingPhoneTransform = true;
        }

        // 손이 휴대폰을 잡는 프레임에 맞춰 소품을 표시합니다.
        _phoneProp.SetActive(true);
    }

    // Animation Event와 State 강제 종료에서 함께 호출됩니다.
    public void HidePhone()
    {
        // 전용 Transform을 사용했다면 다음 휴대폰 동작 전에 기본값으로 복원합니다.
        if (_isUsingWalkingWhileTextingPhoneTransform)
        {
            Transform phoneTransform = _phoneProp.transform;
            phoneTransform.localPosition = _defaultPhoneLocalPosition;
            phoneTransform.localRotation = _defaultPhoneLocalRotation;
            _isUsingWalkingWhileTextingPhoneTransform = false;
        }

        // 손이 휴대폰을 넣었거나 휴대폰 상태를 벗어나면 소품을 숨깁니다.
        _phoneProp.SetActive(false);
    }

    // Animation Event: Phone Start의 마지막 프레임에서 호출됩니다.
    public void CompletePhoneStarting()
    {
        // 현재 활성 Walk State가 실제 이동을 시작할 수 있도록 완료 이벤트를 보냅니다.
        PhoneStartingCompleted?.Invoke();
    }

    // Animation Event: Phone End의 마지막 프레임에서 호출됩니다.
    public void CompletePhoneEnding()
    {
        // 기본 Idle 또는 Walk로 돌아온 뒤 같은 Phone Variant가 재진입하지 않도록 비웁니다.
        _animator.SetInteger(AnimationVariantHash, 0);
        // 현재 활성 Idle 또는 Walk State가 행동 구간을 마칠 수 있도록 완료 이벤트를 보냅니다.
        PhoneEndingCompleted?.Invoke();
    }

    private bool IsPlayingPhoneAction()
    {
        // 현재 State가 PhoneAction이면 휴대폰 소품을 유지합니다.
        if (_animator.GetCurrentAnimatorStateInfo(0).IsTag(PhoneActionTag))
        {
            return true;
        }

        // 전환 중에는 다음 State까지 확인해 중간 프레임에 소품이 꺼지지 않게 합니다.
        return _animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).IsTag(PhoneActionTag);
    }

    // 대상 Animator State가 현재 재생 중이거나 전환 후 진입할 State인지 확인합니다.
    private bool IsAnimatorStateActive(int stateHash)
    {
        // 현재 재생 중인 State가 대상인지 먼저 확인합니다.
        if (_animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash)
        {
            return true;
        }

        // 전환 중이면 다음에 진입할 State도 대상에 포함합니다.
        return _animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).shortNameHash == stateHash;
    }
}
