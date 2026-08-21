using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// CCTV 화면에서 커서를 대상에 가져다 대면 네 귀퉁이 괄호 조준 표시와 이름을 띄운다.
// UI_TargetReticle(ArrestChaseUI)과 모양은 같지만, 그쪽은 Camera.main 기준 화면 좌표를 쓰기 때문에
// RenderTexture로 그려지는 CCTV 화면에는 쓸 수 없어 배치 로직을 따로 둔다.
//
// 가림 판정은 물리 레이캐스트로 하지 않는다. 건물 콜라이더가 창문·틈까지 채운 BoxCollider라
// (예: building-hotel은 삼각형 1만 개 메시에 BoxCollider 하나) 화면과 어긋난다.
// 대신 CCTV 화면에 그려진 외곽선 픽셀을 직접 읽어, 화면에 보이는 것과 판정을 일치시킨다.
public class CCTVItemReticle : MonoBehaviour
{
	// 아이템이 멀리 있으면 화면에서 몇 픽셀밖에 안 되므로, 괄호를 그릴 때 이보다 작아지지 않게 한다.
	// 커서 판정에는 쓰지 않는다. 판정 영역은 화면에 보이는 범위와 같아야 한다.
	private const float MinBoxSize = 34f;

	// 외곽선은 아이템 실루엣 바깥으로 번져 그려진다. 눈에 보이는 건 외곽선까지이므로 그만큼 판정도 넓힌다.
	private const float OutlineThickness = 12f;

	// 대상이 실제로 화면에 드러났는지는 CCTV 화면에 그려진 외곽선으로 판정한다.
	// 대상 영역을 이 크기로 축소 복사해 읽는다. 작게 잡아도 외곽선은 대상 테두리를 따라 그려져 잡힌다.
	private const int OutlineProbeSize = 32;

	// 야간투시(ColorAdjustments saturation -100)로 base 화면은 탈색되어 회색이 되는 반면,
	// 외곽선은 오버레이 카메라가 후처리를 건너뛰므로 노란색(1, 0.92, 0.02)으로 남는다.
	// 회색은 b/r이 1에 가까워, 가로등 색(1, 0.66, 0.19)은 g/r이 낮아 각각 걸러진다.
	private const float OutlineMinRed = 0.35f;
	private const float OutlineMinGreenRatio = 0.70f;
	private const float OutlineMaxBlueRatio = 0.20f;

	private const float TooltipMinWidth = 120f;
	private const float TooltipPadding = 28f;
	private const float TooltipCursorGap = 16f;

	[Header("=== CCTV 화면(RawImage) ===")]
	[SerializeField] private RawImage _screenImage;

	[Header("=== 외곽선을 그릴 최소 크기 ===")]
	[Tooltip("화면에 이 픽셀보다 작게 그려진 대상은 외곽선을 끈다. 멀리 있어 점처럼 보이는 것까지 " +
			 "외곽선이 깔리면 화면이 지저분해진다. 외곽선이 없으면 조준·이름 표시도 따라서 걸러진다.")]
	[SerializeField, Min(0f)] private float _minOutlinePixelSize = 5f;

	[Header("=== 조준 괄호와 아이템 이름 창 ===")]
	[SerializeField] private RectTransform _reticle;
	[SerializeField] private RectTransform _tooltip;
	[SerializeField] private TextMeshProUGUI _tooltipLabel;

	private RectTransform _screenRect;
	private Camera _cctvCamera;
	private Vector2 _lastCursorLocal;

	private RenderTexture _outlineProbeTexture;
	private Texture2D _outlineProbePixels;

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

	private void OnDestroy()
	{
		if (_outlineProbeTexture != null)
		{
			_outlineProbeTexture.Release();
			Destroy(_outlineProbeTexture);
			_outlineProbeTexture = null;
		}

		if (_outlineProbePixels != null)
		{
			Destroy(_outlineProbePixels);
			_outlineProbePixels = null;
		}
	}

