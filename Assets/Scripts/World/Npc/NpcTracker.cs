using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// NPC에 위치추적기(BeaconTracker)가 부착됐는지 여부를 관리한다.
public class NpcTracker : NetworkBehaviour
{
    [SerializeField] private string _trackerItemId = "BeaconTracker";

    // 서버 재검사 시 네트워크 지연으로 인한 위치 오차를 흡수하기 위한 여유 거리 (ArrestCandidateInteractable의 동명 필드와 같은 용도).
    [SerializeField, Min(0f)] private float _rangeTolerance = 2f;

    // 부착 시 등에 보여줄 시각 오브젝트. 기본 비활성 상태로 미리 배치해두고, 부착 여부에 따라 켜고 끈다.
    // 네트워크로 새로 스폰하지 않고 로컬에서 SetActive만 하므로, 모든 클라이언트에 미리 배치돼 있어야 한다.
    [SerializeField] private GameObject _trackerVisual;

    // 현재 부착되어 활성화된 NpcTracker만 모아둔다. NPC가 150마리씩 있어도 본부 미니맵/목록 UI가
    // 전체를 순회하지 않고 이 목록만 읽도록 하기 위함. 클라이언트마다 로컬로 유지되며,
    // 서버가 확정한 _isTracked 값이 각자에게 동기화될 때 자신을 등록/해제한다.
    private static readonly List<NpcTracker> _trackedInstances = new();
    public static IReadOnlyList<NpcTracker> TrackedInstances => _trackedInstances;

    private readonly NetworkVariable<bool> _isTracked =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsTracked => _isTracked.Value;

    // 부착 상태 변경(서버가 확정한 값)을 구독해 목록과 시각 오브젝트에 반영한다. 늦게 들어온 클라이언트도
    // 스폰 시점에 현재 값을 한 번 반영받으므로 별도 초기화가 필요 없다.
    public override void OnNetworkSpawn()
    {
        _isTracked.OnValueChanged += HandleTrackedChanged;
        UpdateRegistry(_isTracked.Value);
        ApplyVisual(_isTracked.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isTracked.OnValueChanged -= HandleTrackedChanged;
        _trackedInstances.Remove(this);
    }

    // OnNetworkDespawn을 거치지 않고 파괴되는 경우(예: 강제 Destroy)를 대비한 안전장치.
    private void OnDestroy()
    {
        _trackedInstances.Remove(this);
    }

    // PlayerInteraction이 E키(Interact 액션) 입력을 받았을 때, 실제로 부착 요청을 보내기 전에 로컬에서 먼저 확인하는 진입점.
    public bool CanAttach(GameObject interactor)
    {
        return IsSpawned && !_isTracked.Value && IsTrackerItemSelected(interactor);
    }

    // 인벤토리에서 추적기 아이템을 선택 중인지만 확인한다.
    // ArrestCandidateInteractable이 안내 문구를 고를 때도 이 값이 필요해서 별도로 뺐다.
    public bool IsTrackerItemSelected(GameObject interactor)
    {
        return interactor.TryGetComponent(out PlayerInventory inventory)
            && inventory.TryGetSelectedItem(out string itemId)
            && itemId == _trackerItemId;
    }

    public void RequestAttach()
    {
        RequestAttachRpc();
    }

    // 서버가 라운드 전환/게임 재시작 시점에 이전 라운드에 부착됐던 추적기를 해제한다.
    // MiniGameInteractable.ResetForNewRound()와 같은 목적, 같은 호출 시점(RoundManager)을 따른다.
    public void ResetForNewRound()
    {
        if (!IsServer)
        {
            return;
        }

        _isTracked.Value = false;
    }

    // 클라이언트 요청을 신뢰하지 않고, 서버에서 "정말 이 NPC 근처에 있는지 + 정말 추적기를 갖고 있는지"를
    // 다시 확인한 뒤에만 부착을 확정한다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestAttachRpc(RpcParams rpcParams = default)
    {
        // 이미 부착된 NPC에 중복 요청이 온 경우(예: 두 클라이언트가 동시에 시도) 여기서 막는다.
        if (!IsSpawned || _isTracked.Value)
        {
            Debug.LogWarning($"[NpcTracker] '{name}' 부착 요청을 무시합니다. (스폰됨: {IsSpawned}, 이미 부착됨: {_isTracked.Value})");
            return;
        }

        // 1) 요청을 보낸 플레이어가 서버 기준으로도 실제 상호작용 범위 안에 있는지 확인한다.
        if (!NpcInteractionValidation.TryGetInteractionCollider(NetworkManager, rpcParams.Receive.SenderClientId, out SphereCollider interactionCollider) ||
            !NpcInteractionValidation.IsWithinInteractionRange(transform.position, interactionCollider, _rangeTolerance))
        {
            Debug.LogWarning($"[NpcTracker] clientId {rpcParams.Receive.SenderClientId}의 상호작용 범위 확인에 실패해 '{name}' 부착을 거부합니다.");
            return;
        }

        // 2) 그 플레이어가 실제로 추적기를 갖고 있는지 서버 인벤토리에서 확인하고, 있으면 그 자리에서 소모(제거)한다.
        if (!NpcInteractionValidation.TryGetSenderInventory(NetworkManager, rpcParams.Receive.SenderClientId, out PlayerInventory inventory) ||
            !inventory.TryRemoveSelectedItemOnServer(_trackerItemId))
        {
            Debug.LogWarning($"[NpcTracker] clientId {rpcParams.Receive.SenderClientId}의 추적기 소모에 실패해 '{name}' 부착을 거부합니다.");
            return;
        }

        // 두 검증을 모두 통과했을 때만 부착 상태로 전환한다. NetworkVariable이라 변경 즉시 모든 클라이언트에 동기화된다.
        _isTracked.Value = true;
    }

    // 서버가 확정한 부착 상태가 모든 클라이언트에 도착했을 때 호출된다.
    private void HandleTrackedChanged(bool previousValue, bool currentValue)
    {
        UpdateRegistry(currentValue);
        ApplyVisual(currentValue);
    }

    private void UpdateRegistry(bool isTracked)
    {
        if (isTracked)
        {
            if (!_trackedInstances.Contains(this))
            {
                _trackedInstances.Add(this);
            }
        }
        else
        {
            _trackedInstances.Remove(this);
        }
    }

    private void ApplyVisual(bool isTracked)
    {
        if (_trackerVisual != null)
        {
            _trackerVisual.SetActive(isTracked);
        }
    }
}
