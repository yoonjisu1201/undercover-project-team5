using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 범인 메뉴 하위의 외계인 분신 제거·스폰 토글 명령을 처리합니다.
public sealed partial class DebugMenuController
{
    [Header("Alien")]
    [SerializeField] private GameObject _alienPanel;
    [SerializeField] private Button _alienSpawnButton;

    // 스폰 허용 여부는 서버만 들고 있는 값이라, 클라이언트는 브로드캐스트로 받은 값을 표시용으로 기억한다.
    private bool _alienSpawnEnabled = true;

    // 범인 패널 안에서 외계인 하위 메뉴만 열고 닫습니다.
    public void OnAlienMenuClick()
    {
        ToggleNestedSubMenu(_alienPanel, null);
        RefreshAlienSpawnButton();
    }

    // 살아있는 분신을 모두 없앱니다.
    public void OnClearAliensClick()
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestClearAliensRpc();
    }

    // 분신 스폰을 켜고 끕니다. 켜져 있으면 초록, 꺼져 있으면 빨강입니다.
    public void OnToggleAlienSpawnClick()
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestToggleAlienSpawnRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestClearAliensRpc()
    {
        AlienCloneManager manager = FindFirstObjectByType<AlienCloneManager>();
        if (manager == null)
        {
            Debug.LogWarning("[DebugMenu] AlienCloneManager를 찾지 못했습니다.");
            return;
        }

        AnnounceAlienClearedRpc(manager.DespawnAllClones());
    }

    [Rpc(SendTo.Everyone)]
    private void AnnounceAlienClearedRpc(int removedCount)
    {
        ShowStatus($"외계인 분신 {removedCount}마리를 제거했습니다.");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestToggleAlienSpawnRpc()
    {
        AlienCloneManager manager = FindFirstObjectByType<AlienCloneManager>();
        if (manager == null)
        {
            Debug.LogWarning("[DebugMenu] AlienCloneManager를 찾지 못했습니다.");
            return;
        }

        manager.SetSpawningEnabled(!manager.SpawningEnabled);
        ApplyAlienSpawnStateRpc(manager.SpawningEnabled);
    }

    [Rpc(SendTo.Everyone)]
    private void ApplyAlienSpawnStateRpc(bool enabled)
    {
        _alienSpawnEnabled = enabled;
        SetOnOffButtonColor(_alienSpawnButton, enabled);
        ShowStatus(enabled ? "외계인 분신 스폰을 허용했습니다." : "외계인 분신 스폰을 차단했습니다.");
    }

    // 메뉴를 열 때 현재 상태로 색을 맞춥니다. 서버는 실제 값을, 클라이언트는 마지막으로 받은 값을 씁니다.
    private void RefreshAlienSpawnButton()
    {
        if (IsServer)
        {
            AlienCloneManager manager = FindFirstObjectByType<AlienCloneManager>();
            if (manager != null)
            {
                _alienSpawnEnabled = manager.SpawningEnabled;
            }
        }

        SetOnOffButtonColor(_alienSpawnButton, _alienSpawnEnabled);
    }
}
