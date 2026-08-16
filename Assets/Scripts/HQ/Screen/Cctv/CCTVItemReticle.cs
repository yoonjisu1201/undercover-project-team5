using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// CCTV 화면에서 커서를 아이템에 가져다 대면 네 귀퉁이 괄호 조준 표시를 띄운다.
// UI_TargetReticle(ArrestChaseUI)과 모양은 같지만, 그쪽은 Camera.main 기준 화면 좌표를 쓰기 때문에
// RenderTexture로 그려지는 CCTV 화면에는 쓸 수 없어 배치 로직을 따로 둔다.
public class CCTVItemReticle : MonoBehaviour
{
	// 아이템이 멀리 있으면 화면에서 몇 픽셀밖에 안 되므로, 괄호를 그릴 때 이보다 작아지지 않게 한다.
	// 커서 판정에는 쓰지 않는다. 판정 영역은 화면에 보이는 범위와 같아야 한다.
	private const float MinBoxSize = 34f;

	// 외곽선은 아이템 실루엣 바깥으로 번져 그려진다. 눈에 보이는 건 외곽선까지이므로 그만큼 판정도 넓힌다.
	private const float OutlineThickness = 12f;

	private const float TooltipMinWidth = 120f;
	private const float TooltipPadding = 28f;
	private const float TooltipCursorGap = 16f;

	[Header("=== CCTV 화면(RawImage) ===")]
	[SerializeField] private RawImage _screenImage;

	[Header("=== 조준 괄호와 아이템 이름 창 ===")]
	[SerializeField] private RectTransform _reticle;
	[SerializeField] private RectTransform _tooltip;
	[SerializeField] private TextMeshProUGUI _tooltipLabel;

	// 커서가 아이템 위에 있는 동안 매 프레임 여러 번 레이캐스트를 쏘므로 결과 버퍼를 재사용한다.
	private static readonly RaycastHit[] OcclusionHits = new RaycastHit[16];

	private RectTransform _screenRect;
	private Camera _cctvCamera;
	private Vector2 _lastCursorLocal;

	public void Initialize(Camera cctvCamera)
	{
		_cctvCamera = cctvCamera;

		if (_screenImage == null || _reticle == null || _tooltip == null || _tooltipLabel == null)
		{
			Debug.LogError($"'{name}'의 CCTVItemReticle에 연결되지 않은 UI 참조가 있습니다.", this);
			enabled = false;
			return;
		}

		_screenRect = _screenImage.rectTransform;

		_reticle.gameObject.SetActive(false);
		_tooltip.gameObject.SetActive(false);
	}

	private void LateUpdate()
	{
		if (_cctvCamera == null)
		{
			return;
		}

		ItemBase hovered = FindHoveredItem(out Rect itemRect);

		if (hovered == null)
		{
			_reticle.gameObject.SetActive(false);
			_tooltip.gameObject.SetActive(false);
			return;
		}

		_reticle.gameObject.SetActive(true);
		_reticle.anchoredPosition = itemRect.center;
		_reticle.sizeDelta = itemRect.size;

		UpdateTooltip(hovered);
	}

	private void UpdateTooltip(ItemBase item)
	{
		string displayName = item.ItemData != null ? item.ItemData.DisplayName : null;

		if (string.IsNullOrEmpty(displayName))
		{
			_tooltip.gameObject.SetActive(false);
			return;
		}

		_tooltip.gameObject.SetActive(true);
		_tooltipLabel.text = displayName;

		// 높이는 프리팹에 설정된 값을 그대로 쓰고, 이름 길이에 맞춰 너비만 늘린다.
		float height = _tooltip.sizeDelta.y;
		float width = Mathf.Max(TooltipMinWidth, _tooltipLabel.preferredWidth + TooltipPadding);
		_tooltip.sizeDelta = new Vector2(width, height);

		// 커서 오른쪽 아래에 붙이되, 화면 밖으로 나가면 반대편으로 넘긴다.
		Rect screenArea = _screenRect.rect;
		Vector2 position = _tooltip.anchoredPosition;
		position.x = _lastCursorLocal.x + TooltipCursorGap;
		position.y = _lastCursorLocal.y - TooltipCursorGap;

		if (position.x + width > screenArea.xMax)
		{
			position.x = _lastCursorLocal.x - TooltipCursorGap - width;
		}

		if (position.y - height < screenArea.yMin)
		{
			position.y = _lastCursorLocal.y + TooltipCursorGap + height;
		}

		_tooltip.anchoredPosition = position;
	}

