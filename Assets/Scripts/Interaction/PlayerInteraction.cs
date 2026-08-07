using Unity.Netcode;
using UnityEngine;

// 플레이어 상호작용의 코어: 참조/라이프사이클과 프레임별 입력 분배를 담당한다.
// 세부 로직은 partial로 분리되어 있다.
//   - PlayerInteraction.Targeting.cs            : 화면 중심 조준으로 대상 감지·선택
//   - PlayerInteraction.ItemUI.cs               : 아이템 획득 시 단서·가이드 북 UI 여닫기
//   - PlayerInteraction.Drop.cs                 : 선택한 아이템 드롭
[RequireComponent(typeof(PlayerInventory), typeof(PlayerHealth))]
public partial class PlayerInteraction : NetworkBehaviour
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
    [SerializeField] private string _guideBookItemId = "GuideBook";

    private InteractableBase _currentTarget;  // 현재 상호작용 가능한 대상

    private CustomInputActions _actions;
    private PlayerInventory _inventory;
    private PlayerHealth _health;
    private IUsableItem _usableItem;

    public CartBase CarryingCart { get; set; } // 플레이어가 끌고 있는 카트. null이면 카트를 끌고 있지 않다.

    private void Awake()
    {
        _actions = new CustomInputActions();
        _inventory = GetComponent<PlayerInventory>();
        _health = GetComponent<PlayerHealth>();
        _usableItem = GetComponent<IUsableItem>();

        if (_inventory == null)
        {
            Debug.LogError("[PlayerInteraction] PlayerInventory가 없어 상호작용 인벤토리 처리를 할 수 없습니다.", this);
        }

        if (_health == null)
        {
            Debug.LogError("[PlayerInteraction] PlayerHealth가 없어 다운 상태를 확인할 수 없습니다.", this);
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
            _inventoryUI = FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
        }

        _inventoryUI?.BindInventory(_inventory);
        if (_usableItem is UsableItem usableItem)
        {
            usableItem.BindInventoryUI(_inventoryUI);
        }

        if (_inventory != null)
        {
            _inventory.ItemAdded += HandleItemAdded;
            _inventory.InventoryChanged += HandleInventoryChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_inventory != null)
        {
            _inventory.ItemAdded -= HandleItemAdded;
            _inventory.InventoryChanged -= HandleInventoryChanged;
        }
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        // 쓰러지면 상호작용 불가능하게 + 혹시라도 카트와 상호작용중이었다면 카트 놓도록
        if (_health.IsDowned)
        {
            SetCurrentTarget(null);
            return;
        }

        if (CarryingCart != null)
        {
            UpdateCartInteraction();
            return;
        }

        if (GameplayUiMode.IsActive)
        {
            SetCurrentTarget(null);

            if (_actions.Player.Interact.WasPressedThisFrame())
            {
                // 가이드 북이 열려 있으면 먼저 닫고, 아니면 단서 UI를 닫는다.
                if (!TryCloseGuideBook())
                {
                    TryCloseVisibleClue();
                }
            }

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

        // 선택한 사용 아이템이 E 입력을 처리했다면 단서 UI를 열지 않는다.
        if (_usableItem != null && _usableItem.TryHandleSelectedItemUse(out string message))
        {
            _inventoryUI?.ShowTemporaryPrompt(message);
            return;
        }

        TryShowSelectedClue();
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
}
