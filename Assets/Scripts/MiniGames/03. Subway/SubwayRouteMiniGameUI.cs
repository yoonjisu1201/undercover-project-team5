using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// 프리팹에 배치된 UI를 문제 데이터에 맞게 갱신하는 코드
public sealed partial class SubwayRouteMiniGame
{
    private static readonly Color CardColor = new(0.08f, 0.13f, 0.17f);

    // 선택된 노선 이름과 플레이 방법을 표시한다.
    private void SetRouteHeader(SubwayRoute route, Color routeColor)
    {
        Text routeName = FindChild(_routeMap, "PreviewLineName").GetComponent<Text>();
        routeName.gameObject.SetActive(true);
        routeName.color = routeColor;
        routeName.text = $"{route.Region}  |  {route.LineName}";

        Text guide = FindChild(_routeMap, "PreviewGuide").GetComponent<Text>();
        guide.gameObject.SetActive(true);
        guide.text = "1번 역은 공개됩니다. 나머지 역 카드를 알맞은 슬롯에 놓으세요.";
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
            Text placeholder = FindChild(slotTransform, "Placeholder").GetComponent<Text>();

            // 첫 번째 슬롯은 공개되므로 색상을 밝게 하고, 나머지 슬롯은 반투명하게 한다.
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
        _answers.AddRange(stations.Select(NormalizeAnswer));
        List<string> shuffled = stations.Skip(1).OrderBy(_ => Random.value).ToList();   // 첫 역은 공개되므로 2~5번 역만 섞는다.

        Transform preview = FindChild(_routeMap, "StaticDragDropPreview");  // 보기 영역의 카드 4개를 드래그 가능한 카드로 초기화
        Transform pool = FindChild(preview, "StationPool");
        _stationPool = pool;

        pool.GetComponent<Image>().raycastTarget = true;
        StationPoolDropTarget poolTarget = pool.GetComponent<StationPoolDropTarget>() ?? pool.gameObject.AddComponent<StationPoolDropTarget>();
        poolTarget.Initialize(this);
        FindChild(pool, "PoolTitle").GetComponent<Text>().color = routeColor;   // 보기 영역 제목의 글자 색을 현재 노선 색으로 변경

        for (int index = 0; index < shuffled.Count; index++)    // 카드 4장에 섞인 역 이름과 드래그 기능을 연결한다.
        {
            Transform cardTransform = FindChild(_routeMap, $"StationCard_{index + 2}"); // 0 -> 2번 카드, 1 -> 3번 카드, 2 -> 4번 카드, 3 -> 5번 카드
            cardTransform.gameObject.SetActive(true);
            cardTransform.SetParent(pool, false);

            // 카드의 배경 이미지를 가져와 기본 카드 색상으로 초기화
            Image image = cardTransform.GetComponent<Image>();
            image.color = CardColor;
            image.raycastTarget = true;

            FindChild(cardTransform, "StationName").GetComponent<Text>().text = shuffled[index];

            // 카드에 CanvasGroup 컴포넌트가 없으면 추가한다. (드래그 시 투명도 조절을 위해 필요)
            if (cardTransform.GetComponent<CanvasGroup>() == null)
            {
                cardTransform.gameObject.AddComponent<CanvasGroup>();
            }

            // 드래그하는 동안 카드가 슬롯의 마우스 입력을 막지 않도록 blocksRaycasts를 변경할 때 사용
            StationCardDragHandler card = cardTransform.GetComponent<StationCardDragHandler>() ?? cardTransform.gameObject.AddComponent<StationCardDragHandler>();
            card.Initialize(this, shuffled[index], routeColor);
            card.CurrentSlotIndex = -1; // 카드가 보기에 있는 상태(역 슬롯에 들어가지 않음)
            _stationCards.Add(card);
        }
    }
}
