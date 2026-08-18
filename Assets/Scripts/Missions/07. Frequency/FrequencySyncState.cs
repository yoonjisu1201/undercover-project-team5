using System;
using Unity.Netcode;
using UnityEngine;

// 통신 주파수 동기화 미션에서 세 역할이 공유해야 하는 상태를 서버 권한으로 관리한다.
// P1(파형 화면)  : 안테나 방향을 돌려 신호가 가장 센 방향(=목표 방향)을 찾아 P2에게 알려준다.
// P2(안테나 운반): 안테나를 들고 그 방향으로 이동해 안테나 존에 들어간다.
// P3(주파수 다이얼): 안테나가 존에 들어온 뒤에야 다이얼을 돌려 목표 주파수를 맞출 수 있다.
public sealed class FrequencySyncState : NetworkBehaviour
{
    public const float MinFrequency = 100f;
    public const float MaxFrequency = 115f;
    // 다이얼이 한 번에 움직이는 최소 단위다.
    public const float FrequencyStep = 0.05f;
    // 목표로 인정하는 폭이다. 한 칸(0.05)만 인정하면 3초를 버티는 게 사실상 불가능해서 넉넉히 잡는다.
    public const float MatchTolerance = 0.4f;
    // 이 범위 안에 들어오면 튜닝 사운드로 바뀐다. 목표가 가까워지는 걸 소리로 알 수 있다.
    public const float NearTolerance = 2f;
    // 이 거리 밖이면 신호가 잡히지 않는다. 가까워질수록 파형이 또렷해진다.
    public const float SignalRange = 30f;

    // 좌표는 최대 3곳까지 준비해 둔다. 실제로 몇 곳을 돌게 할지는 라운드에 따라 정한다.
    public const int MaxZoneCount = 3;
    // 이 라운드 번호부터 좌표를 2곳 돌게 한다. (라운드 번호는 1부터)
    private const int TwoZoneFromRound = 3;

    // 좌표를 뽑는 거리 범위다. 기계에서 이 사이의 거리에 세 지점이 생긴다.
    [SerializeField] private float _minZoneDistance = 10f;
    [SerializeField] private float _maxZoneDistance = 28f;
    // 구역 경계에서 안쪽으로 이만큼 띄운다. 경계에 딱 붙으면 벽 밖이나 못 가는 자리에 생긴다.
    [SerializeField] private float _zoneBoundsInset = 4f;
    // 세 좌표가 서로 이만큼은 떨어지게 한다. 겹치면 한 자리에서 세 번 설치하는 꼴이 된다.
    [SerializeField] private float _minZoneSeparation = 8f;
    // 현재 단계의 좌표로 옮겨 다니는 안테나 존이다. 하나를 세 좌표에 재사용한다.
    [SerializeField] private FrequencyAntennaZone _antennaZone;
    // 이 아이템을 들고 있는 플레이어가 P2다. 존 판정과 레이더 추적이 같은 기준을 쓰도록 여기서 한 번만 정한다.
    [SerializeField] private ItemType _antennaItemId = ItemType.Antenna;
    // 소지자 방향을 다시 재는 간격이다. 시야 회전을 따라가야 하므로 짧게 둔다.
    private const float TrackRefreshSeconds = 0.08f;
    // 목표 주파수에 이 시간만큼 머물러야 한 단계가 통과된다. 스치듯 지나가는 것으로는 안 된다.
    public const float RequiredHoldSeconds = 3f;
    // 설치한 안테나를 세워 둘 프리팹이다. 줍기 컴포넌트가 없어 다시 집을 수 없다.
    [SerializeField] private GameObject _installedAntennaPrefab;

    private MissionInteractable _interactable;

