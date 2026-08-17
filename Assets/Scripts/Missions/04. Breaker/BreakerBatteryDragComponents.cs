using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Breaker 미션 데이터를 공용 UI 드래그 시스템에 연결한다.
public sealed class BatteryDragItem : UIDraggableItem
{
    // Watt는 정답 합산에, ItemId는 공용 드래그 시스템의 항목 식별에 사용한다.
    public int Watt { get; private set; }
    public ItemType ItemId { get; private set; }
    // 슬롯에서 빠졌을 때 돌아갈 원래 인벤토리 셀이다.
    public RectTransform SourceCell { get; private set; }

    // 서버가 들고 있는 배터리 목록에서 이 항목이 가리키는 배터리 번호다.
    public int EntryId { get; private set; }

    // 내가 넣은 배터리만 옮길 수 있다. 남의 것은 보이기만 한다.
    public bool IsMine { get; private set; }

    public void Initialize(
        BreakerBatteryMission owner, int watt, ItemType itemId, RectTransform sourceCell, int entryId, bool isMine)
    {
        // 배터리 고유 정보와 반환 위치를 저장한 뒤 공용 드래그 동작을 초기화한다.
        Watt = watt;
        ItemId = itemId;
        SourceCell = sourceCell;
        EntryId = entryId;
        IsMine = isMine;
        InitializeDrag(owner, itemId);
    }
}

public sealed class BatteryDropSlot : UIDropSlot
{
    // 공용 드롭 슬롯에 미션 소유자, 슬롯 번호, 표시 요소를 전달하는 어댑터다.
    public void Initialize(
        BreakerBatteryMission owner,
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
    public void Initialize(BreakerBatteryMission owner) => InitializePool(owner);
}
