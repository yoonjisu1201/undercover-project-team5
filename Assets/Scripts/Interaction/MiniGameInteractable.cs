using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

// 미니게임 UI 실행, 완료 동기화, 단서 보상 생성을 공통으로 처리한다.
public sealed class MiniGameInteractable : InteractableBase
{
    [Header("미니게임 UI")]
    [SerializeField] private GameObject _uiPrefab;
    [SerializeField] private string _interactionText = "미니게임 시작";
    [SerializeField] private ItemData _completionReward;
    [SerializeField] private Vector3 _rewardSpawnOffset = new(0f, 0.5f, 1.2f);

    private readonly NetworkVariable<bool> _isCompleted = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _puzzleSeed = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private GameObject _uiInstance;
    private Transform _interactingPlayer;
    private static MiniGameInteractable _activeInteractable;

    public bool IsCompleted => _isCompleted.Value;
    public override string InteractionText => IsCompleted ? "완료된 게임" : _interactionText;
    public override bool CanInteract(GameObject interactor) => _uiPrefab != null && _activeInteractable == null;

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

    // 서버에서 생성한 미니게임 기계에 대응 단서 데이터를 설정한다.
    public void ConfigureCompletionReward(ItemData reward)
    {
        _completionReward = reward;
    }

    // 미니게임 UI를 열고 완료된 게임이면 완료 안내만 표시한다.
    public override void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor))
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

        MiniGameUIController controller = _uiInstance.GetComponent<MiniGameUIController>();
        if (controller == null)
        {
            Debug.LogError($"'{_uiPrefab.name}'에 MiniGameUIController가 없습니다.", _uiPrefab);
            CloseUI();
            return;
        }

        controller.Initialize(this);
        if (IsCompleted)
        {
            controller.ShowCompletedState();
        }

        GameplayUiMode.Instance?.ActivateCursor();
    }

    // 결과 확인을 누른 클라이언트가 서버에 완료 확정을 요청한다.
    public void RequestCompletion()
    {
        if (IsSpawned && !IsCompleted)
        {
            Vector3 playerPosition = _interactingPlayer != null
                ? _interactingPlayer.position
                : transform.position + transform.forward * 3f;
            CompleteMiniGameRpc(playerPosition);
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
    }

    // 서버가 완료 상태를 한 번만 기록하고 모든 클라이언트에 보일 단서를 생성한다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void CompleteMiniGameRpc(Vector3 requestingPlayerPosition)
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
        if (!currentValue || _uiInstance == null)
        {
            return;
        }

        _uiInstance.GetComponent<MiniGameUIController>()?.ShowCompletedState();
    }

    // 새 라운드 시드가 도착하면 이전 라운드의 열려 있던 UI를 닫는다.
    private void HandlePuzzleSeedChanged(int previousValue, int currentValue)
    {
        if (previousValue == 0 || previousValue == currentValue || _uiInstance == null)
        {
            return;
        }

        _uiInstance.GetComponent<MiniGameUIController>()?.CloseWithoutCompletion();
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
            Debug.LogError($"[MiniGame] '{name}'에 완료 단서가 설정되지 않았습니다.", this);
            return;
        }

        // 프리팹의 로컬 축 대신 실제 플레이어가 서 있는 쪽을 기계의 앞 방향으로 사용한다.
        Vector3 playerDirection = requestingPlayerPosition - transform.position;
        playerDirection.y = 0f;
        float playerDistance = playerDirection.magnitude;
        playerDirection = playerDirection.sqrMagnitude > 0.001f
            ? playerDirection.normalized
            : transform.forward;

        // 가까운 플레이어를 지나쳐 발밑에 생성되지 않도록 최대 거리와 플레이어 거리의 45% 중 작은 값을 사용한다.
        float maximumSpawnDistance = Mathf.Abs(_rewardSpawnOffset.z);
        float spawnDistance = Mathf.Min(maximumSpawnDistance, playerDistance * 0.45f);
        Vector3 spawnPosition =
            transform.position +
            playerDirection * spawnDistance +
            Vector3.up * _rewardSpawnOffset.y;
        GameObject rewardObject = Instantiate(
            _completionReward.WorldPrefab,
            spawnPosition,
            Quaternion.identity);

        if (!rewardObject.TryGetComponent(out PickupItem pickupItem)
            || !rewardObject.TryGetComponent(out NetworkObject networkObject))
        {
            Debug.LogError($"[MiniGame] '{_completionReward.WorldPrefab.name}'에 PickupItem 또는 NetworkObject가 없습니다.", this);
            Destroy(rewardObject);
            return;
        }

        pickupItem.Configure(_completionReward);
        networkObject.Spawn(destroyWithScene: true);

        GetComponent<MiniGameRewardLauncher>()?
            .Launch(rewardObject, spawnPosition, requestingPlayerPosition);
    }

    // 열려 있는 미니게임 UI를 닫는다.
    public void CloseUI()
    {
        if (_uiInstance == null)
        {
            ReleaseActiveState();
            return;
        }

        MiniGameUIController controller = _uiInstance.GetComponent<MiniGameUIController>();
        if (controller != null)
        {
            controller.Close();
            return;
        }

        Destroy(_uiInstance);
        GameplayUiMode.Instance?.DeactivateCursor();
        NotifyUIClosed(null);
    }

    // 닫힌 UI 참조와 전역 미니게임 사용 상태를 해제한다.
    public void NotifyUIClosed(MiniGameUIController controller)
    {
        if (controller == null || controller.gameObject == _uiInstance)
        {
            _uiInstance = null;
            ReleaseActiveState();
        }
    }

    // 단계형 게임 UI 인스턴스는 유지하고 현재 사용 상태만 해제한다.
    public void NotifyUISuspended(MiniGameUIController controller)
    {
        if (controller != null && controller.gameObject == _uiInstance)
        {
            ReleaseActiveState();
        }
    }

    // 현재 기계가 사용 중인 미니게임 상태를 해제한다.
    private void ReleaseActiveState()
    {
        if (_activeInteractable == this)
        {
            _activeInteractable = null;
        }
    }

    // 미니게임 UI 입력에 필요한 EventSystem이 없으면 생성한다.
    private static void EnsureEventSystem(Transform uiRoot)
    {
        if (FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new("MiniGameEventSystem");
        eventSystemObject.transform.SetParent(uiRoot, false);
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }

    // 기계가 제거될 때 열려 있는 UI와 전역 사용 상태를 정리한다.
    public override void OnDestroy()
    {
        if (_uiInstance != null)
        {
            MiniGameUIController controller = _uiInstance.GetComponent<MiniGameUIController>();
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
