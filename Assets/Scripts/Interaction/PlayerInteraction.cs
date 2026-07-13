using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    [Header("상호작용 설정")]
    [SerializeField] private Camera _playerCamera;
    [SerializeField] private float _interactionDistance = 2f;   // 상호작용 가능한 최대 거리
    [SerializeField] private ItemCatalog _itemCatalog;

    private CustomInputActions _actions;
    private PlayerInventory _inventory;

    private void Awake()
    {
        _actions = new CustomInputActions();
        _inventory = GetComponent<PlayerInventory>();
    }

    private void OnEnable()
    {
        _actions.Enable();
    }

    private void OnDisable()
    {
        _actions.Disable();
    }

    private void Update()
    {
        if (_actions.Player.Interact.WasPressedThisFrame())
        {
            TryInteract();
        }

        if (_actions.Player.Drop.WasPressedThisFrame())
        {
            TryDropSelectedItem();
        }
    }
    private void TryDropSelectedItem()
    {
        if (_inventory == null ||
            _itemCatalog == null ||
            _playerCamera == null)
        {
            return;
        }

        if (!_inventory.TryGetSelectedItem(
                out string itemId))
        {
            Debug.Log(
                "선택한 슬롯에 아이템이 없습니다."
            );

            return;
        }

        if (!_itemCatalog.TryGet(
                itemId,
                out ItemData itemData))
        {
            Debug.LogWarning(
                $"ItemCatalog에 '{itemId}'가 없습니다."
            );

            return;
        }

        if (itemData.WorldPrefab == null)
        {
            Debug.LogWarning(
                $"'{itemId}'의 WorldPrefab이 없습니다."
            );

            return;
        }

        // --- 아이템 드롭 처리 --- ///

        // 아이템을 드롭할 위치를 계산합니다.
        Transform cameraTransform = _playerCamera.transform;

        // 카메라 앞쪽으로 1m 떨어진 위치에 아이템을 생성합니다.
        Vector3 dropPosition = cameraTransform.position + cameraTransform.forward * 1f;

        // 아이템을 생성합니다.
        GameObject droppedObject = Instantiate(itemData.WorldPrefab, dropPosition, Quaternion.identity);

        if (droppedObject.TryGetComponent(out Rigidbody rigidbody))
        {
            // 아이템이 카메라 앞쪽에서 위 방향으로 튀어오르게 하는 힘
            Vector3 velocity = cameraTransform.forward * 2f + Vector3.up;

            // Rigidbody에 힘을 가하여 아이템을 던집니다.
            rigidbody.AddForce(velocity, ForceMode.VelocityChange);
        }

        _inventory.RemoveSelectedItem();
    }

    private void TryInteract()
    {
        Ray ray = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, _interactionDistance))
        {
            return;
        }

        IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();

        if (interactable == null)
        {
            return;
        }

        interactable.Interact(gameObject);
    }
}
