using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    [SerializeField] private PlayerInventory _inventory;
    [SerializeField] private Image[] _itemIcons;
    [SerializeField] private RectTransform _selectionOutline;
    [SerializeField] private ItemCatalog _itemCatalog;
    [SerializeField] private TMP_Text _interactionPromptText;
    [SerializeField, Min(0f)] private float _selectedItemPromptDuration = 1f;
    private GameObject[] _slots;    // 인벤토리 슬롯 UI 오브젝트 배열

    private CustomInputActions _actions;
    private Coroutine _selectedItemPromptRoutine;
    private string _interactionText;

    private void Awake()
    {
        _actions = new CustomInputActions();
        _slots = new GameObject[4];
        _itemIcons = new Image[4];

        if (_interactionPromptText == null)
        {
            _interactionPromptText = transform.Find("InteractionPrompt")?.GetComponent<TMP_Text>();
        }

        if (_itemCatalog == null)
        {
            _itemCatalog = FindFirstObjectByType<ItemCatalog>();
        }

        SetInteractionPrompt(null);

        for (int i = 0; i < _slots.Length; i++)
        {
            Transform slot = transform.Find($"InventoryPanel/Slot{i + 1}");
            _slots[i] = slot != null ? slot.gameObject : null;

            if (_slots[i] != null)
            {
                _slots[i].SetActive(true);
                _itemIcons[i] = _slots[i].transform
                    .Find("ItemIcon")?.GetComponent<Image>();
            }
        }
    }

    private void OnEnable()
    {
        _actions ??= new CustomInputActions();
        _actions.Enable();

        if (_inventory != null)
        {
            _inventory.InventoryChanged += Refresh;
        }

        Refresh();
    }

    private void OnDisable()
    {
        _actions?.Disable();

        if (_selectedItemPromptRoutine != null)
        {
            StopCoroutine(_selectedItemPromptRoutine);
            _selectedItemPromptRoutine = null;
        }

        if (_inventory != null)
        {
            _inventory.InventoryChanged -= Refresh;
        }
    }

    private void Update()
    {
        if (_inventory == null)
        {
            return;
        }

        if (_actions.Player.Slot1.WasPressedThisFrame())
            SelectSlotAndShowItem(0);

        if (_actions.Player.Slot2.WasPressedThisFrame())
            SelectSlotAndShowItem(1);

        if (_actions.Player.Slot3.WasPressedThisFrame())
            SelectSlotAndShowItem(2);

        if (_actions.Player.Slot4.WasPressedThisFrame())
            SelectSlotAndShowItem(3);

        float scrollY = _actions.Player.InventoryScroll.ReadValue<Vector2>().y;
        if (scrollY != 0f)
            SelectSlotByScroll(scrollY);
    }

    private void SelectSlotByScroll(float scrollY)
    {
        int direction = scrollY > 0f ? -1 : 1;
        int currentIndex = _inventory.SelectedIndex;
        int nextIndex = currentIndex < 0
            ? (direction > 0 ? 0 : _slots.Length - 1)
            : (currentIndex + direction + _slots.Length) % _slots.Length;

        SelectSlotAndShowItem(nextIndex);
    }

    public void BindInventory(PlayerInventory inventory)    // 인벤토리 UI에 플레이어 인벤토리 연결
    {
        if (_inventory == inventory)
        {
            return;
        }

        if (_inventory != null)
        {
            _inventory.InventoryChanged -= Refresh;
        }

        _inventory = inventory;

        if (_inventory != null && isActiveAndEnabled)
        {
            _inventory.InventoryChanged += Refresh;
        }

        Refresh();
    }

    // 상호작용 프롬프트 텍스트 설정. showKeyHint가 false면 " : [E]" 힌트 없이 문구만 보여준다
    // (예: 눌러도 아무 동작이 없는 안내성 문구).
    public void SetInteractionPrompt(string interactionText, bool showKeyHint = true)
    {
        _interactionText = interactionText;

        // 월드 아이템이나 플레이어 등 실제 상호작용 안내가 선택 아이템 이름보다 우선한다.
        if (!string.IsNullOrWhiteSpace(interactionText))
        {
            if (_selectedItemPromptRoutine != null)
            {
                StopCoroutine(_selectedItemPromptRoutine);
                _selectedItemPromptRoutine = null;
            }

            ApplyInteractionPrompt(interactionText, showKeyHint);
            return;
        }

        // 상호작용 대상이 사라져도 진행 중인 선택 아이템 안내는 남은 시간 동안 유지한다.
        if (_selectedItemPromptRoutine != null)
        {
            return;
        }

        ApplyInteractionPrompt(interactionText);
    }

    // 슬롯 선택 시 현재 손에 든 아이템 이름을 상호작용 안내 위치에 잠시 표시한다.
    private void SelectSlotAndShowItem(int index)
    {
        _inventory.SelectSlot(index);

        if (index < 0 || index >= _inventory.Slots.Length)
        {
            return;
        }

        InventorySlot slot = _inventory.Slots[index];
        if (slot == null || slot.IsEmpty)
        {
            return;
        }

        string displayName = slot.ItemId;
        if (_itemCatalog != null && _itemCatalog.TryGet(slot.ItemId, out ItemData itemData))
        {
            displayName = itemData.DisplayName;
        }

        // 상호작용 가능한 대상을 보고 있을 때는 낮은 우선순위의 선택 아이템 안내를 표시하지 않는다.
        if (!string.IsNullOrWhiteSpace(_interactionText))
        {
            return;
        }

        ShowTemporaryPrompt(displayName);
    }

    public void ShowTemporaryPrompt(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (_selectedItemPromptRoutine != null)
        {
            StopCoroutine(_selectedItemPromptRoutine);
        }

        _selectedItemPromptRoutine = StartCoroutine(ShowSelectedItemPrompt(message));
    }

    private IEnumerator ShowSelectedItemPrompt(string message)
    {
        if (_interactionPromptText != null)
        {
            _interactionPromptText.text = message;
            _interactionPromptText.gameObject.SetActive(true);
        }

        yield return new WaitForSecondsRealtime(_selectedItemPromptDuration);

        _selectedItemPromptRoutine = null;
        ApplyInteractionPrompt(_interactionText);
    }

    private void ApplyInteractionPrompt(string interactionText, bool showKeyHint = true)
    {
        if (_interactionPromptText == null)
        {
            return;
        }

        bool isVisible = !string.IsNullOrWhiteSpace(interactionText);
        if (isVisible)
        {
            _interactionPromptText.text = showKeyHint ? $"{interactionText} : [E]" : interactionText;
        }

        _interactionPromptText.gameObject.SetActive(isVisible);
    }

    private void Refresh()
    {
        if (_inventory == null)
            return;

        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i]?.SetActive(true);

            InventorySlot inventorySlot = _inventory.Slots[i];
            bool hasItem = inventorySlot != null && !inventorySlot.IsEmpty;

            if (_itemIcons[i] == null)
                continue;


            _itemIcons[i].sprite = null;
            _itemIcons[i].enabled = false;

            if (hasItem && _itemCatalog != null && _itemCatalog.TryGet(inventorySlot.ItemId, out ItemData itemData))
            {
                _itemIcons[i].sprite = itemData.Icon;
                _itemIcons[i].enabled = true;
            }
        }

        // 아이템이 인벤토리에 없으면 선택 테두리를 숨기기 (없으면 SelectedIndex 는 -1)
        bool showSelection = _inventory.HasAnyItem && _inventory.SelectedIndex >= 0 && _inventory.SelectedIndex < _slots.Length;

        if (_selectionOutline == null)
            return;

        _selectionOutline.gameObject.SetActive(showSelection);

        if (showSelection)  // 선택한 슬롯의 위치를 찾아서 선택 테두리를 그 위치로 옮기기
        {
            RectTransform selectedSlot = (RectTransform)_slots[_inventory.SelectedIndex].transform;

            _selectionOutline.anchoredPosition = selectedSlot.anchoredPosition;
        }
    }

    public void Interact(GameObject interactor)
    {
        throw new System.NotImplementedException();
    }
}
