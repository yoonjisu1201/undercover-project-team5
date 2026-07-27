using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 
public sealed partial class SubwayRouteMiniGame
{
    private static readonly Color CardColor = new(0.08f, 0.13f, 0.17f);

    // 선택된 노선 이름과 플레이 방법을 화면 상단에 표시한다.
    private void CreateRouteHeader(Route route, Color routeColor)
    {
        Text routeName = CreateText("RouteName", _routeMap, 32, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetAnchors(routeName.rectTransform, new Vector2(0.1f, 0.83f), new Vector2(0.9f, 0.98f));
        routeName.color = routeColor;
        routeName.text = $"{route.Region}  |  {route.LineName}";

        Text guide = CreateText("Guide", _routeMap, 19, FontStyle.Normal, TextAnchor.MiddleCenter);
        SetAnchors(guide.rectTransform, new Vector2(0.1f, 0.72f), new Vector2(0.9f, 0.84f));
        guide.color = new Color(0.76f, 0.83f, 0.87f);
        guide.text = "1번 역은 공개됩니다. 하단의 2~5번 역 카드를 알맞은 슬롯에 놓으세요.";
    }

    // 노선 선, 원형 역 노드, 고정 1번 역과 드롭 슬롯을 만든다.
    private void CreateRouteDiagram(IReadOnlyList<string> stations, Color routeColor)
    {
        const float firstX = 0.12f;
        const float spacing = 0.19f;
        const float nodeY = 0.59f;

        for (int index = 0; index < SegmentLength - 1; index++)
        {
            Image line = CreateImage($"Line{index}", _routeMap, routeColor);
            line.rectTransform.anchorMin = new Vector2(firstX + spacing * index, nodeY);
            line.rectTransform.anchorMax = new Vector2(firstX + spacing * (index + 1), nodeY);
            line.rectTransform.offsetMin = new Vector2(14f, -5f);
            line.rectTransform.offsetMax = new Vector2(-14f, 5f);
        }

        for (int index = 0; index < SegmentLength; index++)
        {
            float x = firstX + spacing * index;
            CreateStationNode(index, x, nodeY, routeColor);
            if (index == 0)
            {
                CreateFixedFirstStation(x, stations[0], routeColor);
            }
            else
            {
                CreateDropSlot(index, x, routeColor);
            }
        }
    }

    // 번호와 원형 표시를 포함한 역 노드를 만든다.
    private void CreateStationNode(int index, float x, float y, Color routeColor)
    {
        RectTransform node = CreateRect($"Station{index}", _routeMap);
        node.anchorMin = node.anchorMax = new Vector2(x, y);
        node.sizeDelta = new Vector2(150f, 180f);
        CreateStationCircle(node, routeColor);

        Text order = CreateText("Order", node, 20, FontStyle.Bold, TextAnchor.UpperCenter);
        SetAnchors(order.rectTransform, Vector2.zero, new Vector2(1f, 0.3f));
        order.color = Color.white;
        order.text = $"{index + 1}번";
    }

    // 기준이 되는 1번 역을 이동할 수 없는 공개 카드로 만든다.
    private void CreateFixedFirstStation(float x, string stationName, Color routeColor)
    {
        Image card = CreateImage("FixedFirstStation", _routeMap, Color.Lerp(CardColor, routeColor, 0.35f));
        SetPointRect(card.rectTransform, new Vector2(x, 0.46f), new Vector2(140f, 66f));
        card.raycastTarget = false;
        CreateCardLabel(card.transform, stationName, 18);
    }

    // 2~5번 역 카드를 받을 드롭 슬롯을 만든다.
    private void CreateDropSlot(int index, float x, Color routeColor)
    {
        GameObject slotObject = new(
            $"StationSlot_{index + 1}",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(StationDropSlot));
        slotObject.transform.SetParent(_routeMap, false);

        RectTransform rect = slotObject.GetComponent<RectTransform>();
        SetPointRect(rect, new Vector2(x, 0.46f), new Vector2(140f, 66f));
        Image image = slotObject.GetComponent<Image>();
        image.color = new Color(routeColor.r, routeColor.g, routeColor.b, 0.2f);

        Text placeholder = CreateCardLabel(slotObject.transform, $"{index + 1}번 역", 16);
        placeholder.color = new Color(1f, 1f, 1f, 0.5f);
        StationDropSlot slot = slotObject.GetComponent<StationDropSlot>();
        slot.Initialize(this, index, rect, image, placeholder);
        _dropSlots[index] = slot;
    }

    // 노선 색 외곽과 흰색 중심으로 구성된 역 원을 만든다.
    private void CreateStationCircle(RectTransform node, Color routeColor)
    {
        StationCircleGraphic outer = CreateCircle("StationCircle", node, routeColor, 42f);
        CreateCircle("StationCenter", outer.transform, Color.white, 26f);
    }

    // 2~5번 역 이름을 섞어 드래그 가능한 보기 카드로 만든다.
    private void CreateStationCards(IReadOnlyList<string> stations, Color routeColor)
    {
        _answers.AddRange(stations.Select(NormalizeAnswer));
        List<string> shuffled = stations.Skip(1).OrderBy(_ => Random.value).ToList();
        if (shuffled.SequenceEqual(stations.Skip(1)))
        {
            (shuffled[0], shuffled[1]) = (shuffled[1], shuffled[0]);
        }

        foreach (string stationName in shuffled)
        {
            GameObject cardObject = new(
                "StationCard",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(CanvasGroup), typeof(StationCardDragHandler));
            cardObject.transform.SetParent(_routeMap, false);
            cardObject.GetComponent<RectTransform>().sizeDelta = new Vector2(132f, 64f);
            cardObject.GetComponent<Image>().color = CardColor;
            CreateCardLabel(cardObject.transform, stationName, 18);

            StationCardDragHandler card = cardObject.GetComponent<StationCardDragHandler>();
            card.Initialize(this, stationName, routeColor);
            _stationCards.Add(card);
        }

        CreateStationPool(routeColor);
        RefreshCardPositions();
    }

    // 카드 보기 영역과 액션 버튼을 만든다.
    private void CreateStationPool(Color routeColor)
    {
        Image pool = CreateImage("StationPool", _routeMap, new Color(0.025f, 0.055f, 0.075f, 0.94f));
        SetAnchors(pool.rectTransform, new Vector2(0.04f, 0.10f), new Vector2(0.96f, 0.31f));
        pool.gameObject.AddComponent<StationPoolDropTarget>().Initialize(this, pool);

        Text title = CreateText("PoolTitle", pool.transform, 15, FontStyle.Bold, TextAnchor.UpperLeft);
        SetAnchors(title.rectTransform, new Vector2(0.02f, 0.7f), new Vector2(0.7f, 0.96f));
        title.color = routeColor;
        title.text = "역 이름 보기  ·  카드를 이곳에 놓으면 되돌아옵니다";

        CreateButton("ResetCardsButton", "다시 하기", new Vector2(0.38f, 0.35f), new Vector2(200f, 48f), 16, routeColor, ResetAllCards);
        pool.transform.SetAsFirstSibling();
    }

    // 현재 카드 순서를 검사하는 확인 버튼을 만든다.
    private void CreateCheckButton(Color routeColor)
    {
        CreateButton("CheckAnswer", "노선 복구 확인", new Vector2(0.62f, 0.35f), new Vector2(240f, 48f), 20, routeColor, CheckAnswers);
    }

    // 공통 스타일의 액션 버튼을 만든다.
    private void CreateButton(
        string name, string label, Vector2 anchor, Vector2 size,
        int fontSize, Color color, UnityAction onClick)
    {
        GameObject buttonObject = new(
            name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(_routeMap, false);
        SetPointRect(buttonObject.GetComponent<RectTransform>(), anchor, size);
        buttonObject.GetComponent<Image>().color = color;
        buttonObject.GetComponent<Button>().onClick.AddListener(onClick);
        Text text = CreateCardLabel(buttonObject.transform, label, fontSize);
        text.color = new Color(0.03f, 0.06f, 0.08f);
    }

    // 카드 안에 공통 스타일의 역 이름 텍스트를 만든다.
    private Text CreateCardLabel(Transform parent, string value, int fontSize)
    {
        Text text = CreateText("Label", parent, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform, 5f);
        text.color = Color.white;
        text.text = value;
        return text;
    }

    // 공통 글꼴 설정을 적용한 UI 텍스트를 만든다.
    private Text CreateText(string name, Transform parent, int size, FontStyle style, TextAnchor alignment)
    {
        GameObject textObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = _font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 10;
        text.resizeTextMaxSize = size;
        return text;
    }

    // 지정한 색상의 UI 이미지를 만든다.
    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject imageObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    // 지정한 크기와 색상의 원형 UI 그래픽을 만든다.
    private static StationCircleGraphic CreateCircle(string name, Transform parent, Color color, float size)
    {
        GameObject circleObject = new(
            name, typeof(RectTransform), typeof(CanvasRenderer), typeof(StationCircleGraphic));
        circleObject.transform.SetParent(parent, false);
        StationCircleGraphic circle = circleObject.GetComponent<StationCircleGraphic>();
        circle.color = color;
        circle.raycastTarget = false;
        SetPointRect(circle.rectTransform, Vector2.one * 0.5f, Vector2.one * size);
        return circle;
    }

    // 부모 아래에 빈 UI RectTransform을 만든다.
    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject rectObject = new(name, typeof(RectTransform));
        rectObject.transform.SetParent(parent, false);
        return rectObject.GetComponent<RectTransform>();
    }

    // RectTransform을 한 지점 앵커에 지정한 크기로 배치한다.
    private static void SetPointRect(RectTransform rect, Vector2 anchor, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
    }

    // RectTransform의 앵커 영역과 오프셋을 설정한다.
    private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    // RectTransform을 부모 영역에 여백만 남기고 맞춘다.
    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * inset;
        rect.offsetMax = Vector2.one * -inset;
    }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class StationCircleGraphic : MaskableGraphic
{
    private const int SegmentCount = 32;

    // 사각형 UI 영역 안에 원형 메시를 생성한다.
    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect rect = GetPixelAdjustedRect();
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
        Vector2 center = rect.center;

        vertexHelper.AddVert(center, color, Vector2.one * 0.5f);
        for (int index = 0; index <= SegmentCount; index++)
        {
            float angle = index * Mathf.PI * 2f / SegmentCount;
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            vertexHelper.AddVert(
                center + direction * radius,
                color,
                direction * 0.5f + Vector2.one * 0.5f);
        }

        for (int index = 0; index < SegmentCount; index++)
        {
            vertexHelper.AddTriangle(0, index + 1, index + 2);
        }
    }
}
