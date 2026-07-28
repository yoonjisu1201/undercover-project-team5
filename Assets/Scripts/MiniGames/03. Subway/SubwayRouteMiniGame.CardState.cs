using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 슬롯에 카드를 배치하고, 기존 카드가 있으면 교환한다. 슬롯에 드롭되지 않은 카드는 하단 보기 영역으로 되돌리는 코드
public sealed partial class SubwayRouteMiniGame
{
    void IUIDragDropContext.DropOnSlot(UIDraggableItem item, int slotIndex) =>
        PlaceCard((StationCardDragHandler)item, slotIndex);

    void IUIDragDropContext.DropOnItem(UIDraggableItem target, UIDraggableItem dragged)
    {
        StationCardDragHandler targetCard = (StationCardDragHandler)target;
        if (targetCard.CurrentSlotIndex >= 0)
        {
            PlaceCard((StationCardDragHandler)dragged, targetCard.CurrentSlotIndex);
        }
        else
        {
            ReturnCardToPool((StationCardDragHandler)dragged);
        }
    }

    void IUIDragDropContext.ReturnToPool(UIDraggableItem item) =>
        ReturnCardToPool((StationCardDragHandler)item);

    void IUIDragDropContext.RefreshItemPositions() => RefreshCardPositions();

    // 카드를 대상 슬롯에 배치하고 기존 카드가 있으면 교환한다.
    internal void PlaceCard(StationCardDragHandler card, int slotIndex)
    {
        if (slotIndex <= 0 || slotIndex >= SegmentLength)
        {
            RefreshCardPositions();
            return;
        }

        int sourceIndex = card.CurrentSlotIndex;    // 카드가 현재 배치된 슬롯 인덱스, -1이면 보기 영역에 있는 상태
        StationCardDragHandler displacedCard = _placedCards[slotIndex]; // 슬롯에 이미 배치된 카드, 없으면 null

        if (sourceIndex >= 0)   // 카드가 유효한 슬롯에 배치되어 있다면, 기존 슬롯을 비운다.
        {
            _placedCards[sourceIndex] = displacedCard;
            if (displacedCard != null)  // 기존 슬롯에 있던 카드를 새 슬롯으로 이동시킨다.
            {
                displacedCard.CurrentSlotIndex = sourceIndex;
            }
        }
        else if (displacedCard != null)
        {
            displacedCard.CurrentSlotIndex = -1;
        }

        _placedCards[slotIndex] = card;
        card.CurrentSlotIndex = slotIndex;
        RefreshCardPositions();
    }

    // 슬롯의 카드를 하단 보기 영역으로 되돌린다.
    internal void ReturnCardToPool(StationCardDragHandler card)
    {
        if (card.CurrentSlotIndex > 0)
        {
            _placedCards[card.CurrentSlotIndex] = null;
        }

        card.CurrentSlotIndex = -1;
        RefreshCardPositions();
    }

    // 배치된 2~5번 카드를 모두 보기 영역으로 초기화한다.
    private void ResetAllCards()
    {
        foreach (StationCardDragHandler card in _stationCards)
        {
            card.CurrentSlotIndex = -1;
        }

        for (int index = 1; index < SegmentLength; index++)
        {
            _placedCards[index] = null;
        }

        RefreshCardPositions();
    }

    // 카드 상태에 따라 보기 또는 슬롯 위치로 정렬한다.
    internal void RefreshCardPositions()
    {
        List<StationCardDragHandler> poolCards = _stationCards
            .Where(card => card.CurrentSlotIndex < 0)
            .ToList();

        for (int index = 0; index < poolCards.Count; index++)
        {
            MoveCard(poolCards[index], _stationPool, new Vector2(0.2f + 0.2f * index, 0.42f));
        }

        for (int index = 1; index < SegmentLength; index++)
        {
            StationCardDragHandler card = _placedCards[index];
            _dropSlots[index].SetOccupied(card != null);
            if (card != null)
            {
                MoveCard(card, _dropSlots[index].transform, new Vector2(0.5f, 0.5f));
            }
        }
    }

    // 카드를 보기 또는 노선도 부모 아래의 지정한 앵커 위치로 이동한다.
    private static void MoveCard(StationCardDragHandler card, Transform parent, Vector2 anchor)
    {
        RectTransform rect = (RectTransform)card.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.anchoredPosition = Vector2.zero;
        card.SetBackgroundToDefault();
    }
}

public sealed class StationPoolDropTarget : UIDropPool
{
    // 보기 영역에 미니게임을 연결한다.
    public void Initialize(SubwayRouteMiniGame owner)
    {
        InitializePool(owner);
    }
}
