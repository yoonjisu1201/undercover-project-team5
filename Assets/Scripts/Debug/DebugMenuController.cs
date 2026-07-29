#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 실제 uGUI 프리팹의 버튼 이벤트를 처리하는 개발용 컨트롤러입니다.
/// UI 오브젝트는 코드에서 생성하지 않습니다.
/// </summary>
public sealed class DebugMenuController : NetworkBehaviour
{
    private const float FastWalkMultiplier = 3f;
    private const string WaitingRoomSceneName = "WaitingRoom";

    private static readonly FieldInfo MoveSpeedField =
        typeof(PlayerMoveSample).GetField("_moveSpeed", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo HqSpawnPointField =
        typeof(HqEntrance).GetField("_hqSpawnPoint", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo FieldSpawnPointField =
        typeof(HqExit).GetField("_fieldSpawnPoint", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo CctvPointsField =
        typeof(CCTVHub).GetField("_cctvPoints", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo CctvAreasField =
        typeof(CCTVHub).GetField("_cctvAreas", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo CctvCameraField =
        typeof(CCTVHub).GetField("_cctvCamera", BindingFlags.Instance | BindingFlags.NonPublic);

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
    [SerializeField] private Material _cctvGlitchMaterial;
    [SerializeField] private Texture _cctvDisconnectedTexture;

    [Header("Dynamic Player Buttons")]
    [SerializeField] private Button[] _otherPlayerButtons;
    [SerializeField] private TMP_Text[] _otherPlayerButtonTexts;

    private readonly Dictionary<PlayerMoveSample, float> _originalMoveSpeeds = new();
    private readonly Dictionary<RawImage, (Color Color, Rect UvRect, Material Material, Texture Texture)>
        _originalCctvImages = new();
    private readonly CctvDebugState[] _cctvStates = new CctvDebugState[5];
    private bool _fastWalkEnabled;
    private int _selectedCctvIndex = -1;
    private float _nextLocationLabelRefreshTime;
    private Camera _debugCctvCamera;
    private int _originalCctvCullingMask;

    private enum CctvDebugState
    {
        Enabled,
        Partial,
        Disabled
    }

    private void Awake()
    {
        SetMenuVisible(false);
        SetAllSubMenusInactive();
        RefreshButtonLabels();
    }

    private void OnEnable()
    {
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        RestoreWalkSpeed();
        RestoreCctvCamera();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
        {
            SetMenuVisible(_menuRoot != null && !_menuRoot.activeSelf);
        }

        ApplyCurrentCctvState();

        if (_menuRoot != null && _menuRoot.activeSelf && Time.unscaledTime >= _nextLocationLabelRefreshTime)
        {
            _nextLocationLabelRefreshTime = Time.unscaledTime + 0.2f;
            RefreshHqFieldButtonLabel();
        }
    }

    public void OnCloseButtonClick()
    {
        SetMenuVisible(false);
    }

    public void OnTeleportMenuClick()
    {
        ToggleRootSubMenu(_teleportPanel);
        RefreshHqFieldButtonLabel();
    }

    public void OnPlayerMenuClick()
    {
        ToggleNestedSubMenu(_playerPanel, _miniGamePanel);
        RefreshOtherPlayerButtons();
    }

    public void OnMiniGameMenuClick()
    {
        ToggleNestedSubMenu(_miniGamePanel, _playerPanel);
    }

    public void OnItemMenuClick()
    {
        ToggleRootSubMenu(_itemPanel);
    }

    public void OnInterferenceMenuClick()
    {
        ToggleRootSubMenu(_interferencePanel);
    }

    public void OnCctvMenuClick()
    {
        ToggleRootSubMenu(_cctvPanel);
    }

    public void OnRegionMenuClick()
    {
        ToggleRootSubMenu(_regionPanel);
    }

    public void OnTeleportPlayer1Click()
    {
        TeleportToOtherPlayer(0);
    }

    public void OnTeleportPlayer2Click()
    {
        TeleportToOtherPlayer(1);
    }

    public void OnTeleportPlayer3Click()
    {
        TeleportToOtherPlayer(2);
    }

    public void OnTeleportCriminalClick()
    {
        CriminalNpcManager manager = FindFirstObjectByType<CriminalNpcManager>();
        if (manager == null || manager.CriminalNpc == null)
        {
            ShowStatus("현재 범인 NPC를 찾지 못했습니다.");
            return;
        }

        Transform criminal = manager.CriminalNpc.transform;
        TeleportLocalPlayer(criminal.position - criminal.forward * 2f, criminal.rotation);
        ShowStatus("범인 위치로 이동했습니다.");
    }

    public void OnTeleportMiniGame1Click()
    {
        TeleportToMiniGame(0);
    }

    public void OnTeleportMiniGame2Click()
    {
        TeleportToMiniGame(1);
    }

    public void OnTeleportMiniGame3Click()
    {
        TeleportToMiniGame(2);
    }

    public void OnTeleportMiniGame4Click()
    {
        TeleportToMiniGame(3);
    }

    public void OnTeleportMiniGame5Click()
    {
        TeleportToMiniGame(4);
    }

    public void OnTeleportMiniGame6Click()
    {
        TeleportToMiniGame(5);
    }

    public void OnTeleportMiniGame7Click()
    {
        TeleportToMiniGame(6);
    }

    public void OnTeleportMiniGame8Click()
    {
        TeleportToMiniGame(7);
    }

    public void OnToggleHqFieldClick()
    {
        Player localPlayer = GetLocalPlayer();
        HqEntrance entrance = FindFirstObjectByType<HqEntrance>();
        HqExit exit = FindFirstObjectByType<HqExit>();
        if (localPlayer == null || entrance == null || exit == null)
        {
            ShowStatus("본부/필드 이동 지점을 찾지 못했습니다.");
            return;
        }

        if (IsCloserToHq(localPlayer.transform.position, entrance, exit))
        {
            exit.Interact(localPlayer.gameObject);
            ShowStatus("필드로 이동했습니다.");
        }
        else
        {
            entrance.Interact(localPlayer.gameObject);
            ShowStatus("본부로 이동했습니다.");
        }

        RefreshButtonLabels();
    }

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

    public void OnClearInventoryClick()
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestClearInventoryRpc();
        ShowStatus("인벤토리를 비웠습니다.");
    }

    public void OnFogInterferenceClick()
    {
        StartInterference(InterferenceEffectType.FieldVision);
    }

    public void OnGlitchInterferenceClick()
    {
        StartInterference(InterferenceEffectType.Glitch);
    }

    public void OnSelectCctv1Click() => SelectCctv(0);
    public void OnSelectCctv2Click() => SelectCctv(1);
    public void OnSelectCctv3Click() => SelectCctv(2);
    public void OnSelectCctv4Click() => SelectCctv(3);
    public void OnSelectCctv5Click() => SelectCctv(4);

    public void OnEnableSelectedCctvClick() => SetSelectedCctvActive(true);
    public void OnPartialSelectedCctvClick() => SetSelectedCctvState(CctvDebugState.Partial);
    public void OnDisableSelectedCctvClick() => SetSelectedCctvActive(false);

    public void OnCompleteCurrentGameClick()
    {
        MiniGameUIController controller = FindObjectsByType<MiniGameUIController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.gameObject.activeInHierarchy);
        if (controller == null)
        {
            ShowStatus("현재 열린 미니게임 UI가 없습니다.");
            return;
        }

        controller.MarkCompletionReady();
        controller.Close();
        ShowStatus("현재 미니게임을 완료 처리했습니다.");
    }

    public void OnUnlockRegionAClick() => UnlockRegion("A");
    public void OnUnlockRegionBClick() => UnlockRegion("B");
    public void OnUnlockRegionCClick() => UnlockRegion("C");
    public void OnUnlockRegionDClick() => UnlockRegion("D");
    public void OnUnlockRegionEClick() => UnlockRegion("E");
    public void OnUnlockRegionFClick() => UnlockRegion("F");

    public void OnArrestCriminalClick()
    {
        RoundManager manager = RoundManager.Instance;
        if (manager == null)
        {
            ShowStatus("RoundManager를 찾지 못했습니다.");
            return;
        }

        manager.ReportArrestServerRpc();
        ShowStatus("범인 검거 성공을 보고했습니다.");
    }

    public void OnNextRoundClick()
    {
        RoundManager manager = RoundManager.Instance;
        if (manager == null)
        {
            ShowStatus("RoundManager를 찾지 못했습니다.");
            return;
        }

        if (manager.CurrentState != RoundState.InRound)
        {
            ShowStatus($"현재 상태({manager.CurrentState})에서는 다음 라운드로 이동할 수 없습니다.");
            return;
        }

        manager.ReportArrestServerRpc();
        ShowStatus("현재 라운드를 클리어했습니다. 결과 대기 후 다음 라운드로 이동합니다.");
    }

    public void OnGoToWaitingRoomClick()
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        SetMenuVisible(false);
        RequestWaitingRoomRpc();
    }

    private void TeleportToOtherPlayer(int index)
    {
        Player localPlayer = GetLocalPlayer();
        Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None)
            .Where(player => player != localPlayer)
            .OrderBy(player => player.OwnerClientId)
            .ToArray();
        if (index < 0 || index >= players.Length)
        {
            ShowStatus("해당 플레이어를 찾지 못했습니다.");
            return;
        }

        Player target = players[index];
        Vector3 destination = target.transform.position - target.transform.forward * 1.5f;
        TeleportLocalPlayer(destination, target.transform.rotation);
        ShowStatus($"{GetPlayerDisplayName(target)} 위치로 이동했습니다.");
    }

    private void TeleportToMiniGame(int index)
    {
        MiniGameInteractable[] miniGames = FindObjectsByType<MiniGameInteractable>(FindObjectsSortMode.None)
            .OrderBy(miniGame => miniGame.name, StringComparer.Ordinal)
            .ThenBy(miniGame => miniGame.GetInstanceID())
            .ToArray();
        if (index < 0 || index >= miniGames.Length)
        {
            ShowStatus($"미니게임 {index + 1}을 찾지 못했습니다.");
            return;
        }

        Transform target = miniGames[index].transform;
        TeleportLocalPlayer(target.position - target.forward * 2f, target.rotation);
        ShowStatus($"미니게임 {index + 1} 위치로 이동했습니다.");
    }

    private void TeleportLocalPlayer(Vector3 destination, Quaternion rotation)
    {
        Player player = GetLocalPlayer();
        if (player == null || player.PlayerMove == null)
        {
            ShowStatus("로컬 플레이어를 찾지 못했습니다.");
            return;
        }

        destination.y += 0.2f;
        player.PlayerMove.TeleportToPosition(destination, rotation);
    }

    private void StartInterference(InterferenceEffectType effectType)
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestInterferenceRpc(effectType);
        ShowStatus($"{effectType} 방해요소를 요청했습니다.");
    }

