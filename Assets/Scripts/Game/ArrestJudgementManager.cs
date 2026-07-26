using Unity.Netcode;
using UnityEngine;

public enum ArrestResult
{
    None,
    Success,     // 실제 범인 검거 성공
    WrongTarget  // 오검거 (일반 NPC)
}

// 검거 투표가 가결됐을 때 대상 NPC가 실제 범인인지 판정하고, 결과에 따라 NPC 정지와 라운드 전환을 처리한다. (임시)
public class ArrestJudgementManager : NetworkBehaviour
{
    public static ArrestJudgementManager Instance { get; private set; }

    [SerializeField] private CriminalNpcManager _criminalNpcManager;

    // 가장 최근 검거 판정 결과. UI가 매 프레임 읽어서 오검거 여부를 표시하는 데 사용한다.
    private readonly NetworkVariable<ArrestResult> _arrestResult =
        new(ArrestResult.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 현재 라운드에서 발생한 오검거 횟수. Round1/Round2가 새로 시작될 때마다 리셋된다.
    private readonly NetworkVariable<int> _wrongArrestCount =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public ArrestResult CurrentArrestResult => _arrestResult.Value;
    public int WrongArrestCount => _wrongArrestCount.Value;

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
        if (IsServer && ArrestVoteManager.Instance != null)
        {
            ArrestVoteManager.Instance.OnVotePassed += HandleVotePassed;
            ArrestVoteManager.Instance.OnVoteStateChanged += HandleVoteStateChanged;
        }

        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && ArrestVoteManager.Instance != null)
        {
            ArrestVoteManager.Instance.OnVotePassed -= HandleVotePassed;
            ArrestVoteManager.Instance.OnVoteStateChanged -= HandleVoteStateChanged;
        }

        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }
    }

    // 새 투표가 시작되면 이전 라운드의 판정 결과를 지운다.
    private void HandleVoteStateChanged(ArrestVoteState state)
    {
        if (state == ArrestVoteState.Voting)
        {
            _arrestResult.Value = ArrestResult.None;
        }
    }

    // 라운드가 새로 시작될 때마다 오검거 횟수를 리셋한다.
    private void HandleRoundStateChanged(RoundState state)
    {
        if (!IsServer) return;
        if (state == RoundState.InRound)
        {
            _wrongArrestCount.Value = 0;
        }
    }

    // 투표가 가결됐을 때 호출된다. 후보 NPC가 실제 범인인지 판정해 성공/오검거를 확정한다.
    private void HandleVotePassed()
    {
        NetworkObject candidate = ArrestVoteManager.Instance.ArrestCandidate;

        if (candidate == null) return;
        if (!IsServer) return;

        if (_criminalNpcManager == null)
        {
            Debug.LogError("[ArrestJudgementManager] CriminalNpcManager 참조가 없어 검거 판정을 할 수 없습니다.");
            return;
        }

        bool isCriminal = _criminalNpcManager.IsCriminal(candidate);

        if (!isCriminal)
        {
            _arrestResult.Value = ArrestResult.WrongTarget;
            _wrongArrestCount.Value++;

            // 오검거이고 남은 투표 횟수도 없다면 더 이상 기회가 없으므로 즉시 실패 처리한다.
            if (ArrestVoteManager.Instance.RemainingVoteAttempts <= 0)
            {
                RoundManager.Instance.ForceFail();
            }
            return;
        }

        _arrestResult.Value = ArrestResult.Success;

        // 실제 추격전(게이지 채우기)은 ArrestChaseManager가 담당한다.
        // 게이지가 다 차면 그쪽에서 RoundManager.ReportArrestServerRpc()를 호출한다.
        ArrestChaseManager.Instance?.StartChase(candidate);
    }

    public override void OnDestroy()
    {
        base.OnDestroy();

        if (Instance == this)
        {
            Instance = null;
        }
    }
}
