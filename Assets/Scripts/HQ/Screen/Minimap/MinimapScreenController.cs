using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 활성화된 맵 구역의 미니맵 스프라이트를 띄우고, 월드 좌표를 그 스프라이트 위 좌표로 변환해준다.
// 예전처럼 렌더 텍스처를 쓰는 미니맵 카메라는 사용하지 않는다.
public class MinimapScreenController : ScreenBase, IDragHandler, IScrollHandler {
	// 스프라이트를 그린 방향이 월드 방향과 다른 구역이 있어(A 구역은 90도 돌아가 있음) 구역별로 보정한다.
	public enum MapRotation {
		None = 0,
		CW90 = 90,
		Half = 180,
		CCW90 = 270
	}

	[Header("=== 미니맵 스프라이트를 그릴 Image ===")]
	[SerializeField] private Image _mapImage;

	[Header("=== 구역별 미니맵 스프라이트 (E 구역은 아직 없음) ===")]
	[Tooltip("칸 순서가 RegionId 순서(A, B, C, D, E, Basement)와 그대로 맞아야 한다. 없는 구역은 비워둔다.")]
	[SerializeField] private Sprite[] _regionSprites = Array.Empty<Sprite>();

	[Header("=== 구역별 스프라이트 회전 보정 ===")]
	[Tooltip("스프라이트가 월드 기준으로 돌아가 있는 각도. 칸 순서는 위 스프라이트와 같다.")]
	[SerializeField] private MapRotation[] _regionRotations = Array.Empty<MapRotation>();

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
	private float _mapAngle;

	// 미니맵이 돌아간 만큼 마커 아이콘은 반대로 돌려 똑바로 세운다.
	public Quaternion MarkerCounterRotation => Quaternion.Euler(0f, 0f, -_mapAngle);

	// 미니맵 위에 아이콘을 얹는 쪽에서 이 RectTransform을 기준 좌표계로 쓴다.
	public RectTransform MapRect => _mapImage != null ? _mapImage.rectTransform : null;

	// 칸 개수를 RegionId 개수에 고정해, 드래그로 채운 순서가 구역과 어긋나지 않게 한다.
	private void OnValidate() {
		int regionCount = Enum.GetValues(typeof(RegionId)).Length;
		if (_regionSprites.Length != regionCount) {
			Array.Resize(ref _regionSprites, regionCount);
		}

		if (_regionRotations.Length != regionCount) {
			Array.Resize(ref _regionRotations, regionCount);
		}
	}

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
		_mapAngle = region != null ? (float)FindRotation(region.RegionId) : 0f;

		if (_mapImage != null) {
			_mapImage.sprite = sprite;
			_mapImage.enabled = sprite != null;
			_mapImage.rectTransform.anchoredPosition = Vector2.zero;
			_mapImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, _mapAngle);
			FitToViewport(sprite);
		}

		if (region != null && sprite == null) {
			Debug.LogWarning($"[MinimapScreenController] '{region.RegionId}' 구역의 미니맵 스프라이트가 등록되지 않았습니다.", this);
		}

		SetZoom(_minZoom);
	}

	// 구역마다 스프라이트 비율이 다를 수 있으므로, 뷰포트 안에 비율을 유지한 최대 크기로 맞춘다.
	// 마커 좌표를 이 RectTransform 기준으로 계산하기 때문에 rect가 실제로 그려지는 영역과 같아야 한다.
	private void FitToViewport(Sprite sprite) {
		RectTransform viewport = _mapImage.rectTransform.parent as RectTransform;
		if (sprite == null || viewport == null) {
			return;
		}

		Vector2 spriteSize = sprite.rect.size;
		Vector2 displayedSize = IsQuarterTurned ? new Vector2(spriteSize.y, spriteSize.x) : spriteSize;
		float fitScale = Mathf.Min(viewport.rect.width / displayedSize.x, viewport.rect.height / displayedSize.y);
		_mapImage.rectTransform.sizeDelta = spriteSize * fitScale;
	}

	private Sprite FindSprite(RegionId regionId) {
		int index = (int)regionId;
		return index >= 0 && index < _regionSprites.Length ? _regionSprites[index] : null;
	}

	private MapRotation FindRotation(RegionId regionId) {
		int index = (int)regionId;
		return index >= 0 && index < _regionRotations.Length ? _regionRotations[index] : MapRotation.None;
	}

	// 90도, 270도 회전은 화면에 그려지는 가로·세로가 뒤바뀐다.
	private bool IsQuarterTurned => Mathf.Approximately(Mathf.Abs(Mathf.Sin(_mapAngle * Mathf.Deg2Rad)), 1f);

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

		// 화면 기준 위치를 먼저 구한 뒤, 회전된 Image의 로컬 좌표로 되돌린다.
		Vector2 rectSize = _mapImage.rectTransform.rect.size;
		Vector2 displayedSize = IsQuarterTurned ? new Vector2(rectSize.y, rectSize.x) : rectSize;
		Vector2 displayedOffset = new Vector2(
			(normalizedX - 0.5f) * displayedSize.x,
			(normalizedY - 0.5f) * displayedSize.y);

		anchoredPosition = MarkerCounterRotation * displayedOffset;
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

		Vector2 rectSize = _mapImage.rectTransform.rect.size;
		Vector2 displayedSize = IsQuarterTurned ? new Vector2(rectSize.y, rectSize.x) : rectSize;
		float limitX = Mathf.Max(0f, (displayedSize.x * _zoom - viewport.rect.width) * 0.5f);
		float limitY = Mathf.Max(0f, (displayedSize.y * _zoom - viewport.rect.height) * 0.5f);

		return new Vector2(
			Mathf.Clamp(anchoredPosition.x, -limitX, limitX),
			Mathf.Clamp(anchoredPosition.y, -limitY, limitY));
	}
}
