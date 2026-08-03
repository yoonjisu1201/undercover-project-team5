using System.Linq;
using GlitchSample;
using UnityEngine;
using UnityEngine.Rendering;
using URPGlitch;

// 공용 CCTV 연결 상태를 실제 후처리와 벽면 모니터에 반영합니다.
[RequireComponent(typeof(Volume))]
public sealed class CCTVGlitchController : MonoBehaviour
{
    private AnalogGlitchVolume _analogGlitch;
    private UndercoverBlockGlitchVolume _blockGlitch;
    private CCTVHub _cctvHub;
    private Material _wallDisconnectedMaterial;
    private MeshRenderer[] _wallScreens;
    private Material[] _wallDefaultMaterials;

    public bool IsGlitchActive { get; private set; }

    // CCTV 전용 Volume 효과를 캐시하고 시작 상태를 비활성화합니다.
    private void Awake()
    {
        // CCTV 카메라 전용 Volume Profile의 두 글리치 효과를 캐시합니다.
        Volume cctvVolume = GetComponent<Volume>();
        cctvVolume.profile.TryGet(out _analogGlitch);
        cctvVolume.profile.TryGet(out _blockGlitch);

        SetGlitchActive(false);
    }

    // 공용 CCTV 상태 변경 이벤트를 구독합니다.
    private void OnEnable()
    {
        // Start 이후 다시 활성화된 경우 CCTV 번호/연결 상태 변경 이벤트도 다시 구독합니다.
        if (_cctvHub != null)
        {
            _cctvHub.OnCctvNumberChanged += HandleCctvNumberChanged;
            _cctvHub.OnAnyPointStateChanged += HandleCameraConnectionStateChanged;
        }
    }

    // CCTVHub와 벽면 모니터를 찾은 뒤 현재 연결 상태를 화면에 반영합니다.
    private void Start()
    {
        // CCTVHub가 Awake에서 카메라 위치 목록을 만든 뒤 현재 카메라 상태를 반영합니다.
        _cctvHub = GetComponentInParent<CCTVHub>();
        _cctvHub.OnCctvNumberChanged += HandleCctvNumberChanged;
        _cctvHub.OnAnyPointStateChanged += HandleCameraConnectionStateChanged;

        InitializeWallScreens();
        RefreshGlitchState();
        RefreshAllWallScreens();
    }

    // 비활성화할 때 이벤트를 해제해 재활성화 시 중복 구독되는 것을 방지합니다.
    private void OnDisable()
    {
        if (_cctvHub != null)
        {
            _cctvHub.OnCctvNumberChanged -= HandleCctvNumberChanged;
            _cctvHub.OnAnyPointStateChanged -= HandleCameraConnectionStateChanged;
        }
    }

    // 사용자가 다른 CCTV로 이동하면 해당 카메라의 글리치 상태를 반영합니다.
    private void HandleCctvNumberChanged(int _)
    {
        RefreshGlitchState();
    }

    // CCTV 연결 상태가 바뀌면 현재 영상과 해당 벽면 모니터를 갱신합니다.
    private void HandleCameraConnectionStateChanged(
        int cameraIndex,
        CCTVConnectionState _)
    {
        RefreshGlitchState();

        if (_wallScreens != null)
        {
            RefreshWallScreens(cameraIndex);
        }
    }

    // 현재 보고 있는 CCTV가 Partial일 때만 전용 Volume 글리치를 활성화합니다.
    private void RefreshGlitchState()
    {
        if (_cctvHub == null)
        {
            return;
        }

        CCTVConnectionState state = _cctvHub.GetPoint(_cctvHub.UsingCctvNumber).ConnectionState;
        SetGlitchActive(state == CCTVConnectionState.Partial);
    }

    // 씬의 벽면 CCTV 화면과 원래 재질을 카메라 번호 순서대로 캐시합니다.
    private void InitializeWallScreens()
    {
        // 벽면 CCTV를 이름순으로 정렬해 실행마다 동일한 카메라 번호에 대응시킵니다.
        _wallDisconnectedMaterial = Resources.Load<Material>("CCTV/CCTVDisconnectedMaterial");
        _wallScreens = Resources.FindObjectsOfTypeAll<MeshRenderer>()
            .Where(renderer => renderer.gameObject.scene.IsValid() && renderer.name.StartsWith("CCTVScreen_"))
            .OrderBy(renderer => renderer.name).ToArray();
        _wallDefaultMaterials = _wallScreens.Select(renderer => renderer.sharedMaterial).ToArray();
    }

    // 모든 벽면 CCTV 화면에 현재 연결 상태를 반영합니다.
    private void RefreshAllWallScreens()
    {
        for (int cameraIndex = 0; cameraIndex < _cctvHub.CameraCount; cameraIndex++)
        {
            RefreshWallScreens(cameraIndex);
        }
    }

    // 지정한 CCTV 번호에 대응하는 벽면 화면만 갱신합니다.
    private void RefreshWallScreens(int cameraIndex)
    {
        for (int screenIndex = 0; screenIndex < _wallScreens.Length; screenIndex++)
        {
            if (GetCameraIndexForWallScreen(screenIndex) != cameraIndex)
            {
                continue;
            }

            // 완전 단절 상태만 정지 이미지를 사용합니다.
            bool isDisconnected = _cctvHub.GetPoint(cameraIndex).ConnectionState == CCTVConnectionState.Disconnected;
            _wallScreens[screenIndex].sharedMaterial = isDisconnected ? _wallDisconnectedMaterial : _wallDefaultMaterials[screenIndex];
        }
    }

    // 이름순 벽면 화면을 1~5번 CCTV에 순환 배정합니다. 예: 01·06·11번 화면은 CCTV 1에 대응합니다.
    private int GetCameraIndexForWallScreen(int screenIndex)
    {
        return screenIndex % _cctvHub.CameraCount;
    }

    // CCTV 전용 Analog 및 Block 글리치 효과를 함께 켜거나 끕니다.
    private void SetGlitchActive(bool isActive)
    {
        IsGlitchActive = isActive;

        if (_analogGlitch != null)
        {
            _analogGlitch.active = IsGlitchActive;
        }

        if (_blockGlitch != null)
        {
            _blockGlitch.active = IsGlitchActive;
        }
    }
}
