using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;

// 라운드의 주요 진행 목표와 배전반 미션 흐름을 로컬 플레이어에게만 안내한다.
// 실제 게임 상태는 각 시스템이 계속 소유하고, 이 컴포넌트는 플레이어가 이번 라운드에
// 어떤 안내를 이미 봤는지만 기억한다. 따라서 가이드 표시를 위해 네트워크 상태를 추가하지 않는다.
[RequireComponent(typeof(PlayerInventory), typeof(PlayerClueBook))]
public sealed class GameplayGuideController : NetworkBehaviour
{
    private const string LocalizationTable = "Language Table";
    private const string StartGuideTitleKey = "start_guide_title";
    private const string BreakerGuideTitleKey = "breaker_guide_title";
    private const string CaptureToolTitleKey = "item_display_AlienCaptureGun";
    private const string Round2GuideTitleKey = "round_start_guide_title_2";
    private const string Round3GuideTitleKey = "round_start_guide_title_3";
    private const string ClueGuideTitleKey = "clue_guide_title";

    private PlayerInventory _inventory;
    private PlayerClueBook _clueBook;
    private RoundManager _roundManager;
    // 라운드 진행 중에만 월드 가이드를 띄운다. 결과 화면이나 라운드 대기 중에는 안내가 방해가 된다.
    private bool _roundActive;
    // 배전반이 완료된 뒤에는 전력 복구 관련 안내를 모두 막는다.
    private bool _breakerCompleted;
    // 완료 안내를 띄워야 하지만 미션 UI에 가려지는 상태. UI가 닫힌 뒤로 미룬다.
    private bool _breakerCompletionGuidePending;
    private bool _missingGuideUiWarned;

    // 같은 플레이어 오브젝트에 있는 로컬 인벤토리를 캐시한다.
    private void Awake()
    {
        _inventory = GetComponent<PlayerInventory>();
        _clueBook = GetComponent<PlayerClueBook>();
    }

    // 소유자 플레이어만 가이드에 필요한 로컬 이벤트를 구독한다.
    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            return;
        }

        _inventory.OnItemAdded += HandleItemAdded;
        _clueBook.OnClueAdded += HandleClueAdded;
        BreakerCircuitState.CompletionChangedLocally += HandleCompletionChanged;
        BindRoundManager();
    }

    // 플레이어가 네트워크에서 제거될 때 로컬 이벤트 구독을 정리한다.
    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            _inventory.OnItemAdded -= HandleItemAdded;
            _clueBook.OnClueAdded -= HandleClueAdded;
            BreakerCircuitState.CompletionChangedLocally -= HandleCompletionChanged;
            UnbindRoundManager();
        }

        base.OnNetworkDespawn();
    }

    // 구독으로는 잡을 수 없는 두 시점을 매 프레임 확인한다.
    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        // 스폰 순서 때문에 RoundManager를 아직 찾지 못한 경우 연결될 때까지 다시 시도한다.
        if (_roundManager == null)
        {
            BindRoundManager();
        }

        // 미션 UI가 닫히는 시점을 알려주는 이벤트가 없어, UI가 내려간 프레임을 직접 잡아 미뤄둔 안내를 띄운다.
        if (_breakerCompletionGuidePending && !GameplayUiMode.IsActive)
        {
            _breakerCompletionGuidePending = false;
            Show(BreakerGuideTitleKey, "breaker_guide_completed");
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

    // 새 라운드가 시작되면 배전반 안내를 다시 허용하고 이번 라운드의 주요 목표를 표시한다.
    private void HandleRoundStarted(int roundIndex)
    {
        _roundActive = true;

        // 배전반은 라운드마다 서버에서 초기화되므로(MissionInteractable.ServerRoundReset),
        // 완료 해제 이벤트가 언제 도착하든 로컬 안내 상태도 라운드 시작 시점에 함께 되돌린다.
        _breakerCompleted = false;
        _breakerCompletionGuidePending = false;

        if (roundIndex == 0)
        {
            Show(StartGuideTitleKey, "start_guide_hq_intro");
        }
        else if (roundIndex == 1)
        {
            Show(Round2GuideTitleKey, "time_limit_guide_round_2");
        }
        else if (roundIndex == 2)
        {
            Show(Round3GuideTitleKey, "time_limit_guide_round_3");
        }
    }

    // 라운드 진행 중에만 월드 가이드가 표시되도록 현재 상태를 기억한다.
    private void HandleRoundStateChanged(RoundState state)
    {
        _roundActive = state == RoundState.InRound;
    }

    // 배전반 완료 전에는 본부 안전지대를 벗어날 때마다 전력 복구 목적을 다시 안내한다.
    private void OnTriggerExit(Collider other)
    {
        if (!IsOwner || !_roundActive || _breakerCompleted
            || other.GetComponent<HqSafeZone>() == null)
        {
            return;
        }

        Show(BreakerGuideTitleKey, "breaker_guide_power_outage");
    }

    // 배전반 완료 전에는 배전반용 건전지를 얻을 때마다 사용 목적을 안내한다.
    private void HandleItemAdded(ItemBase item)
    {
        if (!_roundActive || item == null)
        {
            return;
        }

        if (item.ItemId == ItemType.AlienCaptureGun)
        {
            Show(CaptureToolTitleKey, "capture_tool_guide_picked_up");
            return;
        }

        if (!_breakerCompleted && BreakerBatteryTypes.TryGetWatt(item.ItemId, out _))
        {
            Show(BreakerGuideTitleKey, "breaker_guide_battery_picked_up");
        }
    }

    // 모든 플레이어에게 공유된 단서가 이번 라운드 목록에 처음 들어온 순간 다음 협동 행동을 안내한다.
    private void HandleClueAdded(int clueNumber)
    {
        // 단서 목록은 라운드 전환 때 전체가 비워지고 개별 제거는 없으므로,
        // 개수가 1이 되는 순간이 곧 이번 라운드의 첫 단서다.
        if (!_roundActive || _clueBook.ClueNumbers.Count != 1)
        {
            return;
        }

        Show(ClueGuideTitleKey, "clue_guide_first_acquired");
    }

    // 복제된 완료 상태를 받으면 이후 배전반 안내를 차단하고, 열려 있는 UI가 닫힌 뒤 성공 안내를 표시하도록 예약한다.
    private void HandleCompletionChanged(bool completed)
    {
        _breakerCompleted = completed;

        if (!completed)
        {
            _breakerCompletionGuidePending = false;
            return;
        }

        if (!_roundActive)
        {
            return;
        }

        _breakerCompletionGuidePending = true;
    }

    // 지정한 현지화 제목과 본문을 현재 클라이언트의 공용 가이드 카드에 표시한다.
    private void Show(string titleKey, string bodyKey)
    {
        if (GuideUI.Instance == null)
        {
            // 씬에 GuideUI 프리팹이 없으면 모든 안내가 조용히 사라지므로, 한 번은 원인을 남긴다.
            if (!_missingGuideUiWarned)
            {
                _missingGuideUiWarned = true;
                Debug.LogWarning("[GameplayGuideController] 씬에 GuideUI가 없어 진행 가이드를 표시할 수 없습니다.", this);
            }

            return;
        }

        GuideUI.Instance.ShowGuide(
            new LocalizedString(LocalizationTable, titleKey),
            new LocalizedString(LocalizationTable, bodyKey));
    }
}
