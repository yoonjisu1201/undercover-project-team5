using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 디버그 메뉴의 패널 전환과 버튼 시각 상태를 관리합니다.
public sealed partial class DebugMenuController
{
    [Header("Menu")]
    [SerializeField] private GameObject _menuRoot;
    [SerializeField] private GameObject _teleportPanel;
    [SerializeField] private GameObject _playerPanel;
    [SerializeField] private GameObject _miniGamePanel;
    [SerializeField] private GameObject _itemPanel;
    [SerializeField] private GameObject _rolePanel;
    [SerializeField] private GameObject _criminalPanel;
    [SerializeField] private GameObject _interferencePanel;
    [SerializeField] private GameObject _cctvPanel;
    [SerializeField] private GameObject _cctvPowerPanel;
    [SerializeField] private GameObject _regionPanel;
    [SerializeField] private GameObject _roundPanel;
    [SerializeField] private GameObject _hpPanel;
    [SerializeField] private GameObject _closeButton;
    [SerializeField] private Button[] _regionButtons;
    [SerializeField] private Button _criminalMarkerButton;
    [SerializeField] private Button _soloCaptureButton;
    [SerializeField] private Button _criminalFreezeButton;
    [SerializeField] private Button _timeStopButton;

    [Header("Feedback")]
    [SerializeField] private TMP_Text _fastWalkButtonText;
    [SerializeField] private TMP_Text _hqFieldButtonText;
    [SerializeField] private TMP_Text _cctvSelectionText;

    // 디버그 메뉴 전체를 닫습니다.
    public void OnCloseButtonClick() => SetMenuVisible(false);

    // 텔레포트 루트 하위 메뉴를 열거나 닫습니다.
    public void OnTeleportMenuClick()
    {
        ToggleRootSubMenu(_teleportPanel);
        RefreshHqFieldButtonLabel();
    }

    // 다른 플레이어 목록을 갱신하고 플레이어 하위 메뉴를 토글합니다.
    public void OnPlayerMenuClick()
    {
        ToggleNestedSubMenu(_playerPanel, _miniGamePanel);
        RefreshOtherPlayerButtons();
    }

    // 미니게임 텔레포트 하위 메뉴를 토글합니다.
    public void OnMiniGameMenuClick() => ToggleNestedSubMenu(_miniGamePanel, _playerPanel);

    // 각 루트 하위 메뉴를 열거나 닫습니다.
    public void OnItemMenuClick() => ToggleRootSubMenu(_itemPanel);
    public void OnInterferenceMenuClick() => ToggleRootSubMenu(_interferencePanel);

    // 역할 메뉴를 열고 현재 역할 색상을 반영합니다.
    public void OnRoleMenuClick()
    {
        ToggleRootSubMenu(_rolePanel);
        RefreshRoleButtonColors();
    }

    // 범인 메뉴를 열 때 외계인 하위 메뉴는 접은 상태에서 시작합니다.
    public void OnCriminalMenuClick()
    {
        ToggleRootSubMenu(_criminalPanel);
        _alienPanel?.SetActive(false);
    }
    public void OnCctvMenuClick() => ToggleRootSubMenu(_cctvPanel);
    public void OnRoundMenuClick() => ToggleRootSubMenu(_roundPanel);
    public void OnHpMenuClick() => ToggleRootSubMenu(_hpPanel);

    // 지역 메뉴를 열고 현재 해방 상태의 색상을 반영합니다.
    public void OnRegionMenuClick()
    {
        ToggleRootSubMenu(_regionPanel);
        RefreshRegionButtonColors();
    }

    // 메뉴 표시 상태와 커서 모드를 함께 전환합니다.
    private void SetMenuVisible(bool visible)
    {
        if (_menuRoot == null || _menuRoot.activeSelf == visible) return;

        _menuRoot.SetActive(visible);
        if (visible)
        {
            SetAllSubMenusInactive();
            RefreshButtonLabels();
            RefreshOtherPlayerButtons();
            GameplayUiMode.Instance?.ActivateCursor();
            return;
        }

        GameplayUiMode.Instance?.DeactivateCursor();
    }

    // 다른 루트 메뉴를 모두 닫은 뒤 선택한 메뉴만 토글합니다.
    private void ToggleRootSubMenu(GameObject target)
    {
        bool shouldOpen = target != null && !target.activeSelf;
        SetAllSubMenusInactive();
        target?.SetActive(shouldOpen);
        _closeButton?.SetActive(true);
    }

    // 같은 단계의 형제 메뉴를 닫고 선택한 중첩 메뉴를 토글합니다.
    private static void ToggleNestedSubMenu(GameObject target, GameObject sibling)
    {
        bool shouldOpen = target != null && !target.activeSelf;
        sibling?.SetActive(false);
        target?.SetActive(shouldOpen);
    }

    // 모든 하위 메뉴를 닫되 닫기 버튼은 항상 표시합니다.
    private void SetAllSubMenusInactive()
    {
        GameObject[] panels =
        {
            _teleportPanel, _playerPanel, _miniGamePanel, _itemPanel, _rolePanel,
            _criminalPanel, _interferencePanel, _cctvPanel, _cctvPowerPanel, _regionPanel,
            _roundPanel, _hpPanel, _missionPanel, _missionCompletePanel, _missionSoloPanel,
            _alienPanel
        };

        foreach (GameObject panel in panels) panel?.SetActive(false);
        _closeButton?.SetActive(true);
    }

    // A~F 지역 버튼 색상을 현재 해방 상태에 맞춥니다.
    private void RefreshRegionButtonColors()
    {
        if (_regionButtons == null) return;

        MapRegion[] regions = FindObjectsByType<MapRegion>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int index = 0; index < _regionButtons.Length; index++)
        {
            Button button = _regionButtons[index];
            if (button == null) continue;

            string regionId = ((char)('A' + index)).ToString();
            bool isUnlocked = regions.Any(region =>
                string.Equals(region.RegionId, regionId, StringComparison.OrdinalIgnoreCase) &&
                region.IsUnlocked);
            SetButtonStateColor(button, isUnlocked ? UnlockedRegionButtonColor : LockedRegionButtonColor);
        }
    }

    // 초록(켜짐)/빨강(꺼짐)으로 상태를 구분하는 버튼 색을 칠합니다.
    private static void SetOnOffButtonColor(Button button, bool isOn)
    {
        if (button != null)
        {
            SetButtonStateColor(button, isOn ? OnStateButtonColor : OffStateButtonColor);
        }
    }

    // ON/OFF 디버그 버튼의 강조 상태를 갱신합니다.
    private static void SetToggleButtonState(Button button, bool enabled)
    {
        if (button == null) return;
        SetButtonStateColor(button, enabled ? UnlockedRegionButtonColor : LockedRegionButtonColor);
    }

    // 버튼의 기본·선택 색상과 실제 그래픽 색상을 함께 변경합니다.
    private static void SetButtonStateColor(Button button, Color color)
    {
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.selectedColor = color;
        button.colors = colors;
        button.targetGraphic.color = color;
    }
}
