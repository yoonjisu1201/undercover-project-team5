using System;
using UnityEngine;
using UnityEngine.EventSystems;

// 다이얼 노브를 마우스로 잡고 돌리는 입력만 담당한다. 실제 주파수 계산은 FrequencyDialUI가 한다.
// 노브 중심을 기준으로 포인터가 회전한 각도를 그대로 넘기므로, 손목을 돌리듯 돌릴 수 있다.
public sealed class FrequencyDialKnob : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    // 포인터가 이만큼 회전할 때마다 주파수 한 칸이 움직인다.
    // 작을수록 한 바퀴에 더 많이 돌아간다. 2도면 한 바퀴(360도)에 180칸 = 9MHz다.
    public const float DegreesPerStep = 2f;

    private RectTransform _rect;
    private float _previousAngle;

    // 드래그로 회전한 각도(시계 방향이 +)를 알린다.
    public event Action<float> OnRotated;

    // 노브를 잡을 수 없는 상태(안테나 미배치·완료)에서는 회전을 무시한다.
    public bool Interactable { get; set; } = true;

    private void Awake()
    {
        _rect = (RectTransform)transform;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _previousAngle = AngleFromCenter(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!Interactable)
        {
            return;
        }

        float angle = AngleFromCenter(eventData);
        // 0/360 경계를 넘어갈 때 각도가 튀지 않도록 최단 회전량으로 환산한다.
        float delta = Mathf.DeltaAngle(angle, _previousAngle);
        _previousAngle = angle;

        if (!Mathf.Approximately(delta, 0f))
        {
            OnRotated?.Invoke(delta);
        }
    }

    private float AngleFromCenter(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);
        return Mathf.Atan2(localPoint.y, localPoint.x) * Mathf.Rad2Deg;
    }
}
