using System;
using UnityEngine;
using UnityEngine.EventSystems;

// 미니맵 CCTV 마커의 클릭 입력을 받는다.
// 지도를 끌다가 마커 위에서 손을 뗀 경우를 클릭으로 오판하지 않도록, 누른 위치와 뗀 위치를 비교한다.
public class CctvMapMarker : MonoBehaviour, IPointerDownHandler, IPointerClickHandler {
	[Tooltip("이 픽셀 이상 포인터가 움직이면 드래그로 보고 클릭을 무시한다.")]
	[SerializeField] private float _dragThresholdPixels = 10f;

	public event Action Clicked;

	private Vector2 _pressedPosition;

	public void OnPointerDown(PointerEventData eventData) {
		_pressedPosition = eventData.position;
	}

	public void OnPointerClick(PointerEventData eventData) {
		if ((eventData.position - _pressedPosition).sqrMagnitude > _dragThresholdPixels * _dragThresholdPixels) {
			return;
		}

		Clicked?.Invoke();
	}
}
