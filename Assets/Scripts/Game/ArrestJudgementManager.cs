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

    // 범인으로 판정됐을 때, 판정 결과 패널까지 다 끝난 뒤 추격전을 시작하기 위해 기억해두는 대상.
    private NetworkObject _pendingChaseCandidate;

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
            ArrestVoteManager.Instance.OnJudgementPhaseStarted += HandleJudgementPhaseStarted;
            ArrestVoteManager.Instance.OnJudgementPhaseEnded += HandleJudgementPhaseEnded;
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
            ArrestVoteManager.Instance.OnJudgementPhaseStarted -= HandleJudgementPhaseStarted;
            ArrestVoteManager.Instance.OnJudgementPhaseEnded -= HandleJudgementPhaseEnded;
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

        // 위장 해제는 결과 패널이 뜰 때(OnJudgementPhaseStarted),
        // 실제 추격전(게이지 채우기)은 그 패널까지 다 끝난 뒤(OnJudgementPhaseEnded) 시작한다.
        _pendingChaseCandidate = candidate;
    }

    // 가결 패널에서 미리 변하면 결과가 먼저 새어 나가므로, 결과 패널이 뜨는 순간에 본모습을 드러낸다.
    private void HandleJudgementPhaseStarted()
    {
        if (!IsServer || _pendingChaseCandidate == null) return;

        if (_pendingChaseCandidate.TryGetComponent(out CriminalAlienReveal alienReveal))
        {
            alienReveal.Reveal();
        }
        else
        {
            Debug.LogError("[ArrestJudgementManager] 범인 NPC에 CriminalAlienReveal이 없습니다.", _pendingChaseCandidate);
        }

        // 정체가 드러난 뒤에는 분신을 더 내보내지 않는다. 다음 라운드 시작 때 다시 켜진다.
        FindFirstObjectByType<AlienCloneManager>()?.StopSpawningForRevealedCriminal();
    }

    // 판정 결과(범인/오검거) 패널까지 다 끝나고 Idle로 돌아왔을 때 호출된다.
    // 범인으로 판정됐으면 추격전을 시작하고, 오검거였으면 방해 효과를 랜덤 발동한다.
    private void HandleJudgementPhaseEnded()
    {
        if (!IsServer) return;

        if (_arrestResult.Value == ArrestResult.WrongTarget)
        {
            TriggerRandomInterferenceEffect();
            CCTVDisruptionController.Instance?.TriggerWrongArrestDisruption();
        }

        if (_pendingChaseCandidate != null)
        {
            // 실제 추격전(게이지 채우기)은 ArrestChaseManager가 담당한다.
            // 게이지가 다 차면 그쪽에서 RoundManager.ReportArrestServerRpc()를 호출한다.
            ArrestChaseManager.Instance?.StartChase(_pendingChaseCandidate);
            _pendingChaseCandidate = null;
        }
    }

    // 오검거 시 방해 효과 2종(시야 방해/글리치) 중 하나를 랜덤으로 발동한다.
    private void TriggerRandomInterferenceEffect()
    {
        InterferenceEffectType[] effectOptions = { InterferenceEffectType.FieldVision, InterferenceEffectType.Glitch };
        InterferenceEffectType chosen = effectOptions[Random.Range(0, effectOptions.Length)];

        InterferenceEffectManager.Instance?.TryStartEffect(chosen);
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