	// 컴포넌트가 꺼지면(예: CCTV 영상 끊김) 표시가 그대로 남지 않도록 정리한다.
	private void OnDisable()
	{
		if (_reticle != null)
		{
			_reticle.gameObject.SetActive(false);
		}

		if (_tooltip != null)
		{
			_tooltip.gameObject.SetActive(false);
		}
	}

	private void LateUpdate()
	{
		if (_cctvCamera == null)
		{
			return;
		}

		string hovered = ScanTargets(out Rect itemRect);

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

	private void UpdateTooltip(string displayName)
	{
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

	// 등록된 대상을 한 번 순회하면서 두 가지를 한다.
	//   ① 화면에 작게 그려진 대상은 외곽선을 끈다 (커서와 무관하게 매 프레임)
	//   ② 커서 아래에 있는 대상 중 가장 가까운 것을 골라 이름과 조준 사각형을 돌려준다
	// 화면 사각형을 한 곳에서만 계산해, 외곽선 판정과 조준 판정이 어긋날 수 없게 한다.
	private string ScanTargets(out Rect itemRect)
	{
		itemRect = default;

		bool hasCursor = TryGetCursorPosition(out Vector2 cursorLocal);
		if (hasCursor)
		{
			_lastCursorLocal = cursorLocal;
		}

		string bestName = null;
		float bestDistance = float.MaxValue;

		foreach (ICctvHighlightTarget target in CctvHighlight.RegisteredTargets)
		{
			if (target == null)
			{
				continue;
			}

			if (!TryGetScreenRect(target.CctvBounds, out Rect reticleRect, out Rect pixelRect))
			{
				// 화면 밖이면 외곽선 상태를 그대로 둔다. 다시 들어올 때 판정한다.
				continue;
			}

			// ① itemPixelRect에는 외곽선 두께가 더해져 있으므로 그만큼 빼고 실제 크기를 본다.
			Vector2 drawnSize = pixelRect.size - Vector2.one * (OutlineThickness * 2f);
			bool isLargeEnough = Mathf.Max(drawnSize.x, drawnSize.y) >= _minOutlinePixelSize;
			CctvHighlight.SetOutlineEnabled(target, isLargeEnough);

			// ② 조준 후보 선정. 외곽선이 꺼졌거나 표시할 이름이 없으면 대상이 아니다.
			if (!hasCursor || !isLargeEnough)
			{
				continue;
			}

			if (!target.IsVisibleOnCctv || !CctvHighlight.IsKindEnabled(target.CctvKind))
			{
				continue;
			}

			string displayName = target.CctvDisplayName;
			if (string.IsNullOrEmpty(displayName))
			{
				continue;
			}

			// 판정 영역은 화면에 그려진 크기 그대로다. 괄호를 최소 크기로 키우더라도
			// 판정까지 커지면, 눈에 안 보이는 대상이 커서에 잡히게 된다.
			if (!pixelRect.Contains(cursorLocal))
			{
				continue;
			}

			float distance = Vector2.Distance(pixelRect.center, cursorLocal);
			if (distance >= bestDistance)
			{
				continue;
			}

			// 화면에 외곽선이 그려지지 않았다면 가려진 것이다. 리드백이 있으니 마지막에 본다.
			if (!HasOutlinePixel(reticleRect))
			{
				continue;
			}

			bestName = displayName;
			bestDistance = distance;
			itemRect = reticleRect;
		}

		return bestName;
	}

	// 커서가 CCTV 화면 안에 있으면 그 위치를 RawImage 로컬 좌표로 돌려준다.
	private bool TryGetCursorPosition(out Vector2 cursorLocal)
	{
		cursorLocal = default;

		if (Mouse.current == null)
		{
			return false;
		}

		// ScreenCanvas는 Screen Space - Overlay라 카메라를 넘기지 않는다.
		if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
				_screenRect, Mouse.current.position.ReadValue(), null, out cursorLocal))
		{
			return false;
		}

		return _screenRect.rect.Contains(cursorLocal);
	}

