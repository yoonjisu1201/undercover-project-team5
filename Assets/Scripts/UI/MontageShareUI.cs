using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;

public class MontageShareUI : MonoBehaviour {
	[Header("=== MontageShareManager 등록 ===")]
	[SerializeField] private MontageShareManager _montageShareManager;
	
	[Header("=== 접혔을 때의 UI와 열렸을 때의 UI ===")]
	[SerializeField] private GameObject _foldedUI;
	[SerializeField] private GameObject _expandedUI;

	[Header("=== 접혔을 때 보일 Text UI ===")]
	[SerializeField] private LocalizeStringEvent _stateText;
	
	[Header("=== 열었을 때 보이는 뱃지 텍스트 ===")]
	[SerializeField] private LocalizeStringEvent _sentSecondsAgoText;

	[Header("=== 전송된 적이 없다면, RawImage를 비활성화해두기 위해 저장 ===")]
	[SerializeField] private GameObject _montageImage;

	[Header("=== 내부에서 사용할 LocalizationText ===")] 
	[SerializeField] private LocalizedString _noMontageShared;
	[SerializeField] private LocalizedString _newMontageShared;
	[SerializeField] private LocalizedString _montageAlreadyViewed;
	[SerializeField] private LocalizedString _sentSecondsAgo;
	
	private CustomInputActions _inputActions;
	
	// 내가 확인하지 않은 새 몽타주가 있는가?
	private bool _isMontageRenewed = false;

	private void Awake() {
		_inputActions = new CustomInputActions();
	}

	private void OnEnable() { _inputActions.UI.Enable(); }
	private void OnDisable() { _inputActions.UI.Disable(); }

	private void Start() {
		RoundManager.Instance.OnRoundStarted += OnRoundStarted;
		_montageShareManager.OnMontageStateChanged += HandleMontageStateChanged;
		
		UpdateUiState();
	}

	public void OnDestroy() {
		RoundManager.Instance.OnRoundStarted -= OnRoundStarted;
		_montageShareManager.OnMontageStateChanged -= HandleMontageStateChanged;
	}

	private void Update() {
		if (_inputActions.UI.Montage.WasPressedThisFrame()) {
			TogglePanelState();
		}
		
		_sentSecondsAgoText.StringReference = _sentSecondsAgo;
		_sentSecondsAgoText.StringReference.Arguments = new List<object> { (int)(NetworkManager.Singleton.ServerTime.Time - _montageShareManager.LastSharedTime.Value) };
		_sentSecondsAgoText.RefreshString();
	}
	
	// 패널을 열거나 닫을 때 사용. 패널을 열거나 닫으면 새 메세지 상태 갱신
	private void TogglePanelState() {
		_foldedUI.gameObject.SetActive(!_foldedUI.gameObject.activeSelf);
		_expandedUI.gameObject.SetActive(!_expandedUI.gameObject.activeSelf);
		
		_isMontageRenewed = false;
		
		UpdateUiState();
	}

	
	// 새 라운드가 시작되면, UI 관련 정보 모두 없앤다.
	private void OnRoundStarted(int round) {
		_isMontageRenewed = false;
		
		UpdateUiState();
	}
	
	// ShareManager를 구독하면서 본부가 몽타주 공유하면 바로 renewed값 갱신
	private void HandleMontageStateChanged(MontageState state) {
		_isMontageRenewed = true;
		
		UpdateUiState();
	}
	
	private void UpdateUiState() {
		// 1. 몽타주가 전송된 적이 없다? 그럼 전송된 적 없다는 메세지 출력 및 몽타주 이미지 비활성화해두기
		if (!_montageShareManager.IsMontageShared.Value) {
			_montageImage.SetActive(false);
			_stateText.StringReference = _noMontageShared;
			_stateText.RefreshString();
			
			return;
		} 
		
		// 위에 걸리지 않았으면 몽타주 전송된 것. 몽타주 이미지 활성화한다
		_montageImage.SetActive(true);
		
		// 확인하지 않은 새 몽타주가 있는 경우 메세지
		if (_isMontageRenewed) {
			_stateText.StringReference = _newMontageShared;
		}
		// 확인하지 않은 새 몽타주가 없는 경우 메세지
		else {
			_stateText.StringReference = _montageAlreadyViewed;
		}
		
		// 메시지 적용
		_stateText.RefreshString();
	}
}
