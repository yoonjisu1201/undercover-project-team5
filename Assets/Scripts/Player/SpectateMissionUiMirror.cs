using Unity.Netcode;
using UnityEngine;

// 관전 중인 팀원이 미션 기기를 열면 내 화면에도 같은 패널을 띄운다.
//
// 패널을 만드는 일은 MissionInteractable이 그대로 맡는다. 이 컴포넌트는 "지금 보고 있는
// 사람이 무엇을 열었는가"만 따라다니며 열고 닫으라고 시킨다.
public sealed class SpectateMissionUiMirror : NetworkBehaviour
{
    private PlayerSpectator _spectator;

    // 지금 구독 중인 관전 대상. 대상이 바뀌면 구독을 옮긴다.
    private Player _watched;

    // 지금 내 화면에 띄워 둔 기기.
    private MissionInteractable _mirrored;

    private void Awake()
    {
        _spectator = GetComponent<PlayerSpectator>();
    }

    public override void OnNetworkSpawn()
    {
        // 관전은 내 화면에서만 일어나는 일이라 남의 복제본에서는 돌 필요가 없다.
        if (!IsOwner || _spectator == null)
        {
            enabled = false;
            return;
        }

        _spectator.TargetChanged += HandleTargetChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        if (_spectator != null)
        {
            _spectator.TargetChanged -= HandleTargetChanged;
        }

        Unwatch();
        CloseMirror();
    }

    private void HandleTargetChanged(Player target)
    {
        Unwatch();

        // 내 시점으로 돌아왔으면 남의 패널을 계속 띄워 둘 이유가 없다.
        if (target == null)
        {
            CloseMirror();
            return;
        }

        _watched = target;
        _watched.OpenMissionChanged += HandleOpenMissionChanged;

        // 이미 열어 둔 사람으로 전환했을 수 있다. 지금 상태를 한 번 반영한다.
        ApplyOpenMission(_watched.OpenMissionObjectId);
    }

    private void HandleOpenMissionChanged(ulong previousValue, ulong newValue)
    {
        ApplyOpenMission(newValue);
    }

    private void ApplyOpenMission(ulong networkObjectId)
    {
        // 다른 기기로 갈아탔을 수도 있어 항상 먼저 치운다.
        CloseMirror();

        if (networkObjectId == 0) return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject spawned)) return;

        if (!spawned.TryGetComponent(out MissionInteractable mission)) return;

        _mirrored = mission;
        _mirrored.OpenMirrorUi();
    }

    private void CloseMirror()
    {
        if (_mirrored == null) return;

        _mirrored.CloseMirrorUi();
        _mirrored = null;
    }

    private void Unwatch()
    {
        if (_watched == null) return;

        _watched.OpenMissionChanged -= HandleOpenMissionChanged;
        _watched = null;
    }
}
