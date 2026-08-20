using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 활성화된 맵 구역의 미니맵 스프라이트를 띄우고, 월드 좌표를 그 스프라이트 위 좌표로 변환해준다.
// 예전처럼 렌더 텍스처를 쓰는 미니맵 카메라는 사용하지 않는다.
public class MinimapScreenController : ScreenBase, IDragHandler, IScrollHandler {
	[Serializable]
	private struct RegionMap {
		public RegionId RegionId;
		public Sprite Sprite;
	}

	[Header("=== 미니맵 스프라이트를 그릴 Image ===")]
	[SerializeField] private Image _mapImage;

	[Header("=== 구역별 미니맵 스프라이트 (E 구역은 아직 없음) ===")]
	[SerializeField] private RegionMap[] _regionMaps = Array.Empty<RegionMap>();

	[Header("=== 활성 구역을 알려줄 컨트롤러 ===")]
	[SerializeField] private MapRegionController _regionController;

	[Header("=== 드래그 이동 감도 ===")]
	[SerializeField, Min(0f)] private float _dragSensitivity = 1f;

	[Header("=== 확대/축소 최소, 최대 배율 및 감도 ===")]
	[SerializeField, Min(0.01f)] private float _minZoom = 1f;
	[SerializeField, Min(0.01f)] private float _maxZoom = 4f;
	[SerializeField, Min(0f)] private float _zoomSensitivity = 0.25f;

	private Slider _zoomSlider;
	private Button _zoomInButton;
	private Button _zoomOutButton;

	private MapRegion _activeRegion;
	private float _zoom = 1f;

	// 미니맵 위에 아이콘을 얹는 쪽에서 이 RectTransform을 기준 좌표계로 쓴다.
	public RectTransform MapRect => _mapImage != null ? _mapImage.rectTransform : null;

	public override void Initialize() {
		Transform zoomControl = transform.Find("ZoomControl");
		if (zoomControl != null) {
			_zoomSlider = zoomControl.Find("ZoomSlider")?.GetComponent<Slider>();
			_zoomInButton = zoomControl.Find("ZoomInButton")?.GetComponent<Button>();
			_zoomOutButton = zoomControl.Find("ZoomOutButton")?.GetComponent<Button>();

			_zoomSlider?.onValueChanged.AddListener(OnZoomSliderChanged);
			_zoomInButton?.onClick.AddListener(ZoomIn);
			_zoomOutButton?.onClick.AddListener(ZoomOut);
		}

		if (_regionController != null) {
			_regionController.ActiveRegionChanged += HandleActiveRegionChanged;
			HandleActiveRegionChanged(_regionController.ActiveRegion);
		}

		SetZoom(_minZoom);
	}

	private void OnDestroy() {
		_zoomSlider?.onValueChanged.RemoveListener(OnZoomSliderChanged);
		_zoomInButton?.onClick.RemoveListener(ZoomIn);
		_zoomOutButton?.onClick.RemoveListener(ZoomOut);

		if (_regionController != null) {
			_regionController.ActiveRegionChanged -= HandleActiveRegionChanged;
		}
	}

	public override void ActivateScreen() {
		base.ActivateScreen();
		SyncZoomSlider();
	}

	// 활성 구역이 바뀌면 해당 구역의 스프라이트로 갈아끼우고 시야를 초기화한다.
	private void HandleActiveRegionChanged(MapRegion region) {
		_activeRegion = region;

		Sprite sprite = region != null ? FindSprite(region.RegionId) : null;
		if (_mapImage != null) {
			_mapImage.sprite = sprite;
			_mapImage.enabled = sprite != null;
			_mapImage.rectTransform.anchoredPosition = Vector2.zero;
		}

		if (region != null && sprite == null) {
			Debug.LogWarning($"[MinimapScreenController] '{region.RegionId}' 구역의 미니맵 스프라이트가 등록되지 않았습니다.", this);
		}

		SetZoom(_minZoom);
	}

