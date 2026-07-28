using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Breaker 미니게임의 런타임 UI 생성과 인벤토리 표시를 담당한다.
public sealed partial class BreakerBatteryMiniGame
{
    // 프리팹 UI와 게임 데이터 참조를 찾아 초기 화면 영역을 조정한다.
    private void CacheReferences()
    {
        _inventoryPanel = FindChild(transform, "CollectedBatteries");
        _inventoryPanel.gameObject.SetActive(true);
        FindChild(_inventoryPanel, "Title").GetComponent<TMP_Text>().text = "INVENTORY";

        RectTransform powerBoard = (RectTransform)FindChild(transform, "PowerBoard");
        powerBoard.anchorMin = new Vector2(0.34f, 0.04f);
        powerBoard.anchorMax = new Vector2(0.98f, 0.95f);
        powerBoard.offsetMin = powerBoard.offsetMax = Vector2.zero;

        _currentText = FindChild(powerBoard, "Current").GetComponent<TMP_Text>();
        _progressText = FindChild(transform, "ProgressText").GetComponent<TMP_Text>();
        _itemCatalog = FindFirstObjectByType<ItemCatalog>();
        _playerInventory = FindLocalInventory();
    }

    private void SetupScrollableInventory()
    {
        // ScrollRect와 Scrollbar의 구조 및 참조는 MiniGameUI4 프리팹에서 직접 설정한다.
        ScrollRect scrollRect = FindChild(_inventoryPanel, "InventoryScroll").GetComponent<ScrollRect>();
        _inventoryContent = scrollRect.content;
        // 드롭 반환 동작만 런타임 컴포넌트로 연결하고, 스크롤 UI 자체는 프리팹 값을 사용한다.
        InventoryGridPool pool = scrollRect.viewport.GetComponent<InventoryGridPool>()
            ?? scrollRect.viewport.gameObject.AddComponent<InventoryGridPool>();
        pool.Initialize(this);
    }

    private void RebuildInventoryGrid()
    {
        // PlayerInventory.Slots를 기준으로 UI를 통째로 다시 만들어 데이터와 표시를 동기화한다.
        ClearInventoryItems();
        InventorySlot[] slots = _playerInventory != null ? _playerInventory.Slots : Array.Empty<InventorySlot>();
        // 스크롤 영역의 세로 공간을 활용할 수 있도록 인벤토리가 비어 있어도 최소 여덟 칸은 보여 준다.
        int visibleSlotCount = Mathf.Max(8, slots.Length);

        for (int index = 0; index < visibleSlotCount; index++)
        {
            RectTransform cell = CreateInventoryCell(index);
            _inventoryCells.Add(cell);

            if (index >= slots.Length || slots[index] == null || slots[index].IsEmpty)
            {
                // 실제 슬롯이 없거나 빈 슬롯이면 배경 셀만 남긴다.
                continue;
            }

            string itemId = slots[index].ItemId;
            CreateInventoryItem(cell, itemId, GetItemIcon(itemId));
        }

        RefreshItemPositions();
    }

