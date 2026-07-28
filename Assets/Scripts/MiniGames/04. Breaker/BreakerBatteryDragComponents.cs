using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Breaker 미니게임 데이터를 공용 UI 드래그 시스템에 연결한다.
public sealed class BatteryDragItem : UIDraggableItem
{
    // Watt는 정답 합산에, ItemId는 공용 드래그 시스템의 항목 식별에 사용한다.
    public int Watt { get; private set; }
    public string ItemId { get; private set; }
    // 슬롯에서 빠졌을 때 돌아갈 원래 인벤토리 셀이다.
    public RectTransform SourceCell { get; private set; }

    public void Initialize(BreakerBatteryMiniGame owner, int watt, string itemId, RectTransform sourceCell)
    {
        // 배터리 고유 정보와 반환 위치를 저장한 뒤 공용 드래그 동작을 초기화한다.
        Watt = watt;
        ItemId = itemId;
        SourceCell = sourceCell;
        InitializeDrag(owner, itemId);
    }
}

public sealed class BatteryDropSlot : UIDropSlot
{
    // 공용 드롭 슬롯에 미니게임 소유자, 슬롯 번호, 표시 요소를 전달하는 어댑터다.
    public void Initialize(
        BreakerBatteryMiniGame owner,
        int slotIndex,
        Image background,
        TMP_Text placeholder)
    {
        InitializeSlot(owner, slotIndex, background, placeholder);
    }
}

public sealed class InventoryGridPool : UIDropPool
{
    // 왼쪽 인벤토리 영역을 공용 드롭 시스템의 반환 위치로 등록한다.
    public void Initialize(BreakerBatteryMiniGame owner) => InitializePool(owner);
}
