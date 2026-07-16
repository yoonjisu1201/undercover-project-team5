using System;
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

    // ==================== [추가] ====================
    // NPC가 Idle 상태에서 기다릴 시간입니다.
    [Header("Wander Settings")]
    [SerializeField, Min(0f)] private float _idleDurationSeconds = 1f;

    // Spawner가 전달한 Scene Checkpoint 목록입니다.
    private Transform[] _checkpoints = Array.Empty<Transform>();

    // 현재 Idle 상태에서 누적된 시간입니다.
    private float _idleElapsedSeconds;
    // ================================================

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
        _currentState?.Execute(_context);

        // ==================== [추가] ====================
        // 현재 상태가 Idle이면 배회 대기 시간을 계산합니다.
        UpdateWander();
        // ================================================
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

        // ==================== [추가] ====================
        // 배회에 사용한 런타임 데이터를 정리합니다.
        _checkpoints = new Transform[0];
        _idleElapsedSeconds = 0f;
        // ================================================

        StateChanged = null;
    }

    // ==================== [추가] ====================

    /// <summary>
    /// NPC가 배회할 Checkpoint 목록을 설정합니다.
    /// Spawner가 NPC를 생성한 직후 호출합니다.
    /// </summary>
    /// <param name="checkpoints">
    /// 목적지로 사용할 Scene Transform 목록입니다.
    /// </param>
    public void Configure(Transform[] checkpoints)
    {
        _checkpoints = checkpoints ?? new Transform[0];
    }

    // ================================================

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

    // ==================== [추가] ====================

    /// <summary>
    /// Idle 상태에서 시간을 누적하고 대기가 끝나면
    /// 무작위 Checkpoint로 Walk를 요청합니다.
    /// </summary>
    private void UpdateWander()
    {
        //_currentState와 _idleState가 동일한 객체가 아니라면 if 내부를 실행
        if (!ReferenceEquals(_currentState, _idleState))
        {
            _idleElapsedSeconds = 0f;
            return;
        }

        _idleElapsedSeconds += Time.deltaTime;

        if (_idleElapsedSeconds < _idleDurationSeconds)
        {
            return;
        }

        _idleElapsedSeconds = 0f;

        if (!TryGetRandomDestination(out Vector3 destination))
        {
            return;
        }

        RequestWalk(destination);
    }

    /// <summary>
    /// Checkpoint 목록 중 하나의 월드 위치를 무작위로 가져옵니다.
    /// </summary>
    private bool TryGetRandomDestination(out Vector3 destination)
    {
        destination = default;

        if (_checkpoints.Length == 0)
        {
            return false;
        }

        int checkpointIndex = UnityEngine.Random.Range(
            0,
            _checkpoints.Length);

        Transform checkpoint = _checkpoints[checkpointIndex];

        if (checkpoint == null)
        {
            return false;
        }

        destination = checkpoint.position;
        return true;
    }

    // ================================================
}
