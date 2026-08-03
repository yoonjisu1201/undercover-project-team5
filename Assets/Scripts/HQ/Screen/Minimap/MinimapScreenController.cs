using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MinimapScreenController : ScreenBase, IDragHandler, IScrollHandler {
	[Header("=== 조작할 미니맵 카메라 ===")]
	[SerializeField] private Camera _minimapCamera;

	[Header("=== 드래그 이동 감도 ===")]
	[SerializeField, Min(0f)] private float _dragSensitivity = 0.002f;

	[Header("=== 확대/축소 최소, 최대 범위 및 감도 ===")]
	[SerializeField, Min(0.01f)] private float _minOrthographicSize = 40f;
	[SerializeField, Min(0.01f)] private float _maxOrthographicSize = 200f;
	[SerializeField, Min(0f)] private float _zoomSensitivity = 10f;

	[Header("=== 카메라 이동 가능 범위 (XZ 평면 좌표) ===")]
	[SerializeField] private Vector2 _panBoundsMin;
	[SerializeField] private Vector2 _panBoundsMax;

	private Vector3 _defaultPosition;
	private float _defaultOrthographicSize;
	private Slider _zoomSlider;
	private Button _zoomInButton;
	private Button _zoomOutButton;

	private void Awake() {
		_defaultPosition = _minimapCamera.transform.position;
		_defaultOrthographicSize = _minimapCamera.orthographicSize;

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
		SyncZoomSlider();
	}

	private void OnDestroy() {
		_zoomSlider?.onValueChanged.RemoveListener(OnZoomSliderChanged);
		_zoomInButton?.onClick.RemoveListener(ZoomIn);
		_zoomOutButton?.onClick.RemoveListener(ZoomOut);
	}

	// 지도 화면을 열 때마다 카메라 위치와 확대 비율을 기본값으로 되돌림
	public override void ActivateScreen() {
		base.ActivateScreen();
		// _minimapCamera.transform.position = _defaultPosition;
		// _minimapCamera.orthographicSize = _defaultOrthographicSize;
		SyncZoomSlider();
	}

	public void OnDrag(PointerEventData eventData) {
		Transform cameraTransform = _minimapCamera.transform;

		// 드래그 방향으로 지도가 따라오도록 카메라는 반대 방향으로 이동
		float scale = _dragSensitivity * _minimapCamera.orthographicSize;
		Vector3 movement = (-cameraTransform.right * eventData.delta.x - cameraTransform.up * eventData.delta.y) * scale;

		cameraTransform.position = ClampToPanBounds(cameraTransform.position + movement);
	}

	public void OnScroll(PointerEventData eventData) {
		SetOrthographicSize(_minimapCamera.orthographicSize - eventData.scrollDelta.y * _zoomSensitivity);
	}

	private void ZoomIn() {
		SetOrthographicSize(_minimapCamera.orthographicSize - _zoomSensitivity);
	}

	private void ZoomOut() {
		SetOrthographicSize(_minimapCamera.orthographicSize + _zoomSensitivity);
	}

	private void OnZoomSliderChanged(float normalizedZoom) {
		SetOrthographicSize(Mathf.Lerp(_maxOrthographicSize, _minOrthographicSize, normalizedZoom), false);
	}

	private void SetOrthographicSize(float size, bool syncSlider = true) {
		_minimapCamera.orthographicSize = Mathf.Clamp(size, _minOrthographicSize, _maxOrthographicSize);

		if (syncSlider) {
			SyncZoomSlider();
		}
	}

	private void SyncZoomSlider() {
		if (_zoomSlider == null) {
			return;
		}

		float normalizedZoom = Mathf.InverseLerp(
			_maxOrthographicSize,
			_minOrthographicSize,
			_minimapCamera.orthographicSize
		);
		_zoomSlider.SetValueWithoutNotify(normalizedZoom);
	}

	private Vector3 ClampToPanBounds(Vector3 position) {
		position.x = Mathf.Clamp(position.x, _panBoundsMin.x, _panBoundsMax.x);
		position.z = Mathf.Clamp(position.z, _panBoundsMin.y, _panBoundsMax.y);
		return position;
	}
}
