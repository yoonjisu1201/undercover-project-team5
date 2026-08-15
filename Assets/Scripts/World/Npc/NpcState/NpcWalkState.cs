using System;
using Unity.Netcode.Components;
using UnityEngine;

// 지정한 목적지까지 걷는 이동과 구간별 애니메이션, 장거리 휴식을 관리합니다.
public sealed class NpcWalkState : INpcState
{
    // Animator의 Walk 상태 값은 Root Transition의 NpcState 조건과 동일한 1입니다.
    private const int NpcStateValue = 1;

    // 무작위 선택 결과와 걷기 애니메이션을 같은 순서로 대응합니다.
    private enum WalkAnimation
    {
        Walk,
        TextingAndWalking,
        WalkingWhileTexting,
        Count
    }

    // 동시에 성립할 수 없는 Walk와 Phone 진행 단계를 하나의 값으로 표현합니다.
    private enum WalkPhase
    {
        Inactive,
        Walking,
        PhoneStarting,
        PhoneWalking,
        PhoneEndingToIdle,
        PhoneEndingToRest,
        PhoneEndingToRun
    }

    // Walk State에서 NavMeshAgent에 적용할 기존 이동 속도입니다.
    [Range(1f, 2f)] private float _speed = 2f;

    // 처음 목적지가 이 거리 이상 떨어져 있을 때만 이동 중 휴식을 사용합니다.
    [Min(0f)] private float _longTravelDistanceThreshold = 20f;
    // 장거리 이동 중 이 거리만큼 진행할 때마다 한 이동 구간을 끝내고 휴식합니다.
    [Min(0.1f)] private float _restIntervalDistance = 10f;
    // 휴식 State에 전달할 무작위 행동 시간 범위입니다.
    [Min(0f)] private float _minimumRestSeconds = 1f;
    [Min(0f)] private float _maximumRestSeconds = 3f;

    // 기본 Walk 대신 휴대폰 Walk 애니메이션을 선택할 확률입니다.
    [Range(0f, 1f)] private float _alternativeAnimationChance = 0.45f;
    // 예상 이동 시간이 이 값보다 짧으면 휴대폰 애니메이션을 선택하지 않습니다.
    [Min(0f)] private float _minimumPhoneSeconds = 6f;

    // State가 직접 사용하는 이동, Animator, 네트워크 Trigger와 휴대폰 이벤트를 보관합니다.
    private NpcMovement _movement;
    private Animator _animator;
    private NetworkAnimator _networkAnimator;
    private NpcAnimationEvents _animationEvents;

    // 일반 도착으로 Walk 구간이 끝났음을 StateMachine에 알립니다.
    public event Action IdleRequested;
    // 장거리 이동 구간 종료 시 목적지와 휴식 시간을 StateMachine에 전달합니다.
    public event Action<Vector3, float> RestRequested;
    // 외부 전환 요청으로 시작한 Phone End가 끝나면 StateMachine에 전환 가능 시점을 알립니다.
    public event Action PhoneExitCompleted;

    // 현재 이동 구간의 목적지와 장거리 휴식 누적값을 보관합니다.
    private Vector3 _destination;
    private Vector3 _lastTravelSamplePosition;
    private float _distanceSinceRest;
    private float _pendingRestSeconds;
    // 최초 장거리 판정과 현재 Walk·Phone 진행 단계를 보관합니다.
    private bool _shouldRestDuringTravel;
    private WalkPhase _phase;

    // 외부 시스템은 Phone Start와 End를 제외한 실제 이동 구간만 이동 중으로 판단합니다.
    public bool IsMovementActive =>
        _phase == WalkPhase.Walking ||
        _phase == WalkPhase.PhoneWalking;

    private bool IsUsingPhone =>
        _phase == WalkPhase.PhoneStarting ||
        _phase == WalkPhase.PhoneWalking ||
        IsPhoneEnding;

