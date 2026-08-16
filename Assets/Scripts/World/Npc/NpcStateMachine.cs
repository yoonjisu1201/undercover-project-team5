using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// Idle, Walk, Run의 현재 상태와 Enter, Execute, Exit 전환 순서만 관리합니다.
[RequireComponent(typeof(NpcMovement))]
public sealed class NpcStateMachine : MonoBehaviour
{
    [Header("States")]
    // 각 State가 자신의 설정과 행동을 직접 소유하도록 Inspector에 묶어서 표시합니다.
    [SerializeField] private NpcIdleState _idleState = new();
    [SerializeField] private NpcWalkState _walkState = new();
    [SerializeField] private NpcRunState _runState = new();

    // State를 실행할 때 필요한 이동, Animator와 휴대폰 이벤트 컴포넌트입니다.
    private NpcMovement _movement;
    private Animator _animator;
    private NetworkAnimator _networkAnimator;
    private NpcAnimationEvents _animationEvents;

    // Phone End를 기다리는 Run 요청의 목적지 유무와 위치를 보관합니다.
    private bool _isRunPendingAfterPhone;
    private bool _pendingRunHasDestination;
    private Vector3 _pendingRunDestination;

    // 외부 시스템이 현재 상태를 읽되 직접 교체하지 못하도록 setter를 제한합니다.
    public INpcState CurrentState { get; private set; }

    // 배회 로직은 Idle 행동이 끝난 뒤에만 다음 목적지를 선택합니다.
    public bool CanSelectDestination => CurrentState == _idleState && _idleState.IsComplete;

    // 실제 Walk 이동 구간 또는 Run 중일 때만 이동 중으로 판단합니다.
    public bool IsMoving =>
        ((CurrentState == _walkState && _walkState.IsMovementActive) ||
         CurrentState == _runState) &&
        !_movement.IsHeldExternally &&
        !_movement.HasArrived;

    // NPC 행동 선택과 상태 실행은 서버 한 곳에서만 처리합니다.
    private bool HasServerAuthority => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    private void Awake()
    {
        // 같은 NPC의 이동 컴포넌트를 모든 State가 공유하도록 가져옵니다.
        _movement = GetComponent<NpcMovement>();
        // HumanVisual 자식의 Animator를 각 State가 직접 제어하도록 가져옵니다.
        _animator = GetComponentInChildren<Animator>(true);
        // Trigger를 클라이언트에 전달할 기존 NetworkAnimator를 가져옵니다.
        _networkAnimator = GetComponent<NetworkAnimator>();
        // HumanVisual 자식의 휴대폰 Animation Event 수신기를 가져옵니다.
        _animationEvents = GetComponentInChildren<NpcAnimationEvents>(true);

        // 각 행동에 실제로 필요한 컴포넌트만 State에 연결합니다.
        _idleState.Initialize(_movement, _animator, _networkAnimator, _animationEvents);
        _walkState.Initialize(_movement, _animator, _networkAnimator, _animationEvents);
        _runState.Initialize(_movement, _animator);

        // State가 판단한 전환 의도를 StateMachine의 실제 전환 메서드에 연결합니다.
        _idleState.ResumeWalkRequested += ResumeWalkAfterRest;
        _idleState.PhoneExitCompleted += CompletePendingRunAfterPhone;
        _walkState.RestRequested += BeginRest;
        _walkState.IdleRequested += ChangeToIdle;
        _walkState.PhoneExitCompleted += CompletePendingRunAfterPhone;
    }

    private void Start()
    {
        // 서버가 시작될 때 첫 상태를 Idle로 초기화합니다.
        if (HasServerAuthority)
        {
            TransitionTo(_idleState);
        }
    }

    private void Update()
    {
        // 클라이언트와 외부에서 붙잡힌 NPC는 코드 FSM을 진행하지 않습니다.
        if (!HasServerAuthority || _movement.IsHeldExternally)
        {
            return;
        }

        // 현재 State 하나의 Execute만 매 프레임 호출합니다.
        CurrentState?.Execute();
    }

    public void ChangeToIdle()
    {
        // 이미 Idle이면 Sub-State Machine Entry를 다시 실행할 필요가 없습니다.
        if (CurrentState == _idleState)
        {
            //Debug.Log($"[NpcStateMachine] '{name}' NPC가 이미 Idle 상태이므로 현재 상태를 유지합니다.", this);
            return;
        }

        // 다른 상태에서는 일반 전환 순서로 Idle에 진입합니다.
        TransitionTo(_idleState);
    }

    public void WaitForNextIdleCompletion()
    {
        // 목적지 선택에 실패하면 새 Variant를 고르지 않고 다음 기본 Idle 완료를 기다립니다.
        _idleState.WaitForNextCompletion();
    }

    public void ChangeToWalk(Vector3 destination)
    {
        // 이미 Walk 중이면 상태 수명 주기를 반복하지 않고 목적지만 갱신합니다.
        if (CurrentState == _walkState)
        {
            _walkState.UpdateDestination(destination);
            return;
        }

        // 새 이동 목적지를 준비한 뒤 Walk State로 전환합니다.
        _walkState.PrepareTravel(destination, resumeTravel: false);
        TransitionTo(_walkState);
    }

