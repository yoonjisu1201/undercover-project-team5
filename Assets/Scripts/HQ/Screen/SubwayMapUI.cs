using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SubwayMapUI : MonoBehaviour, IDragHandler, IScrollHandler {
	[Header("=== 지하철 노선도 사진 ===")] 
	[SerializeField] private RectTransform _subwayMap;
	[Header("=== 뷰포트 ===")] 
	[SerializeField] private RectTransform _viewPort;
	[Header("=== 닫기 버튼 ===")]
	[SerializeField] private Button _closeButton;

	[Header("=== 조작 관련 변수 ===")]
	[SerializeField] [Range(0.5f, 2.0f)] private float _dragSpeed;
	[SerializeField] [Range(0.1f, 0.5f)] private float _wheelSpeed; 
	[SerializeField] [Range(0.5f, 1.0f)] private float _minScale;
	[SerializeField] [Range(2.0f, 4.0f)] private float _maxScale;

	private Slider _zoomSlider;
	private Button _zoomInButton;
	private Button _zoomOutButton;
	
	public event Action OnWindowClosed;

	private void Awake() {
		Transform zoomControl = transform.Find("ZoomControl");
		if (zoomControl == null) {
			return;
		}

		_zoomSlider = zoomControl.Find("ZoomSlider")?.GetComponent<Slider>();
		_zoomInButton = zoomControl.Find("ZoomInButton")?.GetComponent<Button>();
		_zoomOutButton = zoomControl.Find("ZoomOutButton")?.GetComponent<Button>();

		_zoomSlider?.onValueChanged.AddListener(OnZoomSliderChanged);
		_zoomInButton?.onClick.AddListener(ZoomIn);
		_zoomOutButton?.onClick.AddListener(ZoomOut);
	}
	
	private void OnEnable() {
		_closeButton.onClick.AddListener(() => gameObject.SetActive(false));
		SyncZoomSlider();
	}

	private void OnDisable() {
		OnWindowClosed?.Invoke();
		_closeButton.onClick.RemoveAllListeners();
	}

	private void OnDestroy() {
		_zoomSlider?.onValueChanged.RemoveListener(OnZoomSliderChanged);
		_zoomInButton?.onClick.RemoveListener(ZoomIn);
		_zoomOutButton?.onClick.RemoveListener(ZoomOut);
	}

	public void OnDrag(PointerEventData eventData) {
		_subwayMap.anchoredPosition += eventData.delta * _dragSpeed;
		ClampPosition();
	}
	
	public void OnScroll(PointerEventData eventData) {
		SetScale(_subwayMap.localScale.x + eventData.scrollDelta.y * _wheelSpeed);
	}

	private void ZoomIn() {
		SetScale(_subwayMap.localScale.x + _wheelSpeed);
	}

	private void ZoomOut() {
		SetScale(_subwayMap.localScale.x - _wheelSpeed);
	}

	private void OnZoomSliderChanged(float normalizedZoom) {
		SetScale(Mathf.Lerp(_minScale, _maxScale, normalizedZoom), false);
	}

	private void SetScale(float scale, bool syncSlider = true) {
		float previousScale = _subwayMap.localScale.x;
		float clampedScale = Mathf.Clamp(scale, _minScale, _maxScale);

		// 지도 자체의 피벗이 아니라 현재 뷰포트 중앙에 보이는 지점을 기준으로 확대·축소합니다.
		if (previousScale > Mathf.Epsilon) {
			float scaleRatio = clampedScale / previousScale;
			_subwayMap.anchoredPosition *= scaleRatio;
		}

		_subwayMap.localScale = Vector3.one * clampedScale;
		ClampPosition();

		if (syncSlider) {
			SyncZoomSlider();
		}
	}

	private void SyncZoomSlider() {
		if (_zoomSlider == null) {
			return;
		}

		float normalizedZoom = Mathf.InverseLerp(_minScale, _maxScale, _subwayMap.localScale.x);
		_zoomSlider.SetValueWithoutNotify(normalizedZoom);
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