	// 아이템 바운즈의 여덟 꼭짓점을 CCTV 카메라로 투영해 RawImage 로컬 좌표계의 사각형을 만든다.
	private bool TryGetScreenRect(Bounds bounds, out Rect result, out Rect itemPixelRect)
	{
		result = default;
		itemPixelRect = default;

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

	// 대상이 그려진 영역을 CCTV 화면에서 축소 복사해 읽고, 외곽선 색이 있으면 화면에 드러난 것으로 본다.
	// 외곽선은 EPO가 픽셀 단위 깊이 테스트로 그리므로, 이 판정은 화면에 보이는 것과 정확히 일치한다.
	private bool HasOutlinePixel(Rect areaInScreen)
	{
		if (!(_screenImage.texture is RenderTexture source))
		{
			// 화면 텍스처를 못 읽는 상황이면 판정을 걸지 않는다. 조준이 통째로 죽는 것보다 낫다.
			return true;
		}

		Rect screenArea = _screenRect.rect;
		if (screenArea.width <= 0f || screenArea.height <= 0f)
		{
			return true;
		}

		// 대상 영역을 RawImage 로컬 좌표에서 0~1 정규화 좌표로 바꾼다.
		float x = (areaInScreen.xMin - screenArea.xMin) / screenArea.width;
		float y = (areaInScreen.yMin - screenArea.yMin) / screenArea.height;
		float width = areaInScreen.width / screenArea.width;
		float height = areaInScreen.height / screenArea.height;

		// 화면 밖으로 나간 부분은 잘라낸다.
		float xMax = Mathf.Min(1f, x + width);
		float yMax = Mathf.Min(1f, y + height);
		x = Mathf.Max(0f, x);
		y = Mathf.Max(0f, y);
		width = xMax - x;
		height = yMax - y;

		if (width <= 0f || height <= 0f)
		{
			return false;
		}

		// RawImage가 텍스처의 일부만 쓰고 있을 수 있어 uvRect를 함께 반영한다.
		Rect uvRect = _screenImage.uvRect;
		Vector2 scale = new Vector2(width * uvRect.width, height * uvRect.height);
		Vector2 offset = new Vector2(uvRect.xMin + x * uvRect.width, uvRect.yMin + y * uvRect.height);

		EnsureProbeTextures();

		RenderTexture previousActive = RenderTexture.active;
		Graphics.Blit(source, _outlineProbeTexture, scale, offset);
		RenderTexture.active = _outlineProbeTexture;
		_outlineProbePixels.ReadPixels(new Rect(0f, 0f, OutlineProbeSize, OutlineProbeSize), 0, 0, false);
		RenderTexture.active = previousActive;

		foreach (Color pixel in _outlineProbePixels.GetPixels())
		{
			if (IsOutlineColor(pixel))
			{
				return true;
			}
		}

		return false;
	}

	private void EnsureProbeTextures()
	{
		if (_outlineProbeTexture == null)
		{
			_outlineProbeTexture = new RenderTexture(OutlineProbeSize, OutlineProbeSize, 0, RenderTextureFormat.ARGB32);
		}

		if (_outlineProbePixels == null)
		{
			_outlineProbePixels = new Texture2D(OutlineProbeSize, OutlineProbeSize, TextureFormat.RGBA32, false);
		}
	}

	// 외곽선만 통과시킨다. 회색(탈색된 배경)은 파란 성분이 크고, 주황색(가로등)은 초록 성분이 작다.
	private static bool IsOutlineColor(Color pixel)
	{
		return pixel.r >= OutlineMinRed
			   && pixel.g >= pixel.r * OutlineMinGreenRatio
			   && pixel.b <= pixel.r * OutlineMaxBlueRatio;
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
