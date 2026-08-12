using System;
using Unity.Netcode.Components;
using UnityEngine;

// NPC가 멈춰 있는 동안의 애니메이션 선택과 행동 완료 이벤트를 관리합니다.
public sealed class NpcIdleState : INpcState
{
    // Animator의 Idle 상태 값과 enum 기본값을 같은 0으로 유지합니다.
    private const int NpcStateValue = 0;

    // 무작위 선택 결과와 정지 애니메이션을 같은 순서로 대응합니다.
    private enum IdleAnimation
    {
        Idle,
        ArmStretching,
        TalkingOnCellPhone,
        TalkingOnPhone,
        TextingWhileStanding,
        Count
    }

    // 기본 Idle 대신 다른 정지 애니메이션을 선택할 확률입니다.
    [Range(0f, 1f)] private float _alternativeAnimationChance = 0.9f;

    // State가 직접 사용하는 이동, Animator, 네트워크 Trigger와 Animation Event를 보관합니다.
    private NpcMovement _movement;
    private Animator _animator;
    private NetworkAnimator _networkAnimator;
    private NpcAnimationEvents _animationEvents;

    // 휴식이 끝난 뒤 같은 목적지로 Walk를 재개하도록 StateMachine에 알립니다.
    public event Action<Vector3> ResumeWalkRequested;
    // 외부 전환 요청으로 시작한 Phone End가 끝나면 StateMachine에 전환 가능 시점을 알립니다.
    public event Action PhoneExitCompleted;

    // 휴식 후 재개할 목적지와 휴식 종료 시각을 보관합니다.
    private Vector3 _restDestination;
    private float _restEndTime;
    // 현재 Idle이 휴식인지 휴대폰 행동인지 구분해 이벤트 흐름을 선택합니다.
    private bool _hasRestRequest;
    private bool _usesPhone;
    private bool _isPhoneEnding;
    private bool _isPhoneExitRequested;

    // 배회 로직은 Animation Event가 행동 완료를 알린 뒤에만 다음 목적지를 선택합니다.
    public bool IsComplete { get; private set; }

    public void Initialize(
        NpcMovement movement,
        Animator animator,
        NetworkAnimator networkAnimator,
        NpcAnimationEvents animationEvents)
    {
        // 필요한 컴포넌트만 직접 전달받습니다.
        _movement = movement;
        _animator = animator;
        _networkAnimator = networkAnimator;
        _animationEvents = animationEvents;
    }

    public void Enter()
    {
        // 새 Idle 구간이 시작되므로 이전 완료 상태와 휴대폰 진행 상태를 초기화합니다.
        IsComplete = false;
        _usesPhone = false;
        _isPhoneEnding = false;
        _isPhoneExitRequested = false;

        // 활성 Idle State만 현재 클립의 행동 완료 이벤트를 받도록 구독합니다.
        _animationEvents.IdleAnimationCompleted += HandleIdleAnimationCompleted;
        _animationEvents.PhoneEndingRequested += RequestPhoneEnding;
        _animationEvents.PhoneEndingCompleted += CompletePhoneEnding;

        // 장거리 휴식은 기존 경로를 보존하고, 일반 Idle만 현재 이동 경로를 정리합니다.
        if (_hasRestRequest)
        {
            _movement.Pause();
        }
        else
        {
            _movement.Stop();
        }

        _animationEvents.HidePhone();

        // 장거리 이동 휴식은 별도 애니메이션을 고르지 않고 기본 Idle만 재생합니다.
        if (_hasRestRequest)
        {
            ApplyAnimationSelection(IdleAnimation.Idle);
            return;
        }

        // 일반 Idle 진입이면 이 구간에서 사용할 애니메이션 하나를 선택합니다.
        PlaySelectedAnimation();
    }

    public void Execute()
    {
        // 일반 Idle의 완료 시점은 Animation Event가 결정하므로 매 프레임 시간을 계산하지 않습니다.
        if (!_hasRestRequest || Time.time < _restEndTime)
        {
            return;
        }

        // 휴식이 끝나면 같은 목적지를 담아 새 Walk 구간을 시작하도록 알립니다.
        Vector3 destination = _restDestination;
        _hasRestRequest = false;
        ResumeWalkRequested?.Invoke(destination);
    }

    public void Exit()
    {
        // 비활성 State가 이후 Animation Event를 받지 않도록 구독을 해제합니다.
        _animationEvents.IdleAnimationCompleted -= HandleIdleAnimationCompleted;
        _animationEvents.PhoneEndingRequested -= RequestPhoneEnding;
        _animationEvents.PhoneEndingCompleted -= CompletePhoneEnding;

        Debug.Log($"[NpcIdleState] '{_movement.name}' Idle State 구독 해제", _movement);

        // Phone 종료 대기 밖에서 State가 종료돼도 남은 휴대폰을 정리합니다.
        if (_usesPhone)
        {
            _animationEvents.HidePhone();
        }

        // End Trigger가 소비되기 전에 강제 전환됐다면 다음 Phone 행동에 남지 않도록 정리합니다.
        if (_isPhoneEnding)
        {
            ResetTrigger(AnimatorHashes.EndPhoneAction);
        }

        // 다음 Idle 진입에 현재 구간 정보가 남지 않도록 진행 상태를 초기화합니다.
        _hasRestRequest = false;
        _usesPhone = false;
        _isPhoneEnding = false;
        _isPhoneExitRequested = false;
        IsComplete = false;
    }

    public void UnsubscribeAnimationEvents()
    {
        if (_animationEvents == null)
        {
            return;
        }

        _animationEvents.IdleAnimationCompleted -= HandleIdleAnimationCompleted;
        _animationEvents.PhoneEndingRequested -= RequestPhoneEnding;
        _animationEvents.PhoneEndingCompleted -= CompletePhoneEnding;
    }

