using System;
using System.Collections.Generic;
using System.Reflection;
using EPOOutline;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class CCTVHub : MonoBehaviour {
	private const string OutlineOverlayCameraName = "CCTVOutlineCamera";

	[SerializeField] private Camera _cctvCamera;
	private Camera _outlineOverlayCamera;

	private readonly List<CCTVPoint> _cctvPoints = new List<CCTVPoint>();
	private Dictionary<RegionId, CCTVRegion> _cctvRegions = new();

	public IReadOnlyList<CCTVPoint> CCTVPoints => _cctvPoints;
	public int CameraCount => _cctvPoints.Count;

	// CCTV를 특정 포인트들로 옮겨가면서 여러 위치의 CCTV를 구현
	private int _usingCctvNumber;
	public int UsingCctvNumber => _usingCctvNumber;
	
	// 사용하는 CCTV 외부에서 접근 가능하도록 공유
	public Camera CctvCamera => _cctvCamera;

	public event Action<int> OnCctvNumberChanged;

	// 개별 CCTV 포인트의 연결 상태 변경을 카메라 번호와 함께 한 곳에서 받고 싶은 소비자를 위한 집계 이벤트
	public event Action<int, CCTVConnectionState> OnAnyPointStateChanged;
	public event Action OnCctvPointsActivated;

	private void Awake() {
		ConfigureItemOutlines();
	}

	// 시작할 때 CCTV Region 찾고, 초기화
	public void Initialize() {
		ConfigureItemOutlines();

		CCTVRegion[] regions = GetComponentsInChildren<CCTVRegion>();
		foreach (var region in regions) {
			_cctvRegions[region.RegionId] = region;
			region.Initialize();
		}
	}
	
	public void ActivateCCTVInRegion(RegionId id) {
		// 이전 포인트들이 있었다면 비활성화
		DeactivateAllPoints();

		if (!_cctvRegions.TryGetValue(id, out CCTVRegion region)) {
			Debug.LogError($"'{id}' 구역에 등록된 CCTVRegion이 없습니다.", this);
			return;
		}

		foreach (CCTVPoint point in region.Points) {
			if (point == null) {
				Debug.LogError($"'{id}' 구역에 비어있는 CCTVPoint 참조가 있습니다.", this);
				continue;
			}

			point.Initialize(_cctvPoints.Count);
			// CCTV 상태 변경 시에 변경된 CCTV 번호와 상태를 발행해주는 이벤트
			point.OnConnectionStateChanged += state => OnAnyPointStateChanged?.Invoke(point.CameraNumber, state);
			_cctvPoints.Add(point);
		}

		if (_cctvPoints.Count == 0) {
			Debug.LogError($"'{id}' 구역에 활성화할 CCTVPoint가 없습니다.", this);
			return;
		}

		// 사용중인 포인트 0번으로 수정
		_usingCctvNumber = 0;
		SwitchCCTV(_usingCctvNumber);
		OnCctvPointsActivated?.Invoke();
	}

	private void ConfigureItemOutlines() {
		if (_cctvCamera == null) {
			Debug.LogError("CCTV 카메라 참조가 없습니다.", this);
			return;
		}

		// NameToLayer는 레이어가 없으면 -1을 돌려준다. 그대로 두면 컬링 마스크가 엉뚱한 비트로 조용히 깨진다.
		if (Layers.Item < 0) {
			Debug.LogError("프로젝트 설정에 'Item' 레이어가 없어 CCTV 아이템 외곽선을 설정할 수 없습니다.", this);
			return;
		}

		Layers.ShowLayerToCamera(_cctvCamera, Layers.Item);
		Layers.ShowLayerToCamera(_cctvCamera, Layers.CCTVPostProcessing);

		SetupOutlineOverlayCamera();
	}

	// 야간투시 볼륨이 화면 채도를 없애서, 후처리 전에 그리면 외곽선이 무조건 흰색이 된다.
	// 후처리를 끈 URP 오버레이 카메라를 스택에 얹어 본 화면 위에 원래 색으로 덧그린다.
	private void SetupOutlineOverlayCamera() {
		// Awake와 Initialize 양쪽에서 불리므로 이미 만들어 뒀으면 그대로 둔다.
		if (_outlineOverlayCamera != null) {
			return;
		}

		// 본 카메라에 Outliner가 남아 있으면 야간투시 후처리에 채도가 죽어 외곽선이 흰색이 된다.
		Outliner baseOutliner = _cctvCamera.GetComponent<Outliner>();
		if (baseOutliner != null) {
			Destroy(baseOutliner);
		}

		GameObject overlayObject = new GameObject(OutlineOverlayCameraName);
		overlayObject.transform.SetParent(_cctvCamera.transform, false);

		Camera overlayCamera = overlayObject.AddComponent<Camera>();
		overlayCamera.CopyFrom(_cctvCamera);
		overlayCamera.targetTexture = null;             // 오버레이는 베이스 카메라의 타깃에 그린다.
		overlayCamera.cullingMask = 1 << Layers.Item;   // 아이템만 다시 그린다.

		UniversalAdditionalCameraData overlayData = overlayCamera.GetUniversalAdditionalCameraData();
		overlayData.renderType = CameraRenderType.Overlay;
		overlayData.renderPostProcessing = false;
		SetClearDepth(overlayData, false);              // 깊이를 유지해야 벽 뒤 아이템이 비치지 않는다.

		Outliner overlayOutliner = overlayObject.AddComponent<Outliner>();
		overlayOutliner.OutlineLayerMask = ItemBase.CctvOutlineMask;
		overlayOutliner.PrimaryRendererScale = 1f;
		overlayOutliner.PrimarySizeReference = 800;
		overlayOutliner.DilateShift = 1f;
		// 조준 괄호 표시와 겹쳐도 지저분하지 않도록 외곽선은 얇게 둔다.
		overlayOutliner.DilateIterations = 1;
		overlayOutliner.BlurShift = 1f;
		overlayOutliner.BlurIterations = 1;

		// 스택을 비우지 않는다. 다른 곳에서 이 카메라에 붙여 둔 오버레이가 있으면 그대로 둬야 한다.
		UniversalAdditionalCameraData baseData = _cctvCamera.GetUniversalAdditionalCameraData();
		if (!baseData.cameraStack.Contains(overlayCamera)) {
			baseData.cameraStack.Add(overlayCamera);
		}

		_outlineOverlayCamera = overlayCamera;
	}

	// URP의 clearDepth는 읽기 전용 프로퍼티라 직렬화 필드를 직접 건드려야 한다.
	private static void SetClearDepth(UniversalAdditionalCameraData cameraData, bool clearDepth) {
		FieldInfo field = typeof(UniversalAdditionalCameraData)
			.GetField("m_ClearDepth", BindingFlags.NonPublic | BindingFlags.Instance);

		if (field == null) {
			Debug.LogWarning("URP의 m_ClearDepth 필드를 찾지 못했습니다. 오버레이 외곽선이 벽 뒤로 비칠 수 있습니다.");
			return;
		}

		field.SetValue(cameraData, clearDepth);
	}

	private void DeactivateAllPoints() {
		foreach (CCTVPoint point in _cctvPoints) {
			point.Deactivate();
		}
		_cctvPoints.Clear();
	}

	// 카메라 번호로 해당 CCTV 포인트를 바로 찾아간다.
	public CCTVPoint GetPoint(int cameraNumber) {
		return _cctvPoints[NormalizeIndex(cameraNumber)];
	}

	// 포인트 참조 없이 카메라 번호만 아는 소비자(네트워크 레이어 등)를 위한 전달 창구.
	public void ApplyConnectionMask(int cameraNumber, int connectionMask) {
		GetPoint(cameraNumber).ApplyConnectionMask(connectionMask);
	}

	public void SwitchToPrevious() {
		SwitchCCTV(_usingCctvNumber - 1);	
	}
	public void SwitchToNext() {
		SwitchCCTV(_usingCctvNumber + 1);
	}
	
	private void SwitchCCTV(int number) {
		if (_cctvPoints.Count == 0) {
			return;
		}

		number = NormalizeIndex(number);
		_usingCctvNumber = number;
		
		// CCTV 포인트와 완전히 동일한 위치에 놓이도록 할 것
		_cctvCamera.transform.SetParent(_cctvPoints[number].transform);
		_cctvCamera.transform.localPosition = Vector3.zero;
		_cctvCamera.transform.localRotation = Quaternion.identity;
		
		OnCctvNumberChanged?.Invoke(_usingCctvNumber);
	}
	
	// 값 자체를 0 ~ CctvPoints.Count - 1 안의 값으로 넣어주기 위한 함수.
	private int NormalizeIndex(int index) {
		if (_cctvPoints.Count == 0) {
			return 0;
		}

		return (index % _cctvPoints.Count + _cctvPoints.Count)
		       % _cctvPoints.Count;
	}
}
