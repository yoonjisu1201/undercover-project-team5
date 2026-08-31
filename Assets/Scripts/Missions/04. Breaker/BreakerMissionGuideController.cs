using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;

// 배전반 미션의 월드 진행 흐름을 로컬 플레이어에게만 안내한다.
// 실제 퍼즐 상태는 BreakerCircuitState가 계속 소유하고, 이 컴포넌트는 플레이어가 이번 라운드에
// 어떤 안내를 이미 봤는지만 기억한다. 따라서 가이드 표시를 위해 네트워크 상태를 추가하지 않는다.
[RequireComponent(typeof(PlayerInventory))]
public sealed class BreakerMissionGuideController : NetworkBehaviour
{
    private const string LocalizationTable = "Language Table";
    private const string GuideTitleKey = "breaker_guide_title";

    private PlayerInventory _inventory;
    private RoundManager _roundManager;
    private bool _roundActive;
    private bool _hasShownPowerOutage;
    private bool _hasShownBatteryPickup;
    private bool _hasOpenedFieldBreaker;
    private bool _hasOpenedHqMonitor;
    private bool _hasShownCompletion;

    // 같은 플레이어 오브젝트에 있는 로컬 인벤토리를 캐시한다.
    private void Awake()
    {
        _inventory = GetComponent<PlayerInventory>();
    }

    // 소유자 플레이어만 가이드에 필요한 로컬 이벤트를 구독한다.
    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            return;
        }

        _inventory.OnItemAdded += HandleItemAdded;
        BreakerCircuitState.CompletionChangedLocally += HandleCompletionChanged;
        BindRoundManager();
    }

    // 플레이어가 네트워크에서 제거될 때 로컬 이벤트 구독을 정리한다.
    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            _inventory.OnItemAdded -= HandleItemAdded;
            BreakerCircuitState.CompletionChangedLocally -= HandleCompletionChanged;
            UnbindRoundManager();
        }

        base.OnNetworkDespawn();
    }

    // 스폰 순서 때문에 RoundManager를 아직 찾지 못한 경우 연결될 때까지만 다시 시도한다.
    private void Update()
    {
        if (IsOwner && _roundManager == null)
        {
            BindRoundManager();
        }
    }

    // 현재 씬의 RoundManager에 라운드 시작·상태 변경 이벤트를 연결한다.
    private void BindRoundManager()
    {
        if (RoundManager.Instance == null || _roundManager == RoundManager.Instance)
        {
            return;
        }

        UnbindRoundManager();
        _roundManager = RoundManager.Instance;
        _roundManager.OnRoundStarted += HandleRoundStarted;
        _roundManager.OnRoundStateChanged += HandleRoundStateChanged;
        _roundActive = _roundManager.CurrentState == RoundState.InRound;
    }

    // 씬 전환이나 네트워크 해제 전에 기존 RoundManager 이벤트 연결을 끊는다.
    private void UnbindRoundManager()
    {
        if (_roundManager == null)
        {
            return;
        }

        _roundManager.OnRoundStarted -= HandleRoundStarted;
        _roundManager.OnRoundStateChanged -= HandleRoundStateChanged;
        _roundManager = null;
    }

    // 새 라운드가 시작되면 모든 일회성 가이드를 다시 볼 수 있도록 기록을 초기화한다.
    private void HandleRoundStarted(int roundIndex)
    {
        _roundActive = true;
        _hasShownPowerOutage = false;
        _hasShownBatteryPickup = false;
        _hasOpenedFieldBreaker = false;
        _hasOpenedHqMonitor = false;
        _hasShownCompletion = false;
    }

    // 라운드 진행 중에만 월드 가이드가 표시되도록 현재 상태를 기억한다.
    private void HandleRoundStateChanged(RoundState state)
    {
        _roundActive = state == RoundState.InRound;
    }

    // 본부 안전지대를 처음 벗어날 때 가로등을 복구해야 한다는 목적을 소개한다.
    private void OnTriggerExit(Collider other)
    {
        if (!IsOwner || !_roundActive || _hasShownPowerOutage
            || other.GetComponent<HqSafeZone>() == null)
        {
            return;
        }

        _hasShownPowerOutage = true;
        Show("breaker_guide_power_outage");
    }

    // 이번 라운드에 배전반용 건전지를 처음 얻은 순간 사용 목적을 안내한다.
    private void HandleItemAdded(ItemBase item)
    {
        if (!_roundActive || _hasShownBatteryPickup || item == null
            || !BreakerBatteryTypes.TryGetWatt(item.ItemId, out _))
        {
            return;
        }

        _hasShownBatteryPickup = true;
        Show("breaker_guide_battery_picked_up");
    }

    // 배전반 UI를 연 플레이어에게 레버를 내린 뒤 건전지를 배치하는 순서를 최초 한 번 안내한다.
    public void NotifyFieldBreakerOpened()
    {
        if (!IsOwner || !_roundActive || _hasOpenedFieldBreaker)
        {
            return;
        }

        _hasOpenedFieldBreaker = true;
        Show("breaker_guide_first_open");
    }

    // 본부 전력 감시 화면을 처음 연 플레이어에게 목표 지점을 현장에 전달하도록 안내한다.
    public void NotifyHqMonitorOpened()
    {
        if (!IsOwner || !_roundActive || _hasOpenedHqMonitor)
        {
            return;
        }

        _hasOpenedHqMonitor = true;
        Show("breaker_guide_hq_monitor_open");
    }

    // 복제된 완료 상태를 받아 성공 안내를 표시하고, 완료가 풀리면 다음 라운드를 위해 기록을 되돌린다.
    private void HandleCompletionChanged(bool completed)
    {
        if (!completed)
        {
            _hasShownCompletion = false;
            return;
        }

        if (!_roundActive || _hasShownCompletion)
        {
            return;
        }

        _hasShownCompletion = true;
        Show("breaker_guide_completed");
    }

    // 지정한 현지화 키를 현재 클라이언트의 배전반 전용 가이드 카드에 표시한다.
    private static void Show(string key)
    {
        BreakerGuideUI.Instance?.ShowGuide(
            new LocalizedString(LocalizationTable, GuideTitleKey),
            new LocalizedString(LocalizationTable, key));
    }
}
