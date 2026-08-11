using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

// 미션 UI 실행, 완료 동기화, 단서 보상 생성을 공통으로 처리한다.
public sealed class MissionInteractable : InteractableBase
{
    [Header("미션 UI")]
    [SerializeField] private GameObject _uiPrefab;  // 미션 ui
    [SerializeField] private string _interactionText = "미션 시작";

    [SerializeField] private ItemData _completionReward;    // 미션이 끝나면 나오는 아이템
    [SerializeField] private Vector3 _rewardSpawnOffset = new(0f, 0.5f, 1.2f);  // 리워드가 앞쪽으로 스폰되는 위치


    // 미션을 하기 위한 조건을 설정합니다. 조건의 충족 여부따라서 상호작용 안내 문구가 달라집니다.
    [Header("미션 시작 아이템")]
    [SerializeField] private ItemData _requiredItem;
    [SerializeField] private string _requiredItemInsertText = "투입";
    [SerializeField] private string _requiredItemMissingText = "필요";
    [SerializeField] private string _requiredItemInsertedText = "분석 가능";


    // 완료 여부와 퍼즐 시드는 서버가 기록하고 모든 클라이언트가 읽는다. (서버에서만 쓰기 가능)
    private readonly NetworkVariable<bool> _isCompleted = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _puzzleSeed = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _requiredItemInserted = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private GameObject _uiInstance; // 열려있는 미션 ui 인스턴스
    private Transform _interactingPlayer;
    private static MissionInteractable _activeInteractable;

    private IInteractionApplier _cachedApplier;

    public bool IsCompleted => _isCompleted.Value;
    public bool IsRequiredItemInserted => _requiredItem == null || _requiredItemInserted.Value;
    public ItemType RequiredItemId => _requiredItem != null ? _requiredItem.ItemId : ItemType.None;

    // 완료 여부가 바뀔 때마다 알린다. 배터리 회로처럼 다른 컴포넌트가 완료 시점에 반응해야 할 때 사용한다.
    public event System.Action<bool> IsCompletedChanged;


    // 라운드가 새로 시작될 때 서버에서 알린다. 미션이 자체적으로 들고 있는 정답·진행 상태를 초기화할 시점이다.
    public event System.Action ServerRoundReset;
    public override string InteractionText => IsCompleted ? "완료된 게임" : _interactionText;

    // 역할 제한은 여기서 보지 않는다. 조준은 되어야 GetInteractionText로 제한 안내를 띄울 수 있다. (HqScreen과 같은 방식)
    public override bool CanInteract(GameObject interactor) => true;

    // 시작 아이템이 필요한 기계는 지금 그 아이템을 들고 있는지에 따라 문구가 달라진다.
    public override string GetInteractionText(GameObject interactor)
    {
        if (IsCompleted || _requiredItem == null)
        {
            return InteractionText;
        }

        if (IsRequiredItemInserted)
        {
            return $"{_requiredItem.DisplayName} {_requiredItemInsertedText}";
        }

        return TryGetApplier(interactor, out _)
            ? $"{_requiredItem.DisplayName} {_requiredItemInsertText}"
            : $"{_requiredItem.DisplayName} {_requiredItemMissingText}";
    }

    // 아이템이 없으면 눌러도 아무 일이 없으므로 [E] 힌트를 감춰 안내 문구만 남긴다.
    public override bool ShowInteractionKeyHint(GameObject interactor)
    {
        return IsCompleted || IsRequiredItemInserted || TryGetApplier(interactor, out _);
    }


    //---   미션 시작 아이템을 기계에 넣을 때는 실수 방지를 위해 E를 길게 눌러 투입   ---///
    public override bool RequiresHoldInteraction(GameObject interactor) => !IsRequiredItemInserted && TryGetApplier(interactor, out _cachedApplier);
    public override float HoldInteractionDuration => _cachedApplier != null ? _cachedApplier.ApplyHoldDuration : base.HoldInteractionDuration;


    // 플레이어가 현재 선택한 아이템이 이 기계에 투입 가능한 IInteractionApplier인지 확인한다.
    private bool TryGetApplier(GameObject interactor, out IInteractionApplier applier)
    {
        applier = null;

        if (_requiredItem == null
            || !interactor.TryGetComponent(out PlayerInventory inventory)
            || !inventory.TryGetSelectedItemBase(out ItemBase item)
            || item is not IInteractionApplier itemApplier
            || !itemApplier.CanApplyTo(interactor, this, out _))
        {
            return false;
        }

        applier = itemApplier;
        return true;
    }


