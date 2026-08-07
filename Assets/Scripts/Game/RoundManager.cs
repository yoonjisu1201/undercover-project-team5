using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum RoundState
{
    Waiting,     // 대기 (게임 시작 전)
    InRound,     // 라운드 진행 중 (몇 번째 라운드인지는 CurrentRoundIndex로 구분)
    RoundClear,  // 라운드 클리어, 다음 라운드 대기 중 (마지막 라운드 클리어는 곧바로 Success)
    Fail,        // 게임 실패 (시간 초과)
    Success      // 게임 성공 (마지막 라운드 검거 성공)
}

[System.Serializable]
public class RoundConfig
{
    public float Duration = 600f;          // 라운드 제한 시간 (초 단위)
    public float ClearWaitDuration = 5f;  // 클리어 후 다음 라운드 자동 시작까지 대기 시간
    public float MontageShareCooldown = 20f; // 몽타주 재전송 쿨타임
}

public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance { get; private set; }

    [Header("라운드별 설정 (제한 시간 / 클리어 후 다음 라운드 대기 시간)")]
    [SerializeField]
    private RoundConfig[] _rounds = new RoundConfig[]  //라운드 개수만큼
    {
        new RoundConfig { Duration = 600f, ClearWaitDuration = 5f },
        new RoundConfig { Duration = 600f, ClearWaitDuration = 5f },
    };

    [Header("게임 종료 후 돌아갈 대기방 씬")]
    [SerializeField] private string _waitingRoomSceneName = "WaitingRoom";

    [Header("스폰 완료 확인 (로딩 화면과 라운드 시작 시점을 맞추기 위함)")]
    [SerializeField] private NpcSpawner _npcSpawner;
    [SerializeField] private ClueSpawner _clueSpawner;

    [Header("캐릭터 스폰 담당하는 클래스 (게임 시작하면서 캐릭터를 적절한 위치에 스폰함)")]
    [SerializeField] private PlayerSpawner _playerSpawner;

    [Header("본부 검거도구 스폰 담당 (로딩 게이트 대상 아님)")]
    [SerializeField] private HqItemSpawner _captureGunSpawner;

    [Header("본부 에일리언 샷건 스폰 담당 (로딩 게이트 대상 아님)")]
    [SerializeField] private HqItemSpawner _shotgunSpawner;

    [Header("게임 시작하면서 몽타주 데이터 로딩하기 위함")]
    [Header("게임 시작하면서 몽타주 의류 데이터 로딩하기 위함")]
    [SerializeField] private MontageClothCatalog _catalog;
    [SerializeField] private MontageSyncManager _syncManager;
    [SerializeField] private MontageShareManager _shareManager;

    private readonly NetworkVariable<RoundState> _currentState =
        new(RoundState.Waiting, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _currentRoundIndex =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<double> _roundEndTime =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _debugTimeStopped =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> _debugStoppedRemainingTime =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 결과 확인 버튼을 누른 클라이언트 목록. 접속 중인 전원이 모이면 웨이팅룸으로 전환한다.
    private readonly NetworkList<ulong> _confirmedClients = new();

    // 게임 시작 시점 인원 수 스냅샷. 클라이언트는 전체 접속자 수를 알 수 없어 서버가 동기화해준다.
    private readonly NetworkVariable<int> _totalPlayerCount =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 라운드가 클리어되는 순간의 해당 라운드 잔여 시간 스냅샷.
    // RoundClear 상태에서는 _roundEndTime이 "다음 라운드 자동 시작까지 남은 시간"으로 재사용되어
    // GetRemainingTime()으로는 방금 끝난 라운드의 남은 시간을 구할 수 없으므로 별도로 기록해둔다.
    // (Success/Fail은 GetRemainingTime()의 _cachedRemainingTime이 전환 시점 값을 그대로 유지하므로 별도 스냅샷이 필요 없다)
    private readonly NetworkVariable<float> _roundRemainingTimeAtClear =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // GetRemainingTime()이 Fail/Success 이후에도 재계산 없이 반환할 마지막 남은 시간 (최종성공/실패 시점 값 고정용)
    private float _cachedRemainingTime;

    // 검거 투표가 진행되는 동안 라운드 타이머를 멈추기 위한 상태 (서버만 사용)
    private bool _isPausedForVote;
    private double _votePauseStartTime;
    private bool _isStartingNextRound;

    // 스폰 완료 확인 응답을 보낸 클라이언트 목록 (서버만 사용, 네트워크 동기화 불필요)
    private readonly HashSet<ulong> _spawnReadyConfirmedClients = new();

    public RoundState CurrentState => _currentState.Value;
    public int CurrentRoundIndex => _currentRoundIndex.Value;

    // HQ 타이머 UI가 남은 시간 비율(색상 변화 등)을 계산하려면 현재 라운드의 총 시간이 필요해서 노출
    public float RoundDuration => CurrentState == RoundState.InRound ?
        _rounds[_currentRoundIndex.Value].Duration : 0f;
    public float MontageShareCooldown => CurrentState == RoundState.InRound ?
        _rounds[_currentRoundIndex.Value].MontageShareCooldown : 0f;

    // 투표/검거 시스템이 아직 없어 임시로 노출 — 각 시스템이 만들어지면 이 프로퍼티를 참조해 입력을 막는다.
    public bool CanVote => _currentState.Value == RoundState.InRound;
    public bool CanArrest => _currentState.Value == RoundState.InRound;

    // 결과 패널에 "확인한 인원/총 인원"을 표시하기 위한 값
    public int ConfirmedCount => _confirmedClients.Count;
    public int TotalPlayerCount => _totalPlayerCount.Value;

    // Success/Fail 전환 시점에 멈춰있는 남은 시간을 그대로 읽기 위한 프로퍼티 (GetRemainingTime()의 재계산 분기를 타지 않음)
    public float CachedRemainingTime => _cachedRemainingTime;

    // 결과 패널에서 "Round 클리어 시점의 Round 남은 시간"을 표시하기 위한 값
    public float RoundRemainingTimeAtClear => _roundRemainingTimeAtClear.Value;
    public bool IsDebugTimeStopped => _debugTimeStopped.Value;

    public event Action<RoundState> OnRoundStateChanged; // 라운드 상태가 바뀔 때마다 전달 (늦참 클라이언트는 스폰 시 현재 상태로 1회 발동)
    public event Action<RoundState> OnRoundResult; // 결과 패널을 띄워야 하는 상태(RoundClear/Fail/Success) 진입 시 발동

    // 라운드 클리어 시점의 잔여시간, 라운드 자동시작까지 카운트다운 시간을 RPC 파라미터로 원자적으로 전달한다.
    // (NetworkVariable 여러 개를 같은 틱에 동시 갱신하면 클라이언트의 변경 알림 발동 순서 문제로
    //  아직 갱신 전 값을 읽는 문제가 있어, 대신 RPC로 직접 넘긴다)
    public event Action<float, float> OnRoundClearAnnounced;

    // 라운드가 시작될 때(InRound 진입) 라운드 인덱스를 RPC로 원자적으로 전달한다.
    // (CurrentRoundIndex를 OnRoundStateChanged 콜백 안에서 직접 읽으면, 클라이언트에서
    //  _currentState보다 _currentRoundIndex의 네트워크 갱신이 늦게 도착해 한 틱 전 값을 읽는 문제가 있다)
    public event Action<int> OnRoundStarted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        _currentState.OnValueChanged += HandleStateChanged;
        OnRoundStateChanged?.Invoke(_currentState.Value); // OnValueChanged는 최초 동기화값에는 발동하지 않으므로 직접 1회 호출

        // 서버/클라이언트(호스트 포함) 모두 자기 화면에 NPC/단서가 다 왔는지 직접 확인한 뒤 서버에 보고한다.
        WaitForLocalSpawnReadyAsync(this.GetCancellationTokenOnDestroy()).Forget();

        if (IsServer && ArrestVoteManager.Instance != null)
        {
            ArrestVoteManager.Instance.OnVoteStateChanged += HandleArrestVoteStateChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        _currentState.OnValueChanged -= HandleStateChanged;

        if (IsServer && ArrestVoteManager.Instance != null)
        {
            ArrestVoteManager.Instance.OnVoteStateChanged -= HandleArrestVoteStateChanged;
        }
    }

    // 로컬 씬에 NPC/단서가 기대한 수만큼 존재할 때까지 기다린 뒤 서버에 준비됐다고 보고한다.
    private async UniTaskVoid WaitForLocalSpawnReadyAsync(CancellationToken cancellationToken)
    {
        if (_npcSpawner == null || _clueSpawner == null)
        {
            Debug.LogError("[RoundManager] NpcSpawner/ClueSpawner 참조가 비어 있어 스폰 완료를 확인할 수 없습니다.", this);
            return;
        }

        await UniTask.WaitUntil(
            () => FindObjectsByType<NpcStateMachine>(FindObjectsSortMode.None).Length >= _npcSpawner.SpawnCount
                && FindObjectsByType<PickupItem>(FindObjectsSortMode.None).Length >= _clueSpawner.SpawnCount,
            cancellationToken: cancellationToken);

        // 나를 적절한 위치로 스폰시킨다
        _playerSpawner.SpawnPlayer(NetworkManager.Singleton.LocalClient.PlayerObject);

        // 몽타주 옷 데이터를 미리 로딩하고, 지금까지 조합된 몽타주를 내 화면에도 조립해둔다.
        await _catalog.LoadClothData();
        await _syncManager.InitializeAsync();
        await _shareManager.InitializeAsync();
        
        ReportSpawnReadyServerRpc();
    }

    // 접속자 전원의 준비 보고가 모이면 게임을 시작한다.
    [Rpc(SendTo.Server)]
    private void ReportSpawnReadyServerRpc(RpcParams rpcParams = default)
    {
        _spawnReadyConfirmedClients.Add(rpcParams.Receive.SenderClientId);

        if (_spawnReadyConfirmedClients.Count >= NetworkManager.ConnectedClientsIds.Count)
        {
            StartGame();
        }
    }

    // 검거 투표 시작부터 결과(가결/부결) 표시가 끝날 때까지 라운드 타이머를 멈추고,
    // Idle로 돌아가는 순간 멈춰있던 만큼 종료 시각을 뒤로 밀어서 재개한다.
    private void HandleArrestVoteStateChanged(ArrestVoteState state)
    {
        if (!IsServer) return;

        bool shouldBePaused = state == ArrestVoteState.Voting
            || state == ArrestVoteState.Passed
            || state == ArrestVoteState.Rejected;

        if (shouldBePaused && !_isPausedForVote)
        {
            _isPausedForVote = true;
            _votePauseStartTime = NetworkManager.ServerTime.Time;
        }
        else if (!shouldBePaused && _isPausedForVote)
        {
            _isPausedForVote = false;
            // 이 보정은 라운드 진행 중(InRound) 타이머를 위한 것이다. 투표가 가결되어 이미
            // RoundClear 등으로 전환된 뒤라면 _roundEndTime이 다른 용도(다음 라운드 카운트다운)로
            // 바뀌어 있으므로 보정을 적용하면 안 된다.
            if (_currentState.Value == RoundState.InRound)
            {
                double pausedDuration = NetworkManager.ServerTime.Time - _votePauseStartTime;
                _roundEndTime.Value += pausedDuration;
            }
        }
    }

    //최신 값으로 동기화
    private void HandleStateChanged(RoundState previous, RoundState current)
    {
        OnRoundStateChanged?.Invoke(current);

        if (current == RoundState.RoundClear || current == RoundState.Fail || current == RoundState.Success)
        {
            OnRoundResult?.Invoke(current);
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer) return;
        if (_isPausedForVote || _debugTimeStopped.Value) return;
        if (NetworkManager.ServerTime.Time < _roundEndTime.Value) return;

        switch (_currentState.Value)
        {
            case RoundState.InRound:
                _currentState.Value = RoundState.Fail; // 시간 초과로 실패 처리
                break;
            case RoundState.RoundClear:
                if (!_isStartingNextRound)
                {
                    StartNextRoundAsync(this.GetCancellationTokenOnDestroy()).Forget();
                }
                break;
        }
    }

    // 최신 필드 해방 상태로 NPC를 다시 배치한 뒤 다음 라운드를 시작합니다.
    private async UniTaskVoid StartNextRoundAsync(CancellationToken cancellationToken)
    {
        _isStartingNextRound = true;

        try
        {
            _currentRoundIndex.Value++;
            _debugTimeStopped.Value = false;
            _debugStoppedRemainingTime.Value = 0f;
            ResetMissionsForNewRound();
            ResetNpcTrackersForNewRound();
            ResetPlayerHealthForNewRound();

            // 이전 라운드 인벤토리와 필드 단서를 먼저 제거해 전환 중 드롭된 단서가 남지 않게 합니다.
            _clueSpawner?.PrepareForNextRound();
            _playerSpawner?.RespawnAllPlayers();

            if (_npcSpawner != null)
            {
                await _npcSpawner.RespawnAsync(cancellationToken);
            }

            FindFirstObjectByType<MissionSpawner>()?.RespawnMissionMachines();
            FindFirstObjectByType<BatterySpawner>()?.RespawnBatteries();
            _clueSpawner?.SpawnForNextRound();
            _captureGunSpawner?.RespawnTools(); // 다음 라운드 마다 본부에 검거도구 재생성
            _shotgunSpawner?.RespawnTools(); // 다음 라운드 마다 본부에 에일리언 샷건 재생성
            _roundEndTime.Value = NetworkManager.ServerTime.Time + _rounds[_currentRoundIndex.Value].Duration;
            _currentState.Value = RoundState.InRound;
            AnnounceRoundStartRpc(_currentRoundIndex.Value);
        }
        finally
        {
            _isStartingNextRound = false;
        }
    }

    // 게임씬 스폰 시 서버에서 자동 호출한다.
    public void StartGame()
    {
        if (!IsServer) return;
        if (_currentState.Value != RoundState.Waiting) return;

        _totalPlayerCount.Value = NetworkManager.ConnectedClientsIds.Count; // 게임 시작 시점 인원 수를 스냅샷으로 저장
        _currentRoundIndex.Value = 0;
        _debugTimeStopped.Value = false;
        _debugStoppedRemainingTime.Value = 0f;
        ResetMissionsForNewRound();
        ResetNpcTrackersForNewRound();
        _roundEndTime.Value = NetworkManager.ServerTime.Time + _rounds[0].Duration;
        _currentState.Value = RoundState.InRound;
        AnnounceRoundStartRpc(0);
    }

    // 서버가 모든 미션의 완료 상태와 랜덤 문제를 새 라운드 기준으로 초기화한다.
    private void ResetMissionsForNewRound()
    {
        if (!IsServer)
        {
            return;
        }

        MissionInteractable[] missions =
            FindObjectsByType<MissionInteractable>(FindObjectsSortMode.None);
        foreach (MissionInteractable mission in missions)
        {
            mission.ResetForNewRound();
        }
    }

    // 서버가 이전 라운드에 부착된 위치추적기를 모두 해제한다.
    // NpcTracker.TrackedInstances(현재 부착된 것만 모은 목록)를 복사본으로 순회한다 —
    // ResetForNewRound()가 내부에서 이 목록 자체를 갱신(제거)하므로 원본을 그대로 돌면 컬렉션 변경 예외가 난다.
    private void ResetNpcTrackersForNewRound()
    {
        if (!IsServer)
        {
            return;
        }

        List<NpcTracker> trackedNpcs = new(NpcTracker.TrackedInstances);
        foreach (NpcTracker tracker in trackedNpcs)
        {
            tracker.ResetForNewRound();
        }
    }

    // 서버가 라운드 시작/재시작 시점에 모든 플레이어의 체력을 초기화한다.
    private void ResetPlayerHealthForNewRound()
    {
        if (!IsServer)
        {
            return;
        }

        PlayerHealth[] playerHealths = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        foreach (PlayerHealth playerHealth in playerHealths)
        {
            playerHealth.ResetForNewRound();
        }
    }

    // 검거했다고 서버에게 알려주는 rpc
    // 검거 판정 로직(또는 테스트용 입력)에서 호출한다. 마지막 라운드가 아니면 RoundClear로, 마지막 라운드면 Success로 전환한다.
    [Rpc(SendTo.Server)]
    public void ReportArrestServerRpc()
    {
        if (!CanArrest) return;

        bool isLastRound = _currentRoundIndex.Value >= _rounds.Length - 1;
        if (isLastRound)
        {
            _currentState.Value = RoundState.Success;
            return;
        }

        // _roundEndTime을 RoundClear 대기시간으로 덮어쓰기 전에 현재 라운드 남은 시간을 직접 계산해 스냅샷으로 남긴다.
        // 검거 투표 중에는 타이머가 멈춰있는 상태라, 투표로 흘러간 시간을 빼기 위해
        // 현재 시각이 아니라 투표(일시정지) 시작 시각을 기준으로 계산한다.
        double referenceTime = _isPausedForVote ? _votePauseStartTime : NetworkManager.ServerTime.Time;
        float remainingAtClear = _debugTimeStopped.Value
            ? _debugStoppedRemainingTime.Value
            : Mathf.Max(0f, (float)(_roundEndTime.Value - referenceTime));
        _debugTimeStopped.Value = false;
        _debugStoppedRemainingTime.Value = 0f;
        _roundRemainingTimeAtClear.Value = remainingAtClear;

        float clearWaitDuration = _rounds[_currentRoundIndex.Value].ClearWaitDuration;
        _roundEndTime.Value = NetworkManager.ServerTime.Time + clearWaitDuration;
        AnnounceRoundClearRpc(remainingAtClear, clearWaitDuration);
        _currentState.Value = RoundState.RoundClear;
    }

    // 라운드 클리어 시점 값을 RPC로 전달 -> 결과패널에 남은 타이머 노출을 위한것
    [Rpc(SendTo.ClientsAndHost)]
    private void AnnounceRoundClearRpc(float remainingTimeAtClear, float countdownDuration)
    {
        OnRoundClearAnnounced?.Invoke(remainingTimeAtClear, countdownDuration);
    }

    // 라운드 시작 시점의 라운드 인덱스를 RPC로 전달 -> 라운드 번호 표시 등에서 사용
    [Rpc(SendTo.ClientsAndHost)]
    private void AnnounceRoundStartRpc(int roundIndex)
    {
        OnRoundStarted?.Invoke(roundIndex);
    }

    // 검거 투표 횟수를 모두 소진했는데 마지막 결과도 성공(가결+범인)이 아니면 결과 대기 없이 즉시 실패 처리한다.
    public void ForceFail()
    {
        if (!IsServer) return;
        if (_currentState.Value != RoundState.InRound) return;

        _currentState.Value = RoundState.Fail;
    }

    // 라운드 전환/게임 재시작 시 전체 플레이어 인벤토리를 아이템 종류 무관하게 초기화한다.
    public void ClearAllPlayerInventories()
    {
        if (!IsServer) return;

        PlayerInventory[] inventories = FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None);

        foreach (PlayerInventory inventory in inventories)
        {
            inventory.ClearAllItemsOnServer();
        }
    }

    // 결과 패널의 "확인" 버튼을 누르면 클라이언트가 호출한다. 접속 중인 전원이 확인하면 서버가 대기방 씬으로 전환한다.
    [Rpc(SendTo.Server)]
    public void ConfirmResultServerRpc(RpcParams rpcParams = default)
    {
        if (_currentState.Value != RoundState.Fail && _currentState.Value != RoundState.Success) return;

        var clientId = rpcParams.Receive.SenderClientId;
        if (_confirmedClients.Contains(clientId)) return;

        _confirmedClients.Add(clientId);
        if (_confirmedClients.Count < NetworkManager.ConnectedClientsIds.Count) return;

        _confirmedClients.Clear();
        ReturnToWaitingRoom();
    }

    // 게임 종료(성공/실패) 시 서버가 대기방 씬으로 전환한다.
    private void ReturnToWaitingRoom()
    {
        NetworkManager.SceneManager.LoadScene(_waitingRoomSceneName, LoadSceneMode.Single);
    }

    // 디버그 메뉴에서 라운드 제한시간만 정지하거나 저장된 시간부터 재개합니다.
    public bool SetDebugTimeStopped(bool stopped)
    {
        if (!IsServer || _currentState.Value != RoundState.InRound)
        {
            return false;
        }

        if (stopped == _debugTimeStopped.Value)
        {
            return true;
        }

        if (stopped)
        {
            _debugStoppedRemainingTime.Value =
                Mathf.Max(0f, (float)(_roundEndTime.Value - NetworkManager.ServerTime.Time));
        }
        else
        {
            _roundEndTime.Value =
                NetworkManager.ServerTime.Time + _debugStoppedRemainingTime.Value;
            _debugStoppedRemainingTime.Value = 0f;
        }

        _debugTimeStopped.Value = stopped;
        return true;
    }

    // 클라이언트 UI(시계 등)가 매 프레임 호출해서 남은 시간을 계산한다.
    // Fail/Success 등 라운드 진행 상태가 아닐 때는 재계산하지 않고, 직전에 계산된 값을 그대로 반환한다.
    // (그래야 성공/실패 순간 남아있던 시간이 0으로 바뀌지 않고 그대로 화면에 유지된다)
    public float GetRemainingTime()
    {
        if (!IsSpawned) return 0f;

        // 검거 투표 시작부터 결과 표시가 끝날 때까지, 서버/클라이언트 모두 남은 시간 계산을 멈춰서 타이머가 멎어 보이게 한다.
        ArrestVoteState? arrestVoteState = ArrestVoteManager.Instance?.CurrentVoteState;
        bool isVotingInProgress = arrestVoteState == ArrestVoteState.Voting
            || arrestVoteState == ArrestVoteState.Passed
            || arrestVoteState == ArrestVoteState.Rejected;

        if (_debugTimeStopped.Value)
        {
            _cachedRemainingTime = _debugStoppedRemainingTime.Value;
        }
        else if (!isVotingInProgress &&
            (_currentState.Value == RoundState.InRound || _currentState.Value == RoundState.RoundClear))
        {
            _cachedRemainingTime = Mathf.Max(0f, (float)(_roundEndTime.Value - NetworkManager.ServerTime.Time));
        }

        return _cachedRemainingTime;
    }
}