    private bool IsPhoneEnding =>
        _phase == WalkPhase.PhoneEndingToIdle ||
        _phase == WalkPhase.PhoneEndingToRest ||
        _phase == WalkPhase.PhoneEndingToRun;

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
        // 활성 Walk State만 Phone Start와 End 완료 이벤트를 받도록 구독합니다.
        _animationEvents.PhoneStartingCompleted += CompletePhoneStarting;
        _animationEvents.PhoneEndingCompleted += CompletePhoneEnding;

        // 새 이동 구간의 애니메이션 진행 상태와 거리 누적값을 초기화합니다.
        _phase = WalkPhase.Inactive;
        _distanceSinceRest = 0f;
        _lastTravelSamplePosition = _movement.transform.position;

        // 선택한 애니메이션이 실제 이동을 시작할 때 사용할 Walk 속도를 준비합니다.
        _movement.SetSpeed(_speed);

        // 이 이동 구간에서 사용할 Walk 애니메이션 하나를 선택합니다.
        PlaySelectedAnimation();
    }

    public void Execute()
    {
        // 실제 Walk 구간 밖에서는 도착 판정과 거리 누적을 진행하지 않습니다.
        if (!IsMovementActive)
        {
            return;
        }

        // 기존 도착 판정을 그대로 사용해 목적지에 도착하면 현재 이동 구간을 끝냅니다.
        if (_movement.HasArrived)
        {
            _movement.Stop();
            EndSegment(false);
            return;
        }

        // 장거리 목적지가 아니면 휴식 거리 계산 없이 목적지까지 계속 이동합니다.
        if (!_shouldRestDuringTravel)
        {
            return;
        }

        // 직전 프레임 위치부터 실제로 이동한 거리를 장거리 휴식 누적값에 더합니다.
        Vector3 currentPosition = _movement.transform.position;
        _distanceSinceRest += Vector3.Distance(_lastTravelSamplePosition, currentPosition);
        _lastTravelSamplePosition = currentPosition;

        // 휴식 간격에 도달하기 전에는 현재 이동 구간을 계속 유지합니다.
        if (_distanceSinceRest < _restIntervalDistance)
        {
            return;
        }

        // 휴식 간격에 도달하면 경로를 보존한 채 이동을 멈추고 구간 종료를 시작합니다.
        _distanceSinceRest = 0f;
        _pendingRestSeconds = GetRestSeconds();
        _movement.Pause();
        EndSegment(true);
    }

    public void Exit()
    {
        // 비활성 State가 이후 Animation Event를 받지 않도록 구독을 해제합니다.
        _animationEvents.PhoneStartingCompleted -= CompletePhoneStarting;
        _animationEvents.PhoneEndingCompleted -= CompletePhoneEnding;

        //Debug.Log($"[NpcWalkState] '{_movement.name}' Walk State 구독 해제", _movement);

        // Phone 종료 대기 밖에서 State가 종료돼도 남은 휴대폰을 정리합니다.
        if (IsUsingPhone)
        {
            _animationEvents.HidePhone();
        }

        // End Trigger가 소비되기 전에 강제 전환됐다면 다음 Phone 행동에 남지 않도록 정리합니다.
        if (IsPhoneEnding)
        {
            ResetTrigger(AnimatorHashes.EndPhoneAction);
        }

        // 새 목적지는 Prepare에서 장거리 여부를 덮어쓰고, 휴식 재개는 현재 계획을 그대로 사용합니다.
        _distanceSinceRest = 0f;
        _phase = WalkPhase.Inactive;
    }

    public bool TryCompletePhoneBeforeExit()
    {
        // 휴대폰을 사용하지 않는 Walk는 End를 기다리지 않고 즉시 다른 State로 나갈 수 있습니다.
        if (!IsUsingPhone)
        {
            return false;
        }

        // Phone Start 또는 자연 End 중이었다면 Run이 이어받을 기존 목적지 경로를 먼저 준비합니다.
        bool shouldRequestPhoneEnd = !IsPhoneEnding;
        if (_phase == WalkPhase.PhoneStarting)
        {
            StartMovement();
        }
        else if (_phase == WalkPhase.PhoneEndingToIdle || _phase == WalkPhase.PhoneEndingToRest)
        {
            PrepareMovementRoute();
        }

        // 준비된 경로는 보존한 채 이동만 멈추고 Phone End 완료 뒤 Run에서 이어서 사용합니다.
        _movement.Pause();

        // 이미 도착이나 휴식으로 자연 종료 중이면 현재 End를 그대로 기다립니다.
        _phase = WalkPhase.PhoneEndingToRun;
        if (shouldRequestPhoneEnd)
        {
            SetTrigger(AnimatorHashes.EndPhoneAction);
        }

        return true;
    }

    public void PrepareTravel(Vector3 destination, bool resumeTravel)
    {
        // State 진입 전에 목적지를 저장하고 새 이동일 때만 최초 장거리 여부를 판정합니다.
        _destination = destination;
        if (!resumeTravel)
        {
            _shouldRestDuringTravel =
                Vector3.Distance(_movement.transform.position, destination) >= _longTravelDistanceThreshold;
        }
    }

    public void UpdateDestination(Vector3 destination)
    {
        // 이미 Walk 중인 NPC의 목적지가 변경되면 장거리 휴식 기준도 새 거리로 다시 계산합니다.
        _destination = destination;
        _shouldRestDuringTravel =
            Vector3.Distance(_movement.transform.position, destination) >= _longTravelDistanceThreshold;
        _distanceSinceRest = 0f;
        _lastTravelSamplePosition = _movement.transform.position;

        // Phone Start와 End 사이의 실제 이동 중일 때만 NavMesh 목적지를 즉시 갱신합니다.
        if (IsMovementActive)
        {
            _movement.MoveTo(destination);
        }
    }

    private void CompletePhoneStarting()
    {
        // 현재 Walk가 Phone Start를 기다리는 경우에만 실제 이동을 시작합니다.
        if (_phase != WalkPhase.PhoneStarting)
        {
            Debug.Log(
                    $"[NpcWalkState] '{_movement.name}' NPC가 Phone Start 완료 이벤트를 받았지만 " +
                    $"현재 단계가 Phone Start 대기 상태가 아니어서 무시합니다. (현재 단계: {_phase})",
                    _movement);
            return;
        }

        StartMovement();
    }

    private void CompletePhoneEnding()
    {
        // 현재 Walk가 Phone End를 기다리는 경우에만 이동 구간 완료를 처리합니다.
        if (!IsPhoneEnding)
        {
            Debug.LogWarning(
                    $"[NpcWalkState] '{_movement.name}' NPC가 Phone End 완료 이벤트를 받았지만 " +
                    $"현재 단계가 Phone End 대기 상태가 아닙니다. (현재 단계: {_phase})",
                    _movement);
            return;
        }

        // Phone End Animation Event가 도착하면 완료 목적과 소품을 먼저 정리합니다.
        WalkPhase completedPhase = _phase;

        _phase = WalkPhase.Inactive;
        _animationEvents.HidePhone();

        // 외부 전환이 대기 중이면 도착·휴식 처리보다 우선해 StateMachine에 알립니다.
        if (completedPhase == WalkPhase.PhoneEndingToRun)
        {
            PhoneExitCompleted?.Invoke();
            return;
        }

        // 자연 종료된 Walk Phone 행동은 기존 도착 또는 휴식 전환을 완료합니다.
        CompleteSegment(completedPhase == WalkPhase.PhoneEndingToRest);
    }

    private void PlaySelectedAnimation()
    {
        // 기본값은 휴대폰을 사용하지 않아 즉시 이동을 시작하는 Walk입니다.
        WalkAnimation animation = WalkAnimation.Walk;
        // 현재 직선거리와 Walk 속도로 휴대폰 Start와 End를 사용할 여유 시간을 추정합니다.
        float estimatedTravelSeconds =
            _speed > 0f ? Vector3.Distance(_movement.transform.position, _destination) / _speed : 0f;

        // 충분히 긴 이동 구간에서만 확률에 따라 두 Phone Walk 중 하나를 선택합니다.
        if (estimatedTravelSeconds >= _minimumPhoneSeconds &&
            UnityEngine.Random.value < _alternativeAnimationChance)
        {
            animation = (WalkAnimation)UnityEngine.
                Random.Range(
                (int)WalkAnimation.TextingAndWalking,
                (int)WalkAnimation.Count);
        }

        // 선택값을 Animator에 전달하고 Phone Walk는 Start 완료를 기다리도록 기록합니다.
        ApplyAnimationSelection(animation);
        if (animation != WalkAnimation.Walk)
        {
            _phase = WalkPhase.PhoneStarting;

            //Debug.Log(
            //    $"[NpcWalkState] '{_movement.name}' NPC가 Phone Start 완료까지 이동 시작을 대기합니다. " +
            //    $"(애니메이션: {animation})",
            //    _movement);

            return;
        }

        // 기본 Walk는 Phone Start Animation Event가 없으므로 즉시 이동합니다.
        StartMovement();
    }

    private void StartMovement()
    {
        // Phone Start에서 이어진 이동과 기본 Walk를 각각 실행 단계로 전환합니다.
        _phase = _phase == WalkPhase.PhoneStarting ? WalkPhase.PhoneWalking : WalkPhase.Walking;
        PrepareMovementRoute();
    }

    private void PrepareMovementRoute()
    {
        // 현재 위치를 거리 기준으로 맞추고 실제 이동 시점에 NavMesh 경로를 재개합니다.
        _lastTravelSamplePosition = _movement.transform.position;
        _movement.Resume();
        _movement.MoveTo(_destination);
    }

    private void EndSegment(bool rest)
    {
        // Phone Walk는 현재 Loop에서 End로 전환한 뒤 Animation Event를 기다립니다.
        if (_phase == WalkPhase.PhoneWalking)
        {
            _phase = rest ? WalkPhase.PhoneEndingToRest : WalkPhase.PhoneEndingToIdle;

            //Debug.Log(
            //    $"[NpcWalkState] '{_movement.name}' NPC가 Phone End 완료까지 다음 상태 전환을 대기합니다. " +
            //    $"(전환 목적: {_phase})",
            //    _movement);

            // 현재 Phone Loop에서 같은 묶음의 Phone End로 전환하도록 Trigger를 보냅니다.
            SetTrigger(AnimatorHashes.EndPhoneAction);
            return;
        }

        // 기본 Walk는 End 애니메이션이 없으므로 바로 다음 State 전환을 요청합니다.
        _phase = WalkPhase.Inactive;
        CompleteSegment(rest);
    }

    private void CompleteSegment(bool rest)
    {
        // 휴식으로 끝난 구간은 같은 장거리 이동 계획을 유지한 채 Idle 휴식을 요청합니다.
        if (rest)
        {
            RestRequested?.Invoke(_destination, _pendingRestSeconds);
            return;
        }

        // 목적지 도착으로 끝난 구간은 일반 Idle 전환을 요청합니다.
        IdleRequested?.Invoke();
    }

    private float GetRestSeconds()
    {
        // Inspector에서 최소와 최대가 뒤집혀도 두 값 사이의 정상 범위를 사용합니다.
        float minimum = Mathf.Min(_minimumRestSeconds, _maximumRestSeconds);
        float maximum = Mathf.Max(_minimumRestSeconds, _maximumRestSeconds);
        // 기존과 동일하게 범위 안에서 이번 휴식 시간을 무작위로 선택합니다.
        return UnityEngine.Random.Range(minimum, maximum);
    }

    private void ApplyAnimationSelection(WalkAnimation animation)
    {
        // 먼저 Variant를 설정해 Walk 진입 순간 잘못된 분기가 평가되지 않게 합니다.
        _animator.SetInteger(AnimatorHashes.AnimationVariant, (int)animation);
        // 기존 Root Transition이 Walk 서브 스테이트 머신으로 이동하도록 상태 값을 설정합니다.
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