    // 지금 몇 번째 좌표를 진행 중인지다. 필요한 좌표 수에 도달하면 전부 맞춘 것이다.
    private readonly NetworkVariable<int> _stageIndex = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // 이 라운드에 실제로 돌아야 하는 좌표 수다. 서버가 라운드 번호를 보고 정한다.
    private readonly NetworkVariable<int> _zoneCount = new(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // 게임 시작 시 뽑은 세 좌표다. 클라이언트는 방향·거리 계산에만 쓴다.
    private readonly NetworkVariable<Vector3> _zone0 = new(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Vector3> _zone1 = new(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Vector3> _zone2 = new(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 좌표별 안테나 설치 여부와 통과 여부를 비트로 들고 있다. 순서를 자유롭게 고를 수 있어야 하므로 단계 하나로는 부족하다.
    private readonly NetworkVariable<int> _installedMask = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _completedMask = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _antennaPlaced = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _targetFrequency = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _currentFrequency = new(
        MinFrequency, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 안테나 소지자가 바라보는 방향을 0도로 놓았을 때, 목표 좌표가 어느 쪽에 있는지다.
    // 0이면 지금 보고 있는 정면에 목표가 있다는 뜻이라 그대로 걸어가면 된다.
    private readonly NetworkVariable<float> _holderRelativeBearing = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // 목표 주파수에 머문 시간이다. 3초를 채우면 다음 좌표로 넘어간다.
    private readonly NetworkVariable<float> _holdSeconds = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // 안테나 소지자에서 존까지 남은 거리(m)다. 소지자가 없으면 -1이다.
    private readonly NetworkVariable<float> _holderDistance = new(
        -1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float _nextTrackTime;

    // 진행 중인 좌표 번호(0부터). 세 좌표를 다 맞추면 StageCount와 같아진다.
    public int StageIndex => _stageIndex.Value;
    // 이 라운드에 돌아야 하는 좌표 수다.
    public int ZoneCount => Mathf.Clamp(_zoneCount.Value, 1, MaxZoneCount);
    // 화면에 "2 / 3"처럼 보여주기 위한 값이다.
    public int StageNumber => Mathf.Min(_stageIndex.Value + 1, ZoneCount);
    public int CompletedZoneCount => CountMaskBits(_completedMask.Value, ZoneCount);

    // 현재 찾아가야 하는 좌표다.
    public Vector3 CurrentZonePosition => ZonePosition(_stageIndex.Value);

    public bool AntennaPlaced => _antennaPlaced.Value;
    public float TargetFrequency => _targetFrequency.Value;
    public float CurrentFrequency => _currentFrequency.Value;
    public bool IsCompleted => _interactable != null && _interactable.IsCompleted;
    public ItemType AntennaItemId => _antennaItemId;

    // 소지자가 보는 방향을 위쪽으로 놓았을 때 목표 좌표가 있는 각도다. 소지자가 몸을 돌리면 이 값이 바뀐다.
    public float HolderRelativeBearing => _holderRelativeBearing.Value;
    // 목표 주파수 유지 시간과 진행률(0~1)이다.
    public float HoldSeconds => _holdSeconds.Value;
    public float HoldProgress01 => Mathf.Clamp01(_holdSeconds.Value / RequiredHoldSeconds);
    // 소지자에서 존까지 남은 거리(m). 소지자를 못 찾으면 -1이다.
    public float HolderDistance => _holderDistance.Value;
    public bool HasHolder => _holderDistance.Value >= 0f;

    // 안테나가 존에 들어오기 전에는 다이얼을 잠근다. 세 역할이 순서대로 맞물리게 하는 유일한 잠금이다.
    public bool DialUnlocked => _antennaPlaced.Value && !IsCompleted;

    // 요원이 목표 좌표에 가까울수록 1에 가까워진다. 설치를 마치면 최대치를 유지한다.
    public float SignalStrength01
    {
        get
        {
            if (AntennaPlaced)
            {
                return 1f;
            }

            return HasHolder ? Mathf.Clamp01(1f - _holderDistance.Value / SignalRange) : 0f;
        }
    }

    // 목표 주파수에 얼마나 근접했는지(0~1). 0이면 전혀 안 맞고, 1이면 정답 범위 안이다.
    // 파형과 사운드가 이 값 하나로 단계적으로 변한다.
    public float TuneCloseness01
    {
        get
        {
            if (!AntennaPlaced || _targetFrequency.Value == 0f)
            {
                return 0f;
            }

            float error = Mathf.Abs(_currentFrequency.Value - _targetFrequency.Value);
            if (error <= MatchTolerance)
            {
                return 1f;
            }

            if (error >= NearTolerance)
            {
                return 0f;
            }

            return 1f - (error - MatchTolerance) / (NearTolerance - MatchTolerance);
        }
    }

    // 정답 범위 안에 들어와 유지 시간을 쌓는 중인지.
    public bool IsOnTarget => TuneCloseness01 >= 1f;

    // 상태가 바뀔 때마다 알린다. 세 화면 모두 이 이벤트만 구독해 다시 그린다.
    public event Action OnStateChanged;

    // 좌표 하나를 통과한 순간 모든 화면에 알린다. 인자는 통과한 좌표 번호(1부터)다.
    public event Action<int> OnZoneCleared;

    private void Awake()
    {
        _interactable = GetComponent<MissionInteractable>();
    }

    public override void OnNetworkSpawn()
    {
        _holderRelativeBearing.OnValueChanged += HandleTrackingChanged;
        _holderDistance.OnValueChanged += HandleTrackingChanged;
        _stageIndex.OnValueChanged += HandleIntChanged;
        _zoneCount.OnValueChanged += HandleIntChanged;
        _zone0.OnValueChanged += HandleZoneChanged;
        _zone1.OnValueChanged += HandleZoneChanged;
        _zone2.OnValueChanged += HandleZoneChanged;
        _installedMask.OnValueChanged += HandleIntChanged;
        _completedMask.OnValueChanged += HandleIntChanged;
        _antennaPlaced.OnValueChanged += HandleBoolChanged;
        _targetFrequency.OnValueChanged += HandleFloatChanged;
        _currentFrequency.OnValueChanged += HandleFloatChanged;

        if (_interactable != null)
        {
            _interactable.IsCompletedChanged += HandleBoolChanged;
            _interactable.ServerRoundReset += RandomizePuzzle;
        }

        if (IsServer && _targetFrequency.Value == 0f)
        {
            RandomizePuzzle();
        }

        ApplyZonePlacement();
    }

    public override void OnNetworkDespawn()
    {
        _holderRelativeBearing.OnValueChanged -= HandleTrackingChanged;
        _holderDistance.OnValueChanged -= HandleTrackingChanged;
        _stageIndex.OnValueChanged -= HandleIntChanged;
        _zoneCount.OnValueChanged -= HandleIntChanged;
        _zone0.OnValueChanged -= HandleZoneChanged;
        _zone1.OnValueChanged -= HandleZoneChanged;
        _zone2.OnValueChanged -= HandleZoneChanged;
        _installedMask.OnValueChanged -= HandleIntChanged;
        _completedMask.OnValueChanged -= HandleIntChanged;
        _antennaPlaced.OnValueChanged -= HandleBoolChanged;
        _targetFrequency.OnValueChanged -= HandleFloatChanged;
        _currentFrequency.OnValueChanged -= HandleFloatChanged;

        if (_interactable != null)
        {
            _interactable.IsCompletedChanged -= HandleBoolChanged;
            _interactable.ServerRoundReset -= RandomizePuzzle;
        }

        // 존을 기계에서 떼어냈으므로 기계가 사라질 때 같이 정리한다. 그냥 두면 콜라이더만 남는다.
        if (_antennaZone != null && _antennaZone.transform.parent == null)
        {
            Destroy(_antennaZone.gameObject);
        }
    }

    // 게임(라운드) 시작 시 세 좌표를 뽑고 첫 단계로 되돌린다.
    private void RandomizePuzzle()
    {
        if (!IsServer)
        {
            return;
        }

        // 라운드가 올라가면 돌아야 하는 좌표가 늘어난다.
        int roundNumber = RoundManager.Instance != null ? RoundManager.Instance.CurrentRoundIndex + 1 : 1;
        _zoneCount.Value = roundNumber >= TwoZoneFromRound ? 2 : 1;

        _zone0.Value = PickZonePosition(Vector3.zero, Vector3.zero);
        _zone1.Value = PickZonePosition(_zone0.Value, Vector3.zero);
        _zone2.Value = PickZonePosition(_zone0.Value, _zone1.Value);

        _installedMask.Value = 0;
        _completedMask.Value = 0;
        _stageIndex.Value = 0;
        BeginStageOnServer();
    }

    // 기계가 속한 구역 안에서만 좌표를 고른다.
    // 구역 밖으로 나가면 요원이 갈 수 없는 자리에 안테나를 설치하라고 하는 셈이 된다.
    private Vector3 PickZonePosition(Vector3 avoidA, Vector3 avoidB)
    {
        Bounds? area = TryGetRegionArea();

        for (int attempt = 0; attempt < 40; attempt++)
        {
            Vector3 candidate;

            if (area.HasValue)
            {
                // 구역 경계에서 안쪽으로 들어온 범위에서만 뽑는다.
                Bounds bounds = area.Value;
                candidate = new Vector3(
                    UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
                    transform.position.y,
                    UnityEngine.Random.Range(bounds.min.z, bounds.max.z));
            }
            else
            {
                float angle = UnityEngine.Random.Range(0f, 360f);
                float distance = UnityEngine.Random.Range(_minZoneDistance, _maxZoneDistance);
                candidate = transform.position + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
            }

            if (!IsFarEnough(candidate, transform.position, _minZoneDistance)
                || !IsFarEnough(candidate, avoidA, _minZoneSeparation)
                || !IsFarEnough(candidate, avoidB, _minZoneSeparation))
            {
                continue;
            }

            if (!UnityEngine.AI.NavMesh.SamplePosition(
                    candidate, out UnityEngine.AI.NavMeshHit hit, 4f, UnityEngine.AI.NavMesh.AllAreas))
            {
                continue;
            }

            // NavMesh로 스냅하면서 경계 밖으로 밀려날 수 있으므로 다시 확인한다.
            if (area.HasValue && !ContainsHorizontally(area.Value, hit.position))
            {
                continue;
            }

            return hit.position;
        }

        // 끝내 못 찾으면 기계 앞을 쓴다. 진행이 막히는 것보다는 낫다.
        return transform.position + transform.forward * _minZoneDistance;
    }

    // 기계가 서 있는 구역의 경계를 안쪽으로 줄여서 돌려준다.
    private Bounds? TryGetRegionArea()
    {
        MapRegionController controller = FindFirstObjectByType<MapRegionController>();
        if (controller == null || !controller.TryGetUnlockedRegionAt(transform.position, out MapRegion region))
        {
            return null;
        }

        if (region == null || region.Bounds == null)
        {
            return null;
        }

        Bounds bounds = region.Bounds.bounds;
        bounds.Expand(new Vector3(-_zoneBoundsInset * 2f, 0f, -_zoneBoundsInset * 2f));

        // 오프셋을 주고 나면 남는 범위가 없을 만큼 좁은 구역이면 그대로 쓴다.
        return bounds.size.x <= 0f || bounds.size.z <= 0f ? region.Bounds.bounds : bounds;
    }

    private static bool ContainsHorizontally(Bounds bounds, Vector3 point)
    {
        return point.x >= bounds.min.x && point.x <= bounds.max.x
            && point.z >= bounds.min.z && point.z <= bounds.max.z;
    }

    // 기준점이 아직 정해지지 않았으면(0,0,0) 거리 조건을 보지 않는다.
    private static bool IsFarEnough(Vector3 candidate, Vector3 from, float minDistance)
    {
        if (from == Vector3.zero)
        {
            return true;
        }

        Vector3 delta = candidate - from;
        delta.y = 0f;
        return delta.sqrMagnitude >= minDistance * minDistance;
    }

    // 고른 좌표를 진행 대상으로 삼는다. 그 좌표의 설치 여부에 맞춰 다이얼 잠금과 목표 주파수를 정한다.
    private void BeginStageOnServer()
    {
        _holdSeconds.Value = 0f;
        _currentFrequency.Value = MinFrequency;
        _antennaPlaced.Value = IsInstalled(_stageIndex.Value);

        // 다이얼 눈금과 어긋나 절대 맞출 수 없는 목표가 나오지 않도록 0.05MHz 배수로 맞춘다.
        int steps = Mathf.RoundToInt((MaxFrequency - MinFrequency) / FrequencyStep);
        _targetFrequency.Value = MinFrequency + UnityEngine.Random.Range(1, steps) * FrequencyStep;
    }

    private bool IsInstalled(int index) => (_installedMask.Value & (1 << index)) != 0;

    public bool IsZoneCompleted(int index) => (_completedMask.Value & (1 << index)) != 0;

    private Vector3 ZonePosition(int index)
    {
        switch (index)
        {
            case 0: return _zone0.Value;
            case 1: return _zone1.Value;
            default: return _zone2.Value;
        }
    }

    // 존을 이번 단계 좌표로 옮긴다. 세 좌표를 다 맞춘 뒤에는 마지막 자리에 그대로 둔다.
    private void ApplyZonePlacement()
    {
        if (_antennaZone == null)
        {
            return;
        }

        // 좌표가 아직 서버에서 오지 않았으면 옮기지 않는다. 기계 위치에 그대로 얹혀 있는 상태다.
        Vector3 zonePosition = CurrentZonePosition;
        if (zonePosition == Vector3.zero)
        {
            return;
        }

        // 기계의 자식으로 두면 기계가 움직일 때 존이 따라가고, 하이라키에서도 기계와 같은 자리처럼 보인다.
        // 좌표가 정해지는 순간 떼어내 독립된 위치를 갖게 한다.
        if (_antennaZone.transform.parent != null)
        {
            _antennaZone.transform.SetParent(null, true);
        }

        _antennaZone.transform.position = zonePosition;
    }

    // 안테나 소지자의 좌표는 서버만 알고 있으므로, 서버가 주기적으로 재서 공유 값에 담는다.
    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        UpdateHoldOnServer();

        if (Time.time < _nextTrackTime)
        {
            return;
        }

        _nextTrackTime = Time.time + TrackRefreshSeconds;
        TrackHolderOnServer();
    }

    // 목표 주파수에 머무는 동안 시간을 쌓고, 3초를 채우면 다음 좌표로 넘긴다.
    // 벗어나면 처음부터 다시 재야 한다.
    private void UpdateHoldOnServer()
    {
        bool onTarget = _antennaPlaced.Value
            && _targetFrequency.Value != 0f
            && Mathf.Abs(_currentFrequency.Value - _targetFrequency.Value) <= MatchTolerance;

        if (!onTarget)
        {
            if (_holdSeconds.Value != 0f)
            {
                _holdSeconds.Value = 0f;
            }

            return;
        }

        _holdSeconds.Value += Time.deltaTime;
        if (_holdSeconds.Value >= RequiredHoldSeconds)
        {
            AdvanceStageOnServer();
        }
    }

    private void TrackHolderOnServer()
    {
        Transform holder = FindAntennaHolder();
        if (holder == null)
        {
            _holderDistance.Value = -1f;
            return;
        }

        // 목표 좌표가 소지자의 '정면 기준'으로 어느 쪽에 있는지 잰다.
        // 소지자가 몸을 돌리면 이 값이 바뀌고, 0이 되면 지금 보는 방향으로 곧장 가면 된다.
        Vector3 toZone = CurrentZonePosition - holder.position;
        toZone.y = 0f;

        Vector3 facing = holder.forward;
        facing.y = 0f;

        if (toZone.sqrMagnitude > 0.0001f && facing.sqrMagnitude > 0.0001f)
        {
            float zoneBearing = Mathf.Atan2(toZone.x, toZone.z) * Mathf.Rad2Deg;
            float facingBearing = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            _holderRelativeBearing.Value = Mathf.DeltaAngle(facingBearing, zoneBearing);
        }

        _holderDistance.Value = toZone.magnitude;
    }

    // 인벤토리 어느 칸에든 안테나가 있는 플레이어를 소지자로 본다.
    private Transform FindAntennaHolder()
    {
        foreach (Player player in Player.ActiveInstances)
        {
            PlayerInventory inventory = player != null ? player.PlayerInventory : null;
            if (inventory == null)
            {
                continue;
            }

            foreach (InventorySlot slot in inventory.Slots)
            {
                if (!slot.IsEmpty && slot.ItemId == _antennaItemId)
                {
                    return player.transform;
                }
            }
        }

        return null;
    }

    // P3가 다이얼을 돌린 값을 보고한다.
    public void SubmitFrequency(float frequency)
    {
        if (IsSpawned)
        {
            SetFrequencyRpc(SnapFrequency(frequency));
        }
    }

    // 존에 들어온 소지자의 안테나를 소모하고 그 자리에 설치한다.
    // 설치한 안테나는 다시 집을 수 없으므로, 좌표 하나당 안테나 하나가 필요하다.
    public void TryInstallAntennaOnServer(PlayerInventory inventory, int selectedIndex, Vector3 position)
    {
        if (!IsServer || _antennaPlaced.Value || inventory == null)
        {
            return;
        }

        if (!inventory.TryRemoveSelectedItemOnServer(_antennaItemId, selectedIndex))
        {
            return;
        }

        if (_installedAntennaPrefab != null)
        {
            GameObject installed = Instantiate(_installedAntennaPrefab, position, _installedAntennaPrefab.transform.rotation);

            // 모델 피벗이 중앙에 있어 그대로 두면 바닥에 묻힌다.
            // 렌더러 하단이 설치 지점 높이에 닿도록 올려준다.
            Renderer[] renderers = installed.GetComponentsInChildren<Renderer>();

            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;

                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                installed.transform.position += Vector3.up * (position.y - bounds.min.y);
            }

            if (installed.TryGetComponent(out NetworkObject installedObject))
            {
                installedObject.Spawn(destroyWithScene: true);
            }
            else
            {
                Debug.LogError($"[주파수] '{_installedAntennaPrefab.name}'에 NetworkObject가 없습니다.", this);
                Destroy(installed);
            }
        }

        _installedMask.Value |= 1 << _stageIndex.Value;
        _antennaPlaced.Value = true;
        ShowAntennaInstalledMessageOwnerRpc(RpcTarget.Single(inventory.OwnerClientId, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void ShowAntennaInstalledMessageOwnerRpc(RpcParams rpcParams = default)
    {
        FindFirstObjectByType<InteractionPromptUI>(FindObjectsInactive.Include)?.ShowTemporaryPrompt("안테나 설치 완료");
    }

    // 이 기계는 서버 소유지만 RPC를 호출하는 쪽은 각 화면을 조작하는 클라이언트다. Breaker와 같은 이유로 Everyone으로 열어둔다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetFrequencyRpc(float frequency)
    {
        if (!DialUnlocked)
        {
            return;
        }

        _currentFrequency.Value = frequency;
    }

    // 3초를 채운 순간 이 좌표를 통과 처리하고, 세 좌표를 모두 맞추면 미션을 완료한다.
    private void AdvanceStageOnServer()
    {
        _holdSeconds.Value = 0f;
        _completedMask.Value |= 1 << _stageIndex.Value;
        NotifyZoneClearedRpc(_stageIndex.Value + 1);

        // 아직 남은 좌표가 있으면 그중 하나로 자동으로 옮겨 준다. P1이 버튼으로 다시 고를 수도 있다.
        int count = ZoneCount;
        for (int offset = 1; offset <= count; offset++)
        {
            int next = (_stageIndex.Value + offset) % count;
            if (IsZoneCompleted(next))
            {
                continue;
            }

            _stageIndex.Value = next;
            BeginStageOnServer();
            return;
        }

        _stageIndex.Value = ZoneCount;
        _interactable?.ServerCompleteFromGameplay(transform.position + transform.forward * 3f);
    }

    // 좌표 통과는 서버만 판정하므로, 안내 메시지를 띄울 시점을 모든 화면에 알려준다.
    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyZoneClearedRpc(int zoneNumber)
    {
        OnZoneCleared?.Invoke(zoneNumber);
    }

    private void HandleIntChanged(int previousValue, int currentValue)
    {
        ApplyZonePlacement();
        OnStateChanged?.Invoke();
    }

    private void HandleZoneChanged(Vector3 previousValue, Vector3 currentValue)
    {
        ApplyZonePlacement();
        OnStateChanged?.Invoke();
    }

    private void HandleFloatChanged(float previousValue, float currentValue) => OnStateChanged?.Invoke();

    // 소지자 추적 값은 자주 바뀌므로 존 위치 재계산 없이 화면 갱신만 알린다.
    private void HandleTrackingChanged(float previousValue, float currentValue) => OnStateChanged?.Invoke();

    private void HandleBoolChanged(bool previousValue, bool currentValue) => OnStateChanged?.Invoke();

    private void HandleBoolChanged(bool currentValue) => OnStateChanged?.Invoke();

    public static float SnapFrequency(float frequency)
    {
        float clamped = Mathf.Clamp(frequency, MinFrequency, MaxFrequency);
        return Mathf.Round(clamped / FrequencyStep) * FrequencyStep;
    }

    private static int CountMaskBits(int mask, int count)
    {
        int total = 0;
        for (int index = 0; index < count; index++)
        {
            if ((mask & (1 << index)) != 0)
            {
                total++;
            }
        }

        return total;
    }
}
