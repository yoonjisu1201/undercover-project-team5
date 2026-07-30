using System.Reflection;
using Unity.Netcode;
using UnityEngine;

// 범인 표시, 특정, 단독 검거와 몽타주 전송을 처리합니다.
public sealed partial class DebugMenuController
{
    private static readonly FieldInfo MontageStateField =
        typeof(MontageSyncBase).GetField("_montageState", BindingFlags.Instance | BindingFlags.NonPublic);

    private bool _criminalMarkerVisible;
    private bool _soloCaptureEnabled;
    private bool _criminalFrozen;

    public void OnFindCriminalClick()
    {
        if (!IsSpawned)
        {
            ShowStatus("네트워크 연결 후 사용할 수 있습니다.");
            return;
        }

        RequestIdentifyCriminalRpc();
        ShowStatus("범인을 특정하고 추격 단계를 시작했습니다.");
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
    private void RequestIdentifyCriminalRpc()
    {
        CriminalNpcManager criminalManager = FindFirstObjectByType<CriminalNpcManager>();
        ArrestChaseManager chaseManager = ArrestChaseManager.Instance;
        if (criminalManager?.CriminalNpc == null || chaseManager == null)
        {
            Debug.LogWarning("[DebugMenu] 범인 NPC 또는 ArrestChaseManager를 찾지 못했습니다.");
            return;
        }

        chaseManager.StartChase(criminalManager.CriminalNpc);
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

    // 서버가 범인의 이동을 정지하거나 다시 배회할 수 있도록 해제합니다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestToggleCriminalFreezeRpc()
    {
        CriminalNpcManager criminalManager = FindFirstObjectByType<CriminalNpcManager>();
        NetworkObject criminal = criminalManager?.CriminalNpc;
        if (criminal == null || !criminal.TryGetComponent(out NpcMovement movement))
        {
            Debug.LogWarning("[DebugMenu] 정지할 범인 NPC의 이동 컴포넌트를 찾지 못했습니다.");
            return;
        }

        _criminalFrozen = !_criminalFrozen;
        if (_criminalFrozen)
        {
            criminal.GetComponent<NpcStateMachine>()?.RequestIdle();
            movement.HoldExternally();
        }
        else
        {
            movement.ReleaseExternalHold();
        }

        ApplyCriminalFreezeStateRpc(_criminalFrozen);
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
    private static MontageState CreateCriminalMontageState(OutfitFeature outfit)
    {
        return MontageState.Empty
            .WithCloth(MontageParts.Beard, ToMontageClothId(MontageParts.Beard, outfit.BeardNumber))
            .WithCloth(MontageParts.Eyebrows, ToMontageClothId(MontageParts.Eyebrows, outfit.EyebrowsNumber))
            .WithCloth(MontageParts.Glasses, ToMontageClothId(MontageParts.Glasses, outfit.GlassesNumber))
            .WithCloth(MontageParts.Hair, ToMontageClothId(MontageParts.Hair, outfit.HairNumber))
            .WithCloth(MontageParts.Hats, ToMontageClothId(MontageParts.Hats, outfit.HatNumber))
            .WithCloth(MontageParts.Headphones, ToMontageClothId(MontageParts.Headphones, outfit.HeadphoneNumber))
            .WithCloth(MontageParts.Arms, ToMontageClothId(MontageParts.Arms, outfit.ArmNumber))
            .WithCloth(MontageParts.Pants, ToMontageClothId(MontageParts.Pants, outfit.PantsNumber))
            .WithCloth(MontageParts.Masks, ToMontageClothId(MontageParts.Masks, outfit.MaskNumber))
            .WithCloth(MontageParts.Shoes, ToMontageClothId(MontageParts.Shoes, outfit.ShoesNumber))
            .WithCloth(MontageParts.Torso, ToMontageClothId(MontageParts.Torso, outfit.TorsoNumber));
    }

    // NPC 의상 배열 인덱스를 파츠별 몽타주 카탈로그 ID로 변환합니다.
    private static int ToMontageClothId(MontageParts part, int npcOutfitIndex)
    {
        if (npcOutfitIndex < 0)
        {
            return MontageState.None;
        }

        return part switch
        {
            // Pants_01은 몽타주 카탈로그에 아직 등록되어 있지 않습니다.
            MontageParts.Pants => MontageState.None,
            // NPC에는 기본 Torso_01이 있지만 몽타주 목록은 Torso_02_01부터 ID 0입니다.
            MontageParts.Torso => npcOutfitIndex == 0
                ? MontageState.None
                : npcOutfitIndex - 1,
            // 몽타주 ID 0은 NPC 랜덤 목록에 없는 기본 Shoes_01입니다.
            MontageParts.Shoes => npcOutfitIndex + 1,
            _ => npcOutfitIndex
        };
    }

    // 서버 권한의 몽타주 NetworkVariable에 완성 상태를 기록합니다.
    private static void SetMontageState(MontageSyncBase manager, MontageState state)
    {
        ((NetworkVariable<MontageState>)MontageStateField.GetValue(manager)).Value = state;
    }
}
