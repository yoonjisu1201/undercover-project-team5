using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// 검거 투표를 관리하는 매니저 스크립트.
public class ArrestVoteManager : NetworkBehaviour
{
    public static ArrestVoteManager Instance { get; private set; }

    // Round1, Round2 각각에서 허용되는 최대 투표 시작 횟수
    private const int MaxVoteAttempts = 5;

    // 남은 투표 시작 가능 횟수
    private readonly NetworkVariable<int> _remainingVoteAttempts =
        new(MaxVoteAttempts, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // UI 등 외부에서 남은 횟수를 읽기 전용으로 참조하기 위한 프로퍼티
    public int RemainingVoteAttempts => _remainingVoteAttempts.Value;

    // 남은 횟수가 바뀔 때마다(리셋 포함) UI에 알려주기 위한 이벤트
    public event Action<int> OnRemainingVoteAttemptsChanged;

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

        // 라운드별 투표 횟수 리셋 타이밍을 잡기위해 OnRoundStateChanged를 구독
        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
        }
    }

    //임시 투표 요청 테스트용: 실제 NPC 상호작용/확인 UI가 생기면 제거
    private void Update()
    {
        if (IsSpawned && Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
        {
            RequestStartVoteServerRpc();
        }
    }

    public override void OnNetworkDespawn()
    {
        _remainingVoteAttempts.OnValueChanged -= HandleRemainingVoteAttemptsChanged;

        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }
    }

    private void HandleRemainingVoteAttemptsChanged(int previous, int current)
    {
        OnRemainingVoteAttemptsChanged?.Invoke(current);  // UI 쪽에서 구독
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
        if (_remainingVoteAttempts.Value <= 0) return false;

        _remainingVoteAttempts.Value--;
        return true;
    }

    // NPC 상호작용 확인창에서 투표진행 버튼을 눌렀을 때 클라이언트가 호출하는 진입점.
    // 테스트용으로 결과를 콘솔에 로그로 남긴다.
    [Rpc(SendTo.Server)]
    public void RequestStartVoteServerRpc(RpcParams rpcParams = default)
    {
        bool approved = TryStartVote();
        Debug.Log($"[TEST] 투표 가능 여부={approved}, 남은 투표 횟수={RemainingVoteAttempts}");
    }
}
