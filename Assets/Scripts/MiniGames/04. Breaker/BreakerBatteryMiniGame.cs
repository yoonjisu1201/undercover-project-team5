using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 배터리 배치 상태, 정답 판정, 실제 플레이어 인벤토리 연결을 관리한다.
public sealed partial class BreakerBatteryMiniGame : MonoBehaviour, IUIDragDropContext
{
    // 네 슬롯에 장착한 배터리 합계가 이 값과 같으면 미니게임을 완료한다.
    private const int TargetWatt = 120;
    // 아이템 ID에서 배터리 용량을 판별할 때 허용하는 와트 목록이다.
    private static readonly int[] SupportedWatts = { 20, 30, 40, 50 };
    private static readonly Color CellColor = new(0.035f, 0.065f, 0.085f, 0.95f);

    // 현재 인벤토리 UI에 생성된 드래그 가능한 배터리들이다.
    private readonly List<BatteryDragItem> _batteries = new();
    // 각 전원 슬롯에 장착된 배터리. null이면 빈 슬롯이다.
    private readonly BatteryDragItem[] _slots = new BatteryDragItem[4];
    // 오른쪽 전원 슬롯의 드롭 영역 컴포넌트들이다.
    private readonly UIDropSlot[] _dropSlots = new UIDropSlot[4];
    // 인벤토리를 다시 그릴 때 제거할 UI 셀 목록이다.
    private readonly List<RectTransform> _inventoryCells = new();

    private Transform _inventoryPanel;
    private RectTransform _inventoryContent;
    // 실제 플레이어 인벤토리 데이터와 아이템 아이콘 정보를 제공한다.
    private PlayerInventory _playerInventory;
    private ItemCatalog _itemCatalog;
    private TMP_Text _currentText;
    private TMP_Text _progressText;
    private Button _confirmButton;
    private Button _retryButton;
    private bool _completed;

    private void Awake()
    {
        CacheReferences();
        SetupScrollableInventory();
        SetupSlots(FindChild(transform, "PowerBoard"));
        SetupActionButtons(FindChild(transform, "PowerBoard"));
        RebuildInventoryGrid();

        if (_playerInventory != null)
        {
            // 실제 인벤토리가 바뀌면 미니게임 UI도 즉시 다시 그린다.
            _playerInventory.InventoryChanged += RebuildInventoryGrid;
        }
    }

    private void OnDestroy()
    {
        // 파괴된 UI를 인벤토리 이벤트가 다시 호출하지 않도록 구독을 해제한다.
        if (_playerInventory != null)
        {
            _playerInventory.InventoryChanged -= RebuildInventoryGrid;
        }
    }

    // 현재 슬롯의 전력 합계를 사용자가 확정했을 때만 정답으로 판정한다.
    private void ConfirmAnswer()
    {
        if (_completed)
        {
            return;
        }

        int current = _slots.Where(item => item != null).Sum(item => item.Watt);
        CheckAnswer(current);
    }

    // 모든 슬롯을 비우고 배터리들을 각각의 원래 인벤토리 셀로 되돌린다.
    private void ResetBatteries()
    {
        if (_completed)
        {
            return;
        }

        foreach (BatteryDragItem battery in _slots.Where(item => item != null))
        {
            battery.CurrentSlotIndex = -1;
        }

        Array.Clear(_slots, 0, _slots.Length);
        _currentText.color = Color.white;
        RefreshItemPositions();
    }

    public void DropOnSlot(UIDraggableItem item, int slotIndex)
    {
        if (item is not BatteryDragItem battery)
        {
            // 이 미니게임의 배터리가 아닌 드래그 항목은 슬롯 상태를 바꾸지 않는다.
            RefreshItemPositions();
            return;
        }

        // sourceIndex가 -1이면 인벤토리에서 왔고, 0 이상이면 다른 전원 슬롯에서 왔다.
        int sourceIndex = battery.CurrentSlotIndex;
        BatteryDragItem displaced = _slots[slotIndex];

        if (sourceIndex >= 0)
        {
            // 슬롯끼리 옮길 때 목적지 배터리가 있으면 두 배터리의 위치를 서로 교환한다.
            _slots[sourceIndex] = displaced;
            if (displaced != null)
            {
                displaced.CurrentSlotIndex = sourceIndex;
            }
        }
        else if (displaced != null)
        {
            // 인벤토리 배터리가 차 있는 슬롯에 들어오면 기존 배터리를 인벤토리로 돌려보낸다.
            displaced.CurrentSlotIndex = -1;
        }

        _slots[slotIndex] = battery;
        battery.CurrentSlotIndex = slotIndex;
        RefreshItemPositions();
    }

