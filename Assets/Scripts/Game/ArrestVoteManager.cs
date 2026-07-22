using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum ArrestVoteState
{
    Idle,     // 투표 없음
    Voting,   // 투표 진행 중
    Passed,   // 가결
    Rejected  // 부결
}

// 검거 투표를 관리하는 매니저 스크립트.
public class ArrestVoteManager : NetworkBehaviour
{
    public static ArrestVoteManager Instance { get; private set; }

    // Round1, Round2 각각에서 허용되는 최대 투표 시작 횟수
    private const int MaxVoteAttempts = 5;

    // 투표 제한 시간 (초 단위)
    private const float VoteDurationSeconds = 15f;

    // 가결에 필요한 최소 O표 수
    private const int PassThreshold = 2;

    // 결과(가결/부결) 표시 후 Idle로 돌아가기까지 대기 시간 (초 단위)
    private const float ResultHoldSeconds = 3f;

    // 결과를 Idle로 되돌릴 시각 (ServerTime 기준). 결과 화면 카운트다운 표시를 위해 클라이언트도 읽을 수 있게 동기화한다.
    private readonly NetworkVariable<double> _returnToIdleTime =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 남은 투표 시작 가능 횟수
    private readonly NetworkVariable<int> _remainingVoteAttempts =
        new(MaxVoteAttempts, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 현재 투표 진행 상태
    private readonly NetworkVariable<ArrestVoteState> _currentVoteState =
        new(ArrestVoteState.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 투표 시작 시점의 연결 인원 (참여 인원 스냅샷)
    private readonly NetworkVariable<int> _voteParticipantCount =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 투표 종료 시각 (ServerTime 기준)
    private readonly NetworkVariable<double> _voteEndTime =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ClientId별 제출한 표 (O=true, X=false). 서버에서만 집계하므로 동기화하지 않는다.
    private readonly Dictionary<ulong, bool> _votes = new();

    // 제출 완료 인원 (UI 표시용으로 클라이언트도 읽을 수 있게 동기화)
    private readonly NetworkVariable<int> _submittedCount =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 현재 검거 후보로 지정된 NPC (NPC 상호작용에서 서버가 검증 후 고정)
    private readonly NetworkVariable<NetworkObjectReference> _arrestCandidateReference =
        new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkObject ArrestCandidate { get; private set; }

    // GetRemainingVoteTime()이 Voting 상태가 아닐 때도 재계산 없이 반환할 마지막 값
    private float _cachedRemainingVoteTime;

    // GetRemainingResultTime()이 Passed/Rejected 상태가 아닐 때도 재계산 없이 반환할 마지막 값
    private float _cachedRemainingResultTime;

    // UI 등 외부에서 남은 횟수를 읽기 전용으로 참조하기 위한 프로퍼티
    public int RemainingVoteAttempts => _remainingVoteAttempts.Value;

    public ArrestVoteState CurrentVoteState => _currentVoteState.Value;
    public int SubmittedCount => _submittedCount.Value;
    public int VoteParticipantCount => _voteParticipantCount.Value;

    // 남은 투표 횟수가 바뀔 때마다(리셋 포함) UI에 알려주기 위한 이벤트
    public event Action<int> OnRemainingVoteAttemptsChanged;

    public event Action<ArrestVoteState> OnVoteStateChanged;
    public event Action<int> OnSubmittedCountChanged;

    // 투표가 가결됐을 때 실제 검거 로직이 구독해서 처리하는 이벤트
    public event Action OnVotePassed;

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
        _remainingVoteAttempts.OnValueChanged += HandleRemainingVoteAttemptsChanged;
        _currentVoteState.OnValueChanged += HandleVoteStateChanged;
        _submittedCount.OnValueChanged += HandleSubmittedCountChanged;
        _arrestCandidateReference.OnValueChanged += HandleArrestCandidateChanged;
        ResolveArrestCandidate(_arrestCandidateReference.Value);

        // 라운드별 투표 횟수 리셋 타이밍을 잡기위해 OnRoundStateChanged를 구독
        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer) return;

        switch (_currentVoteState.Value)
        {
            case ArrestVoteState.Voting:
                if (ArrestCandidate == null || !ArrestCandidate.IsSpawned)
                {
                    ForceRejectDueToMissingCandidate(); // 투표 중 대상 NPC가 사라짐. 득표 계산 없이 즉시 부결 처리
                    break;
                }

                if (NetworkManager.ServerTime.Time >= _voteEndTime.Value)
                {
                    ResolveVote(); // 제한 시간 경과. 미제출자는 표에 없으므로 자동으로 X 취급된다.
                }
                break;
            case ArrestVoteState.Passed:
            case ArrestVoteState.Rejected:
                if (NetworkManager.ServerTime.Time >= _returnToIdleTime.Value)
                {
                    ArrestCandidate?.GetComponent<NpcMovement>()?.Resume(); // 멈춰뒀던 후보 NPC 이동을 재개
                    _arrestCandidateReference.Value = default; // 다음 투표를 위해 검거 후보를 초기화
                    _currentVoteState.Value = ArrestVoteState.Idle; // 결과 표시 시간이 끝나 다음 투표를 받을 수 있게 리셋
                }
                break;
        }
    }

