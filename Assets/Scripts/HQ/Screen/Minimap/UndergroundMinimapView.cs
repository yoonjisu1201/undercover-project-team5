using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 지하 맵은 라운드마다 절차적으로 생성되므로 미리 그린 그림 한 장으로 표현할 수 없다.
// 생성기가 배치한 조각을 그대로 다시 그린다. 조각의 위치·회전·크기를 그대로 쓰기 때문에
// 문끼리 맞물리는 규칙을 여기서 다시 구현할 필요가 없다.
public class UndergroundMinimapView : MonoBehaviour {
	[Header("=== 조각 그림을 담을 부모 (미니맵 Image의 자식) ===")]
	[SerializeField] private RectTransform _moduleRoot;

	[Header("=== 지하 맵 생성기 ===")]
	[SerializeField] private UndergroundRandomMapGenerator _generator;

	private readonly List<Image> _moduleImages = new List<Image>();

	private MinimapScreenController _minimapScreen;

	public void Initialize(MinimapScreenController minimapScreen) {
		_minimapScreen = minimapScreen;

		if (_generator != null) {
			_generator.Generated += HandleGenerated;
		}

		Hide();
	}

	private void OnDestroy() {
		if (_generator != null) {
			_generator.Generated -= HandleGenerated;
		}
	}

	// 맵이 다시 생성되면 지금 지하를 보고 있을 때만 즉시 다시 그린다.
	// 지상을 보고 있다면 지하로 전환하는 시점에 그려진다.
	private void HandleGenerated() {
		if (_minimapScreen != null && _minimapScreen.Mode == MinimapScreenController.MinimapMode.Underground) {
			_minimapScreen.RefreshDisplay();
		}
	}

	public void Hide() {
		if (_moduleRoot != null) {
			_moduleRoot.gameObject.SetActive(false);
		}
	}

	// 배치된 조각을 전부 다시 그린다.
	public void Rebuild() {
		if (_moduleRoot == null || _generator == null || _minimapScreen == null) {
			return;
		}

		_moduleRoot.gameObject.SetActive(true);
		ClearModuleImages();

		if (!_minimapScreen.TryGetWorldToMapScale(out float worldToMap)) {
			return;
		}

		foreach (UndergroundModule module in _generator.PlacedModules) {
			if (module == null || module.MinimapSprite == null || module.Bounds == null) {
				continue;
			}

			DrawModule(module, worldToMap);
		}
	}

	private void DrawModule(UndergroundModule module, float worldToMap) {
		// 그림은 조각의 눈에 보이는 범위(벽 포함)를 그린 것이므로, 겹침 판정용 콜라이더가 아니라
		// 실제로 보이는 범위에 맞춰야 조각끼리 틈 없이 이어진다.
		if (!TryGetVisualFootprint(module, out Vector2 localSize, out Vector3 localCenter)) {
			return;
		}

		Vector3 worldCenter = module.transform.TransformPoint(localCenter);
		if (!_minimapScreen.TryProjectToMap(worldCenter, out Vector2 anchoredPosition)) {
			return;
		}

		Vector2 footprint = localSize * worldToMap;

		// 조각이 월드에서 Y축으로 돌아간 각도. 위에서 내려다보는 지도에서는 반대 방향으로 돌려야 한다.
		float yaw = module.transform.eulerAngles.y;
		float spriteAngle = (float)module.MinimapSpriteRotation;
		float totalAngle = -yaw + spriteAngle;

		// 그림이 조각 기준으로 90도 돌아가 있으면, 회전 전 rect의 가로·세로도 뒤바뀐 상태여야 한다.
		bool spriteQuarterTurned = Mathf.Abs(Mathf.Sin(spriteAngle * Mathf.Deg2Rad)) > 0.5f;
		Vector2 rectSize = spriteQuarterTurned ? new Vector2(footprint.y, footprint.x) : footprint;

		// 그림에 투명 여백이 있으면 그만큼 rect를 키워야 실제 내용이 footprint를 채운다.
		Rect content = module.MinimapSpriteContentRect;
		if (content.width > 0f && content.height > 0f) {
			rectSize = new Vector2(rectSize.x / content.width, rectSize.y / content.height);
		}

		Image image = CreateModuleImage();
		image.sprite = module.MinimapSprite;

		RectTransform rect = image.rectTransform;
		rect.sizeDelta = rectSize;
		rect.localRotation = Quaternion.Euler(0f, 0f, totalAngle);

		// 여백 때문에 그림의 내용 중심이 rect 중심에서 벗어나 있으면, 그만큼 되밀어 준다.
		Vector2 contentShift = new Vector2(
			(0.5f - content.center.x) * rectSize.x,
			(0.5f - content.center.y) * rectSize.y);
		rect.anchoredPosition = anchoredPosition + (Vector2)(rect.localRotation * contentShift);
	}