    private void UnlockRegion(string regionId)
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestUnlockRegionRpc(regionId);
    }

    private void ApplyRegionUnlock(string regionId)
    {
        MapRegion region = FindObjectsByType<MapRegion>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => string.Equals(candidate.RegionId, regionId, StringComparison.OrdinalIgnoreCase));
        if (region == null)
        {
            ShowStatus($"지역 {regionId}를 찾지 못했습니다.");
            return;
        }

        region.Unlock();
        FindFirstObjectByType<MapRegionController>()?.RefreshSpawnAreas();
        ShowStatus($"지역 {regionId}를 해방했습니다.");
    }

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

    private void ToggleRootSubMenu(GameObject target)
    {
        bool shouldOpen = target != null && !target.activeSelf;
        SetAllSubMenusInactive();
        target?.SetActive(shouldOpen);
        _closeButton?.SetActive(true);
    }

    private static void ToggleNestedSubMenu(GameObject target, GameObject sibling)
    {
        bool shouldOpen = target != null && !target.activeSelf;
        sibling?.SetActive(false);
        target?.SetActive(shouldOpen);
    }

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

    private void RefreshButtonLabels()
    {
        if (_fastWalkButtonText != null)
        {
            _fastWalkButtonText.text = _fastWalkEnabled ? "빨리 걷기 X3 취소" : "빨리 걷기 X3";
        }

        RefreshHqFieldButtonLabel();
    }

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

    private void ShowStatus(string message)
    {
        Debug.Log($"[DebugMenu] {message}", this);
    }

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

    private void SetSelectedCctvActive(bool active)
    {
        SetSelectedCctvState(active ? CctvDebugState.Enabled : CctvDebugState.Disabled);
    }

    private void SetSelectedCctvState(CctvDebugState state)
    {
        CCTVHub hub = FindFirstObjectByType<CCTVHub>();
        List<Transform> points = GetCctvPoints(hub);
        if (_selectedCctvIndex < 0 || points == null || _selectedCctvIndex >= points.Count ||
            points[_selectedCctvIndex] == null)
        {
            ShowStatus($"CCTV {_selectedCctvIndex + 1} 포인트를 찾지 못했습니다.");
            return;
        }

        _cctvStates[_selectedCctvIndex] = state;
        ApplyCurrentCctvState();
        string stateLabel = state switch
        {
            CctvDebugState.Enabled => "켰습니다",
            CctvDebugState.Partial => "반동작 상태로 변경했습니다",
            _ => "껐습니다"
        };
        ShowStatus($"CCTV {_selectedCctvIndex + 1}을(를) {stateLabel}.");
    }

    private static List<Transform> GetCctvPoints(CCTVHub hub)
    {
        if (hub == null)
        {
            return null;
        }

        List<Transform> runtimePoints = CctvPointsField?.GetValue(hub) as List<Transform>;
        if (runtimePoints != null && runtimePoints.Count > 0)
        {
            return runtimePoints;
        }

        Transform[] areas = CctvAreasField?.GetValue(hub) as Transform[];
        if (areas == null)
        {
            return runtimePoints;
        }

        List<Transform> rebuiltPoints = new();
        foreach (Transform area in areas)
        {
            if (area == null)
            {
                continue;
            }

            foreach (Transform point in area)
            {
                rebuiltPoints.Add(point);
            }
        }

        return rebuiltPoints;
    }

    private void ApplyCurrentCctvState()
    {
        CCTVHub hub = FindFirstObjectByType<CCTVHub>();
        if (hub == null)
        {
            return;
        }

        Camera cctvCamera = CctvCameraField?.GetValue(hub) as Camera;
        if (cctvCamera == null)
        {
            return;
        }

        if (_debugCctvCamera != cctvCamera)
        {
            RestoreCctvCamera();
            _debugCctvCamera = cctvCamera;
            _originalCctvCullingMask = cctvCamera.cullingMask;
            CacheCctvImages(cctvCamera.targetTexture);
        }

        int currentIndex = hub.UsingCctvNumber;
        CctvDebugState state = currentIndex >= 0 && currentIndex < _cctvStates.Length
            ? _cctvStates[currentIndex]
            : CctvDebugState.Enabled;
        bool shouldRender = state != CctvDebugState.Disabled;
        cctvCamera.enabled = true;
        cctvCamera.cullingMask = shouldRender ? _originalCctvCullingMask : 0;
        ApplyCctvImageState(state);
    }

    private void CacheCctvImages(RenderTexture targetTexture)
    {
        _originalCctvImages.Clear();
        if (targetTexture == null)
        {
            return;
        }

        foreach (RawImage image in FindObjectsByType<RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (image.texture == targetTexture)
            {
                _originalCctvImages[image] = (image.color, image.uvRect, image.material, image.texture);
            }
        }
    }

    private void ApplyCctvImageState(CctvDebugState state)
    {
        foreach (KeyValuePair<RawImage, (Color Color, Rect UvRect, Material Material, Texture Texture)> entry
                 in _originalCctvImages)
        {
            if (entry.Key == null)
            {
                continue;
            }

            switch (state)
            {
                case CctvDebugState.Enabled:
                    entry.Key.color = entry.Value.Color;
                    entry.Key.uvRect = entry.Value.UvRect;
                    entry.Key.material = entry.Value.Material;
                    entry.Key.texture = entry.Value.Texture;
                    break;
                case CctvDebugState.Partial:
                    entry.Key.color = entry.Value.Color;
                    entry.Key.uvRect = entry.Value.UvRect;
                    entry.Key.material = _cctvGlitchMaterial;
                    entry.Key.texture = entry.Value.Texture;
                    break;
                default:
                    entry.Key.color = Color.white;
                    entry.Key.uvRect = entry.Value.UvRect;
                    entry.Key.material = entry.Value.Material;
                    entry.Key.texture = _cctvDisconnectedTexture;
                    break;
            }
        }
    }

    private void RestoreCctvCamera()
    {
        if (_debugCctvCamera == null)
        {
            return;
        }

        _debugCctvCamera.enabled = true;
        _debugCctvCamera.cullingMask = _originalCctvCullingMask;
        foreach (KeyValuePair<RawImage, (Color Color, Rect UvRect, Material Material, Texture Texture)> entry
                 in _originalCctvImages)
        {
            if (entry.Key != null)
            {
                entry.Key.color = entry.Value.Color;
                entry.Key.uvRect = entry.Value.UvRect;
                entry.Key.material = entry.Value.Material;
                entry.Key.texture = entry.Value.Texture;
            }
        }

        _originalCctvImages.Clear();
        _debugCctvCamera = null;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestClearInventoryRpc(RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client) &&
            client.PlayerObject != null &&
            client.PlayerObject.TryGetComponent(out Player player) &&
            player.PlayerInventory != null)
        {
            player.PlayerInventory.ClearAllItemsOnServer();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestInterferenceRpc(InterferenceEffectType effectType)
    {
        InterferenceEffectManager.Instance?.TryStartEffect(effectType);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestUnlockRegionRpc(string regionId)
    {
        ApplyRegionUnlockRpc(regionId);
    }

    [Rpc(SendTo.Everyone)]
    private void ApplyRegionUnlockRpc(string regionId)
    {
        ApplyRegionUnlock(regionId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestWaitingRoomRpc()
    {
        if (NetworkManager.SceneManager != null)
        {
            NetworkManager.SceneManager.LoadScene(WaitingRoomSceneName, LoadSceneMode.Single);
        }
    }

    private static Player GetLocalPlayer()
    {
        if (NetworkManager.Singleton?.LocalClient?.PlayerObject != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject.TryGetComponent(out Player networkPlayer))
        {
            return networkPlayer;
        }

        return FindObjectsByType<Player>(FindObjectsSortMode.None).FirstOrDefault(player => player.IsOwner);
    }

    private static bool IsCloserToHq(Vector3 playerPosition, HqEntrance entrance, HqExit exit)
    {
        Transform hqPoint = HqSpawnPointField?.GetValue(entrance) as Transform;
        Transform fieldPoint = FieldSpawnPointField?.GetValue(exit) as Transform;
        return hqPoint != null && fieldPoint != null &&
               Vector3.SqrMagnitude(playerPosition - hqPoint.position) <
               Vector3.SqrMagnitude(playerPosition - fieldPoint.position);
    }

    private void HandleActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        SetMenuVisible(false);
        RestoreWalkSpeed();
    }
}
#endif
