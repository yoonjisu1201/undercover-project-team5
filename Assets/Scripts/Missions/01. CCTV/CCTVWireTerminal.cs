using UnityEngine;
using UnityEngine.EventSystems;

// 시작 단자의 포인터 드래그 이벤트를 CCTV 수리 게임에 전달합니다.
public sealed class CCTVWireTerminal : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private CCTVSignalRepairGame _game;
    private int _wireIndex;

    // 시작 단자에 제어할 게임과 전선 번호를 연결한다.
    public void Configure(CCTVSignalRepairGame game, int wireIndex)
    {
        _game = game;
        _wireIndex = wireIndex;
    }

    // 포인터 드래그 시작을 미션에 전달한다.
    public void OnBeginDrag(PointerEventData eventData) => _game?.BeginWireDrag(_wireIndex, eventData);

    // 포인터 이동을 미션에 전달해 전선 위치를 갱신한다.
    public void OnDrag(PointerEventData eventData) => _game?.ContinueWireDrag(_wireIndex, eventData);

    // 포인터 드래그 종료를 미션에 전달해 연결 여부를 판정한다.
    public void OnEndDrag(PointerEventData eventData) => _game?.EndWireDrag(_wireIndex, eventData);
}
