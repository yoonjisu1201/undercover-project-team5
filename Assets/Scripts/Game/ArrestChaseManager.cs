using System;
using Unity.Netcode;
using UnityEngine;

public enum ArrestChaseState
{
    Idle,      // 추격 없음
    Chasing,   // 추격 진행 중
    Completed  // 게이지가 다 차서 검거가 완료됨
}

// 검거 투표가 가결되고 대상이 실제 범인으로 판정된 뒤의 "추격전" 단계를 관리한다.
// 흐름: ArrestJudgementManager가 범인 판정을 내리면 StartChase()를 호출해 이 매니저가 추격을 시작한다.
public class ArrestChaseManager : NetworkBehaviour
{
    public static ArrestChaseManager Instance { get; private set; }

    // 검거 도구 itemId
    public const ItemType CaptureToolItemId = ItemType.AlienCaptureGun;

    // 추격에 필요한 인원
    public const int RequiredParticipants = 2;
    private bool _debugSoloCaptureEnabled;

    [SerializeField, Min(0.1f)] private float _captureRadius = 5f;      // 대상 NPC 기준, 이 반경 안에 있어야 인원으로 카운트된다.
    [SerializeField, Min(0.1f)] private float _gaugeFillDuration = 5f;   // 조건 충족 시 0 -> 1까지 채우는 데 걸리는 시간(초)
    [SerializeField, Min(0.1f)] private float _gaugeDecayDuration = 5f;  // 조건 미충족 시 1 -> 0까지 되돌아가는 데 걸리는 시간(초)

    // UI가 "지금 이 범위 안에 있나?" 판정을 로컬에서도 할 수 있도록 반경값을 읽기 전용으로 공개한다.
    public float CaptureRadius => _captureRadius;

    // 현재 추격 중인 대상 NPC. 서버에서만 사용하므로 동기화하지 않는다.
    private NetworkObject _target;

    // _target을 클라이언트도 읽을 수 있게 동기화한 참조.
    // ArrestVoteManager.ArrestCandidate는 투표 결과 화면이 끝나면(가결 3초 후) 지워지므로,
    // 추격이 끝날 때까지 유지되는 별도의 스냅샷이 필요해서 여기 따로 둔다.
    private readonly NetworkVariable<NetworkObjectReference> _targetReference =
        new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkObject Target => _targetReference.Value.TryGet(out NetworkObject target) ? target : null;

