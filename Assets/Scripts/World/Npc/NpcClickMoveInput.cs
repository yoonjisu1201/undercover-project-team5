using UnityEngine;
using UnityEngine.InputSystem;

namespace Undercover.World
{
    public sealed class NpcClickMoveInput : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private NpcStateMachine _machine;
        [SerializeField] private LayerMask _groundMask = ~0;
        [SerializeField, Min(0f)] private float _maxDistance = 500f;

        private void Update()
        {
        }
    }
}
