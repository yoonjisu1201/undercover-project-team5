using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// NPC 상태를 생성하고 현재 상태의 수명 주기와 상태 전환을 관리합니다.
/// </summary>
[RequireComponent(typeof(NpcMovement))]
public sealed class NpcStateMachine : MonoBehaviour
{
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    [Header("Movement Settings")]
    [SerializeField, Min(0f)] private float _walkSpeed = 2f;
    [SerializeField, Min(0f)] private float _runSpeed = 4f;

    private NpcMovement _movement;
    private Animator _animator;
    private NpcContext _context;
    private INpcState _currentState;

    private IdleState _idleState;
    private WalkState _walkState;
    private RunState _runState;

    /// <summary>
    /// 상태 전환이 완료된 뒤 새 상태의 식별자를 전달합니다.
    /// </summary>
    public event Action<NpcStateId> StateChanged;

    /// <summary>
    /// 현재 실행 중인 상태 식별자를 가져옵니다.
    /// 아직 상태가 시작되지 않았다면 null입니다.
    /// </summary>
    public NpcStateId? CurrentStateId => _currentState?.Id;

    private void Awake()
    {
        _movement = GetComponent<NpcMovement>();
        _animator = GetComponentInChildren<Animator>(true);

        _context = new NpcContext
        {
            StateMachine = this,
            Movement = _movement,
            LifecycleSource = this,
        };

        _idleState = new IdleState();
        _walkState = new WalkState(_walkSpeed);
        _runState = new RunState(_runSpeed);
    }

    private void Start()
    {
        RequestIdle();
    }

    private void Update()
    {
        if (!NetworkManager.Singleton.IsServer)
        {
            return;
        }

        _currentState?.Execute(_context);
    }

    private void OnDestroy()
    {
        _currentState?.Exit(_context);

        _currentState = null;
        _context = null;
        _movement = null;

        _idleState = null;
        _walkState = null;
        _runState = null;

        StateChanged = null;
    }

    /// <summary>
    /// NPC를 Idle 상태로 전환하도록 요청합니다.
    /// </summary>
    public void RequestIdle()
    {
        ChangeState(_idleState);
    }

    /// <summary>
    /// 지정한 위치로 걷도록 요청합니다.
    /// 이미 걷는 중이면 상태를 재진입하지 않고 목적지만 변경합니다.
    /// </summary>
    /// <param name="worldPos">새 걷기 목적지입니다.</param>
    public void RequestWalk(Vector3 worldPos)
    {
        _walkState.SetDestination(worldPos);

        if (ReferenceEquals(_currentState, _walkState))
        {
            _movement.MoveTo(worldPos);
            return;
        }

        ChangeState(_walkState);
    }

    /// <summary>
    /// 지정한 위치로 달리도록 요청합니다.
    /// 이미 달리는 중이면 상태를 재진입하지 않고 목적지만 변경합니다.
    /// </summary>
    /// <param name="worldPos">새 달리기 목적지입니다.</param>
    public void RequestRun(Vector3 worldPos)
    {
        _runState.SetDestination(worldPos);

        if (ReferenceEquals(_currentState, _runState))
        {
            _movement.MoveTo(worldPos);
            return;
        }

        ChangeState(_runState);
    }

    /// <summary>
    /// 현재 상태를 종료하고 다음 상태에 진입한 뒤
    /// 상태 변경 이벤트를 발생시킵니다.
    /// </summary>
    /// <param name="nextState">전환할 상태 인스턴스입니다.</param>
    private void ChangeState(INpcState nextState)
    {
        if (nextState == null ||
            ReferenceEquals(_currentState, nextState))
        {
            return;
        }

        _currentState?.Exit(_context);

        _currentState = nextState;
        _currentState.Enter(_context);

        if (_animator != null)
        {
            _animator.SetBool(IsMovingHash, _currentState.Id != NpcStateId.Idle);
        }

        StateChanged?.Invoke(_currentState.Id);
    }
}
