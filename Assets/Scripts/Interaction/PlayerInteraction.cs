using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerInteraction : NetworkBehaviour
{
    // 인스펙터에서 연결하는 참조와 상호작용 범위를 조절하는 값.
    [Header("상호작용 설정")]
    [SerializeField] private Camera _playerCamera;
    [Range(0.01f, 0.5f)]
    [SerializeField] private float _screenCenterRadius = 0.2f; // 화면 중심에서 상호작용 가능한 영역의 반지름
    [SerializeField, Min(0f)] private float _dropInteractionDelay = 1.5f;   // 드롭 후 상호작용 차단 시간
    [SerializeField] private ItemCatalog _itemCatalog;
    [SerializeField] private InventoryUI _inventoryUI;
    [SerializeField] private string _clueItemIdPrefix = "Clue";

    // 상호작용 범위 안의 후보 목록과, 그중 현재 조준된 대상.
    private readonly HashSet<InteractableBase> _nearbyInteractables = new();  // SphereCollider 안에 있는 상호작용 가능 오브젝트
    private InteractableBase _currentTarget;  // 현재 상호작용 가능한 대상

    private CustomInputActions _actions;
    private PlayerInventory _inventory;

    private void Awake()
    {
        _actions = new CustomInputActions();
        _inventory = GetComponent<PlayerInventory>();
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

    public override void OnNetworkSpawn()
    {
        // 입력과 UI는 이 플레이어를 조작하는 클라이언트에서만 초기화한다.
        if (_itemCatalog == null)
        {
            _itemCatalog = FindFirstObjectByType<ItemCatalog>();
        }

        if (!IsOwner)
        {
            _actions.Disable();
            return;
        }

        if (_inventoryUI == null)
        {
            _inventoryUI = FindFirstObjectByType<InventoryUI>();
        }

        _inventoryUI?.BindInventory(_inventory);
        if (_inventory != null)
        {
            _inventory.ItemAdded += HandleItemAdded;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_inventory != null)
        {
            _inventory.ItemAdded -= HandleItemAdded;
        }
    }

    // 상호작용 범위에 들어온 대상을 후보 목록에 추가한다.
    private void OnTriggerEnter(Collider other) // SphereCollider에 들어온 아이템을 nearbyInteractables에 추가
    {
        if (!IsOwner)
        {
            return;
        }

        if (IsWanderAreaCollider(other))
        {
            return; // NPC 배회 반경 콜라이더는 상호작용 판정 대상이 아니다
        }

        InteractableBase newTarget = other.GetComponentInParent<InteractableBase>();
        if (newTarget != null)
        {
            _nearbyInteractables.Add(newTarget);
        }
    }

    // 범위를 벗어난 대상을 제거하고, 선택 중이었다면 선택도 해제한다.
    private void OnTriggerExit(Collider other)  // SphereCollider에서 나간 상호작용 대상을 nearbyInteractables에서 제거
    {
        if (!IsOwner)
        {
            return;
        }

        if (IsWanderAreaCollider(other))
        {
            return;
        }

        InteractableBase outTarget = other.GetComponentInParent<InteractableBase>();
        if (outTarget == null)
        {
            return;
        }

        _nearbyInteractables.Remove(outTarget);
        if (ReferenceEquals(outTarget, _currentTarget))
        {
            SetCurrentTarget(null);
        }
    }

    // NPC의 배회 반경 콜라이더인지 확인한다. 같은 오브젝트에 다른 콜라이더(몸체 등)가 있을 수 있으므로 참조까지 비교한다.
    private static bool IsWanderAreaCollider(Collider other)
    {
        return other.TryGetComponent(out NpcRandomWander wander) && wander.WanderAreaCollider == other;
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        if (GameplayUiMode.IsActive)
        {
            SetCurrentTarget(null);
            return;
        }

        UpdateCurrentTarget();

        // 조준 대상을 갱신한 뒤 상호작용과 드롭 입력을 처리한다.
        if (_actions.Player.Interact.WasPressedThisFrame()) // 상호작용 버튼이 눌렸을 때
        {
            HandleInteractInput();
        }
        if (_actions.Player.Drop.WasPressedThisFrame()) // 드롭 버튼이 눌렸을 때
        {
            TryDropSelectedItem();
        }
    }

    private void HandleInteractInput()
    {
        // 조준 중인 대상이 있으면 단서 UI보다 필드 상호작용을 우선한다.
        if (_currentTarget != null)
        {
            TryInteract();
            return;
        }

        if (TryCloseVisibleClue())
        {
            return;
        }

        TryShowSelectedClue();
    }

    private static bool TryCloseVisibleClue()
    {
        ClueUI[] clueDisplays = FindObjectsByType<ClueUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (ClueUI clueDisplay in clueDisplays)
        {
            if (!clueDisplay.gameObject.activeInHierarchy)
            {
                continue;
            }

            clueDisplay.Close();
            return true;
        }

        return false;
    }

    private void HandleItemAdded(string itemId, int _)
    {
        if (TryGetClueIndex(itemId, out int clueIndex))
        {
            ShowClue(clueIndex);
        }
    }

    private void TryShowSelectedClue()
    {
        if (_inventory == null || !_inventory.TryGetSelectedItem(out string itemId))
        {
            return;
        }

        if (TryGetClueIndex(itemId, out int clueIndex))
        {
            ShowClue(clueIndex);
        }
    }

    private bool TryGetClueIndex(string itemId, out int clueIndex)
    {
        clueIndex = -1;

        if (string.IsNullOrWhiteSpace(itemId) || !itemId.StartsWith(_clueItemIdPrefix))
        {
            return false;
        }

        string numberText = itemId[_clueItemIdPrefix.Length..].Trim();
        return int.TryParse(numberText, out int clueNumber) &&
               (clueIndex = clueNumber - 1) >= 0;
    }

    private void ShowClue(int clueIndex)
    {
        ClueUI[] clueDisplays = FindObjectsByType<ClueUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        System.Array.Sort(clueDisplays, (left, right) => string.CompareOrdinal(left.name, right.name));

        if (clueIndex >= clueDisplays.Length)
        {
            Debug.LogWarning($"표시할 ClueDisplay가 부족합니다. 단서 번호: {clueIndex + 1}");
            return;
        }

        clueDisplays[clueIndex].gameObject.SetActive(true);
    }

    private void TryInteract()
    {
        if (_currentTarget == null)
        {
            return;
        }

        // 선택을 먼저 해제해 아웃라인과 안내 문구를 즉시 갱신한다.
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

        // 로컬에서 드롭 가능 여부를 확인하고, 실제 생성과 인벤토리 차감은 서버에 요청한다.
        //--- 선택한 슬롯에 아이템이 있는지 확인 ---//
        if (!_inventory.TryGetSelectedItem(out string itemId)) { Debug.Log("선택한 슬롯에 아이템이 없습니다."); return; }
        if (!_itemCatalog.TryGet(itemId, out ItemData itemData)) { Debug.Log($"ItemCatalog에 '{itemId}'가 없습니다."); return; }
        if (itemData.WorldPrefab == null) { Debug.Log($"'{itemId}'의 WorldPrefab이 없습니다."); return; }

        Transform cameraTransform = _playerCamera.transform;
        Vector3 dropPosition = cameraTransform.position + cameraTransform.forward * 1f;
        Vector3 dropVelocity = cameraTransform.forward * 2f + Vector3.up;

        RequestDropRpc(itemId, dropPosition, dropVelocity);
        TryCloseVisibleClue();
    }

    [Rpc(SendTo.Server)]
    private void RequestDropRpc(string itemId, Vector3 dropPosition, Vector3 dropVelocity)
    {
        // 클라이언트 요청을 신뢰하지 않고 서버에서도 다시 검증한다.
        if (_inventory == null || _itemCatalog == null)
        {
            return;
        }

        if (!_itemCatalog.TryGet(itemId, out ItemData itemData) || itemData.WorldPrefab == null)
        {
            return;
        }

        if (!_inventory.TryRemoveSelectedItemOnServer(itemId))
        {
            return;
        }

        // 서버가 월드 아이템을 생성하고 네트워크 오브젝트로 스폰한다.
        GameObject droppedObject = Instantiate(itemData.WorldPrefab, dropPosition, Quaternion.identity);

        //--- 드롭한 단서가 기존 단서 번호를 유지하도록 데이터 전달 ---//
        if (droppedObject.TryGetComponent(out PickupItem droppedPickupItem))
        {
            droppedPickupItem.Configure(itemData);
        }

        if (!droppedObject.TryGetComponent(out NetworkObject droppedNetworkObject))
        {
            Debug.LogError($"'{itemData.WorldPrefab.name}' 프리팹에 NetworkObject가 없습니다.");
            Destroy(droppedObject);
            return;
        }

        // 방금 버린 아이템을 바로 다시 줍지 못하도록 잠시 막는다.
        if (droppedObject.TryGetComponent(out PickupItem droppedItem))
        {
            droppedItem.BlockInteraction(_dropInteractionDelay);
        }

        droppedNetworkObject.Spawn();

        // 플레이어가 바라보는 방향으로 초기 속도를 적용한다.
        if (droppedObject.TryGetComponent(out Rigidbody rigidbody))
        {
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.linearVelocity = dropVelocity;
        }
    }

    private void UpdateCurrentTarget()
    {
        if (_playerCamera == null)
        {
            SetCurrentTarget(null);
            return;
        }

        _nearbyInteractables.RemoveWhere(target => target == null); // 파괴된 대상 제거
        // 파괴된 대상을 정리한 뒤, 가장 가까운 후보를 찾는다.

        InteractableBase closestTarget = null;
        float closestDistanceSqr = float.MaxValue;

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        // 화면 중앙과의 거리를 기준으로 조준 대상을 비교한다.

        foreach (InteractableBase target in _nearbyInteractables)
        {
            if (target == null)
            {
                continue;
            }

            if (!target.CanInteract(gameObject))  // 상호작용이 차단된 대상은 무시
            {
                continue;
            }

            Vector3 screenPos = _playerCamera.WorldToScreenPoint(target.transform.position);

            if (screenPos.z < 0)
            {
                continue; // 대상이 카메라 뒤에 있는 경우 무시
            }

            Vector2 targetScreenPos = new Vector2(screenPos.x, screenPos.y);

            // 화면 중심과 대상의 스크린 좌표 간의 거리 제곱 계산
            // 제곱 거리를 사용해 불필요한 제곱근 계산을 피한다.
            float distanceSqr = (targetScreenPos - screenCenter).sqrMagnitude;

            // 대상별 배율(AimRadiusMultiplier)을 반영해 판정 반경을 계산한다 (예: 계속 움직이는 NPC는 더 넓게).
            float radiusPixels = Screen.height * _screenCenterRadius * target.AimRadiusMultiplier;
            float radiusSqr = radiusPixels * radiusPixels;

            if (distanceSqr > radiusSqr)
            {
                continue; // 아이템이 상호작용 가능한 영역 밖에 있는 경우 무시
            }

            if (distanceSqr < closestDistanceSqr)
            {
                closestDistanceSqr = distanceSqr;
                closestTarget = target;
            }
        }
        SetCurrentTarget(closestTarget);
    }

    private void SetCurrentTarget(InteractableBase nextTarget)
    {
        if (ReferenceEquals(_currentTarget, nextTarget))
        {
            return;
        }

        // 이전에 선택된 대상의 아웃라인을 끈다.
        _currentTarget?.SetOutline(false);

        _currentTarget = nextTarget;
        _currentTarget?.SetOutline(true);

        string interactionText = _currentTarget?.InteractionText;
        _inventoryUI?.SetInteractionPrompt(interactionText);
    }
}