	// 조각의 렌더러를 모듈 로컬 공간에서 감싸는 XZ 범위. 렌더러가 없으면 콜라이더로 대체한다.
	private bool TryGetVisualFootprint(UndergroundModule module, out Vector2 localSize, out Vector3 localCenter) {
		localSize = Vector2.zero;
		localCenter = Vector3.zero;

		// 그림이 조각의 일부만 그린 경우(StartPoint의 계단 등)에는 직접 지정한 범위를 쓴다.
		Vector2 declaredSize = module.MinimapFootprintSize;
		if (declaredSize.x > 0f && declaredSize.y > 0f) {
			localSize = declaredSize;
			Vector2 declaredCenter = module.MinimapFootprintCenter;
			localCenter = new Vector3(declaredCenter.x, 0f, declaredCenter.y);
			return true;
		}

		Transform moduleTransform = module.transform;
		bool any = false;
		Vector3 min = Vector3.zero;
		Vector3 max = Vector3.zero;

		foreach (Renderer renderer in module.GetComponentsInChildren<Renderer>(false)) {
			if (renderer is ParticleSystemRenderer) {
				continue;
			}

			Bounds worldBounds = renderer.bounds;

			// 월드 AABB의 여덟 꼭짓점을 모듈 로컬로 옮겨야 회전된 조각에서도 로컬 범위가 나온다.
			for (int corner = 0; corner < 8; corner++) {
				Vector3 point = new Vector3(
					(corner & 1) == 0 ? worldBounds.min.x : worldBounds.max.x,
					(corner & 2) == 0 ? worldBounds.min.y : worldBounds.max.y,
					(corner & 4) == 0 ? worldBounds.min.z : worldBounds.max.z);

				Vector3 local = moduleTransform.InverseTransformPoint(point);
				if (!any) {
					min = local;
					max = local;
					any = true;
				}
				else {
					min = Vector3.Min(min, local);
					max = Vector3.Max(max, local);
				}
			}
		}

		if (any) {
			localSize = new Vector2(max.x - min.x, max.z - min.z);
			localCenter = (min + max) * 0.5f;
			return localSize.x > 0f && localSize.y > 0f;
		}

		BoxCollider box = module.Bounds;
		if (box == null) {
			return false;
		}

		localSize = new Vector2(box.size.x, box.size.z);
		localCenter = box.center;
		return localSize.x > 0f && localSize.y > 0f;
	}

	private Image CreateModuleImage() {
		GameObject go = new GameObject("ModuleMap", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		go.layer = _moduleRoot.gameObject.layer;
		go.transform.SetParent(_moduleRoot, false);

		RectTransform rect = (RectTransform)go.transform;
		rect.anchorMin = new Vector2(0.5f, 0.5f);
		rect.anchorMax = new Vector2(0.5f, 0.5f);
		rect.pivot = new Vector2(0.5f, 0.5f);

		Image image = go.GetComponent<Image>();
		image.raycastTarget = false;

		_moduleImages.Add(image);
		return image;
	}

	private void ClearModuleImages() {
		foreach (Image image in _moduleImages) {
			if (image != null) {
				Destroy(image.gameObject);
			}
		}

		_moduleImages.Clear();
	}
}
