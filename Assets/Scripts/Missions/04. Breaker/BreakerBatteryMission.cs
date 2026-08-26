using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

// 배터리 배치 상태, 정답 판정, 실제 플레이어 인벤토리 연결을 관리한다.
public sealed partial class BreakerBatteryMission : MonoBehaviour, IUIDragDropContext
{
    // 아이템 ID에서 배터리 용량을 판별할 때 허용하는 와트 목록이다.
    // 현재 인벤토리 UI에 생성된 드래그 가능한 배터리들이다.
    private readonly List<BatteryDragItem> _batteries = new();
    private readonly BatteryDragItem[] _slots = new BatteryDragItem[4]; // 오른쪽 전원 슬롯의 드롭 영역 컴포넌트들이다.

    private readonly UIDropSlot[] _dropSlots = new UIDropSlot[4];       // 인벤토리를 다시 그릴 때 제거할 UI 셀 목록이다.

    private readonly List<RectTransform> _inventoryCells = new();    // 미션 진입 시 실제 인벤토리에서 꺼내 미션이 임시로 보관하는 건전지 ID들이다.

    private readonly List<ItemType> _stagedBatteryItemIds = new();

    // 서버에 보낸 뒤 아직 복제가 돌아오지 않은 이동. 배터리 번호 → 목표 칸(-1이면 보관함).
    // 이게 없으면 왕복 시간 동안 화면이 옛 상태로 한 번 그려져 원래 자리로 튕겼다 돌아온다.
    private readonly Dictionary<int, int> _pendingPlacements = new();

    private Transform _inventoryPanel;
    private RectTransform _inventoryContent;
    private ScrollRect _inventoryScroll;
    // 실제 플레이어 인벤토리 데이터와 아이템 아이콘 정보를 제공한다.
    private PlayerInventory _playerInventory;
    private ItemCatalog _itemCatalog;
    private TMP_Text _currentValueText;
    private Image _wattFill;
    private RectTransform _wattFillRect;
    private float _wattFillMinAnchorX;
    private float _wattFillMaxAnchorX;
    // 전원이 켜져 있어 배터리를 만질 수 없는 동안 전원판을 어둡게 덮는 오버레이다.
    [SerializeField] private GameObject _dimmer;
    // 레버를 내려 배치를 수정할 수 있을 때만 '다시 하기', 전원이 들어와 측정 중일 때만 '확인'을 누를 수 있다.
    // 버튼은 계속 보이되 못 누르는 상태에서는 회색으로 흐려진다.
    [SerializeField] private Button _retryButton;
    [SerializeField] private Button _confirmButton;
    // 전원·측정·완료 상태를 글로 보여준다. 목표 전력 수치는 C(계기판) 역할의 정보라 여기서는 드러내지 않는다.
    [SerializeField] private TMP_Text _statusText;
    // 현재 보유한 건전지로 만들 수 있는 조합 중 무작위로 선택한 목표 전력이다. A에게는 수치로 보여주지 않는다.
    private int _targetWatt;
    // 레버·게이지 등 다른 역할과 공유하는 전원/전력 상태다. B가 레버를 올려두는 동안(On)에는 배치를 바꿀 수 없다.
    private BreakerCircuitState _circuitState;
    // C 화면의 바늘 연출이 끝나는 시점에 맞춰 이 화면의 결과 창을 띄우기 위한 대기 트윈이다.
    private Tween _resultTween;

    // 확인을 눌러 측정이 끝났는지, 그때 측정된 값이 얼마였는지. 배치를 바꾸면 다시 재야 한다.
    private bool _hasMeasurementResult;
    private int _measuredWatt;

    // 확인을 누른 뒤 계기판 바늘이 움직이는 동안. 이때는 결과 대신 측정 중이라고 알린다.
    private bool _isMeasuring;

    private void Awake()
    {
        CacheReferences();
        SetupScrollableInventory();
        SetupSlots(FindChild(transform, "PowerBoard"));

        // 배터리 등록은 Initialize에서 한다. 여기서는 아직 _circuitState가 없어서 공유 목록에 올릴 수 없다.
    }

    // 닫혀 있는 동안 새로 주운 건전지를 다시 열 때 보관함에 반영한다.
    // Awake에서 이미 들고 있던 건전지를 모두 옮겼으므로 첫 활성화에서는 아무 일도 하지 않는다.
    private void OnEnable()
    {
        StageNewBatteries();

        // 상태·진행도 문구는 코드가 계산해 넣는 값이라 LocalizeStringEvent 의 자동 갱신을 받지 못한다.
        // 패널을 연 채로 언어를 바꾸면 이미 찍힌 문구가 그대로 남으므로 여기서 다시 그린다.
        LocalizationSettings.SelectedLocaleChanged += HandleLocaleChanged;
    }

