using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 지도 화면 상단의 필드 / 지하 전환 토글.
// 선택 표시(Knob)를 눌린 버튼 위로 슬라이드시켜 어느 쪽을 보고 있는지 알려준다.
public class MinimapModeToggle : MonoBehaviour {
	[Header("=== 전환할 지도 화면 ===")]
	[SerializeField] private MinimapScreenController _minimapScreen;

	[Header("=== 버튼 ===")]
	[SerializeField] private Button _fieldButton;
	[SerializeField] private Button _undergroundButton;

	[Header("=== 눌린 쪽으로 움직이는 선택 표시 ===")]
	[SerializeField] private RectTransform _knob;

	[Header("=== 슬라이드 연출 ===")]
	[SerializeField, Min(0f)] private float _slideDuration = 0.2f;
	[SerializeField] private Ease _slideEase = Ease.OutCubic;

	[Header("=== 선택 여부에 따른 글자 색 ===")]
	[SerializeField] private Color _selectedTextColor = Color.white;
	[SerializeField] private Color _unselectedTextColor = new Color(0.6f, 0.6f, 0.6f, 1f);

	private Tween _slideTween;

	private void Awake() {
		_fieldButton?.onClick.AddListener(SelectField);
		_undergroundButton?.onClick.AddListener(SelectUnderground);
	}

	private void OnEnable() {
		// 화면을 다시 열었을 때 지도가 보고 있는 모드와 표시가 어긋나지 않도록 맞춘다.
		ApplySelection(_minimapScreen != null ? _minimapScreen.Mode : MinimapScreenController.MinimapMode.Field, false);
	}

	private void OnDestroy() {
		_fieldButton?.onClick.RemoveListener(SelectField);
		_undergroundButton?.onClick.RemoveListener(SelectUnderground);
		_slideTween?.Kill();
	}

	private void SelectField() {
		_minimapScreen?.ShowField();
		ApplySelection(MinimapScreenController.MinimapMode.Field, true);
	}

	private void SelectUnderground() {
		_minimapScreen?.ShowUnderground();
		ApplySelection(MinimapScreenController.MinimapMode.Underground, true);
	}

	private void ApplySelection(MinimapScreenController.MinimapMode mode, bool animate) {
		bool isField = mode == MinimapScreenController.MinimapMode.Field;

		ApplyTextColor(_fieldButton, isField);
		ApplyTextColor(_undergroundButton, !isField);

		RectTransform target = (isField ? _fieldButton : _undergroundButton)?.transform as RectTransform;
		if (_knob == null || target == null) {
			return;
		}

		_slideTween?.Kill();

		// Knob과 버튼이 같은 부모 아래 있으므로 anchoredPosition을 그대로 옮기면 된다.
		Vector2 destination = target.anchoredPosition;
		if (!animate || _slideDuration <= 0f) {
			_knob.anchoredPosition = destination;
			return;
		}

		_slideTween = _knob.DOAnchorPos(destination, _slideDuration)
			.SetEase(_slideEase)
			.SetUpdate(true);
	}

	private void ApplyTextColor(Button button, bool selected) {
		if (button == null) {
			return;
		}

		TMPro.TMP_Text label = button.GetComponentInChildren<TMPro.TMP_Text>();
		if (label != null) {
			label.color = selected ? _selectedTextColor : _unselectedTextColor;
		}
	}
}
