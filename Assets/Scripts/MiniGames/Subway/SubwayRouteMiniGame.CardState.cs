using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed partial class SubwayRouteMiniGame
{
    // 카드를 대상 슬롯에 배치하고 기존 카드가 있으면 교환한다.
    internal void PlaceCard(StationCardDragHandler card, int slotIndex)
    {
        if (slotIndex <= 0 || slotIndex >= SegmentLength)
        {
            RefreshCardPositions();
            return;
        }

        int sourceIndex = card.CurrentSlotIndex;
        StationCardDragHandler displacedCard = _placedCards[slotIndex];

        if (sourceIndex >= 0)
        {
            _placedCards[sourceIndex] = displacedCard;
            if (displacedCard != null)
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
            MoveCard(poolCards[index], new Vector2(0.215f + 0.19f * index, 0.19f));
        }

        for (int index = 1; index < SegmentLength; index++)
        {
            StationCardDragHandler card = _placedCards[index];
            _dropSlots[index].SetOccupied(card != null);
            if (card != null)
            {
                MoveCard(card, _dropSlots[index].Anchor);
            }
        }
    }

    // 카드를 지정한 앵커 위치로 이동하고 기본 색상을 복원한다.
    private static void MoveCard(StationCardDragHandler card, Vector2 anchor)
    {
        RectTransform rect = (RectTransform)card.transform;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.anchoredPosition = Vector2.zero;
        card.SetBackgroundToDefault();
    }
}

public sealed class StationPoolDropTarget : MonoBehaviour, IDropHandler
{
    private SubwayRouteMiniGame _owner;

    // 보기 영역에 미니게임을 연결한다.
    public void Initialize(SubwayRouteMiniGame owner)
    {
        _owner = owner;
    }

    // 드롭된 카드를 노선 슬롯에서 보기 영역으로 되돌린다.
    public void OnDrop(PointerEventData eventData)
    {
        StationCardDragHandler card = eventData.pointerDrag?.GetComponent<StationCardDragHandler>();
        if (card != null)
        {
            _owner.ReturnCardToPool(card);
        }
    }
}
