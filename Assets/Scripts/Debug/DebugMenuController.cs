#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


// 실제 uGUI 프리팹의 버튼 이벤트를 처리하는 개발용 컨트롤러입니다.
// UI 오브젝트는 코드에서 생성하지 않습니다.
public sealed partial class DebugMenuController : NetworkBehaviour
{
    private const float FastWalkMultiplier = 3f;
    private const string WaitingRoomSceneName = "WaitingRoom";

    private static readonly FieldInfo MoveSpeedField =
        typeof(PlayerMoveSample).GetField("_moveSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
    [Header("Menu")]
    [SerializeField] private GameObject _menuRoot;
    [SerializeField] private GameObject _teleportPanel;
    [SerializeField] private GameObject _playerPanel;
    [SerializeField] private GameObject _miniGamePanel;
    [SerializeField] private GameObject _itemPanel;
    [SerializeField] private GameObject _interferencePanel;
    [SerializeField] private GameObject _cctvPanel;
    [SerializeField] private GameObject _cctvPowerPanel;
    [SerializeField] private GameObject _regionPanel;
    [SerializeField] private GameObject _closeButton;

    [Header("Feedback")]
    [SerializeField] private TMP_Text _fastWalkButtonText;
    [SerializeField] private TMP_Text _hqFieldButtonText;
    [SerializeField] private TMP_Text _cctvSelectionText;

    [Header("Dynamic Player Buttons")]
    [SerializeField] private Button[] _otherPlayerButtons;
    [SerializeField] private TMP_Text[] _otherPlayerButtonTexts;

    private readonly Dictionary<PlayerMoveSample, float> _originalMoveSpeeds = new();
    private bool _fastWalkEnabled;
    private int _selectedCctvIndex = -1;
    private float _nextLocationLabelRefreshTime;

    // 메뉴를 닫힌 초기 상태로 만들고 표시 텍스트를 동기화합니다.
    private void Awake()
    {
        SetMenuVisible(false);
        SetAllSubMenusInactive();
        RefreshButtonLabels();
    }

    // 활성 씬 변경 시 디버그 상태를 정리할 수 있도록 이벤트를 구독합니다.
    private void OnEnable()
    {
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
    }

    // 이벤트 구독과 디버그 이동속도 변경을 원래 상태로 복구합니다.
    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        RestoreWalkSpeed();
    }

    // F9 토글, 위치 버튼 문구 갱신, 메뉴 바깥 클릭 닫기를 처리합니다.
    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
        {
            SetMenuVisible(_menuRoot != null && !_menuRoot.activeSelf);
        }

        if (_menuRoot != null && _menuRoot.activeSelf && Time.unscaledTime >= _nextLocationLabelRefreshTime)
        {
            _nextLocationLabelRefreshTime = Time.unscaledTime + 0.2f;
            RefreshHqFieldButtonLabel();
        }

