using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UI;

// 지하철 역 카드의 드래그와 표시 상태를 관리합니다.
[Preserve]
public sealed class StationCardDragHandler : UIDraggableItem
{
    private Image _background;
    private Color _defaultColor;

    public string StationName { get; private set; }

    public void Initialize(SubwayRouteMission owner, string stationName, Color routeColor)
    {
        StationName = stationName;
        InitializeDrag(owner, stationName);
        _background = GetComponent<Image>();
        _defaultColor = Color.Lerp(_background.color, routeColor, 0.18f);
        _background.color = _defaultColor;
    }

    public void SetBackgroundColor(Color color) => _background.color = color;

    public void SetBackgroundToDefault() => _background.color = _defaultColor;
}
