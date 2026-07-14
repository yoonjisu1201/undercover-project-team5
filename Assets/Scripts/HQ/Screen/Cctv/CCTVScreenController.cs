using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CCTVScreenController : ScreenBase {
	[Header("=== 왼쪽, 오른쪽 카메라로 이동하는 버튼 ===")]
	[SerializeField] private Button _leftButton;
	[SerializeField] private Button _rightButton;

	[Header("=== CCTV Hub 들어가야 함. ===")]
	[SerializeField] private CCTVHub _cctvHub;

	[Header("=== CCTV 텍스트 들어갈 곳 ===")]
	[SerializeField] private TMP_Text _cctvText; 
	
	private void OnEnable() {
		_leftButton.onClick.AddListener(OnPreviousClicked);
		_rightButton.onClick.AddListener(OnNextClicked);
		
		SetUiText(_cctvHub.UsingCctvNumber);
		_cctvHub.OnCctvNumberChanged += SetUiText;
	}

	private void OnDisable() {
		_leftButton.onClick.RemoveListener(OnPreviousClicked);
		_rightButton.onClick.RemoveListener(OnNextClicked);
		
		_cctvHub.OnCctvNumberChanged -= SetUiText;
	}
	
	private void OnPreviousClicked() {
		_cctvHub.SwitchToPrevious();
	}
	
	private void OnNextClicked() {
		_cctvHub.SwitchToNext();
	}
	
	private void SetUiText(int cameraNum) {
		// 카메라 인덱스가 0부터 시작하는 문제 수정을 위해 1 더해서 값 설정
		_cctvText.text = $"Cam {cameraNum + 1}";
	}
}
