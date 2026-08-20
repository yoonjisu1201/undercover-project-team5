using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public enum ArrestResult
{
    None,
    Success,     // 실제 범인 검거 성공
    WrongTarget  // 오검거 (일반 NPC)
}

// 검거 시도가 들어왔을 때 대상 NPC가 실제 범인인지 판정하고, 결과를 알린 뒤 추격전으로 이어준다. (임시)
public class ArrestJudgementManager : NetworkBehaviour
{
    private const float WrongArrestTimePenaltySeconds = 120f;

    public static ArrestJudgementManager Instance { get; private set; }

    [SerializeField] private CriminalNpcManager _criminalNpcManager;

    // 현재 라운드에서 발생한 오검거 횟수. Round1/Round2가 새로 시작될 때마다 리셋된다.
    private readonly NetworkVariable<int> _wrongArrestCount =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int WrongArrestCount => _wrongArrestCount.Value;
    public string LastArrestingPlayerName { get; private set; }

    // 판정 결과가 나왔을 때 각 클라이언트에서 발생한다. UI가 구독해서 결과 패널을 띄운다.
    public event Action<ArrestResult, NetworkObject> OnJudged;

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
        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
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

    // 검거 시도가 들어왔을 때 서버에서 호출하는 진입점.
    // 대상 NPC가 실제 범인인지 판정하고, 결과를 전원에게 알린다.
    public bool TryJudgeArrest(
        NetworkObject candidate,
        ulong arrestingClientId = ulong.MaxValue)
    {
        if (!IsServer) return false;
        if (candidate == null || !candidate.IsSpawned) return false;

        if (_criminalNpcManager == null)
        {
            Debug.LogError("[ArrestJudgementManager] CriminalNpcManager 참조가 없어 검거 판정을 할 수 없습니다.");
            return false;
        }

        bool isCriminal = _criminalNpcManager.IsCriminal(candidate);

        if (isCriminal)
        {
            RevealCriminal(candidate);
        }
        else
        {
            _wrongArrestCount.Value++;
            RoundManager.Instance?.TryReduceRemainingTime(
                WrongArrestTimePenaltySeconds);

            // 확인 패널이 붙잡아 둔 NPC를 여기서 풀어준다.
            // 오검거는 추격전으로 이어지지 않아, 안 풀면 그 NPC가 계속 멈춰 있는다.
            candidate.GetComponent<NpcMovement>()?.ReleaseExternalHold();
        }

        // 결과 패널은 전원에게 띄운다. 같은 결과가 연달아 나와도 누락되지 않도록 Rpc로 알린다.
        string arrestingPlayerName = GetPlayerName(arrestingClientId);
        AnnounceJudgementRpc(
            isCriminal ? ArrestResult.Success : ArrestResult.WrongTarget,
            candidate,
            new FixedString32Bytes(arrestingPlayerName));

        if (isCriminal)
        {
            // 실제 추격전(게이지 채우기)은 ArrestChaseManager가 담당한다.
            // 게이지가 다 차면 그쪽에서 RoundManager.ReportArrestServerRpc()를 호출한다.
            ArrestChaseManager.Instance?.StartChase(candidate);
        }

        return true;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void AnnounceJudgementRpc(
        ArrestResult result,
        NetworkObjectReference candidate,
        FixedString32Bytes arrestingPlayerName)
    {
        LastArrestingPlayerName = arrestingPlayerName.ToString();
        OnJudged?.Invoke(result, candidate.TryGet(out NetworkObject npc) ? npc : null);
    }

    private static string GetPlayerName(ulong clientId)
    {
        foreach (Player player in Player.ActiveInstances)
        {
            if (player.OwnerClientId == clientId)
            {
                return player.PlayerName;
            }
        }

        return $"Player {clientId + 1}";
    }

    // 범인으로 판정된 NPC의 위장을 해제해 본모습을 드러낸다.
    private void RevealCriminal(NetworkObject candidate)
    {
        if (candidate.TryGetComponent(out CriminalAlienReveal alienReveal))
        {
            alienReveal.Reveal();
        }
        else
        {
            Debug.LogError("[ArrestJudgementManager] 범인 NPC에 CriminalAlienReveal이 없습니다.", candidate);
        }

        // 정체가 드러난 뒤에는 분신을 더 내보내지 않는다. 다음 라운드 시작 때 다시 켜진다.
        FindFirstObjectByType<GroundAlienCloneSpawner>()?.StopSpawningForRevealedCriminal();
    }

    // 오검거 시 방해 효과 2종(시야 방해/글리치) 중 하나를 랜덤으로 발동한다.
    // 발동 시점은 검거 방식이 확정된 뒤에 붙인다. 효과 자체는 그대로 둔다.
    // private void TriggerRandomInterferenceEffect()
    // {
    //     InterferenceEffectType[] effectOptions = { InterferenceEffectType.FieldVision, InterferenceEffectType.Glitch };
    //     InterferenceEffectType chosen = effectOptions[UnityEngine.Random.Range(0, effectOptions.Length)];

    //     InterferenceEffectManager.Instance?.TryStartEffect(chosen);
    // }

    public override void OnDestroy()
    {
        base.OnDestroy();

        if (Instance == this)
        {
            Instance = null;
        }
    }
}
