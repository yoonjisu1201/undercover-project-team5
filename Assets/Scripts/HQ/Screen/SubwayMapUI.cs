using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class SubwayMapUI : MonoBehaviour, IDragHandler, IScrollHandler {
	[Header("=== 실제 조작할 지하철 노선도 사진 ===")] 
	[SerializeField] private RectTransform _subwayMap;
	[Header("=== 뷰포트 크기 확인 ===")] 
	[SerializeField] private RectTransform _viewPort;

	[Header("=== 조작 관련 변수 ===")]
	[SerializeField] [Range(0.5f, 2.0f)] private float _dragSpeed;
	[SerializeField] [Range(0.1f, 0.5f)] private float _wheelSpeed; 
	[SerializeField] [Range(0.5f, 1.0f)] private float _minScale;
	[SerializeField] [Range(2.0f, 4.0f)] private float _maxScale;
	
	public event Action OnWindowClosed;

	private void OnDisable() {
		OnWindowClosed?.Invoke();
	}


	public void OnDrag(PointerEventData eventData) {
		_subwayMap.anchoredPosition += eventData.delta * _dragSpeed;
		ClampPosition();
	}
	
	public void OnScroll(PointerEventData eventData) {
		float nextScale = Mathf.Clamp(
			_subwayMap.localScale.x +
			eventData.scrollDelta.y * _wheelSpeed,
			_minScale,
			_maxScale
		);

		_subwayMap.localScale = Vector3.one * nextScale;
		ClampPosition();
	}
	
	private void ClampPosition()
	{
		Vector2 viewportSize = _viewPort.rect.size;

		Vector2 contentSize = Vector2.Scale(
			_subwayMap.rect.size,
			_subwayMap.localScale
		);

		float maxX = Mathf.Max(
			0f,
			(contentSize.x - viewportSize.x) * 0.5f
		);

		float maxY = Mathf.Max(
			0f,
			(contentSize.y - viewportSize.y) * 0.5f
		);

		Vector2 position = _subwayMap.anchoredPosition;

		position.x = Mathf.Clamp(position.x, -maxX, maxX);
		position.y = Mathf.Clamp(position.y, -maxY, maxY);

		_subwayMap.anchoredPosition = position;
	}
}