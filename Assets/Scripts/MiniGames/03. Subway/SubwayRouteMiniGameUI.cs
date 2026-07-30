using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 프리팹에 배치된 UI를 문제 데이터에 맞게 갱신한다.
public sealed partial class SubwayRouteMiniGame
{
    private static readonly Color CardColor = new(0.08f, 0.13f, 0.17f);

    // 선택된 노선 이름과 플레이 방법을 표시한다.
    private void SetRouteHeader(SubwayRoute route, Color routeColor)
    {
        TMP_Text routeName = FindChild(_routeMap, "PreviewLineName").GetComponent<TMP_Text>();
        routeName.gameObject.SetActive(true);
        routeName.color = routeColor;
        routeName.text = $"{route.Region}  |  {route.LineName}";
    }

    // 프리팹의 노선과 원형 역 표시 색상을 현재 노선 색으로 바꾼다.
    private void SetRouteGraphic(Color routeColor)
    {
        Transform preview = FindChild(_routeMap, "StaticDragDropPreview");
        preview.gameObject.SetActive(true);

        for (int index = 1; index < SegmentLength; index++)
        {
            FindChild(preview, $"PreviewLine_{index}").GetComponent<Image>().color = routeColor;
        }

        for (int index = 1; index <= SegmentLength; index++)
        {
            Transform node = FindChild(preview, $"PreviewNode_{index}");
            node.GetComponent<Image>().color = routeColor;
            FindChild(node, "WhiteCenter").GetComponent<Image>().color = Color.white;
        }
    }

    // 첫 역은 공개하고 2~5번 역은 드롭 가능한 프리팹 슬롯으로 연결한다.
    private void SetSlots(IReadOnlyList<string> stations, Color routeColor)
    {
        Transform preview = FindChild(_routeMap, "StaticDragDropPreview");

        for (int index = 0; index < SegmentLength; index++)
        {
            Transform slotTransform = FindChild(preview, $"StationSlot_{index + 1}");
            Image image = slotTransform.GetComponent<Image>();
            TMP_Text placeholder = FindChild(slotTransform, "Placeholder").GetComponent<TMP_Text>();

            // 첫 번째 슬롯은 공개된 역이므로 색상을 연하게 하고 드롭을 막는다.
            image.color = index == 0 ? Color.Lerp(CardColor, routeColor, 0.35f) : new Color(routeColor.r, routeColor.g, routeColor.b, 0.2f);

            image.raycastTarget = index > 0;
            placeholder.text = index == 0 ? stations[0] : $"{index + 1}번 역";

            if (index == 0)
            {
                continue;
            }

            StationDropSlot slot = slotTransform.GetComponent<StationDropSlot>()
                ?? slotTransform.gameObject.AddComponent<StationDropSlot>();
            slot.Initialize(this, index, image, placeholder);
            _dropSlots[index] = slot;
        }
    }

    // 프리팹 카드 4장에 섞인 2~5번 역 이름과 드래그 기능을 연결한다.
    private void SetCards(IReadOnlyList<string> stations, Color routeColor)
    {
        _answers.AddRange(stations.Select(SubwayRouteData.NormalizeAnswer));
        List<string> shuffled = stations.Skip(1).OrderBy(_ => Random.value).ToList();

        Transform preview = FindChild(_routeMap, "StaticDragDropPreview");
        Transform pool = FindChild(preview, "StationPool");
        _stationPool = pool;

        pool.GetComponent<Image>().raycastTarget = true;
        StationPoolDropTarget poolTarget = pool.GetComponent<StationPoolDropTarget>()
            ?? pool.gameObject.AddComponent<StationPoolDropTarget>();
        poolTarget.Initialize(this);
        FindChild(pool, "PoolTitle").GetComponent<TMP_Text>().color = routeColor;

        // 카드 4장을 섞어 배치한다.
        for (int index = 0; index < shuffled.Count; index++)
        {
            Transform cardTransform = FindChild(_routeMap, $"StationCard_{index + 2}");
            cardTransform.gameObject.SetActive(true);
            cardTransform.SetParent(pool, false);

            Image image = cardTransform.GetComponent<Image>();
            image.color = CardColor;
            image.raycastTarget = true;
            FindChild(cardTransform, "StationName").GetComponent<TMP_Text>().text = shuffled[index];

            if (cardTransform.GetComponent<CanvasGroup>() == null)
            {
                cardTransform.gameObject.AddComponent<CanvasGroup>();
            }

            StationCardDragHandler card = cardTransform.GetComponent<StationCardDragHandler>()
                ?? cardTransform.gameObject.AddComponent<StationCardDragHandler>();
            card.Initialize(this, shuffled[index], routeColor);
            card.CurrentSlotIndex = -1;
            _stationCards.Add(card);
        }
    }
}
