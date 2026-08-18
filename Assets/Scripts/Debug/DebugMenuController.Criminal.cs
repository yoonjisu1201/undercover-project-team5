using System.Reflection;
using Unity.Netcode;
using UnityEngine;

// 범인 표시, 단독 검거·투표, 범인 정지, 몽타주 전송을 처리합니다.
public sealed partial class DebugMenuController
{
    private static readonly FieldInfo MontageStateField =
        typeof(MontageSyncBase).GetField("_montageState", BindingFlags.Instance | BindingFlags.NonPublic);

    private bool _criminalMarkerVisible;
    private bool _soloCaptureEnabled;
    private bool _soloVoteEnabled;
    private bool _criminalFrozen;

    // 혼자서도 검거 투표가 가결되도록 가결 기준을 1표로 낮춥니다.
    public void OnToggleSoloVoteClick()
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestToggleSoloVoteRpc();
        ShowStatus("나혼자 투표 상태를 전환했습니다.");
    }

    public void OnToggleCriminalMarkerClick()
    {
        RequestToggleCriminalMarkerRpc();
        ShowStatus("범인 표시 상태를 전환했습니다.");
    }

    public void OnToggleSoloCaptureClick()
    {
        RequestToggleSoloCaptureRpc();
        ShowStatus("나혼자 검거 상태를 전환했습니다.");
    }

    // 범인 NPC의 이동 정지 상태를 서버에서 토글합니다.
    public void OnToggleCriminalFreezeClick()
    {
        RequestToggleCriminalFreezeRpc();
        ShowStatus("범인 정지 상태를 전환했습니다.");
    }

    public void OnShareCriminalMontageClick()
    {
        RequestShareCriminalMontageRpc();
        ShowStatus("범인 몽타주를 완성해 전송했습니다.");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestToggleCriminalMarkerRpc()
    {
        _criminalMarkerVisible = !_criminalMarkerVisible;
        ApplyCriminalMarkerRpc(_criminalMarkerVisible);
    }

    [Rpc(SendTo.Everyone)]
    private void ApplyCriminalMarkerRpc(bool visible)
    {
        CriminalDebugLabel label = FindFirstObjectByType<CriminalDebugLabel>();
        if (label == null)
        {
            Debug.LogWarning("[DebugMenu] 범인 머리 위 표시를 담당하는 CriminalDebugLabel을 찾지 못했습니다.");
            return;
        }

        label.SetVisible(visible);
        SetToggleButtonState(_criminalMarkerButton, visible);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestToggleSoloCaptureRpc()
    {
        _soloCaptureEnabled = !_soloCaptureEnabled;
        ArrestChaseManager.Instance?.SetDebugSoloCaptureEnabled(_soloCaptureEnabled);
        ApplySoloCaptureStateRpc(_soloCaptureEnabled);
    }

    [Rpc(SendTo.Everyone)]
    private void ApplySoloCaptureStateRpc(bool enabled)
    {
        SetToggleButtonState(_soloCaptureButton, enabled);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestToggleSoloVoteRpc()
    {
        _soloVoteEnabled = !_soloVoteEnabled;
        FindFirstObjectByType<ArrestVoteManager>()?.SetDebugSoloVoteEnabled(_soloVoteEnabled);
        ApplySoloVoteStateRpc(_soloVoteEnabled);
    }

    [Rpc(SendTo.Everyone)]
    private void ApplySoloVoteStateRpc(bool enabled)
    {
        SetToggleButtonState(_soloVoteButton, enabled);
    }

    // 서버가 범인과 외계인 분신의 이동을 함께 정지하거나 다시 풀어줍니다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestToggleCriminalFreezeRpc()
    {
        _criminalFrozen = !_criminalFrozen;

        ApplyCriminalMovementFreeze(_criminalFrozen);

        // 분신은 라운드 중간에 계속 새로 스폰되므로, 매니저가 정지 상태를 들고 있다가 새 분신에도 적용한다.
        FindFirstObjectByType<AlienCloneManager>()?.SetClonesFrozen(_criminalFrozen);

        ApplyCriminalFreezeStateRpc(_criminalFrozen);
    }

    // 범인 NPC 한 마리의 이동 정지를 적용합니다. 범인이 아직 없어도 분신 정지는 그대로 진행됩니다.
    private void ApplyCriminalMovementFreeze(bool frozen)
    {
        CriminalNpcManager criminalManager = FindFirstObjectByType<CriminalNpcManager>();
        NetworkObject criminal = criminalManager?.CriminalNpc;
        if (criminal == null || !criminal.TryGetComponent(out NpcMovement movement))
        {
            Debug.LogWarning("[DebugMenu] 정지할 범인 NPC의 이동 컴포넌트를 찾지 못했습니다. 외계인 분신만 적용합니다.");
            return;
        }

        if (frozen)
        {
            criminal.GetComponent<NpcStateMachine>()?.ChangeToIdle();
            movement.HoldExternally();
        }
        else
        {
            movement.ReleaseExternalHold();
        }
    }

    // 모든 클라이언트에서 범인 정지 버튼의 토글 색상을 동기화합니다.
    [Rpc(SendTo.Everyone)]
    private void ApplyCriminalFreezeStateRpc(bool frozen)
    {
        SetToggleButtonState(_criminalFreezeButton, frozen);
    }

    // 새 라운드가 시작되면 범인 정지 토글과 버튼 표시를 OFF로 초기화합니다.
    private void HandleDebugRoundStateChanged(RoundState state)
    {
        if (!IsServer || state != RoundState.InRound)
        {
            return;
        }

        _criminalFrozen = false;
        FindFirstObjectByType<AlienCloneManager>()?.SetClonesFrozen(false);
        ApplyCriminalFreezeStateRpc(false);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestShareCriminalMontageRpc()
    {
        CriminalNpcManager criminalManager = FindFirstObjectByType<CriminalNpcManager>();
        MontageSyncManager syncManager = FindFirstObjectByType<MontageSyncManager>();
        MontageShareManager shareManager = FindFirstObjectByType<MontageShareManager>();
        if (criminalManager?.CriminalFeature?.Outfit == null ||
            syncManager == null ||
            shareManager == null ||
            MontageStateField == null)
        {
            Debug.LogWarning("[DebugMenu] 범인 외형 또는 몽타주 매니저를 찾지 못했습니다.");
            return;
        }

        MontageState state = CreateCriminalMontageState(criminalManager.CriminalFeature.Outfit);
        SetMontageState(syncManager, state);
        shareManager.IsMontageShared.Value = true;
        SetMontageState(shareManager, state);
        shareManager.LastSharedTime.Value = (float)NetworkManager.ServerTime.Time;
    }

    // 범인의 외형 모듈 ID를 몽타주 상태로 변환합니다.
    // NPC 외형과 몽타주가 이제 같은 ClothCatalog/ClothData.Id를 공유하므로 번역 없이 그대로 옮긴다.
    private static MontageState CreateCriminalMontageState(OutfitFeature outfit)
    {
        return MontageState.Empty
            .WithCloth(ClothPart.Beard, outfit.BeardNumber)
            .WithCloth(ClothPart.Eyebrow, outfit.EyebrowsNumber)
            .WithCloth(ClothPart.Glasses, outfit.GlassesNumber)
            .WithCloth(ClothPart.Hair, outfit.HairNumber)
            .WithCloth(ClothPart.Hat, outfit.HatNumber)
            .WithCloth(ClothPart.Headphone, outfit.HeadphoneNumber)
            .WithCloth(ClothPart.Arm, outfit.ArmNumber)
            .WithCloth(ClothPart.Pants, outfit.PantsNumber)
            .WithCloth(ClothPart.Mask, outfit.MaskNumber)
            .WithCloth(ClothPart.Shoes, outfit.ShoesNumber)
            .WithCloth(ClothPart.Torso, outfit.TorsoNumber);
    }

    // 서버 권한의 몽타주 NetworkVariable에 완성 상태를 기록합니다.
    private static void SetMontageState(MontageSyncBase manager, MontageState state)
    {
        ((NetworkVariable<MontageState>)MontageStateField.GetValue(manager)).Value = state;
    }
}
