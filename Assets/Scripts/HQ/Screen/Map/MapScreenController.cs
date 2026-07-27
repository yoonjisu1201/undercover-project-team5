using UnityEngine;
using UnityEngine.EventSystems;

public class MapScreenController : ScreenBase, IDragHandler, IScrollHandler {
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

	private void Awake() {
		_defaultPosition = _minimapCamera.transform.position;
		_defaultOrthographicSize = _minimapCamera.orthographicSize;
	}

	// 지도 화면을 열 때마다 카메라 위치와 확대 비율을 기본값으로 되돌림
	public override void ActivateScreen() {
		base.ActivateScreen();
		// _minimapCamera.transform.position = _defaultPosition;
		// _minimapCamera.orthographicSize = _defaultOrthographicSize;
	}

	public void OnDrag(PointerEventData eventData) {
		Transform cameraTransform = _minimapCamera.transform;

		// 드래그 방향으로 지도가 따라오도록 카메라는 반대 방향으로 이동
		float scale = _dragSensitivity * _minimapCamera.orthographicSize;
		Vector3 movement = (-cameraTransform.right * eventData.delta.x - cameraTransform.up * eventData.delta.y) * scale;

		cameraTransform.position = ClampToPanBounds(cameraTransform.position + movement);
	}

	public void OnScroll(PointerEventData eventData) {
		float size = _minimapCamera.orthographicSize - eventData.scrollDelta.y * _zoomSensitivity;
		_minimapCamera.orthographicSize = Mathf.Clamp(size, _minOrthographicSize, _maxOrthographicSize);
	}

	private Vector3 ClampToPanBounds(Vector3 position) {
		position.x = Mathf.Clamp(position.x, _panBoundsMin.x, _panBoundsMax.x);
		position.z = Mathf.Clamp(position.z, _panBoundsMin.y, _panBoundsMax.y);
		return position;
	}
}
