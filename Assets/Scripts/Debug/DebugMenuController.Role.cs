using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 로컬 플레이어의 디버그 역할 변경을 처리합니다.
public sealed partial class DebugMenuController
{
    [Header("Role")]
    [SerializeField] private Button _fieldRoleButton;
    [SerializeField] private Button _headquarterRoleButton;

    public void OnChangeRoleToHeadquarterClick() => ChangeLocalRole(Role.Headquarter, "본부");
    public void OnChangeRoleToFieldClick() => ChangeLocalRole(Role.Field, "필드");

    // 서버 역할과 로컬 위치를 함께 변경합니다.
    private void ChangeLocalRole(Role role, string roleName)
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestChangePlayerRoleRpc(role);
        MoveLocalPlayerToRoleArea(role);
        ShowStatus($"로컬 플레이어의 역할을 {roleName}로 변경했습니다.");
    }

    // 현재 역할 버튼만 초록, 나머지는 빨강으로 칠합니다.
    private void RefreshRoleButtonColors()
    {
        Player localPlayer = GetLocalPlayer();
        if (localPlayer == null)
        {
            return;
        }

        bool isHeadquarter = localPlayer.PlayerRole == Role.Headquarter;
        SetOnOffButtonColor(_headquarterRoleButton, isHeadquarter);
        SetOnOffButtonColor(_fieldRoleButton, !isHeadquarter);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    // 다른 플레이어와 무관하게 요청한 플레이어의 역할만 변경합니다.
    private void RequestChangePlayerRoleRpc(Role role, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
            client.PlayerObject != null &&
            client.PlayerObject.TryGetComponent(out Player player))
        {
            player.PlayerRole = role;
        }
    }
}
