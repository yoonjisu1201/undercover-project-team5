using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 배터리 배치 상태, 정답 판정, 실제 플레이어 인벤토리 연결을 관리한다.
public sealed partial class BreakerBatteryMiniGame : MonoBehaviour, IUIDragDropContext
{
    // 아이템 ID에서 배터리 용량을 판별할 때 허용하는 와트 목록이다.
    // 현재 인벤토리 UI에 생성된 드래그 가능한 배터리들이다.
    private readonly List<BatteryDragItem> _batteries = new();
    private readonly BatteryDragItem[] _slots = new BatteryDragItem[4]; // 오른쪽 전원 슬롯의 드롭 영역 컴포넌트들이다.

    private readonly UIDropSlot[] _dropSlots = new UIDropSlot[4];       // 인벤토리를 다시 그릴 때 제거할 UI 셀 목록이다.

    private readonly List<RectTransform> _inventoryCells = new();    // 미니게임 진입 시 실제 인벤토리에서 꺼내 미니게임이 임시로 보관하는 건전지 ID들이다.

    private readonly List<string> _stagedBatteryItemIds = new();

    private Transform _inventoryPanel;
    private RectTransform _inventoryContent;
    private ScrollRect _inventoryScroll;
    // 실제 플레이어 인벤토리 데이터와 아이템 아이콘 정보를 제공한다.
    private PlayerInventory _playerInventory;
    private ItemCatalog _itemCatalog;
    private TMP_Text _currentValueText;
    private TMP_Text _targetValueText;
    private Image _wattFill;
    private RectTransform _wattFillRect;
    private float _wattFillMinAnchorX;
    private float _wattFillMaxAnchorX;
    private Button _confirmButton;
    private Button _retryButton;
    // 현재 보유한 건전지로 만들 수 있는 조합 중 무작위로 선택한 목표 전력이다.
    private int _targetWatt;
    private bool _completed;

    private void Awake()
    {
        CacheReferences();
        SetupScrollableInventory();
        SetupSlots(FindChild(transform, "PowerBoard"));
        StageInventoryBatteries();
        SelectRandomTargetWatt();
        RebuildInventoryGrid();
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
        _currentValueText.color = Color.white;
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
        _currentValueText.text = $"{current:000} W";
        UpdateWattGauge(current);
        if (!_completed)
        {
            // 오답 확인 뒤 배치를 수정하면 경고색을 해제한다.
            _currentValueText.color = Color.white;
        }
    }

    private void CheckAnswer(int current)
    {
        // 목표 전력과 다르면 현재 수치를 빨간색으로 표시하고 배치를 계속 수정할 수 있게 한다.
        if (current != _targetWatt)
        {
            _currentValueText.color = new Color(1f, 0.3f, 0.3f);
            return;
        }

        _completed = true;
        DiscardStoredBatteries();
        // 목표 전력에 정확히 도달한 최초 한 번만 완료 UI와 종료 가능 상태를 활성화한다.
        _currentValueText.color = new Color(0.15f, 0.85f, 0.82f);
        _confirmButton.interactable = false;
        _retryButton.interactable = false;
        GetComponent<MiniGameUIController>().MarkCompletionReady();
        FindChild(transform, "ResultOverlay").gameObject.SetActive(true);
    }

}
