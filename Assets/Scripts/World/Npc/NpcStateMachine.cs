using System;
using UnityEngine;

/// <summary>
/// NPC 상태의 생성과 수명 주기를 관리하고 상태 전환을 중재합니다.
/// </summary>
[RequireComponent(typeof(NpcMovement))]
public class NpcStateMachine : MonoBehaviour
{
        [SerializeField] private NpcMovement _movement;
        [SerializeField] private Vector3 _initialDestination = new Vector3(3f, 0f, 3f);

        private NpcContext _context;
        private INpcState _currentState;
        private IdleState _idleState;
        [SerializeField] private WalkState _walkState = new WalkState();
        [SerializeField] private RunState _runState = new RunState();

        /// <summary>상태가 변경된 뒤 새 상태의 식별자를 전달합니다.</summary>
        public event Action<NpcStateId> StateChanged;

        /// <summary>현재 상태의 식별자를 가져옵니다.</summary>
        public NpcStateId CurrentId => _currentState != null ? _currentState.Id : NpcStateId.Idle;

        private void Reset()
        {
            TryGetComponent(out _movement);
        }

        private void Awake()
        {
            if (_movement == null)
            {
                _movement = GetComponent<NpcMovement>();
            }

            _context = new NpcContext
            {
                StateMachine = this,
                Movement = _movement,
                LifecycleSource = this,
            };

            _idleState = new IdleState();
            _walkState ??= new WalkState();
            _runState ??= new RunState();

            _walkState.Configure(_idleState);
            _runState.Configure(_idleState);
        }

        private void Start()
        {
            ChangeState(_idleState);
            RequestWalk(_initialDestination);
        }

        private void Update()
        {
            _currentState?.Execute(_context);
        }

        private void OnDestroy()
        {
            _currentState?.Exit(_context);
            _currentState = null;
            _context = null;
            _idleState = null;
            _walkState = null;
            _runState = null;
            StateChanged = null;
        }

        /// <summary>
        /// 지정한 위치로 걷기 이동을 요청합니다.
        /// </summary>
        /// <param name="worldPos">이동할 월드 좌표입니다.</param>
        public void RequestMove(Vector3 worldPos)
        {
            RequestWalk(worldPos);
        }

        /// <summary>
        /// 걷기 목적지를 저장하고 Idle 상태에 Walk 전환을 요청합니다.
        /// </summary>
        /// <param name="worldPos">이동할 월드 좌표입니다.</param>
        public void RequestWalk(Vector3 worldPos)
        {
            _walkState.SetDestination(worldPos);
            _idleState.RequestTransition(_walkState);
        }

        /// <summary>
        /// 달리기 목적지를 저장하고 Idle 상태에 Run 전환을 요청합니다.
        /// </summary>
        /// <param name="worldPos">이동할 월드 좌표입니다.</param>
        public void RequestRun(Vector3 worldPos)
        {
            _runState.SetDestination(worldPos);
            _idleState.RequestTransition(_runState);
        }

        /// <summary>
        /// 현재 상태를 종료하고 지정한 상태로 전환한 뒤 변경 이벤트를 발생시킵니다.
        /// </summary>
        /// <param name="next">새로 진입할 상태입니다.</param>
        public void ChangeState(INpcState next)
        {
            if (next == null || ReferenceEquals(_currentState, next))
                return;

            _currentState?.Exit(_context);
            _currentState = next;
            _currentState.Enter(_context);
            StateChanged?.Invoke(_currentState.Id);
        }
}
