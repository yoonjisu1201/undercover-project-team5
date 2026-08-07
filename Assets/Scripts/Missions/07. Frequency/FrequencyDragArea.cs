using System;
using UnityEngine;
using UnityEngine.EventSystems;

// 롤러를 좌우로 끌 때 움직인 거리를 알린다. 슬라이더는 유니티 내장 Slider를 쓰므로 여기서 다루지 않는다.
public sealed class FrequencyDragArea : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    private RectTransform _rect;
    private float _previousLocalX;

    // 끌 수 없는 상태(안테나 미배치·완료)에서는 입력을 무시한다.
    public bool Interactable { get; set; } = true;

    // 직전 위치에서 움직인 x 거리(픽셀)를 알린다. 오른쪽으로 끌면 +다.
    public event Action<float> OnDelta;

    private void Awake()
    {
        _rect = (RectTransform)transform;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _previousLocalX = LocalX(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!Interactable)
        {
            return;
        }

        float localX = LocalX(eventData);
        float delta = localX - _previousLocalX;
        _previousLocalX = localX;

        if (!Mathf.Approximately(delta, 0f))
        {
            OnDelta?.Invoke(delta);
        }
    }

    private float LocalX(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);
        return localPoint.x;
    }
}
