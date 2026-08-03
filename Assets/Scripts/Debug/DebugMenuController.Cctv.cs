using UnityEngine;

public sealed partial class DebugMenuController
{
    private int _selectedCctvIndex = -1;

    [Header("=== CCTV Hub 등록 ===")]
    [SerializeField] private CCTVHub _cctvHub;

    // CCTV를 선택하고 전원 제어 하위 메뉴를 토글합니다.
    public void OnSelectCctv1Click() => SelectCctv(0);
    public void OnSelectCctv2Click() => SelectCctv(1);
    public void OnSelectCctv3Click() => SelectCctv(2);
    public void OnSelectCctv4Click() => SelectCctv(3);
    public void OnSelectCctv5Click() => SelectCctv(4);

    // 선택한 CCTV를 정상 연결 상태로 변경합니다.
    public void OnEnableSelectedCctvClick() => SetSelectedCctvState(CCTVConnectionState.Connected);
    // 선택한 CCTV를 일부 연결된 반작동 상태로 변경합니다.
    public void OnPartialSelectedCctvClick() => SetSelectedCctvState(CCTVConnectionState.Partial);
    // 선택한 CCTV를 연결 해제 상태로 변경합니다.
    public void OnDisableSelectedCctvClick() => SetSelectedCctvState(CCTVConnectionState.Disconnected);

    // 제어할 CCTV 번호를 저장하고 동일 버튼을 다시 누르면 하위 메뉴를 닫습니다.
    private void SelectCctv(int index)
    {
        bool shouldOpen = _selectedCctvIndex != index || _cctvPowerPanel == null || !_cctvPowerPanel.activeSelf;
        _selectedCctvIndex = index;
        if (_cctvSelectionText != null)
        {
            _cctvSelectionText.text = $"CCTV {index + 1}";
        }

        _cctvPowerPanel?.SetActive(shouldOpen);
    }

    // 선택된 CCTV 상태를 연결 마스크로 변환해 네트워크 상태에 반영합니다.
    private void SetSelectedCctvState(CCTVConnectionState state)
    {
        if (_selectedCctvIndex < 0 || _selectedCctvIndex >= _cctvHub.CameraCount)
        {
            ShowStatus("선택한 CCTV 번호가 올바르지 않습니다.");
            return;
        }

        int connectionMask = state switch
        {
            CCTVConnectionState.Connected => CCTVPoint.FullConnectionMask,
            CCTVConnectionState.Partial => 1,
            _ => 0
        };

        if (CCTVConnectionNetworkState.Instance != null)
        {
            CCTVConnectionNetworkState.Instance.RequestConnectionMask(_selectedCctvIndex, connectionMask);
        }
        else
        {
            _cctvHub.ApplyConnectionMask(_selectedCctvIndex, connectionMask);
        }

        string stateLabel = state switch
        {
            CCTVConnectionState.Connected => "켰습니다",
            CCTVConnectionState.Partial => "반작동 상태로 변경했습니다",
            _ => "껐습니다"
        };
        ShowStatus($"CCTV {_selectedCctvIndex + 1}을(를) {stateLabel}.");
    }

    // CCTV 수리 화면 완료용으로 모든 CCTV를 정상 연결 상태로 복구합니다.
    private void SetAllCctvConnected()
    {
        for (int cameraIndex = 0; cameraIndex < _cctvHub.CameraCount; cameraIndex++)
        {
            if (CCTVConnectionNetworkState.Instance != null)
            {
                CCTVConnectionNetworkState.Instance.RequestConnectionMask(
                    cameraIndex, CCTVPoint.FullConnectionMask);
            }
            else
            {
                _cctvHub.ApplyConnectionMask(cameraIndex, CCTVPoint.FullConnectionMask);
            }
        }
    }
}
