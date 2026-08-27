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

		// CCTV 화면의 닫기 버튼은 콘솔을 끄는 게 아니라 지도로 돌아가는 요청이다.
		if (_cctvScreen is CCTVScreenController cctvController) {
			cctvController.OnCloseRequested += HandleCctvCloseRequested;
		}
		
		// 처음 시작 시에 지도 스크린으로 시작. CCTV는 미니맵 마커 클릭으로만 진입한다.
		_currentScreen = _mapScreen;
		
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
			_currentScreen = _mapScreen;
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

	// 미니맵 CCTV 마커 클릭처럼 외부에서 CCTV 화면을 바로 열 때 쓴다.
	public void OpenCctvScreen()
	{
		OnCctvButtonClicked();
	}

	// CCTV 화면의 닫기 버튼처럼 외부에서 지도 화면으로 돌아갈 때 쓴다.
	public void OpenMapScreen()
	{
		OnMapButtonClicked();
	}

	// Initialize 때 CCTV 스크린의 닫기 요청을 구독해 지도로 되돌린다.
	private void HandleCctvCloseRequested()
	{
		OpenMapScreen();
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
