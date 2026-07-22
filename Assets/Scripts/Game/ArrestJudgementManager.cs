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

    public ArrestResult CurrentArrestResult => _arrestResult.Value;

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
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && ArrestVoteManager.Instance != null)
        {
            ArrestVoteManager.Instance.OnVotePassed -= HandleVotePassed;
            ArrestVoteManager.Instance.OnVoteStateChanged -= HandleVoteStateChanged;
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
            return;
        }

        _arrestResult.Value = ArrestResult.Success;

        // 검거 성공한 NPC는 완전히 정지시킨다.
        if (candidate.TryGetComponent(out NpcStateMachine stateMachine))
        {
            stateMachine.RequestIdle();
        }

        if (candidate.TryGetComponent(out NpcRandomWander randomWander))
        {
            randomWander.enabled = false;
        }

        RoundManager.Instance.ReportArrestServerRpc();
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