    public void DropOnItem(UIDraggableItem target, UIDraggableItem dragged)
    {
        if (target is not BatteryDragItem targetBattery || dragged is not BatteryDragItem)
        {
            RefreshItemPositions();
            return;
        }

        if (targetBattery.CurrentSlotIndex >= 0)
        {
            // 슬롯에 꽂힌 배터리 위에 놓으면 해당 슬롯에 드롭한 것과 동일하게 처리한다.
            DropOnSlot(dragged, targetBattery.CurrentSlotIndex);
        }
        else
        {
            // 인벤토리의 아이템 위에 놓으면 드래그한 배터리를 인벤토리로 반환한다.
            ReturnToPool(dragged);
        }
    }

    public void ReturnToPool(UIDraggableItem item)
    {
        // 배터리를 전원 슬롯에서 해제하고 원래 인벤토리 셀로 반환한다.
        if (item is not BatteryDragItem battery)
        {
            return;
        }

        if (battery.CurrentSlotIndex >= 0)
        {
            _slots[battery.CurrentSlotIndex] = null;
        }

        battery.CurrentSlotIndex = -1;
        RefreshItemPositions();
    }

    public void RefreshItemPositions()
    {
        // 슬롯 번호가 없는 배터리는 원래 인벤토리 셀로 되돌린다.
        foreach (BatteryDragItem battery in _batteries.Where(item => item.CurrentSlotIndex < 0))
        {
            MoveToInventoryCell(battery);
        }

        for (int index = 0; index < _slots.Length; index++)
        {
            BatteryDragItem battery = _slots[index];
            _dropSlots[index]?.SetOccupied(battery != null);
            if (battery != null)
            {
                MoveToPowerSlot(battery, _dropSlots[index].transform);
            }
        }

        int current = _slots.Where(item => item != null).Sum(item => item.Watt);
        _currentText.text = $"CURRENT  {current:000} W";
        if (!_completed)
        {
            // 오답 확인 뒤 배치를 수정하면 경고색을 해제한다.
            _currentText.color = Color.white;
        }
    }

    private void CheckAnswer(int current)
    {
        // 목표 전력과 다르면 현재 수치를 빨간색으로 표시하고 배치를 계속 수정할 수 있게 한다.
        if (current != TargetWatt)
        {
            _currentText.color = new Color(1f, 0.3f, 0.3f);
            return;
        }

        _completed = true;
        // 목표 전력에 정확히 도달한 최초 한 번만 완료 UI와 종료 가능 상태를 활성화한다.
        _currentText.color = new Color(0.15f, 0.85f, 0.82f);
        _confirmButton.interactable = false;
        _retryButton.interactable = false;
        _progressText.text = "PROGRESS  1 / 1";
        GetComponent<MiniGameUIController>().MarkCompletionReady();
        FindChild(transform, "ResultOverlay").gameObject.SetActive(true);
    }

    private static bool TryGetBatteryWatt(string itemId, out int watt)
    {
        watt = 0;
        // ID에 "battery"가 있어야 숫자가 우연히 포함된 일반 아이템을 배터리로 오인하지 않는다.
        if (string.IsNullOrWhiteSpace(itemId)
            || itemId.IndexOf("battery", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        foreach (int supportedWatt in SupportedWatts)
        {
            if (itemId.Contains(supportedWatt.ToString(), StringComparison.Ordinal))
            {
                watt = supportedWatt;
                return true;
            }
        }

        return false;
    }

    private static PlayerInventory FindLocalInventory()
    {
        // 멀티플레이에서는 로컬 플레이어의 인벤토리를 가장 먼저 사용한다.
        if (NetworkManager.Singleton != null
            && NetworkManager.Singleton.LocalClient?.PlayerObject != null
            && NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out PlayerInventory inventory))
        {
            return inventory;
        }

        // 네트워크 플레이어가 아직 준비되지 않은 테스트/에디터 환경을 위한 대체 탐색이다.
        return FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.IsOwner)
            ?? FindFirstObjectByType<PlayerInventory>();
    }
}
