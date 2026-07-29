using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 프리팹에 제작된 UI 셀을 데이터에 맞게 채우고 드래그 위치를 갱신한다.
public sealed partial class BreakerBatteryMiniGame
{
    private const int MinimumVisibleCellCount = 8;  // 인벤토리 영역에 항상 표시할 최소 셀 수이다. 실제 보관함 배터리 수가 적어도 8칸은 보여야 한다.
    private static readonly Color UnderTargetColor = new(1f, 0.78f, 0.12f);
    private static readonly Color TargetMatchedColor = new(0.15f, 0.85f, 0.45f);
    private static readonly Color OverTargetColor = new(0.95f, 0.2f, 0.25f);

    private void CacheReferences()
    {
        _inventoryPanel = FindChild(transform, "CollectedBatteries");
        _inventoryScroll = FindChild(_inventoryPanel, "InventoryScroll").GetComponent<ScrollRect>();
        _inventoryContent = _inventoryScroll.content;

        Transform powerBoard = FindChild(transform, "PowerBoard");
        _currentValueText = FindChild(powerBoard, "CurrentValue").GetComponent<TMP_Text>();
        _targetValueText = FindChild(powerBoard, "TargetValue").GetComponent<TMP_Text>();
        _wattFill = FindChild(powerBoard, "Fill").GetComponent<Image>();
        _wattFillRect = _wattFill.rectTransform;
        _wattFillMinAnchorX = _wattFillRect.anchorMin.x;
        _wattFillMaxAnchorX = _wattFillRect.anchorMax.x;
        _confirmButton = FindChild(powerBoard, "ConfirmButton").GetComponent<Button>();
        _retryButton = FindChild(powerBoard, "RetryButton").GetComponent<Button>();
        _itemCatalog = FindFirstObjectByType<ItemCatalog>();
        _playerInventory = FindLocalInventory();
    }

    private void SetupScrollableInventory()
    {
        InventoryGridPool pool = _inventoryContent.parent.GetComponent<InventoryGridPool>()
            ?? _inventoryContent.parent.gameObject.AddComponent<InventoryGridPool>();
        pool.Initialize(this);
    }

    private void RebuildInventoryGrid()
    {
        ClearInventoryItems();
        int requiredCellCount = Mathf.Max(MinimumVisibleCellCount, _stagedBatteryItemIds.Count);
        EnsureInventoryCellCount(requiredCellCount);

        for (int index = 0; index < _inventoryCells.Count; index++)
        {
            RectTransform cell = _inventoryCells[index];
            cell.gameObject.SetActive(index < requiredCellCount);

            if (index < _stagedBatteryItemIds.Count)
            {
                BindInventoryItem(cell, _stagedBatteryItemIds[index]);
            }
        }

        // 셀 수에 따른 콘텐츠 높이를 먼저 확정한 뒤 보관함 스크롤을 항상 맨 위로 되돌린다.
        LayoutRebuilder.ForceRebuildLayoutImmediate(_inventoryContent);
        _inventoryScroll.StopMovement();
        _inventoryScroll.verticalNormalizedPosition = 1f;
        RefreshItemPositions();
    }

    // 프리팹의 8칸을 우선 사용하고, 초과 수량만 첫 셀 템플릿을 복제한다.
    private void EnsureInventoryCellCount(int requiredCount)
    {
        if (_inventoryCells.Count == 0)
        {
            _inventoryCells.AddRange(_inventoryContent
                .Cast<Transform>()
                .Select(child => child as RectTransform)
                .Where(child => child != null));
        }

        RectTransform template = _inventoryCells[0];
        while (_inventoryCells.Count < requiredCount)
        {
            RectTransform cell = Instantiate(template, _inventoryContent);
            cell.name = $"InventoryCell{_inventoryCells.Count}";
            _inventoryCells.Add(cell);
        }
    }