	// 커서 아래에 있는 아이템 중 가장 가까운 것을 찾고, 그 아이템의 화면 사각형(RawImage 로컬 좌표)을 돌려준다.
	private ItemBase FindHoveredItem(out Rect itemRect)
	{
		itemRect = default;

		if (Mouse.current == null)
		{
			return null;
		}

		Vector2 mousePosition = Mouse.current.position.ReadValue();

		// ScreenCanvas는 Screen Space - Overlay라 카메라를 넘기지 않는다.
		if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
			    _screenRect, mousePosition, null, out Vector2 cursorLocal))
		{
			return null;
		}

		if (!_screenRect.rect.Contains(cursorLocal))
		{
			return null;
		}

		_lastCursorLocal = cursorLocal;

		ItemBase best = null;
		float bestDistance = float.MaxValue;

		foreach (ItemBase item in ItemBase.SpawnedItemList)
		{
			if (item == null || item.IsStored)
			{
				continue;
			}

			if (!TryGetScreenRect(item, out Rect candidateRect, out Rect itemPixelRect))
			{
				continue;
			}

			// 판정 영역은 화면에 그려진 아이템 크기 그대로다. 괄호를 최소 크기로 키우더라도
			// 판정까지 커지면, 눈에 안 보이는 아이템이 커서에 잡히게 된다.
			if (!itemPixelRect.Contains(cursorLocal))
			{
				continue;
			}

			float distance = Vector2.Distance(itemPixelRect.center, cursorLocal);
			if (distance >= bestDistance)
			{
				continue;
			}

			// 벽 뒤에 있는 아이템은 조준 표시를 띄우지 않는다.
			if (IsOccluded(item))
			{
				continue;
			}

			best = item;
			bestDistance = distance;
			itemRect = candidateRect;
		}

		return best;
	}

	// 아이템 바운즈의 여덟 꼭짓점을 CCTV 카메라로 투영해 RawImage 로컬 좌표계의 사각형을 만든다.
	private bool TryGetScreenRect(ItemBase item, out Rect result, out Rect itemPixelRect)
	{
		result = default;
		itemPixelRect = default;

		Bounds bounds = item.WorldBounds;

		float minX = float.MaxValue, minY = float.MaxValue;
		float maxX = float.MinValue, maxY = float.MinValue;

		for (int i = 0; i < 8; i++)
		{
			Vector3 viewport = _cctvCamera.WorldToViewportPoint(GetCorner(bounds, i));

			// 한 꼭짓점이라도 카메라 뒤에 있으면 투영 좌표가 뒤집혀서 못 쓴다.
			if (viewport.z <= 0f)
			{
				return false;
			}

			minX = Mathf.Min(minX, viewport.x);
			minY = Mathf.Min(minY, viewport.y);
			maxX = Mathf.Max(maxX, viewport.x);
			maxY = Mathf.Max(maxY, viewport.y);
		}

		Rect screenArea = _screenRect.rect;
		Vector2 center = new Vector2(
			screenArea.xMin + (minX + maxX) * 0.5f * screenArea.width,
			screenArea.yMin + (minY + maxY) * 0.5f * screenArea.height);

		// 화면에서 실제로 보이는 크기 = 아이템 + 외곽선. 커서 판정은 이 사각형을 쓴다.
		Vector2 pixelSize = new Vector2(
			(maxX - minX) * screenArea.width + OutlineThickness * 2f,
			(maxY - minY) * screenArea.height + OutlineThickness * 2f);
		itemPixelRect = new Rect(center - pixelSize * 0.5f, pixelSize);

		// 멀리 있는 아이템도 읽히도록 최소 크기를 보장한다.
		Vector2 size = new Vector2(
			Mathf.Max(pixelSize.x, MinBoxSize),
			Mathf.Max(pixelSize.y, MinBoxSize));

		result = new Rect(center - size * 0.5f, size);

		// 화면 밖으로 완전히 벗어난 아이템은 제외한다.
		return result.Overlaps(screenArea);
	}

	// 바운즈 중심 한 점만 보면 건물 모서리나 기둥에 스쳐도 통째로 가려진 것으로 걸러진다.
	// 중심과 여덟 꼭짓점 중 하나라도 뚫려 있으면 보이는 것으로 친다.
	private bool IsOccluded(ItemBase item)
	{
		Bounds bounds = item.WorldBounds;

		if (!IsPointOccluded(bounds.center))
		{
			return false;
		}

		for (int i = 0; i < 8; i++)
		{
			// 꼭짓점을 살짝 안쪽으로 당겨 바닥이나 벽에 붙은 면이 자기 자신에 걸리지 않게 한다.
			Vector3 corner = Vector3.Lerp(GetCorner(bounds, i), bounds.center, 0.1f);

			if (!IsPointOccluded(corner))
			{
				return false;
			}
		}

		return true;
	}

	private bool IsPointOccluded(Vector3 point)
	{
		Vector3 cameraPosition = _cctvCamera.transform.position;
		Vector3 direction = point - cameraPosition;

		int hitCount = Physics.RaycastNonAlloc(cameraPosition, direction.normalized, OcclusionHits,
			direction.magnitude, ~0, QueryTriggerInteraction.Ignore);

		for (int i = 0; i < hitCount; i++)
		{
			// 아이템끼리는 서로 가리는 것으로 치지 않는다. 겹쳐 놓인 아이템도 각각 조준할 수 있어야 한다.
			if (OcclusionHits[i].collider.GetComponentInParent<ItemBase>() != null)
			{
				continue;
			}

			return true;
		}

		return false;
	}

	private static Vector3 GetCorner(Bounds bounds, int index)
	{
		Vector3 min = bounds.min;
		Vector3 max = bounds.max;

		return new Vector3(
			(index & 1) == 0 ? min.x : max.x,
			(index & 2) == 0 ? min.y : max.y,
			(index & 4) == 0 ? min.z : max.z);
	}
}