    // 클라이언트도 UI에 상태를 표시할 수 있도록 읽기는 전체 허용, 쓰기는 서버만 허용한다.
    private readonly NetworkVariable<ArrestChaseState> _state =
        new(ArrestChaseState.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 0~1 사이 값. UI에서 그대로 게이지 바 채움 비율로 쓸 수 있다.
    private readonly NetworkVariable<float> _gauge =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 범위+도구+검거 단축키(ArrestTool, 마우스 좌클릭) 홀드까지 모두 만족하는 플레이어 수.
    // 게이지 상승 조건, 참여 아이콘 표시, 안내 문구 판정에 함께 쓰인다.
    private readonly NetworkVariable<int> _holdingCount =
        new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public ArrestChaseState CurrentState => _state.Value;
    public float Gauge => _gauge.Value; // 0~1
    public int HoldingCount => _holdingCount.Value;
    public int CurrentRequiredParticipants =>
        _debugSoloCaptureEnabled ? 1 :
        RequiredParticipants;

    // 디버그 메뉴에서 혼자 검거할 수 있도록 필요 인원을 한 명으로 전환합니다.
    public void SetDebugSoloCaptureEnabled(bool enabled)
    {
        if (!IsServer) return;
        _debugSoloCaptureEnabled = enabled;
    }

    // UI가 구독해서 게이지 바/안내 문구를 갱신하는 데 쓰는 이벤트. ArrestVoteManager의 이벤트 패턴과 동일하다.
    public event Action<ArrestChaseState> OnStateChanged;
    public event Action<float> OnGaugeChanged;

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
        _state.OnValueChanged += HandleStateChanged;
        _gauge.OnValueChanged += HandleGaugeChanged;

        // 라운드가 끝나거나(성공/실패) 다음 라운드로 넘어갈 때 추격이 어정쩡하게 남아있지 않도록 정리한다.
        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        _state.OnValueChanged -= HandleStateChanged;
        _gauge.OnValueChanged -= HandleGaugeChanged;

        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }
    }

    private void HandleStateChanged(ArrestChaseState previous, ArrestChaseState current)
    {
        OnStateChanged?.Invoke(current);
    }

    private void HandleGaugeChanged(float previous, float current)
    {
        OnGaugeChanged?.Invoke(current);
    }

    // 라운드가 InRound를 벗어나면(클리어/실패/성공) 진행 중이던 추격을 강제로 정리한다.
    // (예: 추격 도중 라운드 제한 시간이 초과돼 Fail로 바뀌는 경우)
    private void HandleRoundStateChanged(RoundState state)
    {
        if (!IsServer) return;
        if (state != RoundState.InRound)
        {
            ResetChase();
        }
    }

    // ArrestJudgementManager가 "이 NPC가 진짜 범인이다"라고 판정한 직후 호출하는 진입점.
    // 여기서부터 매 프레임 Update()가 게이지를 계산하기 시작한다.
    public void StartChase(NetworkObject candidate)
    {
        if (!IsServer) return;

        if (candidate == null)
        {
            Debug.LogWarning("[ArrestChaseManager] 추격을 시작하려 했지만 대상 NPC(candidate)가 없습니다.");
            return;
        }

        _target = candidate;
        _targetReference.Value = candidate;
        _gauge.Value = 0f;
        _state.Value = ArrestChaseState.Chasing;

        if (candidate.TryGetComponent(out NpcStateMachine stateMachine))
        {
            stateMachine.ChangeToRun();
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer) return;
        if (_state.Value != ArrestChaseState.Chasing) return;

        // 추격 도중 대상 NPC가 사라지는 예외 상황(디스폰 등) 방어 코드.
        if (_target == null || !_target.IsSpawned)
        {
            Debug.LogWarning("[ArrestChaseManager] 추격 대상 NPC가 사라져 추격을 중단합니다.");
            ResetChase();
            return;
        }

        // 범인 반경 안에서 검거도구를 장착하고 단축키까지 홀드 중인 플레이어 수
        int holdingCount = CountReadyPlayers();
        _holdingCount.Value = holdingCount; // UI가 참여 아이콘/안내 문구를 판단할 수 있도록 매 프레임 동기화

        // 반경+도구+홀드 조건을 모두 만족하는 인원이 필요 인원 수 이상이어야 게이지가 오른다.
        if (holdingCount >= CurrentRequiredParticipants)
        {
            // 조건 충족: 게이지를 채운다. _gaugeFillDuration초 동안 유지하면 가득 찬다.
            _gauge.Value = Mathf.Min(1f, _gauge.Value + Time.deltaTime / _gaugeFillDuration);

            if (_gauge.Value >= 1f)
            {
                CompleteChase();
            }
        }
        else if (_gauge.Value > 0f)
        {
            // 조건 미충족(인원 부족/범위 이탈/도구 미장착): 즉시 리셋하지 않고 서서히 되돌린다.
            _gauge.Value = Mathf.Max(0f, _gauge.Value - Time.deltaTime / _gaugeDecayDuration);
        }
    }

    // 대상 NPC 주변 _captureRadius 반경 안에서 검거 도구를 장착하고 검거 단축키(ArrestTool, 마우스 좌클릭)까지 홀드 중인 플레이어 수를 센다.
    private int CountReadyPlayers()
    {
        int readyCount = 0;

        foreach (NetworkClient client in NetworkManager.ConnectedClients.Values)
        {
            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null) continue;

            // 쓰러진 플레이어는 검거 인원으로 세지 않는다.
            // 홀드 값(_isHoldingArrestKey)은 Owner가 쓰는 값이라 서버에서 상태를 다시 확인한다.
            if (playerObject.TryGetComponent(out PlayerHealth health) && health.IsDowned) continue;

            // 범위 판정: 단순 거리 비교. (콜라이더 겹침이 아니라 대상 기준 반경 안에 있는지만 본다)
            float distance = Vector3.Distance(playerObject.transform.position, _target.transform.position);
            if (distance > _captureRadius) continue;

            // 도구 판정: 인벤토리에서 "선택된" 슬롯이 검거 도구여야 한다. (그냥 소지만으로는 인정 안 됨)
            if (!playerObject.TryGetComponent(out PlayerInventory inventory)) continue;
            if (!inventory.TryGetSelectedItemId(out ItemType itemId) || itemId != CaptureToolItemId) continue;

            if (!playerObject.TryGetComponent(out PlayerArrestInput arrestInput) || !arrestInput.IsHoldingArrestKey) continue;

            readyCount++;
        }

        return readyCount;
    }

    // 게이지가 다 찼을 때 호출된다. NPC를 완전히 멈추고 라운드 매니저에 검거 완료를 알린다.
    private void CompleteChase()
    {
        if (_target.TryGetComponent(out NpcStateMachine stateMachine))
        {
            stateMachine.ChangeToIdle();
        }

        _state.Value = ArrestChaseState.Completed;
        RoundManager.Instance.ReportArrestServerRpc(); // 라운드 클리어/게임 성공 전환은 RoundManager가 처리
    }

    // 추격 상태를 처음으로 되돌린다. (대상 소실, 라운드 종료 등 정상적으로 끝나지 않은 경우)
    private void ResetChase()
    {
        _target = null;
        _targetReference.Value = default;
        _gauge.Value = 0f;
        _state.Value = ArrestChaseState.Idle;

    }
}
