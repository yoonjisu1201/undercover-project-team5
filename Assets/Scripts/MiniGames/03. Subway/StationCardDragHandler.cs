using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 카드 드래그 담당 클래스
public sealed class StationCardDragHandler : UIDraggableItem
{
    private Image _background;
    private Color _defaultColor;

    public string StationName { get; private set; }

    // 카드에 게임 데이터와 기본 UI 상태를 연결한다.
    public void Initialize(SubwayRouteMiniGame owner, string stationName, Color routeColor)
    {
        StationName = stationName;
        InitializeDrag(owner, stationName);
        _background = GetComponent<Image>();
        _defaultColor = Color.Lerp(_background.color, routeColor, 0.18f);
        _background.color = _defaultColor;
    }

    // 정답 판정 결과 색상을 카드에 적용한다.
    public void SetBackgroundColor(Color color) => _background.color = color;

    // 카드를 기본 노선 색상으로 되돌린다.
    public void SetBackgroundToDefault() => _background.color = _defaultColor;

}

// 카드 드롭 담당 클래스
public sealed class StationDropSlot : UIDropSlot
{
    // 드롭 슬롯에 순서와 표시 요소를 연결한다.
    public void Initialize(
        SubwayRouteMiniGame owner,
        int slotIndex,
        Image background,
        TMP_Text placeholder)
    {
        InitializeSlot(owner, slotIndex, background, placeholder);
    }
}
