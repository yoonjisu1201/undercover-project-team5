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
    [SerializeField] private string _requiredItemMissingText = "필요";
    [SerializeField] private string _requiredItemInsertedText = "분석 가능";


    // 완료 여부와 퍼즐 시드는 서버가 기록하고 모든 클라이언트가 읽는다. (서버에서만 쓰기 가능)
    private readonly NetworkVariable<bool> _isCompleted = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _puzzleSeed = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _requiredItemInserted = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 같은 프리팹을 여러 대 스폰하는 미션에서 각 기계가 담당하는 대상 번호(1부터). 예: CCTV 수리 기계의 CCTV 번호.
    // 프리팹이 하나뿐이라 인스펙터로는 구분할 수 없어, 스폰할 때 서버가 정해 모든 클라이언트에 배포한다.
    private readonly NetworkVariable<int> _targetNumber = new(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private GameObject _uiInstance; // 열려있는 미션 ui 인스턴스
    private Transform _interactingPlayer;
    private static MissionInteractable _activeInteractable;

    public bool IsCompleted => _isCompleted.Value;
    public int TargetNumber => _targetNumber.Value;

    // UI 생성 이후에 퍼즐을 만드는 미션이 같은 문제를 뽑도록 서버가 정한 시드를 공개한다.
    public int PuzzleSeed => _puzzleSeed.Value;
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
    // 아이템을 들고 조준 중일 때 보여줄 문구(투입하기 등)는 PlayerInteraction이 IInteractionApplier
    // 쪽에서 직접 가져가므로, 여기서는 "아직 안 들고 있음" 상태만 신경 쓰면 된다.
    public override string GetInteractionText(GameObject interactor)
    {
        if (IsCompleted || _requiredItem == null)
        {
            return InteractionText;
        }

        return IsRequiredItemInserted
            ? $"{_requiredItem.DisplayName} {_requiredItemInsertedText}"
            : $"{_requiredItem.DisplayName} {_requiredItemMissingText}";
    }

    // 아이템이 없으면 눌러도 아무 일이 없으므로 [E] 힌트를 감춰 안내 문구만 남긴다.
    public override bool ShowInteractionKeyHint(GameObject interactor)
    {
        return IsCompleted || IsRequiredItemInserted;
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

    // 서버가 스폰 직전에 이 기계가 담당할 대상 번호를 정한다. (Spawn 전에 넣어야 클라이언트 최초 값으로 배포된다)
    public void ConfigureTargetNumber(int targetNumber)
    {
        _targetNumber.Value = targetNumber;
    }

    // 미션 UI를 열고 완료된 게임이면 완료 안내만 표시한다.
    // 필요한 아이템 투입은 PlayerInteraction이 IInteractionApplier 쪽에서 직접 처리하므로,
    // 여기서는 아직 투입 전이면(=E를 눌러도 열 게 없으면) 그냥 아무것도 하지 않는다.
    public override void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor) || !IsRequiredItemInserted)
        {
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

        // CCTV 수리는 기계마다 담당 CCTV가 달라, 어느 CCTV를 고치는 화면인지 알려 줘야 한다.
        if (_uiInstance.TryGetComponent(out CCTVSignalRepairGame cctvGame))
        {
            cctvGame.Initialize(this);
        }

        if (IsCompleted)
        {
            controller.ShowCompletedState();
        }
    }

    // 서버 전용: PlayerInteraction의 공용 IInteractionApplier 적용 RPC를 거쳐 검증된 아이템이
    // 투입 완료 상태를 기록할 때 호출한다.
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
        // CCTV 수리처럼 단서를 주지 않는 미션도 있으므로, 보상이 비어 있으면 조용히 넘어간다.
        if (_completionReward == null || _completionReward.WorldPrefab == null)
        {
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