	private Sprite FindSprite(RegionId regionId) {
		foreach (RegionMap map in _regionMaps) {
			if (map.RegionId == regionId) {
				return map.Sprite;
			}
		}

		return null;
	}

	// 월드 좌표를 미니맵 Image 기준 anchoredPosition으로 변환한다.
	// 활성 구역의 Box Collider 범위를 스프라이트 전체 영역에 그대로 대응시킨다.
	public bool TryProjectToMap(Vector3 worldPosition, out Vector2 anchoredPosition) {
		anchoredPosition = Vector2.zero;

		if (_activeRegion == null || _activeRegion.Bounds == null || _mapImage == null || _mapImage.sprite == null) {
			return false;
		}

		Bounds bounds = _activeRegion.Bounds.bounds;
		if (bounds.size.x <= 0f || bounds.size.z <= 0f) {
			return false;
		}

		float normalizedX = Mathf.InverseLerp(bounds.min.x, bounds.max.x, worldPosition.x);
		float normalizedY = Mathf.InverseLerp(bounds.min.z, bounds.max.z, worldPosition.z);

		// 구역 밖(다른 구역이나 지하)에 있는 대상은 미니맵에 올리지 않는다.
		if (normalizedX < 0f || normalizedX > 1f || normalizedY < 0f || normalizedY > 1f) {
			return false;
		}

		Rect rect = _mapImage.rectTransform.rect;
		anchoredPosition = new Vector2(
			(normalizedX - 0.5f) * rect.width,
			(normalizedY - 0.5f) * rect.height);
		return true;
	}

	public void OnDrag(PointerEventData eventData) {
		if (_mapImage == null) {
			return;
		}

		_mapImage.rectTransform.anchoredPosition = ClampToViewport(
			_mapImage.rectTransform.anchoredPosition + eventData.delta * _dragSensitivity);
	}

	public void OnScroll(PointerEventData eventData) {
		SetZoom(_zoom + eventData.scrollDelta.y * _zoomSensitivity);
	}

	private void ZoomIn() {
		SetZoom(_zoom + _zoomSensitivity);
	}

	private void ZoomOut() {
		SetZoom(_zoom - _zoomSensitivity);
	}

	private void OnZoomSliderChanged(float normalizedZoom) {
		SetZoom(Mathf.Lerp(_minZoom, _maxZoom, normalizedZoom), false);
	}

	private void SetZoom(float zoom, bool syncSlider = true) {
		_zoom = Mathf.Clamp(zoom, _minZoom, _maxZoom);

		if (_mapImage != null) {
			_mapImage.rectTransform.localScale = Vector3.one * _zoom;
			_mapImage.rectTransform.anchoredPosition = ClampToViewport(_mapImage.rectTransform.anchoredPosition);
		}

		if (syncSlider) {
			SyncZoomSlider();
		}
	}

	private void SyncZoomSlider() {
		if (_zoomSlider == null) {
			return;
		}

		_zoomSlider.SetValueWithoutNotify(Mathf.InverseLerp(_minZoom, _maxZoom, _zoom));
	}

	// 확대된 스프라이트가 화면 밖으로 밀려 빈 여백이 보이지 않도록 이동 범위를 제한한다.
	private Vector2 ClampToViewport(Vector2 anchoredPosition) {
		RectTransform viewport = _mapImage.rectTransform.parent as RectTransform;
		if (viewport == null) {
			return anchoredPosition;
		}

		Rect mapRect = _mapImage.rectTransform.rect;
		float limitX = Mathf.Max(0f, (mapRect.width * _zoom - viewport.rect.width) * 0.5f);
		float limitY = Mathf.Max(0f, (mapRect.height * _zoom - viewport.rect.height) * 0.5f);

		return new Vector2(
			Mathf.Clamp(anchoredPosition.x, -limitX, limitX),
			Mathf.Clamp(anchoredPosition.y, -limitY, limitY));
	}
}
