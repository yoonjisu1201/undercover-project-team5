using UnityEngine;
using UnityEngine.AI;

namespace Undercover.World
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class NpcMovement : MonoBehaviour
    {
        [SerializeField] private NavMeshAgent _agent;
        [SerializeField, Min(0f)] private float _arrivalThreshold = 0.1f;
        [SerializeField, Min(0f)] private float _walkSpeed = 2f;
        [SerializeField, Min(0f)] private float _runSpeed = 4f;

        public NpcLocomotionMode CurrentMode { get; private set; } = NpcLocomotionMode.Stand;

        public bool HasArrived
        {
            get
            {
                return false;
            }
        }

        private void Reset()
        {
            TryGetComponent(out _agent);
        }

        private void Awake()
        {
        }

        public void MoveTo(Vector3 worldPos)
        {
        }

        public void SetLocomotionMode(NpcLocomotionMode mode)
        {
        }

        public void Stop()
        {
        }
    }
}
