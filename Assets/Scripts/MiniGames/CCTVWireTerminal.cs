using UnityEngine;
using UnityEngine.EventSystems;

public sealed class CCTVWireTerminal : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private CCTVSignalRepairGame _game;
    private int _wireIndex;

    public void Configure(CCTVSignalRepairGame game, int wireIndex)
    {
        _game = game;
        _wireIndex = wireIndex;
    }

    public void OnBeginDrag(PointerEventData eventData) => _game?.BeginWireDrag(_wireIndex, eventData);
    public void OnDrag(PointerEventData eventData) => _game?.ContinueWireDrag(_wireIndex, eventData);
    public void OnEndDrag(PointerEventData eventData) => _game?.EndWireDrag(_wireIndex, eventData);
}
