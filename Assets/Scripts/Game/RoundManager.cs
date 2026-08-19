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
    public int NpcSpawnCount = 20;        // 해당 라운드에 스폰할 NPC 수
    public int ClearReward = 1000;        // 라운드 클리어 시 지급할 공용 크레딧
}

public partial class RoundManager : NetworkBehaviour
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

    [Header("지하 맵 생성 담당 (라운드마다 새로 생성)")]
    [SerializeField] private UndergroundRandomMapGenerator _undergroundGenerator;

    [Header("라운드 클리어 보상 지급 담당")]
    [SerializeField] private ShopManager _shopManager;

    private readonly NetworkVariable<RoundState> _currentState =
        new(RoundState.Waiting, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _currentRoundIndex =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 게임 세션 하나당 한 번만 뽑는 시드. ClothCatalog가 이 값과 라운드 번호를 조합해
    // "이번 라운드에 쓸 옷 목록"을 서버/클라이언트 모두 동일하게 계산하는 데 사용한다.
    private readonly NetworkVariable<int> _clothPoolSessionSeed =
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

    private bool _isStartingNextRound;

    public RoundState CurrentState => _currentState.Value;
    public int CurrentRoundIndex => _currentRoundIndex.Value;
    public int ClothPoolSessionSeed => _clothPoolSessionSeed.Value;

    // 세션 시드 + 라운드 번호 + 호출자가 넘긴 태그를 조합해 이번 라운드용 시드를 계산한다.
    // 옷 풀, 지하 맵처럼 서로 다른 시스템이 같은 세션 시드를 공유해도 태그로 구분되어 값이 안 겹친다.
    public int GetRandomSeed(int tag) => CombineSeed(_clothPoolSessionSeed.Value, _currentRoundIndex.Value, tag);

    // System.HashCode.Combine은 프로세스마다 다른 내부 솔트를 섞어 넣어 같은 입력에도 서버/클라이언트가
    // 서로 다른 값을 얻는다 (보안 목적의 의도된 동작). 여기서는 모든 클라이언트가 반드시 같은 시드를
    // 얻어야 하므로 프로세스와 무관하게 항상 같은 결과를 내는 방식으로 직접 합성한다.
    private static int CombineSeed(int a, int b, int c)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + a;
            hash = hash * 31 + b;
            hash = hash * 31 + c;
            return hash;
        }
    }

    // HQ 타이머 UI가 남은 시간 비율(색상 변화 등)을 계산하려면 현재 라운드의 총 시간이 필요해서 노출
    public float RoundDuration => CurrentState == RoundState.InRound ?
        _rounds[_currentRoundIndex.Value].Duration : 0f;
    public float MontageShareCooldown => CurrentState == RoundState.InRound ?
        _rounds[_currentRoundIndex.Value].MontageShareCooldown : 0f;
    public int NpcSpawnCount => _rounds[_currentRoundIndex.Value].NpcSpawnCount;

    // 검거 입력을 라운드 진행 중에만 허용하기 위해 노출
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

    // 라운드 클리어 시점의 잔여시간, 라운드 자동시작까지 카운트다운 시간, 획득 보상, 지급 후 누적 크레딧을
    // RPC 파라미터로 원자적으로 전달한다.
    // (NetworkVariable 여러 개를 같은 틱에 동시 갱신하면 클라이언트의 변경 알림 발동 순서 문제로
    //  아직 갱신 전 값을 읽는 문제가 있어, 대신 RPC로 직접 넘긴다)
    public event Action<float, float, int, int> OnRoundClearAnnounced;

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
        // 스폰 대기 흐름이 세는 NPC·단서가 확정된 구역 안에 생성되도록 구역을 가장 먼저 정한다.
        BeginRegionFlow();

        // 라운드 1의 NPC는 씬 로드 완료 이벤트로 스폰되면서 그 시점에 바로 옷을 고른다.
        // ClothCatalog가 참조하는 이 시드도 StartGame()(전원 스폰 확인 이후, NPC보다 한참 뒤)이
        // 아니라 그보다 앞선 이 시점에 확정해야 NPC가 고른 옷과 이후 조회 결과가 어긋나지 않는다.
        if (IsServer)
        {
            _clothPoolSessionSeed.Value = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        }

        _currentState.OnValueChanged += HandleStateChanged;
        OnRoundStateChanged?.Invoke(_currentState.Value); // OnValueChanged는 최초 동기화값에는 발동하지 않으므로 직접 1회 호출

        // 서버/클라이언트(호스트 포함) 모두 자기 화면에 NPC/단서가 다 왔는지 직접 확인한 뒤 서버에 보고한다.
        BeginSpawnReadyFlow();

        if (IsServer && _shopManager == null)
        {
            Debug.LogError("[RoundManager] ShopManager 참조가 비어 있어 라운드 클리어 보상을 지급할 수 없습니다.", this);
        }
    }

    public override void OnNetworkDespawn()
    {
        _currentState.OnValueChanged -= HandleStateChanged;
        EndSpawnReadyFlow();
        EndRegionFlow();
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
        if (_debugTimeStopped.Value) return;
        if (NetworkManager.ServerTime.Time < _roundEndTime.Value) return;

        switch (_currentState.Value)
        {
            case RoundState.InRound:
                SetFail(); // 시간 초과로 실패 처리
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
            // 스폰보다 먼저 구역을 바꿔야 NPC·단서·미션이 새 구역 안에 생성된다.
            SelectRegionForRound(avoidCurrent: true);
            _debugTimeStopped.Value = false;
            _debugStoppedRemainingTime.Value = 0f;
            ResetMissionsForNewRound();
            ResetNpcTrackersForNewRound();
            ResetPlayerHealthForNewRound();
            ResetCartsForNewRound();

            // 이전 라운드 인벤토리와 필드 단서를 먼저 제거해 전환 중 드롭된 단서가 남지 않게 합니다.
            ClearAllPlayerInventories();
            _clueSpawner?.PrepareForNextRound();
            // 단서 스폰(SpawnForNextRound)보다 먼저 새 지하 맵을 만들어둬야 지하 스폰 영역이 준비된다.
            // 라운드 종료 시 플레이어는 전부 지상으로 텔레포트되므로, 지하에 남은 인원을 신경 쓸 필요는 없다.
            _undergroundGenerator?.RegenerateForNewRound();
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
        // _clothPoolSessionSeed는 OnNetworkSpawn에서 이미 NPC 스폰보다 먼저 확정해뒀다.
        _debugTimeStopped.Value = false;
        _debugStoppedRemainingTime.Value = 0f;
        ResetMissionsForNewRound();
        ResetNpcTrackersForNewRound();
        ClearAllPlayerInventories();
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

    // 서버가 라운드 전환 시점에 카트 홀더를 해제하고 스폰 위치로 되돌린 뒤 회복량도 다시 채운다.
    // StartPoint가 새 구역으로 옮겨가기 전에 이전 라운드에서 끌려다닌 로컬 오프셋을 지워야
    // 카트가 엉뚱한 위치로 나타나지 않는다.
    private void ResetCartsForNewRound()
    {
        if (!IsServer)
        {
            return;
        }

        CartBase[] carts = FindObjectsByType<CartBase>(FindObjectsSortMode.None);
        foreach (CartBase cart in carts)
        {
            cart.ResetForNewRound();
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
        float remainingAtClear = _debugTimeStopped.Value
            ? _debugStoppedRemainingTime.Value
            : Mathf.Max(0f, (float)(_roundEndTime.Value - NetworkManager.ServerTime.Time));
        _debugTimeStopped.Value = false;
        _debugStoppedRemainingTime.Value = 0f;
        _roundRemainingTimeAtClear.Value = remainingAtClear;

        RoundConfig currentRound = _rounds[_currentRoundIndex.Value];
        float clearWaitDuration = currentRound.ClearWaitDuration;
        _roundEndTime.Value = NetworkManager.ServerTime.Time + clearWaitDuration;

        // 보상을 먼저 지급해야, 지급 후의 누적 크레딧을 같은 RPC에 실어 보낼 수 있다.
        int totalCredits = 0;
        if (_shopManager != null)
        {
            _shopManager.AddCreditsOnServer(currentRound.ClearReward);
            totalCredits = _shopManager.Credits;
        }

        AnnounceRoundClearRpc(remainingAtClear, clearWaitDuration, currentRound.ClearReward, totalCredits);
        _currentState.Value = RoundState.RoundClear;
    }

    // 라운드 클리어 시점 값을 RPC로 전달 -> 결과패널에 남은 타이머 노출을 위한것
    [Rpc(SendTo.ClientsAndHost)]
    private void AnnounceRoundClearRpc(float remainingTimeAtClear, float countdownDuration, int clearReward, int totalCredits)
    {
        OnRoundClearAnnounced?.Invoke(remainingTimeAtClear, countdownDuration, clearReward, totalCredits);
    }

    // 라운드 시작 시점의 라운드 인덱스를 RPC로 전달 -> 라운드 번호 표시 등에서 사용
    [Rpc(SendTo.ClientsAndHost)]
    private void AnnounceRoundStartRpc(int roundIndex)
    {
        OnRoundStarted?.Invoke(roundIndex);
    }

    private void SetFail()
    {
        _currentState.Value = RoundState.Fail;
    }

    // 살아있는 플레이어가 한 명도 없으면(전원 다운) 게임을 실패 처리한다.
    // 플레이어가 다운될 때마다 서버에서 호출된다.
    public void ReportPlayerDowned()
    {
        if (!IsServer) return;
        if (_currentState.Value != RoundState.InRound) return;

        PlayerHealth[] playerHealths = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        if (playerHealths.Length == 0) return;

        foreach (PlayerHealth playerHealth in playerHealths)
        {
            if (!playerHealth.IsDowned) return;
        }

        SetFail();
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

        // 단서는 인벤토리가 아니라 별도 목록에 쌓이고, 라운드마다 번호가 새로 배정되므로 같이 비운다.
        PlayerClueBook[] clueBooks = FindObjectsByType<PlayerClueBook>(FindObjectsSortMode.None);

        foreach (PlayerClueBook clueBook in clueBooks)
        {
            clueBook.ClearOnServer();
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

        if (_debugTimeStopped.Value)
        {
            _cachedRemainingTime = _debugStoppedRemainingTime.Value;
        }
        else if (_currentState.Value == RoundState.InRound || _currentState.Value == RoundState.RoundClear)
        {
            _cachedRemainingTime = Mathf.Max(0f, (float)(_roundEndTime.Value - NetworkManager.ServerTime.Time));
        }

        return _cachedRemainingTime;
    }
}