    public void ChangeToRun()
    {
        // 이미 Run 중인 추격 요청은 현재 경로와 속도를 그대로 유지합니다.
        if (CurrentState == _runState)
        {
            //Debug.Log($"[NpcStateMachine] '{name}' NPC가 이미 Run 상태이므로 현재 경로와 속도를 유지합니다.", this);
            return;
        }

        // Phone 행동 중이면 같은 묶음의 End를 완료한 뒤 현재 경로로 Run을 시작합니다.
        if (TryWaitForPhoneBeforeRun(false, default))
        {
            return;
        }

        StartRun(false, default);
    }

    public void ChangeToRun(Vector3 destination)
    {
        // 이미 Run 중이면 재진입하지 않고 새 목적지만 적용합니다.
        if (CurrentState == _runState)
        {
            _runState.UpdateDestination(destination);
            return;
        }

        // Phone 행동 중이면 End를 완료할 때까지 새 Run 목적지를 보관합니다.
        if (TryWaitForPhoneBeforeRun(true, destination))
        {
            return;
        }

        StartRun(true, destination);
    }

    private bool TryWaitForPhoneBeforeRun(bool hasDestination, Vector3 destination)
    {
        // 현재 State만 자신의 Phone End를 시작하고 완료 시점을 이벤트로 알립니다.
        bool waitsForPhone =
            (CurrentState == _idleState && _idleState.TryCompletePhoneBeforeExit()) ||
            (CurrentState == _walkState && _walkState.TryCompletePhoneBeforeExit());

        if (!waitsForPhone)
        {
            //Debug.Log(
            //    $"[NpcStateMachine] '{name}' NPC는 종료할 Phone 행동이 없어 Run으로 즉시 전환합니다. " +
            //    $"(새 목적지 사용: {hasDestination}, 목적지: {destination})",
            //    this);
            return false;
        }

        // 같은 Run 요청이 반복되면 가장 최근 목적지 정보로 대기 내용을 갱신합니다.
        _isRunPendingAfterPhone = true;
        _pendingRunHasDestination = hasDestination;
        _pendingRunDestination = destination;

        //Debug.Log(
        //    $"[NpcStateMachine] '{name}' NPC의 Run 전환을 Phone End 완료까지 대기합니다. " +
        //    $"(새 목적지 사용: {hasDestination}, 목적지: {destination})",
        //    this);

        return true;
    }

    private void CompletePendingRunAfterPhone()
    {
        // 일반 Phone 종료 이벤트는 Run 요청이 대기 중일 때만 상태 전환에 사용합니다.
        if (!_isRunPendingAfterPhone)
        {
            Debug.LogWarning($"[NpcStateMachine] '{name}' NPC가 Phone Exit 완료 이벤트를 받았지만 대기 중인 Run 요청이 없습니다.", this);
            return;
        }

        bool hasDestination = _pendingRunHasDestination;
        Vector3 destination = _pendingRunDestination;
        _isRunPendingAfterPhone = false;
        _pendingRunHasDestination = false;
        _pendingRunDestination = default;

        StartRun(hasDestination, destination);
    }

    private void StartRun(bool hasDestination, Vector3 destination)
    {
        // 목적지가 없는 추격 요청은 기존 경로를, 목적지가 있으면 새 경로를 사용합니다.
        if (hasDestination)
        {
            _runState.PrepareWithDestination(destination);
        }
        else
        {
            _runState.PrepareWithCurrentPath();
        }

        TransitionTo(_runState);
    }

    private void TransitionTo(INpcState nextState)
    {
        // 같은 상태 전환은 호출부에서 별도 처리하므로 여기서는 중복 실행하지 않습니다.
        if (CurrentState == nextState)
        {
            Debug.LogWarning($"[NpcStateMachine] '{name}' NPC의 현재 State와 동일한 State 전환 요청을 무시합니다.", this);
            return;
        }

        // 이전 State가 가진 이벤트 구독과 진행값을 먼저 정리합니다.
        CurrentState?.Exit();
        // 현재 State 참조를 다음 State로 교체합니다.
        CurrentState = nextState;
        // 다음 State가 자신의 이동과 애니메이션 행동을 시작하게 합니다.
        CurrentState.Enter();
    }

    private void BeginRest(Vector3 destination, float restSeconds)
    {
        // Walk가 끝낸 이동 계획과 휴식 시간을 Idle State에 먼저 전달합니다.
        _idleState.PrepareRest(destination, restSeconds);
        // 휴식도 이동 구간 종료이므로 Walk를 나와 Idle State로 전환합니다.
        TransitionTo(_idleState);
    }

    private void ResumeWalkAfterRest(Vector3 destination)
    {
        // Idle이 보관한 목적지를 Walk에 전달하고 장거리 이동 재개로 표시합니다.
        _walkState.PrepareTravel(destination, resumeTravel: true);
        // 새 Walk 구간으로 전환해 애니메이션을 다시 선택하고 이동을 이어갑니다.
        TransitionTo(_walkState);
    }
}
