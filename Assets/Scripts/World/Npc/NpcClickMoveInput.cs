using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 마우스로 클릭한 바닥 위치를 NPC의 걷기 목적지로 전달합니다.
/// </summary>
public sealed class NpcClickMoveInput : MonoBehaviour
{
        [SerializeField] private Camera _camera;
        [SerializeField] private NpcStateMachine _machine;
        [SerializeField] private LayerMask _groundMask = ~0;
        [SerializeField, Min(0f)] private float _maxDistance = 500f;

        private void Update()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
                return;

            if (_camera == null || _machine == null)
                return;

            Vector2 screenPosition = Mouse.current.position.ReadValue();
            Ray ray = _camera.ScreenPointToRay(screenPosition);

            if (Physics.Raycast(ray, out RaycastHit hit, _maxDistance, _groundMask))
            {
                _machine.RequestMove(hit.point);
            }
        }
}
