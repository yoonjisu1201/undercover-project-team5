using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class HqScreenController : MonoBehaviour, IClosableUi
{
	[Header("=== CCTV 버튼 및 화면 등록 ===")]
	[SerializeField] private ScreenBase _cctvScreen;
	[SerializeField] private Button _cctvButton;

	[Header("=== 지도 화면 및 버튼 등록 ===")]
	[SerializeField] private ScreenBase _mapScreen;
	[SerializeField] private Button _mapButton;

	[Header("=== 몽타주 화면 및 버튼 등록 ===")]
	[SerializeField] private ScreenBase _montageScreen;
	[SerializeField] private Button _montageButton;

	[Header("=== 상점 화면 및 버튼 등록 ===")]
	[SerializeField] private ScreenBase _shopScreen;
	[SerializeField] private Button _shopButton;

	[SerializeField] private Button _closeButton;

	public event Action OnScreenClosed;

	private readonly List<ScreenBase> _screens = new List<ScreenBase>();
	
	private ScreenBase _currentScreen;
	
	// 추후 모든 스크린에 대해 같은 동작 할 때를 대비
	public void Initialize() {
		// 모든 자식 Screens들 모아서 초기화 후 합쳐두기.
		var screens = GetComponentsInChildren<ScreenBase>(includeInactive : true);
		foreach (var screen in screens) {
			screen.Initialize();
			screen.gameObject.SetActive(false);
			_screens.Add(screen);
		}
		
		// 처음 시작 시에 CCTV 스크린으로 시작.
		_currentScreen = _cctvScreen;
		
		// 모든 화면 비활성화
		gameObject.SetActive(false);
		DeactivateAllScreens();
	}

	// 버튼 등록
	private void OnEnable()
	{
		_cctvButton.onClick.AddListener(OnCctvButtonClicked);
		_mapButton.onClick.AddListener(OnMapButtonClicked);
		_montageButton.onClick.AddListener(OnMontageButtonClicked);
		_shopButton.onClick.AddListener(OnShopButtonClicked);
		_closeButton.onClick.AddListener(OnCloseButtonClicked);
		GameplayUiMode.Instance?.RegisterUi(this);
		
		// Initialize()보다 먼저 켜지는 경우가 있어 기본 화면으로 받아둔다.
		if (_currentScreen == null) {
			_currentScreen = _cctvScreen;
		}
		
		_currentScreen.ActivateScreen();
	}

	// 버튼 해제
	private void OnDisable()
	{
		_cctvButton.onClick.RemoveListener(OnCctvButtonClicked);
		_mapButton.onClick.RemoveListener(OnMapButtonClicked);
		_montageButton.onClick.RemoveListener(OnMontageButtonClicked);
		_shopButton.onClick.RemoveListener(OnShopButtonClicked);
		_closeButton.onClick.RemoveListener(OnCloseButtonClicked);
		GameplayUiMode.Instance?.UnregisterUi(this);
		
		DeactivateAllScreens();
	}

	// ESC 등으로 닫으면 닫기 버튼과 동일하게 처리한다. (IClosableUi)
	public void Close()
	{
		OnCloseButtonClicked();
	}

	private void OnCctvButtonClicked()
	{
		DeactivateAllScreens();
		_cctvScreen.ActivateScreen();
		_currentScreen = _cctvScreen;
	}

	private void OnMapButtonClicked()
	{
		DeactivateAllScreens();
		_mapScreen.ActivateScreen();
		_currentScreen = _mapScreen;
	}

	private void OnMontageButtonClicked()
	{
		DeactivateAllScreens();
		_montageScreen.ActivateScreen();
		_currentScreen = _montageScreen;
	}

	private void OnShopButtonClicked()
	{
		DeactivateAllScreens();
		_shopScreen.ActivateScreen();
		_currentScreen = _shopScreen;
	}

	private void OnCloseButtonClicked()
	{
		gameObject.SetActive(false);
		OnScreenClosed?.Invoke();
	}

	// 모든 스크린 한번에 끄기
	private void DeactivateAllScreens() {
		foreach (ScreenBase screen in _screens) {
			screen.DeactivateScreen();
		}
	}
}
