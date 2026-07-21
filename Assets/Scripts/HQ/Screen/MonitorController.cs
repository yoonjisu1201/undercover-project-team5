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
	[SerializeField] private ScreenBase _silhouetteScreen;
	[SerializeField] private Button _silhouetteButton;
	[SerializeField] private Button _closeButton;
	
	public event Action OnScreenClosed;

	private readonly List<ScreenBase> _screens = new List<ScreenBase>();
	
	// Awake에서 스크린들 모아서 Screens로 합쳐두기.
	// 추후 모든 스크린에 대해 같은 동작 할 때를 대비
	private void Awake() {
		_screens.Add(_cctvScreen);
		_screens.Add(_mapScreen);
		_screens.Add(_silhouetteScreen);
		
		// 처음 시작 시에 CCTV 스크린으로 시작. 이 패널은 비활성화 상태로
		_cctvScreen.gameObject.SetActive(true);
		_mapScreen.gameObject.SetActive(false);
		_silhouetteScreen.gameObject.SetActive(false);
	}

	// 버튼 등록
	private void OnEnable() {
		_cctvButton.onClick.AddListener(OnCctvButtonClicked);
		_mapButton.onClick.AddListener(OnMapButtonClicked);
		_silhouetteButton.onClick.AddListener(OnSilhouetteButtonClicked);
		_closeButton.onClick.AddListener(OnCloseButtonClicked);
	}

	// 버튼 해제
	private void OnDisable() {
		_cctvButton.onClick.RemoveListener(OnCctvButtonClicked);
		_mapButton.onClick.RemoveListener(OnMapButtonClicked);
		_silhouetteButton.onClick.RemoveListener(OnSilhouetteButtonClicked);
		_closeButton.onClick.RemoveListener(OnCloseButtonClicked);
	}

	private void OnCctvButtonClicked() {
		DisableAllScreens();
		_cctvScreen.ActivateScreen();
	}
	
	private void OnMapButtonClicked() {
		DisableAllScreens();
		_mapScreen.ActivateScreen();
	}
	
	private void OnSilhouetteButtonClicked() {
		DisableAllScreens();
		_silhouetteScreen.ActivateScreen();
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
