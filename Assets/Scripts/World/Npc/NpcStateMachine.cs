using System;
using UnityEngine;

namespace Undercover.World
{
    [RequireComponent(typeof(NpcMovement))]
    public class NpcStateMachine : MonoBehaviour
    {
        [SerializeField] private NpcMovement _movement;
        [SerializeField] private Vector3 _initialDestination = new Vector3(3f, 0f, 3f);
        [SerializeField] private NpcLocomotionMode _initialMoveMode = NpcLocomotionMode.Walk;

        private NpcContext _context;
        private INpcState _currentState;
        private IdleState _idleState;
        private WanderState _wanderState;
        private LookAroundState _lookAroundState;

        public event Action<NpcStateId> StateChanged;

        public NpcStateId CurrentId => _currentState != null ? _currentState.Id : NpcStateId.Idle;

        private void Reset()
        {
            TryGetComponent(out _movement);
        }

        private void Awake()
        {
        }

        private void Start()
        {
        }

        private void Update()
        {
        }

        private void OnDestroy()
        {
        }

        public void RequestMove(Vector3 worldPos)
        {
        }

        public void RequestMove(Vector3 worldPos, NpcLocomotionMode moveMode)
        {
        }

        public void TransitionTo(INpcState next)
        {
        }
    }
}