    public override void OnNetworkDespawn()
    {
        _remainingVoteAttempts.OnValueChanged -= HandleRemainingVoteAttemptsChanged;
        _currentVoteState.OnValueChanged -= HandleVoteStateChanged;
        _submittedCount.OnValueChanged -= HandleSubmittedCountChanged;
        _arrestCandidateReference.OnValueChanged -= HandleArrestCandidateChanged;

        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }
    }

    private void HandleRemainingVoteAttemptsChanged(int previous, int current)
    {
        OnRemainingVoteAttemptsChanged?.Invoke(current);  // UI 쪽에서 구독
    }

    private void HandleVoteStateChanged(ArrestVoteState previous, ArrestVoteState current)
    {
        OnVoteStateChanged?.Invoke(current);
    }

    private void HandleSubmittedCountChanged(int previous, int current)
    {
        OnSubmittedCountChanged?.Invoke(current);
    }

    //검거 후보자가 변경될때마다 각 클라이언트들에 후보npc를 동기화한다.
    private void HandleArrestCandidateChanged(NetworkObjectReference previous, NetworkObjectReference current)
    {
        ResolveArrestCandidate(current);
    }

    private void ResolveArrestCandidate(NetworkObjectReference reference)
    {
        ArrestCandidate = reference.TryGet(out NetworkObject candidate) ? candidate : null;
    }

    // Round1 또는 Round2가 "새로" 시작될 때마다 남은 투표 횟수를 최대치(5)로 되돌린다.
    private void HandleRoundStateChanged(RoundState state)
    {
        if (!IsServer) return;
        if (state == RoundState.Round1 || state == RoundState.Round2)
        {
            _remainingVoteAttempts.Value = MaxVoteAttempts;
        }
    }

    // 투표 시작 요청 게이트.
    // 승인 시점에 바로 차감하므로, 투표가 나중에 부결되더라도 이미 사용한 횟수로 남는다.
    public bool TryStartVote()
    {
        if (!IsServer) return false;
        if (_currentVoteState.Value == ArrestVoteState.Voting) return false;
        if (_remainingVoteAttempts.Value <= 0) return false;

        _remainingVoteAttempts.Value--;
        return true;
    }

    // NPC 상호작용(ArrestCandidateInteractable)에서 거리 재검사까지 마친 뒤 호출하는 진입점.
    // 투표 중이거나 이미 진행 중인 상태에서는 대상이 바뀌지 않도록 거절한다.
    public bool TrySetArrestCandidate(NetworkObject npc)
    {
        if (!IsServer) return false;
        if (_currentVoteState.Value != ArrestVoteState.Idle) return false;
        if (npc == null || !npc.IsSpawned) return false;

        _arrestCandidateReference.Value = npc;

        // 투표가 진행되는 동안 후보 NPC가 자리를 벗어나지 않도록 이동을 멈춘다.
        // (상호작용 시점에 이미 멈춰있는 게 보통이지만, 안전하게 한 번 더 보장한다.)
        npc.GetComponent<NpcMovement>()?.Pause();
        return true;
    }

    // NPC 상호작용 확인창에서 투표진행 버튼을 눌렀을 때 클라이언트가 호출하는 진입점.
    // 승인되면 투표를 시작 상태로 전환하고 참여 인원/종료 시각을 기록한다.
    [Rpc(SendTo.Server)]
    public void RequestStartVoteServerRpc(RpcParams rpcParams = default)
    {
        if (!TryStartVote()) return;

        _votes.Clear();
        _voteParticipantCount.Value = NetworkManager.ConnectedClientsIds.Count;
        _submittedCount.Value = 0;
        _currentVoteState.Value = ArrestVoteState.Voting;
        _voteEndTime.Value = NetworkManager.ServerTime.Time + VoteDurationSeconds;
    }

    // 투표 UI에서 O/X 버튼을 눌렀을 때 클라이언트가 호출하는 진입점.
    // ClientId당 한 표만 인정하고, 투표 중이 아니면 무시한다.
    [Rpc(SendTo.Server)]
    public void SubmitVoteServerRpc(bool isYes, RpcParams rpcParams = default)
    {
        if (_currentVoteState.Value != ArrestVoteState.Voting) return;

        ulong clientId = rpcParams.Receive.SenderClientId;
        if (_votes.ContainsKey(clientId)) return;

        _votes[clientId] = isYes;
        _submittedCount.Value = _votes.Count;

        if (_votes.Count >= _voteParticipantCount.Value)
        {
            ResolveVote(); // 참여 인원 전원이 제출했으므로 기다리지 않고 즉시 계산
        }
    }

    // 투표 중 대상 NPC가 파괴/디스폰되어 더 이상 존재하지 않을 때 즉시 부결 처리한다.
    private void ForceRejectDueToMissingCandidate()
    {
        _currentVoteState.Value = ArrestVoteState.Rejected;
        _returnToIdleTime.Value = NetworkManager.ServerTime.Time + ResultHoldSeconds;
        _arrestCandidateReference.Value = default;

        TryFailRoundIfVoteAttemptsExhausted();
    }

    // O표 수가 PassThreshold 이상이면 가결, 아니면 부결로 상태를 확정한다.
    private void ResolveVote()
    {
        int yesCount = 0;
        foreach (bool isYes in _votes.Values)
        {
            if (isYes) yesCount++;
        }

        bool passed = yesCount >= PassThreshold;
        _currentVoteState.Value = passed ? ArrestVoteState.Passed : ArrestVoteState.Rejected;
        _returnToIdleTime.Value = NetworkManager.ServerTime.Time + ResultHoldSeconds;

        if (passed)
        {
            OnVotePassed?.Invoke();  // 실제 검거 로직이 구독할 이벤트
        }
        else
        {
            TryFailRoundIfVoteAttemptsExhausted();
        }
    }

    // 남은 투표 횟수를 모두 소진했는데 이번 투표도 부결(또는 대상 소실로 강제 부결)로 끝났다면
    // 더 이상 기회가 없으므로 결과 대기 없이 즉시 라운드를 실패 처리한다.
    private void TryFailRoundIfVoteAttemptsExhausted()
    {
        if (_remainingVoteAttempts.Value > 0) return;
        RoundManager.Instance?.ForceFail();
    }

    // 투표 UI가 매 프레임 호출해서 남은 시간을 계산한다. RoundManager.GetRemainingTime()과 동일한 패턴.
    public float GetRemainingVoteTime()
    {
        if (!IsSpawned) return 0f;

        if (_currentVoteState.Value == ArrestVoteState.Voting)
        {
            _cachedRemainingVoteTime = Mathf.Max(0f, (float)(_voteEndTime.Value - NetworkManager.ServerTime.Time));
        }

        return _cachedRemainingVoteTime;
    }

    // 결과(가결/부결) 화면의 카운트다운 표시용. Voting의 GetRemainingVoteTime()과 동일한 패턴.
    public float GetRemainingResultTime()
    {
        if (!IsSpawned) return 0f;

        if (_currentVoteState.Value == ArrestVoteState.Passed || _currentVoteState.Value == ArrestVoteState.Rejected)
        {
            _cachedRemainingResultTime = Mathf.Max(0f, (float)(_returnToIdleTime.Value - NetworkManager.ServerTime.Time));
        }

        return _cachedRemainingResultTime;
    }
}
