using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MontageRecordRow : MonoBehaviour {
	[Header("=== 레코드 표시 요소 ===")]
	[SerializeField] private Image _background;
	[SerializeField] private Image _icon;
	[SerializeField] private TMP_Text _idText;
	[SerializeField] private Button _button;

	[Header("=== 온/오프 상태 색상 ===")]
	[SerializeField] private Color _offColor = new Color(0.106f, 0.161f, 0.161f, 1f);
	[SerializeField] private Color _onColor = new Color(0.231f, 0.910f, 0.659f, 0.16f);

	private ClothData _data;
	private Action<MontageRecordRow> _onClicked;
	private bool _isOn;

	public ClothData Data => _data;

	private void Awake() {
		_button.onClick.AddListener(HandleClicked);
	}

	private void OnDestroy() {
		_button.onClick.RemoveListener(HandleClicked);
	}

public void Setup(ClothData data, string recordId, bool isOn, Action<MontageRecordRow> onClicked) {
		_data = data;
		_onClicked = onClicked;

		_icon.sprite = data.Thumbnail;
		_icon.enabled = data.Thumbnail != null;
		_idText.text = recordId;

		SetOn(isOn);
	}

public void SetOn(bool isOn) {
		_isOn = isOn;
		_background.color = isOn ? _onColor : _offColor;
	}

	private void HandleClicked() {
		_onClicked?.Invoke(this);
	}
}