    private RectTransform CreateInventoryCell(int index)
    {
        // 지정된 인벤토리 번호에 배경과 외곽선을 가진 빈 정사각형 셀을 만든다.
        GameObject cellObject = CreateUiObject($"InventoryCell{index}", _inventoryContent);
        Image background = cellObject.AddComponent<Image>();
        background.color = CellColor;
        background.raycastTarget = true;

        Outline outline = cellObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.15f, 0.85f, 0.82f, 0.28f);
        outline.effectDistance = new Vector2(1f, -1f);
        return cellObject.GetComponent<RectTransform>();
    }

    private void CreateInventoryItem(RectTransform cell, string itemId, Sprite icon)
    {
        GameObject iconObject = CreateUiObject("ItemIcon", cell);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        SetStretch(iconRect, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f));

        Image image = iconObject.AddComponent<Image>();
        image.sprite = icon;
        image.preserveAspect = true;
        image.raycastTarget = true;

        if (!TryGetBatteryWatt(itemId, out int watt))
        {
            // 일반 아이템도 인벤토리에 표시하지만 배터리가 아니면 드래그 기능은 붙이지 않는다.
            return;
        }

        if (image.sprite == null)
        {
            // 카탈로그 아이콘이 없을 때 미니게임 전용 배터리 이미지를 대신 사용한다.
            image.sprite = Resources.Load<Sprite>($"MiniGames/Battery/Battery{watt}W");
        }

        BatteryDragItem battery = iconObject.AddComponent<BatteryDragItem>();
        battery.Initialize(this, watt, itemId, cell);
        _batteries.Add(battery);
    }

    private Sprite GetItemIcon(string itemId)
    {
        // 아이템 카탈로그의 아이콘을 우선 사용한다.
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
        // UI 재구성 전에 기존 아이콘과 셀, 전원 슬롯의 참조를 모두 초기화한다.
        // 프리팹 편집 화면에 보이는 기본 정사각형 칸은 실제 인벤토리 칸으로 교체한다.
        foreach (Transform child in _inventoryContent)
        {
            if (child.name.StartsWith("InventoryPreviewCell", StringComparison.Ordinal))
            {
                Destroy(child.gameObject);
            }
        }

        foreach (BatteryDragItem battery in _batteries)
        {
            if (battery != null)
            {
                Destroy(battery.gameObject);
            }
        }

        _batteries.Clear();
        Array.Clear(_slots, 0, _slots.Length);

        foreach (RectTransform cell in _inventoryCells)
        {
            if (cell != null)
            {
                Destroy(cell.gameObject);
            }
        }

        _inventoryCells.Clear();
    }

    private void SetupSlots(Transform powerBoard)
    {
        for (int index = 0; index < _slots.Length; index++)
        {
            Transform slotTransform = FindChild(powerBoard, $"BatterySlot{index}");
            Image image = slotTransform.GetComponent<Image>();
            image.raycastTarget = true;
            TMP_Text placeholder = FindChild(slotTransform, "Slot").GetComponent<TMP_Text>();

            BatteryDropSlot slot = slotTransform.GetComponent<BatteryDropSlot>()
                ?? slotTransform.gameObject.AddComponent<BatteryDropSlot>();
            slot.Initialize(this, index, image, placeholder);
            _dropSlots[index] = slot;
        }
    }

    // 오른쪽 전원 보드 하단에 확인 및 다시 하기 버튼을 만들고 클릭 동작을 연결한다.
    private void SetupActionButtons(Transform powerBoard)
    {
        // 버튼의 위치와 모양은 프리팹에서 편집하고 코드에서는 클릭 동작만 연결한다.
        _retryButton = FindChild(powerBoard, "RetryButton").GetComponent<Button>();
        _retryButton.onClick.RemoveAllListeners();
        _retryButton.onClick.AddListener(ResetBatteries);

        _confirmButton = FindChild(powerBoard, "ConfirmButton").GetComponent<Button>();
        _confirmButton.onClick.RemoveAllListeners();
        _confirmButton.onClick.AddListener(ConfirmAnswer);
    }

    private static void MoveToInventoryCell(BatteryDragItem item)
    {
        // 드래그 중 변경된 부모와 앵커를 원래 인벤토리 셀 기준으로 복원한다.
        RectTransform rect = (RectTransform)item.transform;
        rect.SetParent(item.SourceCell, false);
        SetStretch(rect, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f));
    }

    private static void MoveToPowerSlot(BatteryDragItem item, Transform parent)
    {
        // 정사각형 슬롯 안쪽 여백을 유지하며 아이콘을 채운다.
        RectTransform rect = (RectTransform)item.transform;
        rect.SetParent(parent, false);
        SetStretch(rect, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f));
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        // 런타임 UI 요소에 필요한 RectTransform을 포함해 자식 오브젝트를 생성한다.
        GameObject gameObject = new(objectName, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static void SetStretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        // 지정한 앵커 범위를 부모 안에서 여백 없이 채운다.
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Transform FindChild(Transform parent, string childName)
    {
        // 비활성화된 자식까지 포함해 이름이 같은 첫 번째 Transform을 찾는다.
        return parent.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(child => child.name == childName);
    }
}
