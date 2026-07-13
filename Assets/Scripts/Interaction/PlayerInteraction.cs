using System.Collections.Generic;
using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    [Header("상호작용 설정")]
    [SerializeField] private Camera _playerCamera;
    [Range(0.01f, 0.5f)]
    [SerializeField] private float _screenCenterRadius = 0.2f; // 화면 중심에서 상호작용 가능한 영역의 반지름
    [SerializeField, Min(0f)] private float _dropInteractionDelay = 1.5f;   // 드롭 후 상호작용 차단 시간
    [SerializeField] private ItemCatalog _itemCatalog;
    [SerializeField] private InventoryUI _inventoryUI;

    private readonly HashSet<PickupItem> _nearbyItems = new();  // SphereCollider 안에 있는 아이템
    private IInteractable _currentTarget;  // 현재 상호작용 가능한 대상

    private CustomInputActions _actions;
    private PlayerInventory _inventory;

    private void Awake()
    {
        _actions = new CustomInputActions();
        _inventory = GetComponent<PlayerInventory>();

        if (_inventoryUI == null)
        {
            _inventoryUI = FindFirstObjectByType<InventoryUI>();
        }
    }

    private void OnEnable()
    {
        _actions ??= new CustomInputActions();
        _actions.Enable();
    }

    private void OnDisable()
    {
        SetCurrentTarget(null);
        _actions?.Disable();
    }

    private void OnTriggerEnter(Collider other) // SphereCollider에 들어온 아이템을 nearbyItems에 추가
    {
        PickupItem pickupItem = other.GetComponentInParent<PickupItem>();
        if (pickupItem != null)
        {
            _nearbyItems.Add(pickupItem);
        }
    }

    private void OnTriggerExit(Collider other)  // SphereCollider에서 나간 아이템을 nearbyItems에서 제거
    {
        PickupItem pickupItem = other.GetComponentInParent<PickupItem>();
        if (pickupItem == null)
        {
            return;
        }

        _nearbyItems.Remove(pickupItem);
        if (ReferenceEquals(pickupItem, _currentTarget))
        {
            SetCurrentTarget(null);
        }
    }

    private void Update()
    {
        UpdateCurrentTarget();

        if (_actions.Player.Interact.WasPressedThisFrame()) // 상호작용 버튼이 눌렸을 때
        {
            TryInteract();
        }
        if (_actions.Player.Drop.WasPressedThisFrame()) // 드롭 버튼이 눌렸을 때
        {
            TryDropSelectedItem();
        }
    }

    private void TryInteract()
    {
        if (_currentTarget == null)
        {
            return;
        }

        IInteractable target = _currentTarget;
        SetCurrentTarget(null);
        target.Interact(gameObject);
    }

    private void TryDropSelectedItem()
    {
        if (_inventory == null || _itemCatalog == null || _playerCamera == null)
        {
            return;
        }

        //--- 선택한 슬롯에 아이템이 있는지 확인 ---//
        if (!_inventory.TryGetSelectedItem(out string itemId)) { Debug.Log("선택한 슬롯에 아이템이 없습니다."); return; }
        if (!_itemCatalog.TryGet(itemId, out ItemData itemData)) { Debug.Log($"ItemCatalog에 '{itemId}'가 없습니다."); return; }
        if (itemData.WorldPrefab == null) { Debug.Log($"'{itemId}'의 WorldPrefab이 없습니다."); return; }

        // --- 아이템 드롭 처리 --- ///

        // 아이템을 드롭할 위치를 계산합니다.
        Transform cameraTransform = _playerCamera.transform;

        // 카메라 앞쪽으로 1m 떨어진 위치에 아이템을 생성합니다.
        Vector3 dropPosition = cameraTransform.position + cameraTransform.forward * 1f;

        // 아이템을 생성합니다.
        GameObject droppedObject = Instantiate(itemData.WorldPrefab, dropPosition, Quaternion.identity);

        if (droppedObject.TryGetComponent(out PickupItem droppedItem))  // 드롭 한 후 바로 상호작용 차단
        {
            droppedItem.BlockInteraction(_dropInteractionDelay);
        }

        if (droppedObject.TryGetComponent(out Rigidbody rigidbody))
        {
            // 아이템이 카메라 앞쪽에서 위 방향으로 튀어오르게 하는 힘
            Vector3 velocity = cameraTransform.forward * 2f + Vector3.up;

            // 얇은 아이템이 바닥을 관통하지 않도록 연속 충돌 검사를 사용합니다.
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.linearVelocity = velocity;
        }

        _inventory.RemoveSelectedItem();
    }

    private void UpdateCurrentTarget()
    {
        if (_playerCamera == null)
        {
            SetCurrentTarget(null);
            return;
        }

        _nearbyItems.RemoveWhere(item => item == null); // null인 아이템 제거

        PickupItem closestItem = null;
        float closestDistanceSqr = float.MaxValue;

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        float radiusPixels = Screen.height * _screenCenterRadius;
        float radiusSqr = radiusPixels * radiusPixels;

        foreach (PickupItem item in _nearbyItems)
        {
            if (!item.CanInteract)  // 상호작용이 차단된 아이템은 무시
            {
                continue;
            }

            Vector3 screenPos = _playerCamera.WorldToScreenPoint(item.transform.position);

            if (screenPos.z < 0)
            {
                continue; // 아이템이 카메라 뒤에 있는 경우 무시
            }

            Vector2 itemScreenPos = new Vector2(screenPos.x, screenPos.y);

            // 화면 중심과 아이템의 스크린 좌표 간의 거리 제곱 계산
            float distanceSqr = (itemScreenPos - screenCenter).sqrMagnitude;

            if (distanceSqr > radiusSqr)
            {
                continue; // 아이템이 상호작용 가능한 영역 밖에 있는 경우 무시
            }

            if (distanceSqr < closestDistanceSqr)
            {
                closestDistanceSqr = distanceSqr;
                closestItem = item;
            }
        }
        SetCurrentTarget(closestItem);
    }

    private void SetCurrentTarget(IInteractable nextTarget)
    {
        if (ReferenceEquals(_currentTarget, nextTarget))
        {
            return;
        }

        if (_currentTarget is PickupItem previousPickupItem)
        {
            previousPickupItem.SetOutline(false);
        }

        _currentTarget = nextTarget;

        if (_currentTarget is PickupItem currentPickupItem)
        {
            currentPickupItem.SetOutline(true);
        }

        string interactionText = _currentTarget?.InteractionText;
        _inventoryUI?.SetInteractionPrompt(interactionText);
    }
}
