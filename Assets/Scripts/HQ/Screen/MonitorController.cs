using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class MonitorController : MonoBehaviour, IClosableUi {
	[Header("=== CCTV 버튼 및 화면 등록 ===")] 
	[SerializeField] private ScreenBase _cctvScreen;
	[SerializeField] private Button _cctvButton;

	[Header("=== 지도 화면 및 버튼 등록 ===")] 
	[SerializeField] private ScreenBase _mapScreen;
	[SerializeField] private Button _mapButton;

	[Header("=== 몽타주 화면 및 버튼 등록 ===")] 
	[SerializeField] private ScreenBase _montageScreen;
	[SerializeField] private Button _montageButton;
	[SerializeField] private Button _closeButton;
	
	public event Action OnScreenClosed;

	private readonly List<ScreenBase> _screens = new List<ScreenBase>();
	
	// Awake에서 스크린들 모아서 Screens로 합쳐두기.
	// 추후 모든 스크린에 대해 같은 동작 할 때를 대비
	private void Awake() {
		_screens.Add(_cctvScreen);
		_screens.Add(_mapScreen);
		_screens.Add(_montageScreen);
		
		// 처음 시작 시에 CCTV 스크린으로 시작. 이 패널은 비활성화 상태로
		_cctvScreen.gameObject.SetActive(true);
		_mapScreen.gameObject.SetActive(false);
		_montageScreen.gameObject.SetActive(false);
	}

	// 버튼 등록
	private void OnEnable() {
		_cctvButton.onClick.AddListener(OnCctvButtonClicked);
		_mapButton.onClick.AddListener(OnMapButtonClicked);
		_montageButton.onClick.AddListener(OnMontageButtonClicked);
		_closeButton.onClick.AddListener(OnCloseButtonClicked);
		GameplayUiMode.Instance?.RegisterUi(this);
	}

	// 버튼 해제
	private void OnDisable() {
		_cctvButton.onClick.RemoveListener(OnCctvButtonClicked);
		_mapButton.onClick.RemoveListener(OnMapButtonClicked);
		_montageButton.onClick.RemoveListener(OnMontageButtonClicked);
		_closeButton.onClick.RemoveListener(OnCloseButtonClicked);
		GameplayUiMode.Instance?.UnregisterUi(this);
	}

	// ESC 등으로 닫으면 닫기 버튼과 동일하게 처리한다. (IClosableUi)
	public void Close() {
		OnCloseButtonClicked();
	}

	private void OnCctvButtonClicked() {
		DisableAllScreens();
		_cctvScreen.ActivateScreen();
	}
	
	private void OnMapButtonClicked() {
		DisableAllScreens();
		_mapScreen.ActivateScreen();
	}
	
	private void OnMontageButtonClicked() {
		DisableAllScreens();
		_montageScreen.ActivateScreen();
	}
	
	private void OnCloseButtonClicked() {
		gameObject.SetActive(false);
		OnScreenClosed?.Invoke();
	}
	
	// 모든 스크린 한번에 끄기
	private void DisableAllScreens() {
		foreach (ScreenBase screen in _screens) {
			screen.DisableScreen();
		}
	}
}
