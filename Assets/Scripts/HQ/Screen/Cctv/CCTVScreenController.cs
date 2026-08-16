using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 본부 CCTV의 카메라 이동 버튼과 Disconnected 공용 오버레이를 제어합니다.
public class CCTVScreenController : ScreenBase
{
	[Header("=== 왼쪽, 오른쪽 카메라로 이동하는 버튼 ===")]
	[SerializeField] private Button _leftButton;
	[SerializeField] private Button _rightButton;

	[Header("=== CCTV Hub 들어가야 함. ===")]
	[SerializeField] private CCTVHub _cctvHub;

	[Header("=== CCTV 텍스트 들어갈 곳 ===")]
	[SerializeField] private TMP_Text _cctvText;

	private GameObject _disconnectedOverlay;
	private CCTVItemReticle _itemReticle;

	// 한 번에 하나의 CCTV만 표시하므로 공용 Disconnected 오버레이 하나만 캐시합니다.
	public override void Initialize()
	{
		Transform overlayTransform = transform.Find("DisconnectedCamera");
		if (overlayTransform == null)
		{
			Debug.LogError($"'{name}'에 DisconnectedCamera 오브젝트가 없습니다.", this);
			return;
		}

		_disconnectedOverlay = overlayTransform.gameObject;

		SetupItemReticle();

		// 카메라 비활성화상태로 시작
		_cctvHub.CctvCamera.enabled = false;
		// 본부에 빈 화면 나오지 않도록 1회 렌더링
		_cctvHub.CctvCamera.Render();
	}

	// 화면이 열리면 이동 버튼과 CCTV 상태 변경 이벤트를 구독합니다. 추가로 CCTV 카메라를 활성화합니다.
	public override void ActivateScreen()
	{
		base.ActivateScreen();

		// 미션에서 연결 상태가 바뀌면 현재 CCTV 오버레이도 즉시 갱신합니다.
		_leftButton.onClick.AddListener(OnPreviousClicked);
		_rightButton.onClick.AddListener(OnNextClicked);

		SetUiText(_cctvHub.UsingCctvNumber);
		_cctvHub.OnCctvNumberChanged += SetUiText;

		// 본부 컨트롤러 활성 여부와 무관하게 공용 연결 상태 변경을 구독합니다.
		_cctvHub.OnAnyPointStateChanged += HandleCameraConnectionStateChanged;

		// 카메라 활성화
		_cctvHub.CctvCamera.enabled = true;
	}

	public override void DeactivateScreen()
	{
		base.DeactivateScreen();

		_leftButton.onClick.RemoveListener(OnPreviousClicked);
		_rightButton.onClick.RemoveListener(OnNextClicked);

		_cctvHub.OnCctvNumberChanged -= SetUiText;

		_cctvHub.OnAnyPointStateChanged -= HandleCameraConnectionStateChanged;

		// 카메라 비활성화
		_cctvHub.CctvCamera.enabled = false;
	}

	// CCTV 화면의 아이템 조준 표시에 카메라를 넘겨 동작시킵니다. UI는 프리팹에 만들어져 있습니다.
	private void SetupItemReticle()
	{
		_itemReticle = GetComponent<CCTVItemReticle>();

		if (_itemReticle == null)
		{
			Debug.LogError($"'{name}'에 CCTVItemReticle 컴포넌트가 없습니다.", this);
			return;
		}

		_itemReticle.Initialize(_cctvHub.CctvCamera);
	}

	// 이전 CCTV 화면으로 이동합니다.
	private void OnPreviousClicked()
	{
		_cctvHub.SwitchToPrevious();
	}

	// 다음 CCTV 화면으로 이동합니다.
	private void OnNextClicked()
	{
		_cctvHub.SwitchToNext();
	}

	// 현재 카메라 번호를 표시하고 해당 카메라의 단절 화면을 갱신합니다.
	private void SetUiText(int cameraNum)
	{
		// 카메라 인덱스가 0부터 시작하는 문제 수정을 위해 1 더해서 값 설정
		_cctvText.text = $"Cam {cameraNum + 1}";
		RefreshDisconnectedOverlay(cameraNum);
	}

	// 현재 보고 있는 CCTV의 상태가 바뀐 경우에만 단절 화면을 즉시 갱신합니다.
	private void HandleCameraConnectionStateChanged(int cameraIndex, CCTVConnectionState _)
	{
		// 현재 보고 있지 않은 CCTV는 전환하는 순간 갱신하므로 여기서는 건너뜁니다.
		if (cameraIndex == _cctvHub.UsingCctvNumber)
		{
			RefreshDisconnectedOverlay(cameraIndex);
		}
	}

	// 현재 CCTV가 Disconnected 상태일 때만 공용 단절 이미지를 표시합니다.
	private void RefreshDisconnectedOverlay(int cameraIndex)
	{
		if (_disconnectedOverlay == null)
		{
			return;
		}

		bool shouldShow = _cctvHub.GetPoint(cameraIndex).ConnectionState == CCTVConnectionState.Disconnected;
		_disconnectedOverlay.SetActive(shouldShow);

		// 영상이 끊긴 화면에서는 조준 판정을 아예 돌리지 않는다.
		if (_itemReticle != null)
		{
			_itemReticle.enabled = !shouldShow;
		}
	}
}
