using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 프리팹에 제작된 UI 셀을 데이터에 맞게 채우고 드래그 위치를 갱신한다.
public sealed partial class BreakerBatteryMission
{
    private const int MinimumVisibleCellCount = 8;  // 인벤토리 영역에 항상 표시할 최소 셀 수이다. 실제 보관함 배터리 수가 적어도 8칸은 보여야 한다.
    private static readonly Color UnderTargetColor = new(1f, 0.78f, 0.12f);
    private static readonly Color TargetMatchedColor = new(0.15f, 0.85f, 0.45f);
    private static readonly Color OverTargetColor = new(0.95f, 0.2f, 0.25f);
    private TMP_Text _progressText;

    private void CacheReferences()
    {
        _inventoryPanel = FindChild(transform, "CollectedBatteries");
        _inventoryScroll = FindChild(_inventoryPanel, "InventoryScroll").GetComponent<ScrollRect>();
        _inventoryContent = _inventoryScroll.content;

        Transform powerBoard = FindChild(transform, "PowerBoard");
        _currentValueText = FindChild(powerBoard, "CurrentValue").GetComponent<TMP_Text>();
        _progressText = FindChild(transform, "ProgressText")?.GetComponent<TMP_Text>();

        // 목표 대비 눈금은 HQ 계기판(C) 역할로 옮겨졌으므로 A 화면 프리팹에는 게이지가 없을 수 있다.
        Transform wattFill = FindChild(powerBoard, "Fill");
        _wattFill = wattFill != null ? wattFill.GetComponent<Image>() : null;
        if (_wattFill != null)
        {
            _wattFillRect = _wattFill.rectTransform;
            _wattFillMinAnchorX = _wattFillRect.anchorMin.x;
            _wattFillMaxAnchorX = _wattFillRect.anchorMax.x;
        }
        _itemCatalog = FindFirstObjectByType<ItemCatalog>();
        _playerInventory = FindLocalInventory();
    }

    private void SetupScrollableInventory()
    {
        InventoryGridPool pool = _inventoryContent.parent.GetComponent<InventoryGridPool>()
            ?? _inventoryContent.parent.gameObject.AddComponent<InventoryGridPool>();
        pool.Initialize(this);
    }

    // 셀은 기계에 들어온 배터리 전부(내 것 + 남의 것)에 대해 하나씩 만든다.
    // 슬롯에 꽂힌 배터리도 셀을 가지고 있어야 빼냈을 때 돌아갈 자리가 있다.
    private void RebuildInventoryGrid()
    {
        ClearInventoryItems();

        IReadOnlyList<BreakerBatteryEntry> entries = _circuitState != null
            ? _circuitState.Batteries
            : Array.Empty<BreakerBatteryEntry>();

        int requiredCellCount = Mathf.Max(MinimumVisibleCellCount, entries.Count);
        EnsureInventoryCellCount(requiredCellCount);

        for (int index = 0; index < _inventoryCells.Count; index++)
        {
            RectTransform cell = _inventoryCells[index];
            cell.gameObject.SetActive(index < requiredCellCount);

            if (index < entries.Count)
            {
                BindInventoryItem(cell, entries[index]);
            }
        }

        // 셀 수에 따른 콘텐츠 높이를 먼저 확정한 뒤 보관함 스크롤을 항상 맨 위로 되돌린다.
        LayoutRebuilder.ForceRebuildLayoutImmediate(_inventoryContent);
        _inventoryScroll.StopMovement();
        _inventoryScroll.verticalNormalizedPosition = 1f;
        RefreshItemPositions();
    }

    // 기존 셀은 그대로 두고 새로 보관된 건전지만 빈 셀에 채운다.

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

    private void BindInventoryItem(RectTransform cell, BreakerBatteryEntry entry)
    {
        // 복제된 셀은 원본 배터리가 전원 슬롯으로 옮겨간 상태였으면 배터리 자식이 없다.
        // 그냥 건너뛰면 그 배터리가 화면에서 통째로 사라지므로, 다른 셀에서 하나 복제해 채운다.
        BatteryDragItem battery = cell.GetComponentInChildren<BatteryDragItem>(true);
        if (battery == null)
        {
            battery = CreateBatteryItemIn(cell);
        }

        if (battery == null)
        {
            return;
        }

        ItemType itemId = GetBatteryItemId(entry.Watt);
        if (itemId == ItemType.None)
        {
            return;
        }

        Image image = battery.GetComponent<Image>();
        image.sprite = GetItemIcon(itemId);
        battery.gameObject.SetActive(true);

        bool isMine = NetworkManager.Singleton != null && entry.Owner == NetworkManager.Singleton.LocalClientId;
        battery.Initialize(this, entry.Watt, itemId, cell, entry.Id, isMine);
        _batteries.Add(battery);
    }