    private void BindInventoryItem(RectTransform cell, string itemId)
    {
        BatteryDragItem battery = cell.GetComponentInChildren<BatteryDragItem>(true);
        Image image = battery.GetComponent<Image>();

        if (!TryGetBatteryWatt(itemId, out int watt))
        {
            return;
        }

        image.sprite = GetItemIcon(itemId);
        battery.gameObject.SetActive(true);
        battery.Initialize(this, watt, itemId, cell);
        _batteries.Add(battery);
    }

    private Sprite GetItemIcon(string itemId)
    {
        if (_itemCatalog != null
            && _itemCatalog.TryGet(itemId, out ItemData itemData)
            && itemData.Icon != null)
        {
            return itemData.Icon;
        }

        return TryGetBatteryWatt(itemId, out int watt)
            ? Resources.Load<Sprite>($"MiniGames/Battery/Battery{watt}W")
            : null;
    }

    private void ClearInventoryItems()
    {
        foreach (BatteryDragItem battery in _batteries)
        {
            if (battery != null)
            {
                battery.CurrentSlotIndex = -1;
                battery.gameObject.SetActive(false);
            }
        }

        _batteries.Clear();
        Array.Clear(_slots, 0, _slots.Length);
    }

    private void SetupSlots(Transform powerBoard)
    {
        for (int index = 0; index < _slots.Length; index++)
        {
            Transform slotTransform = FindChild(powerBoard, $"BatterySlot{index}");
            Image background = slotTransform.GetComponent<Image>();
            TMP_Text placeholder = FindChild(slotTransform, "Slot").GetComponent<TMP_Text>();

            BatteryDropSlot slot = slotTransform.GetComponent<BatteryDropSlot>()
                ?? slotTransform.gameObject.AddComponent<BatteryDropSlot>();
            slot.Initialize(this, index, background, placeholder);
            _dropSlots[index] = slot;
        }
    }

    // Unity Button의 On Click 이벤트에서 배터리 배치를 초기화한다.
    public void OnRetryButtonClick()
    {
        ResetBatteries();
    }

    // Unity Button의 On Click 이벤트에서 현재 전력 조합을 확인한다.
    public void OnConfirmButtonClick()
    {
        ConfirmAnswer();
    }

    // 게이지 업데이트
    private void UpdateWattGauge(int currentWatt)
    {
        // 목표를 초과해도 게이지 길이는 100%에서 멈추고 색상만 빨간색으로 바뀐다.
        float ratio = _targetWatt > 0 ? Mathf.Clamp01((float)currentWatt / _targetWatt) : 0f;
        Vector2 anchorMax = _wattFillRect.anchorMax;
        anchorMax.x = Mathf.Lerp(_wattFillMinAnchorX, _wattFillMaxAnchorX, ratio);
        _wattFillRect.anchorMax = anchorMax;

        _wattFill.color = currentWatt > _targetWatt ? OverTargetColor : currentWatt == _targetWatt ? TargetMatchedColor : UnderTargetColor;
    }

    // 슬롯에서 드래그가 취소되거나 슬롯에 배치할 수 없는 경우, 아이템을 원래 인벤토리 셀로 되돌린다.
    private static void MoveToInventoryCell(BatteryDragItem item)
    {
        RectTransform rect = (RectTransform)item.transform;
        rect.SetParent(item.SourceCell, false);
        StretchToParent(rect);
    }

    // 슬롯에 배치된 배터리를 전원 슬롯 위치로 이동시킨다.
    private static void MoveToPowerSlot(BatteryDragItem item, Transform parent)
    {
        RectTransform rect = (RectTransform)item.transform;
        rect.SetParent(parent, false);
        StretchToParent(rect);
    }

    // 배터리 드래그 아이템이 슬롯에 배치되면 슬롯 번호를 기록하고, 이전 슬롯에 있던 배터리는 제거한다.
    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.08f, 0.08f);
        rect.anchorMax = new Vector2(0.92f, 0.92f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Transform FindChild(Transform parent, string childName)  // 부모 Transform에서 이름이 일치하는 자식 Transform을 찾아 반환한다. 없으면 null이다.
    {
        return parent.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == childName);
    }
}
