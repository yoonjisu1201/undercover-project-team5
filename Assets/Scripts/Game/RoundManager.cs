using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public enum RoundState
{
    Waiting,     // 대기 (게임 시작 전)
    Round1,      // 1라운드 진행 중
    Round1Clear, // 1라운드 검거 성공, 2라운드 대기 중
    Round2,      // 2라운드 진행 중
    Fail,        // 게임 실패 (시간 초과)
    Success      // 게임 성공 (2라운드 검거 성공)
}

public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance { get; private set; }

    [Header("라운드 제한 시간 (초 단위, 테스트용 10분)")]
    [SerializeField] private float _round1Duration = 600f;
    [SerializeField] private float _round2Duration = 600f;

    [Header("1라운드 클리어 후 2라운드 대기 시간 (초 단위)")]
    [SerializeField] private float _round1ClearDuration = 15f;

    [Header("게임 종료 후 돌아갈 대기방 씬")]
    [SerializeField] private string _waitingRoomSceneName = "WaitingRoom";

    [Header("스폰 완료 확인 (로딩 화면과 라운드 시작 시점을 맞추기 위함)")]
    [SerializeField] private NpcSpawner _npcSpawner;
    [SerializeField] private ClueSpawner _clueSpawner;

    [Header("캐릭터 스폰 담당하는 클래스 (게임 시작하면서 캐릭터를 적절한 위치에 스폰함)")] 
    [SerializeField] private PlayerSpawner _playerSpawner;

    private readonly NetworkVariable<RoundState> _currentState =
        new(RoundState.Waiting, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<double> _roundEndTime =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 결과 확인 버튼을 누른 클라이언트 목록. 접속 중인 전원이 모이면 웨이팅룸으로 전환한다.
    private readonly NetworkList<ulong> _confirmedClients = new();

    // 게임 시작 시점 인원 수 스냅샷. 클라이언트는 전체 접속자 수를 알 수 없어 서버가 동기화해준다.
    private readonly NetworkVariable<int> _totalPlayerCount =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Round1이 클리어되는 순간의 Round1 잔여 시간 스냅샷.
    // Round1Clear 상태에서는 _roundEndTime이 "2라운드 자동 시작까지 남은 시간"으로 재사용되어
    // GetRemainingTime()으로는 원래 Round1의 남은 시간을 구할 수 없으므로 별도로 기록해둔다.
    // (Success/Fail은 GetRemainingTime()의 _cachedRemainingTime이 전환 시점 값을 그대로 유지하므로 별도 스냅샷이 필요 없다)
    private readonly NetworkVariable<float> _round1RemainingTimeAtClear =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // GetRemainingTime()이 Fail/Success 이후에도 재계산 없이 반환할 마지막 남은 시간 (최종성공/실패 시점 값 고정용)
    private float _cachedRemainingTime;

    // 검거 투표가 진행되는 동안 라운드 타이머를 멈추기 위한 상태 (서버만 사용)
    private bool _isPausedForVote;
    private double _votePauseStartTime;

    // 스폰 완료 확인 응답을 보낸 클라이언트 목록 (서버만 사용, 네트워크 동기화 불필요)
    private readonly HashSet<ulong> _spawnReadyConfirmedClients = new();

    public RoundState CurrentState => _currentState.Value;

    // HQ 타이머 UI가 남은 시간 비율(색상 변화 등)을 계산하려면 라운드별 총 시간이 필요해서 노출
    public float RoundDuration => CurrentState switch
    {
        RoundState.Round1 => _round1Duration,
        RoundState.Round2 => _round2Duration,
        _ => 0f
    };

    // 투표/검거 시스템이 아직 없어 임시로 노출 — 각 시스템이 만들어지면 이 프로퍼티를 참조해 입력을 막는다.
    public bool CanVote => _currentState.Value == RoundState.Round1 || _currentState.Value == RoundState.Round2;
    public bool CanArrest => _currentState.Value == RoundState.Round1 || _currentState.Value == RoundState.Round2;

    // 결과 패널에 "확인한 인원/총 인원"을 표시하기 위한 값
    public int ConfirmedCount => _confirmedClients.Count;
    public int TotalPlayerCount => _totalPlayerCount.Value;

    // Success/Fail 전환 시점에 멈춰있는 남은 시간을 그대로 읽기 위한 프로퍼티 (GetRemainingTime()의 재계산 분기를 타지 않음)
    public float CachedRemainingTime => _cachedRemainingTime;

    // 결과 패널에서 "Round1 클리어 시점의 Round1 남은 시간"을 표시하기 위한 값
    public float Round1RemainingTimeAtClear => _round1RemainingTimeAtClear.Value;

    public event Action<RoundState> OnRoundStateChanged; // 라운드 상태가 바뀔 때마다 전달 (늦참 클라이언트는 스폰 시 현재 상태로 1회 발동)
    public event Action<RoundState> OnRoundResult; // 결과 패널을 띄워야 하는 상태(Round1Clear/Fail/Success) 진입 시 발동

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

        ReportSpawnReadyServerRpc();
    }

    // 접속자 전원의 준비 보고가 모이면 Round1을 시작한다.
    [Rpc(SendTo.Server)]
    private void ReportSpawnReadyServerRpc(RpcParams rpcParams = default)
    {
        _spawnReadyConfirmedClients.Add(rpcParams.Receive.SenderClientId);

        if (_spawnReadyConfirmedClients.Count >= NetworkManager.ConnectedClientsIds.Count)
        {
            StartRound1();
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
            double pausedDuration = NetworkManager.ServerTime.Time - _votePauseStartTime;
            _roundEndTime.Value += pausedDuration;
        }
    }

    //최신 값으로 동기화
    private void HandleStateChanged(RoundState previous, RoundState current)
    {
        OnRoundStateChanged?.Invoke(current);

        if (current == RoundState.Round1Clear || current == RoundState.Fail || current == RoundState.Success)
        {
            OnRoundResult?.Invoke(current);
        }
    }

    private void Update()
    {
        //임시 검거 테스트용: 실제 검거 판정 시스템 생기면 제거
        if (IsSpawned && Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            ReportArrestServerRpc();
        }

        if (!IsSpawned || !IsServer) return;
        if (_isPausedForVote) return; // 검거 투표 진행 중에는 시간초과 판정도 멈춘다
        if (NetworkManager.ServerTime.Time < _roundEndTime.Value) return;

        switch (_currentState.Value)
        {
            case RoundState.Round1:
            case RoundState.Round2:
                _currentState.Value = RoundState.Fail; // 시간 초과로 실패 처리
                break;
            case RoundState.Round1Clear:
                _clueSpawner.RespawnClues(); // 2라운드 단서 재생성
                _roundEndTime.Value = NetworkManager.ServerTime.Time + _round2Duration;
                _currentState.Value = RoundState.Round2; // 대기 시간 종료, 2라운드 자동 시작
                break;
        }
    }

    // 게임씬 스폰 시 서버에서 자동 호출한다.
    public void StartRound1()
    {
        if (!IsServer) return;
        if (_currentState.Value != RoundState.Waiting) return;
        
        // 게임 시작 시 모든 플레이어를 적절한 위치로 이동시킨다
        _playerSpawner.SpawnClients();

        _totalPlayerCount.Value = NetworkManager.ConnectedClientsIds.Count; // 게임 시작 시점 인원 수를 스냅샷으로 저장
        _roundEndTime.Value = NetworkManager.ServerTime.Time + _round1Duration;
        _currentState.Value = RoundState.Round1;
    }

    // 검거했다고 서버에서 알려주는 rpc
    // 검거 판정 로직(또는 테스트용 입력)에서 호출한다. Round1 성공 시 Round1Clear로, Round2 성공 시 Success로 전환한다.
    [Rpc(SendTo.Server)]
    public void ReportArrestServerRpc()
    {
        if (!CanArrest) return;

        switch (_currentState.Value)
        {
            case RoundState.Round1:
                // _roundEndTime을 Round1Clear 대기시간으로 덮어쓰기 전에 Round1 남은 시간을 스냅샷으로 남긴다.
                _round1RemainingTimeAtClear.Value = _cachedRemainingTime;
                _roundEndTime.Value = NetworkManager.ServerTime.Time + _round1ClearDuration;
                _currentState.Value = RoundState.Round1Clear;
                break;
            case RoundState.Round2:
                _currentState.Value = RoundState.Success;
                break;
        }
    }

    // 검거 투표 횟수를 모두 소진했는데 마지막 결과도 성공(가결+범인)이 아니면 결과 대기 없이 즉시 실패 처리한다.
    public void ForceFail()
    {
        if (!IsServer) return;
        if (_currentState.Value != RoundState.Round1 && _currentState.Value != RoundState.Round2) return;

        _currentState.Value = RoundState.Fail;
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

        if (!isVotingInProgress &&
            (_currentState.Value == RoundState.Round1 || _currentState.Value == RoundState.Round2 ||
             _currentState.Value == RoundState.Round1Clear))
        {
            _cachedRemainingTime = Mathf.Max(0f, (float)(_roundEndTime.Value - NetworkManager.ServerTime.Time));
        }

        return _cachedRemainingTime;
    }
}