        if (_menuRoot != null &&
            _menuRoot.activeSelf &&
            Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame &&
            !IsPointerOverDebugUi(Mouse.current.position.ReadValue()))
        {
            SetMenuVisible(false);
        }
    }

    // 디버그 메뉴 전체를 닫습니다.
    public void OnCloseButtonClick()
    {
        SetMenuVisible(false);
    }

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
    public void OnMiniGameMenuClick()
    {
        ToggleNestedSubMenu(_miniGamePanel, _playerPanel);
    }

    // 아이템 하위 메뉴를 열거나 닫습니다.
    public void OnItemMenuClick()
    {
        ToggleRootSubMenu(_itemPanel);
    }

    // 방해요소 하위 메뉴를 열거나 닫습니다.
    public void OnInterferenceMenuClick()
    {
        ToggleRootSubMenu(_interferencePanel);
    }

    // CCTV 선택 하위 메뉴를 열거나 닫습니다.
    public void OnCctvMenuClick()
    {
        ToggleRootSubMenu(_cctvPanel);
    }

    // 지역 해방 하위 메뉴를 열거나 닫습니다.
    public void OnRegionMenuClick()
    {
        ToggleRootSubMenu(_regionPanel);
    }

    // 로컬 플레이어의 걷기 속도를 3배로 토글하고 원래 값을 보존합니다.
    public void OnToggleFastWalkClick()
    {
        if (_fastWalkEnabled)
        {
            RestoreWalkSpeed();
            ShowStatus("빨리 걷기를 취소했습니다.");
            return;
        }

        Player localPlayer = GetLocalPlayer();
        if (localPlayer == null || localPlayer.PlayerMove == null || MoveSpeedField == null)
        {
            ShowStatus("로컬 플레이어 이동 컴포넌트를 찾지 못했습니다.");
            return;
        }

        PlayerMoveSample move = localPlayer.PlayerMove;
        float originalSpeed = (float)MoveSpeedField.GetValue(move);
        _originalMoveSpeeds[move] = originalSpeed;
        MoveSpeedField.SetValue(move, originalSpeed * FastWalkMultiplier);
        _fastWalkEnabled = true;
        RefreshButtonLabels();
        ShowStatus("걷기 속도를 3배로 변경했습니다.");
    }

    // 메뉴 표시 상태와 커서 모드를 함께 전환합니다.
    private void SetMenuVisible(bool visible)
    {
        if (_menuRoot == null || _menuRoot.activeSelf == visible)
        {
            return;
        }

        _menuRoot.SetActive(visible);
        if (visible)
        {
            SetAllSubMenusInactive();
            RefreshButtonLabels();
            RefreshOtherPlayerButtons();
            GameplayUiMode.Instance?.ActivateCursor();
        }
        else
        {
            GameplayUiMode.Instance?.DeactivateCursor();
        }
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

    // 모든 하위 메뉴를 닫되 항상 닫기 버튼은 표시합니다.
    private void SetAllSubMenusInactive()
    {
        GameObject[] panels =
        {
            _teleportPanel,
            _playerPanel,
            _miniGamePanel,
            _itemPanel,
            _interferencePanel,
            _cctvPanel,
            _cctvPowerPanel,
            _regionPanel
        };

        foreach (GameObject panel in panels)
        {
            panel?.SetActive(false);
        }

        _closeButton?.SetActive(true);
    }

    // 빨리 걷기가 적용된 모든 이동 컴포넌트를 저장된 원래 속도로 복구합니다.
    private void RestoreWalkSpeed()
    {
        foreach (KeyValuePair<PlayerMoveSample, float> entry in _originalMoveSpeeds)
        {
            if (entry.Key != null && MoveSpeedField != null)
            {
                MoveSpeedField.SetValue(entry.Key, entry.Value);
            }
        }

        _originalMoveSpeeds.Clear();
        _fastWalkEnabled = false;
        RefreshButtonLabels();
    }

    // 현재 디버그 상태에 맞춰 동적 버튼 문구를 갱신합니다.
    private void RefreshButtonLabels()
    {
        if (_fastWalkButtonText != null)
        {
            _fastWalkButtonText.text = _fastWalkEnabled ? "빨리 걷기 X3 취소" : "빨리 걷기 X3";
        }

        RefreshHqFieldButtonLabel();
    }

    // 현재 위치에 따라 목적지 버튼을 '본부로 이동' 또는 '필드로 이동'으로 표시합니다.
    private void RefreshHqFieldButtonLabel()
    {
        if (_hqFieldButtonText == null)
        {
            return;
        }

        Player player = GetLocalPlayer();
        HqEntrance entrance = FindFirstObjectByType<HqEntrance>();
        HqExit exit = FindFirstObjectByType<HqExit>();
        _hqFieldButtonText.text =
            player != null && entrance != null && exit != null &&
            IsCloserToHq(player.transform.position, entrance, exit)
                ? "필드로 이동"
                : "본부로 이동";
    }

    // 자신을 제외한 최대 세 명의 플레이어 이름과 버튼 표시 여부를 갱신합니다.
    private void RefreshOtherPlayerButtons()
    {
        Player localPlayer = GetLocalPlayer();
        Player[] otherPlayers = FindObjectsByType<Player>(FindObjectsSortMode.None)
            .Where(player => player != localPlayer)
            .OrderBy(player => player.OwnerClientId)
            .Take(3)
            .ToArray();

        int buttonCount = Mathf.Min(
            _otherPlayerButtons?.Length ?? 0,
            _otherPlayerButtonTexts?.Length ?? 0);
        for (int index = 0; index < buttonCount; index++)
        {
            bool hasPlayer = index < otherPlayers.Length;
            if (_otherPlayerButtons[index] != null)
            {
                _otherPlayerButtons[index].gameObject.SetActive(hasPlayer);
            }

            if (hasPlayer && _otherPlayerButtonTexts[index] != null)
            {
                _otherPlayerButtonTexts[index].text = GetPlayerDisplayName(otherPlayers[index]);
            }
        }
    }

    // 네트워크 플레이어 이름을 읽고 아직 준비되지 않았으면 임시 이름을 반환합니다.
    private static string GetPlayerDisplayName(Player player)
    {
        try
        {
            return player.PlayerName;
        }
        catch (InvalidOperationException)
        {
            return $"Player {player.OwnerClientId + 1}";
        }
    }

    // 디버그 메뉴 작업 결과를 공통 접두사가 포함된 콘솔 로그로 출력합니다.
    private void ShowStatus(string message)
    {
        Debug.Log($"[DebugMenu] {message}", this);
    }

    // 현재 포인터 아래 UI가 이 디버그 메뉴에 속하는지 레이캐스트로 확인합니다.
    private bool IsPointerOverDebugUi(Vector2 screenPosition)
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        PointerEventData pointerData = new(EventSystem.current)
        {
            position = screenPosition
        };
        List<RaycastResult> raycastResults = new();
        EventSystem.current.RaycastAll(pointerData, raycastResults);

        return raycastResults.Any(result =>
            result.gameObject != null &&
            result.gameObject.transform.IsChildOf(transform));
    }

    // 네트워크 소유권을 우선 사용해 현재 클라이언트의 로컬 플레이어를 찾습니다.
    private static Player GetLocalPlayer()
    {
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out Player networkPlayer))
        {
            return networkPlayer;
        }

        return FindObjectsByType<Player>(FindObjectsSortMode.None).FirstOrDefault(player => player.IsOwner);
    }

    // 씬 전환 시 메뉴를 닫고 일시적으로 변경한 이동속도를 복구합니다.
    private void HandleActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        SetMenuVisible(false);
        RestoreWalkSpeed();
        ClearSavedFieldReturnPoses();
    }
}
#endif