    public bool TryCompletePhoneBeforeExit()
    {
        // 휴대폰을 사용하지 않는 Idle은 End를 기다리지 않고 즉시 다른 State로 나갈 수 있습니다.
        if (!_usesPhone)
        {

            return false;
        }

        // 외부 전환은 현재 Phone 묶음의 End 완료 뒤에 실행하도록 표시합니다.
        _isPhoneExitRequested = true;

        // 이미 자연 종료 중이면 현재 End를 그대로 기다리고 Trigger를 중복 전송하지 않습니다.
        if (!_isPhoneEnding)
        {
            _isPhoneEnding = true;
            SetTrigger(AnimatorHashes.EndPhoneAction);
        }

        return true;
    }

    public void WaitForNextCompletion()
    {
        // 목적지를 찾지 못하면 현재 기본 Idle 흐름을 유지하고 다음 완료 이벤트를 기다립니다.
        IsComplete = false;
    }

    public void PrepareRest(Vector3 destination, float restSeconds)
    {
        // Walk State가 넘긴 목적지를 보관해 휴식 후 같은 이동 경로를 재개합니다.
        _restDestination = destination;
        _restEndTime = Time.time + restSeconds;
        _hasRestRequest = true;
    }

    private void HandleIdleAnimationCompleted()
    {
        // 휴식이나 Phone 행동에서 발생한 일반 Idle 애니메이션 완료 이벤트는 상태 완료로 사용하지 않습니다.
        if (_hasRestRequest || _usesPhone)
        {
            return;
        }

        // Standard Idle 또는 Arm Stretching의 마지막 프레임에서 현재 행동을 완료합니다.
        IsComplete = true;
    }

    private void RequestPhoneEnding()
    {
        // 현재 Idle Phone Loop가 아니거나 이미 End를 시작했다면 중복 요청을 무시합니다.
        if (_hasRestRequest || !_usesPhone || _isPhoneEnding)
        {
            return;
        }

        // 클립에 배치한 시점에서 같은 Phone 묶음의 End 애니메이션을 시작합니다.
        _isPhoneEnding = true;
        SetTrigger(AnimatorHashes.EndPhoneAction);
    }

    private void CompletePhoneEnding()
    {
        // 현재 Idle이 Phone End를 기다리는 중이 아니면 다른 이벤트를 무시합니다.
        if (!_usesPhone || !_isPhoneEnding)
        {
            return;
        }

        // Phone End 마지막 프레임에서 소품과 진행 상태를 먼저 정리합니다.
        bool exitRequested = _isPhoneExitRequested;
        _usesPhone = false;
        _isPhoneEnding = false;
        _isPhoneExitRequested = false;
        _animationEvents.HidePhone();

        // 외부 전환이 대기 중이면 기본 Idle 완료 대신 StateMachine에 전환 가능 시점을 알립니다.
        if (exitRequested)
        {
            PhoneExitCompleted?.Invoke();
            return;
        }

        // 자연 종료된 Idle Phone 행동은 다음 배회 목적지를 선택할 수 있도록 완료합니다.
        IsComplete = true;
    }

    private void PlaySelectedAnimation()
    {
        // 확률 선택에 실패하면 휴대폰을 사용하지 않는 기본 Idle을 유지합니다.
        IdleAnimation animation = IdleAnimation.Idle;

        // 대체 애니메이션이 선택되면 Arm Stretching부터 마지막 Phone 행동 사이에서 고릅니다.
        if (UnityEngine.Random.value < _alternativeAnimationChance)
        {
            animation = (IdleAnimation)UnityEngine.Random.Range(
                (int)IdleAnimation.ArmStretching,
                (int)IdleAnimation.Count);
        }

        // Phone 행동 여부는 Animation Event를 어떤 흐름으로 처리할지 구분하는 데만 사용합니다.
        _usesPhone =
            animation == IdleAnimation.TalkingOnCellPhone ||
            animation == IdleAnimation.TalkingOnPhone ||
            animation == IdleAnimation.TextingWhileStanding;

        // 선택 결과를 기존 Idle 내부 Transition 조건에 전달합니다.
        ApplyAnimationSelection(animation);
    }

    private void ApplyAnimationSelection(IdleAnimation animation)
    {
        // 먼저 Variant를 설정해 Idle 진입 순간 잘못된 분기가 평가되지 않게 합니다.
        _animator.SetInteger(AnimatorHashes.AnimationVariant, (int)animation);
        // 기존 Root Transition이 Idle 서브 스테이트 머신으로 이동하도록 상태 값을 설정합니다.
        _animator.SetInteger(AnimatorHashes.NpcState, NpcStateValue);
    }

    private void SetTrigger(int triggerHash)
    {
        // Spawn 이후에는 NetworkAnimator가 서버 Trigger를 클라이언트에도 전달합니다.
        if (_networkAnimator.IsSpawned)
        {
            _networkAnimator.SetTrigger(triggerHash);
            return;
        }

        // 최초 Spawn 전 초기화에서는 로컬 Animator에 직접 Trigger를 적용합니다.
        _animator.SetTrigger(triggerHash);
    }

    private void ResetTrigger(int triggerHash)
    {
        // Spawn 이후에는 Trigger 해제도 NetworkAnimator를 통해 모든 클라이언트에 전달합니다.
        if (_networkAnimator.IsSpawned)
        {
            _networkAnimator.ResetTrigger(triggerHash);
            return;
        }

        // 최초 Spawn 전에는 로컬 Animator의 Trigger만 정리합니다.
        _animator.ResetTrigger(triggerHash);
    }
}
