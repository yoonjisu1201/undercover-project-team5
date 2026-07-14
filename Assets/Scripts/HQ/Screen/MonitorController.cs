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

	[Header("=== 힌트 화면 및 버튼 등록 ===")] 
	[SerializeField] private ScreenBase _hintScreen;
	[SerializeField] private Button _hintButton;

	[SerializeField] private Button _closeButton;
	
	private readonly List<ScreenBase> _screens = new List<ScreenBase>();
	
	// Awake에서 스크린들 모아서 Screens로 합쳐두기.
	// 추후 모든 스크린에 대해 같은 동작 할 때를 대비
	private void Awake() {
		_screens.Add(_cctvScreen);
		_screens.Add(_mapScreen);
		_screens.Add(_hintScreen);
	}

	// 버튼 등록
	private void OnEnable() {
		_cctvButton.onClick.AddListener(OnCctvButtonClicked);
		_mapButton.onClick.AddListener(OnMapButtonClicked);
		_hintButton.onClick.AddListener(OnHintButtonClicked);
		_closeButton.onClick.AddListener(OnCloseButtonClicked);
	}

	// 버튼 해제
	private void OnDisable() {
		_cctvButton.onClick.RemoveListener(OnCctvButtonClicked);
		_mapButton.onClick.RemoveListener(OnMapButtonClicked);
		_hintButton.onClick.RemoveListener(OnHintButtonClicked);
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
	
	private void OnHintButtonClicked() {
		DisableAllScreens();
		_hintScreen.ActivateScreen();
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
