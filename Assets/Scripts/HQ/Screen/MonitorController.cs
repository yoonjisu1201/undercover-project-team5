using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MonitorController : MonoBehaviour {
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
	}

	// 버튼 해제
	private void OnDisable() {
		_cctvButton.onClick.RemoveListener(OnCctvButtonClicked);
		_mapButton.onClick.RemoveListener(OnMapButtonClicked);
		_montageButton.onClick.RemoveListener(OnMontageButtonClicked);
		_closeButton.onClick.RemoveListener(OnCloseButtonClicked);

		// 닫기 버튼이 아니라 외부(라운드 종료 등)에서 강제로 비활성화되는 경우에도
		// 커서 잠금 해제 등 후속 처리가 이루어지도록 보장
		OnScreenClosed?.Invoke();
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
	}
	
	// 모든 스크린 한번에 끄기
	private void DisableAllScreens() {
		foreach (ScreenBase screen in _screens) {
			screen.DisableScreen();
		}
	}
}