    // 완료 상태 변경을 구독해 다른 플레이어가 완료한 결과도 즉시 반영한다.
    public override void OnNetworkSpawn()
    {
        _isCompleted.OnValueChanged += HandleCompletionChanged;
        _puzzleSeed.OnValueChanged += HandlePuzzleSeedChanged;

        // 새로 스폰된 기계는 서버가 최초 퍼즐 시드를 한 번만 정한다.
        if (IsServer && _puzzleSeed.Value == 0)
        {
            _puzzleSeed.Value = CreatePuzzleSeed();
        }
    }

    // 네트워크 해제 시 완료 상태 변경 구독을 제거한다.
    public override void OnNetworkDespawn()
    {
        _isCompleted.OnValueChanged -= HandleCompletionChanged;
        _puzzleSeed.OnValueChanged -= HandlePuzzleSeedChanged;
    }

    // 서버에서 생성한 미션 기계에 대응 단서 데이터를 설정한다.
    public void ConfigureCompletionReward(ItemData reward)
    {
        _completionReward = reward;
    }

    // 미션 UI를 열고 완료된 게임이면 완료 안내만 표시한다.
    public override void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor))
        {
            return;
        }

        // 필요한 아이템이 아직 투입되지 않았다면 UI를 열지 않고 서버에 아이템 투입을 요청한다.
        if (!IsRequiredItemInserted)
        {
            if (TryGetApplier(interactor, out _) && interactor.TryGetComponent(out PlayerInventory inventory)) { InsertRequiredItemRpc(inventory.SelectedIndex); }
            return;
        }

        _activeInteractable = this;
        _interactingPlayer = interactor.transform;

        // 미완료 상태로 닫아 보관한 단계형 게임은 새로 만들지 않고 기존 진행 상태를 재개한다.
        if (_uiInstance != null)
        {
            _uiInstance.SetActive(true);
            GameplayUiMode.Instance?.ActivateCursor();
            return;
        }

        // 모든 클라이언트가 서버 시드로 같은 랜덤 문제를 생성하도록 Random 상태를 잠시 고정한다.
        Random.State previousRandomState = Random.state;
        try
        {
            Random.InitState(_puzzleSeed.Value);
            _uiInstance = Instantiate(_uiPrefab);
        }
        finally
        {
            Random.state = previousRandomState;
        }

        _uiInstance.name = _uiPrefab.name;
        EnsureEventSystem(_uiInstance.transform);

        MissionUIController controller = _uiInstance.GetComponent<MissionUIController>();
        if (controller == null)
        {
            Debug.LogError($"'{_uiPrefab.name}'에 MissionUIController가 없습니다.", _uiPrefab);
            CloseUI();
            return;
        }

        controller.Initialize(this);

        // 미션별 초기화가 실패해도 커서 없이 UI만 열린 상태로 갇히지 않도록 커서를 먼저 활성화한다.
        GameplayUiMode.Instance?.ActivateCursor();

        // 배터리 미션은 레버·게이지 등 다른 역할과 공유하는 상태를 별도 컴포넌트에서 관리한다.
        if (_uiInstance.TryGetComponent(out BreakerBatteryMission breakerGame))
        {
            breakerGame.Initialize(GetComponent<BreakerCircuitState>());
        }

        if (IsCompleted)
        {
            controller.ShowCompletedState();
        }
    }

    // 요청한 플레이어의 선택 슬롯을 서버에서 검증하고 아이템 효과를 적용한다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void InsertRequiredItemRpc(int selectedIndex, RpcParams rpcParams = default)
    {
        if (_requiredItemInserted.Value) { return; }
        if (!NetworkManager.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out NetworkClient client) || client.PlayerObject == null) { return; }
        if ((client.PlayerObject.transform.position - transform.position).sqrMagnitude > 25f) { return; }

        GameObject interactor = client.PlayerObject.gameObject;
        if (!interactor.TryGetComponent(out PlayerInventory inventory)
            || !inventory.TryGetSelectedItemBase(out ItemBase item)
            || item is not IInteractionApplier applier
            || !applier.CanApplyTo(interactor, this, out _))
        {
            return;
        }

        if (applier.ConsumedOnApply && !inventory.TryRemoveSelectedItemOnServer(item.ItemId, selectedIndex)) { return; }

        applier.ApplyToOnServer(interactor, this);
    }

    // 서버 전용: 위 RPC를 거쳐 검증된 적용 아이템이 투입 완료 상태를 기록할 때 호출한다.
    public void MarkRequiredItemInsertedOnServer()
    {
        if (!IsServer) { return; }
        _requiredItemInserted.Value = true;
    }

    // 결과 확인을 누른 클라이언트가 서버에 완료 확정을 요청한다.
    public void RequestCompletion()
    {
        if (IsSpawned && !IsCompleted)
        {
            Vector3 playerPosition = _interactingPlayer != null
                ? _interactingPlayer.position
                : transform.position + transform.forward * 3f;
            CompleteMissionRpc(playerPosition);
        }
    }

    // 서버가 라운드 시작 시 완료 상태를 해제하고 새로운 공통 퍼즐 시드를 배포한다.
    public void ResetForNewRound()
    {
        if (!IsServer)
        {
            return;
        }

        _isCompleted.Value = false;
        _puzzleSeed.Value = CreatePuzzleSeed();
        _requiredItemInserted.Value = false;

        // 시드만 바꿔서는 자체 상태(정답 주파수, 진행 단계 등)를 들고 있는 미션이 초기화되지 않는다.
        ServerRoundReset?.Invoke();
    }

    // 서버가 완료 상태를 한 번만 기록하고 모든 클라이언트에 보일 단서를 생성한다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void CompleteMissionRpc(Vector3 requestingPlayerPosition)
    {
        CompleteMissionInternal(requestingPlayerPosition);
    }

    // 브레이커 회로처럼 서버 로직이 스스로 완료 조건을 감지했을 때, 클라이언트 요청 RPC 없이 직접 호출한다.
    public void ServerCompleteFromGameplay(Vector3 requestingPlayerPosition)
    {
        if (!IsServer)
        {
            return;
        }

        CompleteMissionInternal(requestingPlayerPosition);
    }

    private void CompleteMissionInternal(Vector3 requestingPlayerPosition)
    {
        if (_isCompleted.Value)
        {
            return;
        }

        _isCompleted.Value = true;
        SpawnCompletionReward(requestingPlayerPosition);
    }

    // 다른 플레이어가 완료했으면 현재 열려 있는 UI도 완료 안내로 전환한다.
    private void HandleCompletionChanged(bool previousValue, bool currentValue)
    {
        IsCompletedChanged?.Invoke(currentValue);

        if (!currentValue || _uiInstance == null)
        {
            return;
        }

        // 브레이커의 두 화면(배터리 패널·계기판)은 바늘 연출이 끝나는 같은 시점에 스스로 결과 창을 띄운다.
        // 여기서 미리 띄우면 연출이 잘린다.
        if (_uiInstance.TryGetComponent(out BreakerGaugeMonitorUI _) ||
            _uiInstance.TryGetComponent(out BreakerBatteryMission _))
        {
            return;
        }

        _uiInstance.GetComponent<MissionUIController>()?.ShowCompletedState();
    }

    // 새 라운드 시드가 도착하면 이전 라운드의 열려 있던 UI를 닫는다.
    private void HandlePuzzleSeedChanged(int previousValue, int currentValue)
    {
        if (previousValue == 0 || previousValue == currentValue || _uiInstance == null)
        {
            return;
        }

        _uiInstance.GetComponent<MissionUIController>()?.CloseWithoutCompletion();
    }

    // 0을 제외한 새로운 퍼즐 시드를 생성한다.
    private static int CreatePuzzleSeed()
    {
        return Random.Range(1, int.MaxValue);
    }

    // UI를 닫은 플레이어 방향으로 완료 단서를 튕겨 내보낸다.
    private void SpawnCompletionReward(Vector3 requestingPlayerPosition)
    {
        if (_completionReward == null || _completionReward.WorldPrefab == null)
        {
            Debug.LogError($"[Mission] '{name}'에 완료 단서가 설정되지 않았습니다.", this);
            return;
        }

        // 프리팹의 로컬 축 대신 실제 플레이어가 서 있는 쪽을 기계의 앞 방향으로 사용한다.
        Vector3 playerDirection = requestingPlayerPosition - transform.position;
        playerDirection.y = 0f;
        float playerDistance = playerDirection.magnitude;
        playerDirection = playerDirection.sqrMagnitude > 0.001f ? playerDirection.normalized : transform.forward;

        // 가까운 플레이어를 지나쳐 발밑에 생성되지 않도록 최대 거리와 플레이어 거리의 45% 중 작은 값을 사용한다.
        float maximumSpawnDistance = Mathf.Abs(_rewardSpawnOffset.z);
        float spawnDistance = Mathf.Min(maximumSpawnDistance, playerDistance * 0.45f);
        Vector3 spawnPosition = transform.position + playerDirection * spawnDistance + Vector3.up * _rewardSpawnOffset.y;

        GameObject rewardObject = Instantiate(_completionReward.WorldPrefab, spawnPosition, Quaternion.identity);

        if (!rewardObject.TryGetComponent(out ItemBase pickupItem) || !rewardObject.TryGetComponent(out NetworkObject networkObject))
        {
            Debug.LogError($"[Mission] '{_completionReward.WorldPrefab.name}'에 ItemBase 또는 NetworkObject가 없습니다.", this);
            Destroy(rewardObject);
            return;
        }

        pickupItem.Configure(_completionReward);
        networkObject.Spawn(destroyWithScene: true);

        // 보상이 단서면, 아직 아무 데도 배정되지 않은 번호 중 하나를 무작위로 받아온다.
        if (pickupItem is ClueItem rewardClue)
        {
            if (ClueSpawner.Instance != null && ClueSpawner.Instance.TryClaimRandomClueNumber(out int clueNumber))
            {
                rewardClue.SetClueNumber(clueNumber);
            }
            else
            {
                Debug.LogError($"[Mission] '{name}' 완료 보상에 배정할 단서 번호를 받아오지 못했습니다.", this);
            }
        }

        GetComponent<MissionRewardLauncher>()?.Launch(rewardObject, spawnPosition, requestingPlayerPosition);
    }

    // 열려 있는 미션 UI를 닫는다.
    public void CloseUI()
    {
        if (_uiInstance == null)
        {
            ReleaseActiveState();
            return;
        }

        MissionUIController controller = _uiInstance.GetComponent<MissionUIController>();
        if (controller != null)
        {
            controller.Close();
            return;
        }

        Destroy(_uiInstance);
        GameplayUiMode.Instance?.DeactivateCursor();
        NotifyUIClosed(null);
    }

    // 닫힌 UI 참조와 전역 미션 사용 상태를 해제한다.
    public void NotifyUIClosed(MissionUIController controller)
    {
        if (controller == null || controller.gameObject == _uiInstance)
        {
            _uiInstance = null;
            ReleaseActiveState();
        }
    }

    // 단계형 게임 UI 인스턴스는 유지하고 현재 사용 상태만 해제한다.
    public void NotifyUISuspended(MissionUIController controller)
    {
        if (controller != null && controller.gameObject == _uiInstance)
        {
            ReleaseActiveState();
        }
    }

    // 현재 기계가 사용 중인 미션 상태를 해제한다.
    private void ReleaseActiveState()
    {
        if (_activeInteractable == this)
        {
            _activeInteractable = null;
        }
    }

    // 미션 UI 입력에 필요한 EventSystem이 없으면 생성한다.
    private static void EnsureEventSystem(Transform uiRoot)
    {
        if (FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new("MissionEventSystem");
        eventSystemObject.transform.SetParent(uiRoot, false);
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }

    // 기계가 제거될 때 열려 있는 UI와 전역 사용 상태를 정리한다.
    public override void OnDestroy()
    {
        if (_uiInstance != null)
        {
            MissionUIController controller = _uiInstance.GetComponent<MissionUIController>();
            if (controller != null)
            {
                controller.CloseWithoutCompletion();
            }
            else
            {
                Destroy(_uiInstance);
                ReleaseActiveState();
            }
        }
        else
        {
            ReleaseActiveState();
        }

        base.OnDestroy();
    }
}
