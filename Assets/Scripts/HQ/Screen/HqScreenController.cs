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

	// 콘솔 탭. 관전자가 대상과 같은 탭을 보려면 값으로 주고받을 수 있어야 한다.
	public enum ConsoleTab { Map, Cctv, Montage, Shop }

	public ConsoleTab CurrentTab { get; private set; } = ConsoleTab.Map;

	// 사용자가 직접 탭을 누른 순간만 알린다. 미러가 따라 바꾼 것은 알리지 않는다.
	public event Action<ConsoleTab> TabChanged;

	// 탭 안쪽 상태(지도 모드, 몽타주 항목)까지 따라가야 해서 밖에서 접근할 길을 열어 둔다.
	public MinimapScreenController Minimap => _mapScreen as MinimapScreenController;
	public MontageDressUpUI Montage => _montageScreen as MontageDressUpUI;

	private readonly List<ScreenBase> _screens = new List<ScreenBase>();

	private CanvasGroup _canvasGroup;

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
		CurrentTab = ConsoleTab.Map;
		
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
		
		// Initialize()보다 먼저 켜지는 경우가 있는데, CurrentTab 은 기본값이 Map 이라 그대로 쓸 수 있다.
		ResolveScreen(CurrentTab).ActivateScreen();
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

	private void OnCctvButtonClicked() => SelectTab(ConsoleTab.Cctv, notify: true);
	private void OnMapButtonClicked() => SelectTab(ConsoleTab.Map, notify: true);
	private void OnMontageButtonClicked() => SelectTab(ConsoleTab.Montage, notify: true);
	private void OnShopButtonClicked() => SelectTab(ConsoleTab.Shop, notify: true);

	// 관전 미러가 대상의 탭을 그대로 따라갈 때 쓴다.
	public void ShowTab(ConsoleTab tab) => SelectTab(tab, notify: false);

	private void SelectTab(ConsoleTab tab, bool notify)
	{
		DeactivateAllScreens();

		ResolveScreen(tab).ActivateScreen();
		CurrentTab = tab;

		if (notify)
		{
			TabChanged?.Invoke(tab);
		}
	}

	private ScreenBase ResolveScreen(ConsoleTab tab) => tab switch
	{
		ConsoleTab.Cctv => _cctvScreen,
		ConsoleTab.Montage => _montageScreen,
		ConsoleTab.Shop => _shopScreen,
		_ => _mapScreen,
	};

	// 관전자 화면에서는 보기만 한다. 콘솔 UI는 씬에 하나뿐이라, 열 때마다 조작 가능 여부를
	// 다시 맞추지 않으면 관전이 끝난 뒤에도 막힌 채로 남는다.
	public void SetInteractable(bool interactive)
	{
		// 콘솔이 부모 Canvas 를 공유하면 GraphicRaycaster 가 이 오브젝트 아래에 없어서 못 끈다.
		// CanvasGroup 은 자기 아래 UI 전체의 입력을 막으므로 캔버스 구성과 무관하게 동작한다.
		//
		// interactable 만으로는 부족하다. 그건 Selectable(Button 등)만 막아서, 지도 드래그처럼
		// IDragHandler 를 직접 구현한 쪽이나 미니맵 CCTV 마커는 그대로 반응한다.
		if (_canvasGroup == null && !TryGetComponent(out _canvasGroup))
		{
			_canvasGroup = gameObject.AddComponent<CanvasGroup>();
		}

		_canvasGroup.interactable = interactive;
		_canvasGroup.blocksRaycasts = interactive;

		// OnEnable에서 ESC 스택에 자기를 올린다. 관전자가 ESC로 남의 콘솔을 닫지 못하게 뺀다.
		if (!interactive)
		{
			GameplayUiMode.Instance?.UnregisterUi(this);
		}
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