    private void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= HandleLocaleChanged;
    }

    private void HandleLocaleChanged(Locale locale) => UpdateStatusText();

    private void OnDestroy()
    {
        if (_circuitState != null)
        {
            _circuitState.OnCircuitChanged -= HandleCircuitChanged;
            _circuitState.OnMeasurementRequested -= ScheduleResultOverlay;
        }

        _resultTween?.Kill();
    }

    // 소유 기계의 공유 회로 상태와 연결하고 목표 전력을 최초 한 번 등록한다.
    public void Initialize(BreakerCircuitState circuitState)
    {
        _circuitState = circuitState;
        if (_circuitState == null)
        {
            return;
        }

        _circuitState.OnCircuitChanged += HandleCircuitChanged;
        _circuitState.OnMeasurementRequested += ScheduleResultOverlay;

        // 공유 상태가 붙은 지금에서야 내 배터리를 기계 보관함에 올릴 수 있다.
        StageInventoryBatteries();
        RebuildInventoryGrid();

        SelectRandomTargetWatt();
        HandleCircuitChanged();
    }

    // 확인을 누르면 C 화면에서 바늘이 올라가는 동안 기다렸다가, 완료된 경우에만 같은 시점에 결과 창을 띄운다.
    private void ScheduleResultOverlay()
    {
        // 확인을 누른 시점부터 계기판 바늘이 다 올라갈 때까지는 측정 중으로 표시한다.
        _hasMeasurementResult = false;
        _isMeasuring = true;
        UpdateStatusText();

        _resultTween?.Kill();
        _resultTween = DOVirtual.DelayedCall(
            BreakerCircuitState.MeasurementSweepSeconds + BreakerCircuitState.ResultDelaySeconds,
            () =>
            {
                _isMeasuring = false;

                if (_circuitState == null)
                {
                    return;
                }

                _measuredWatt = _circuitState.CurrentWatt;
                _hasMeasurementResult = true;
                UpdateStatusText();

                if (_circuitState.IsCompleted)
                {
                    GetComponent<MissionUIController>()?.ShowCompletedState();
                }
            });
    }

    // 회로 상태(전원/전력/완료 여부)가 바뀔 때마다 화면을 다시 그린다.
    private void HandleCircuitChanged()
    {
        if (_circuitState.IsCompleted)
        {
            // 완료된 뒤에는 보관 중이던 배터리를 실제 인벤토리로 되돌리지 않고 소모한다.
            DiscardStoredBatteries();
        }

        // B가 레버를 내리고 있지 않으면(전원 On) 배터리를 만질 수 없으므로 전원판을 어둡게 덮는다.
        bool powerOn = _circuitState.PowerOn;
        if (_dimmer != null)
        {
            _dimmer.SetActive(powerOn);
        }

        // 배치를 바꿀 수 있을 때만 '다시 하기', 전원이 들어와 측정된 뒤에만 '확인'을 쓸 수 있다.
        SetButtonUsable(_retryButton, !powerOn);
        SetButtonUsable(_confirmButton, powerOn);

        UpdateStatusText();
        RebuildInventoryGridIfChanged();
        RefreshItemPositions();
    }

    // 버튼을 숨기지 않고 못 누르게만 한다. 흐려지는 표현은 Button의 Disabled Color가 담당한다.
    private static void SetButtonUsable(Button button, bool usable)
    {
        if (button != null)
        {
            button.interactable = usable;
        }
    }

    // 모든 슬롯을 비우고 배터리들을 각각의 원래 인벤토리 셀로 되돌린다.
    private void ResetBatteries()
    {
        if (_circuitState != null && _circuitState.PowerOn)
        {
            // 전원이 켜져 있으면(레버를 올린 상태) 배치를 건드릴 수 없다.
            return;
        }

        foreach (BatteryDragItem battery in _slots.Where(item => item != null))
        {
            _circuitState?.RequestStoreBattery(battery.EntryId);
        }

        RefreshItemPositions();
    }

    public void DropOnSlot(UIDraggableItem item, int slotIndex)
    {
        if (_circuitState != null && _circuitState.PowerOn)
        {
            // 전원이 켜져 있으면(레버를 올린 상태) 배치를 건드릴 수 없다.
            RefreshItemPositions();
            return;
        }

        if (item is not BatteryDragItem battery)
        {
            // 이 미션의 배터리가 아닌 드래그 항목은 슬롯 상태를 바꾸지 않는다.
            RefreshItemPositions();
            return;
        }

        // 배치는 서버가 정한다. 여기서는 요청만 보내고, 복제된 결과로 화면을 맞춘다.
        // 로컬에서 먼저 꽂아 두면 서버가 거절했을 때 두 화면이 어긋난다.
        // 배치가 바뀌면 앞선 측정 결과도, 진행 중이던 측정도 무효다.
        _hasMeasurementResult = false;
        _isMeasuring = false;
        _resultTween?.Kill();

        _pendingPlacements[battery.EntryId] = slotIndex;
        _circuitState?.RequestPlaceBattery(battery.EntryId, slotIndex);
        SoundManager.Instance?.Play(SoundKey.Mission_ItemInsert);
        RefreshItemPositions();
    }

    public void DropOnItem(UIDraggableItem target, UIDraggableItem dragged)
    {
        if (target is not BatteryDragItem targetBattery || dragged is not BatteryDragItem)
        {
            RefreshItemPositions();
            return;
        }

        if (targetBattery.CurrentSlotIndex >= 0)
        {
            // 슬롯에 꽂힌 배터리 위에 놓으면 해당 슬롯에 드롭한 것과 동일하게 처리한다.
            DropOnSlot(dragged, targetBattery.CurrentSlotIndex);
        }
        else
        {
            // 인벤토리의 아이템 위에 놓으면 드래그한 배터리를 인벤토리로 반환한다.
            ReturnToPool(dragged);
        }
    }

    public void ReturnToPool(UIDraggableItem item)
    {
        if (_circuitState != null && _circuitState.PowerOn)
        {
            // 전원이 켜져 있으면(레버를 올린 상태) 배치를 건드릴 수 없다.
            RefreshItemPositions();
            return;
        }

        // 배터리를 전원 슬롯에서 해제하고 원래 인벤토리 셀로 반환한다.
        if (item is not BatteryDragItem battery)
        {
            return;
        }

        if (battery.CurrentSlotIndex >= 0)
        {
            _hasMeasurementResult = false;
            _isMeasuring = false;
            _resultTween?.Kill();
            _pendingPlacements[battery.EntryId] = -1;
            _circuitState?.RequestStoreBattery(battery.EntryId);
        }

        RefreshItemPositions();
    }

    // 화면 배치는 전적으로 서버 목록을 따른다. 내 조작이든 남의 조작이든 같은 결과가 된다.
    private void SyncSlotsFromShared()
    {
        if (_circuitState == null)
        {
            return;
        }

        Array.Clear(_slots, 0, _slots.Length);
        foreach (BatteryDragItem item in _batteries)
        {
            item.CurrentSlotIndex = -1;
        }

        foreach (BreakerBatteryEntry entry in _circuitState.Batteries)
        {
            // 서버 응답이 도착해 내 요청과 같아졌으면 더 이상 덮어쓸 필요가 없다.
            if (_pendingPlacements.TryGetValue(entry.Id, out int requested) && requested == entry.SlotIndex)
            {
                _pendingPlacements.Remove(entry.Id);
            }

            if (entry.IsStored || entry.SlotIndex >= _slots.Length)
            {
                continue;
            }

            BatteryDragItem item = _batteries.FirstOrDefault(candidate => candidate.EntryId == entry.Id);
            if (item == null)
            {
                continue;
            }

            _slots[entry.SlotIndex] = item;
            item.CurrentSlotIndex = entry.SlotIndex;
        }

        ApplyPendingPlacements();
    }

    // 아직 복제가 안 온 내 조작을 화면에 먼저 반영한다. 서버가 거절하면 다음 복제 때 되돌아간다.
    private void ApplyPendingPlacements()
    {
        if (_pendingPlacements.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<int, int> pending in _pendingPlacements)
        {
            BatteryDragItem item = _batteries.FirstOrDefault(candidate => candidate.EntryId == pending.Key);
            if (item == null)
            {
                continue;
            }

            // 이 배터리가 지금 차지하고 있는 칸을 먼저 비운다.
            if (item.CurrentSlotIndex >= 0 && item.CurrentSlotIndex < _slots.Length
                && _slots[item.CurrentSlotIndex] == item)
            {
                _slots[item.CurrentSlotIndex] = null;
            }

            int target = pending.Value;
            if (target < 0 || target >= _slots.Length || _slots[target] != null)
            {
                // 보관함으로 되돌리는 요청이거나, 그 사이 남이 먼저 차지한 칸이다.
                item.CurrentSlotIndex = -1;
                continue;
            }

            _slots[target] = item;
            item.CurrentSlotIndex = target;
        }
    }

    public void RefreshItemPositions()
    {
        SyncSlotsFromShared();

        // 슬롯 번호가 없는 배터리는 원래 인벤토리 셀로 되돌린다.
        foreach (BatteryDragItem battery in _batteries.Where(item => item.CurrentSlotIndex < 0))
        {
            MoveToInventoryCell(battery);
        }

        for (int index = 0; index < _slots.Length; index++)
        {
            BatteryDragItem battery = _slots[index];
            _dropSlots[index]?.SetOccupied(battery != null);
            if (battery != null)
            {
                MoveToPowerSlot(battery, _dropSlots[index].transform);
            }
        }

        // 합계는 내 배치만이 아니라 기계에 꽂힌 전부다. 서버가 들고 있는 값을 그대로 쓴다.
        int current = _circuitState != null
            ? _circuitState.GetArrangedWatt()
            : _slots.Where(item => item != null).Sum(item => item.Watt);

        _currentValueText.text = $"{current:000} W";
        UpdateWattGauge(current);
        _currentValueText.color = Color.white;
    }
}