    // 배터리 자식이 없는 셀을 채우기 위해, 아직 자식이 남아 있는 셀에서 하나 복제한다.
    private BatteryDragItem CreateBatteryItemIn(RectTransform cell)
    {
        foreach (RectTransform other in _inventoryCells)
        {
            if (other == cell)
            {
                continue;
            }

            BatteryDragItem source = other.GetComponentInChildren<BatteryDragItem>(true);
            if (source == null)
            {
                continue;
            }

            BatteryDragItem copy = Instantiate(source, cell);
            StretchToParent((RectTransform)copy.transform);
            return copy;
        }

        return null;
    }

    // 보관함 목록이 바뀌었을 때만 셀을 다시 만든다. 드래그 중에 매번 새로 만들면 잡고 있던 항목이 사라진다.
    private void RebuildInventoryGridIfChanged()
    {
        int sharedCount = _circuitState != null ? _circuitState.Batteries.Count : 0;
        if (sharedCount != _batteries.Count)
        {
            RebuildInventoryGrid();
        }
    }

    private Sprite GetItemIcon(ItemType itemId)
    {
        if (_itemCatalog != null
            && _itemCatalog.TryGet(itemId, out ItemData itemData)
            && itemData.Icon != null)
        {
            return itemData.Icon;
        }

        return TryGetBatteryWatt(itemId, out int watt)
            ? Resources.Load<Sprite>($"Missions/Battery/Battery{watt}W")
            : null;
    }

    private void ClearInventoryItems()
    {
        foreach (BatteryDragItem battery in _batteries)
        {
            if (battery == null)
            {
                continue;
            }

            // 전원 슬롯에 가 있는 배터리는 원래 셀로 돌려놓는다.
            // 셀을 복제할 때 첫 셀을 템플릿으로 쓰는데, 그 셀에 배터리 자식이 없으면
            // 복제본도 비어 나오고 해당 배터리는 화면에서 통째로 빠진다.
            if (battery.SourceCell != null && battery.transform.parent != battery.SourceCell)
            {
                MoveToInventoryCell(battery);
            }

            battery.CurrentSlotIndex = -1;
            battery.gameObject.SetActive(false);
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

    // Unity Button의 On Click 이벤트에서 측정을 요청한다. C(계기판)들이 바늘 연출과 결과 창을 이어서 처리한다.
    // 여기서 패널을 닫으면 UI가 파괴돼 배치가 사라지므로, 닫기는 '뒤로' 버튼에만 맡긴다.
    public void OnConfirmButtonClick()
    {
        _circuitState?.RequestMeasurement();
        UpdateStatusText();
    }

    // 전원 여부와 완료 여부를 글로 알려준다. 목표까지 얼마나 남았는지는 C(계기판)만 알 수 있어야 하므로 방향도 밝히지 않는다.
    private void UpdateStatusText()
    {
        if (_statusText == null)
        {
            return;
        }

        if (_circuitState == null)
        {
            UpdateProgressText(false);
            _statusText.text = "회로 연결 대기 중";
            return;
        }

        if (_circuitState.IsCompleted)
        {
            UpdateProgressText(true);
            _statusText.text = "전력 연결 완료";
            return;
        }

        UpdateProgressText(false);

        if (!_circuitState.PowerOn)
        {
            // 레버가 내려가 있으면 전류가 흐르지 않아 측정 자체를 할 수 없다.
            _statusText.text = "측정 불가 — 레버를 올려서 전력을 측정하세요";
            return;
        }

        // 확인을 누른 뒤 계기판 바늘이 올라가는 동안이다.
        if (_isMeasuring)
        {
            _statusText.text = "측정 중 — 전력을 측정하고 있습니다";
            return;
        }

        // 바늘이 다 움직인 뒤에야 결과를 알려준다.
        // 모자란지 넘쳤는지는 밝히지 않는다. 그 방향은 C(계기판)만 알 수 있어야 한다.
        if (_hasMeasurementResult)
        {
            _statusText.text = _measuredWatt == _circuitState.TargetWatt
                ? "측정 완료 — 목표 전력에 도달했습니다"
                : "측정 완료 — 목표 전력과 맞지 않습니다";
            return;
        }

        _statusText.text = "측정 가능 — 확인 버튼을 눌러서 전력을 측정하세요";
    }

    private void UpdateProgressText(bool completed)
    {
        if (_progressText != null)
        {
            _progressText.text = completed ? "진행도  1 / 1" : "진행도  0 / 1";
        }
    }

    // 게이지 업데이트
    private void UpdateWattGauge(int currentWatt)
    {
        if (_wattFill == null)
        {
            return;
        }

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
