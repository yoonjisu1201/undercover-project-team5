using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 기계 한 대가 담당하는 CCTV 한 대의 배선 입력을 처리하고 연결 상태와 화면을 동기화합니다.
public sealed class CCTVSignalRepairGame : MonoBehaviour
{
    private const int WireCount = CCTVPoint.RequiredConnectionCount;

    [Header("CCTV 표시")]
    [Header("현지화 문구")]
    [Tooltip("{0} 에 CCTV 번호가 들어간다.")]
    [SerializeField] private LocalizedString _objectiveDone;

    [Tooltip("{0} 에 CCTV 번호가 들어간다.")]
    [SerializeField] private LocalizedString _objectiveTodo;

    [SerializeField] private TMP_Text _cameraLabel;
    [SerializeField] private TMP_Text _cameraFeed;

    [Header("배선 보드")]
    [SerializeField] private RectTransform _connectionBoard;
    [SerializeField] private RectTransform[] _sourceTerminals;
    [SerializeField] private RectTransform[] _targetTerminals;
    [SerializeField] private Image[] _wireImages;
    [SerializeField] private Graphic[] _sourceGraphics;
    [SerializeField] private Graphic[] _targetGraphics;

    [Header("상태 표시")]
    [SerializeField] private TMP_Text _objectiveText;
    [SerializeField] private TMP_Text _progressText;
    [SerializeField] private GameObject _resultOverlay;

    [Header("보조 상태 레일")]
    [SerializeField] private TMP_Text _leftRailLabel;
    [SerializeField] private TMP_Text _rightRailLabel;
    [SerializeField] private Image _faultBar;
    [SerializeField] private Image _signalBar;
    [SerializeField] private Image[] _warningLamp;
    [SerializeField] private Image[] _leftBoardLink;
    [SerializeField] private Image[] _rightBoardLink;

    private CCTVRepairPuzzleState _state;
    private readonly Color[] _wireColors =
    {
        new Color(0.95f, 0.18f, 0.18f), // 빨강
        new Color(0.15f, 0.45f, 1f),    // 파랑
        new Color(1f, 0.82f, 0.12f),    // 노랑
        new Color(0.95f, 0.18f, 0.75f)  // 분홍
    };

    // 이 화면이 담당하는 CCTV 번호. 기계가 알려 주기 전까지는 아무 것도 하지 않는다.
    private int _cameraIndex = -1;
    private CCTVHub _cctvHub;

    // 왼쪽 시작 단자에 해당 전선 번호와 게임 참조를 전달한다.
    private void Awake()
    {
        for (int wireIndex = 0; wireIndex < WireCount; wireIndex++)
        {
            CCTVWireTerminal terminal = _sourceTerminals[wireIndex].GetComponent<CCTVWireTerminal>();
            terminal.Configure(this, wireIndex);
        }

        _resultOverlay.SetActive(false);
    }

    // 기계마다 담당 CCTV가 다르므로, UI를 연 기계가 자기 번호를 알려 준 시점에 퍼즐을 구성한다.
    public void Initialize(MissionInteractable owner)
    {
        // 이 미션은 프리팹으로 동적 생성되어 씬의 CCTVHub를 미리 참조할 수 없으므로 직접 찾는다.
        _cctvHub = FindFirstObjectByType<CCTVHub>();
        if (_cctvHub == null || _cctvHub.CameraCount == 0)
        {
            Debug.LogError("[CCTV] 활성화된 CCTVHub를 찾지 못해 배선 퍼즐을 시작할 수 없습니다.", this);
            return;
        }

        // 기계 번호는 1부터 시작하고, CCTV 포인트 번호는 0부터 시작한다.
        _cameraIndex = Mathf.Clamp(owner.TargetNumber - 1, 0, _cctvHub.CameraCount - 1);

        // 같은 기계를 여러 명이 동시에 열어도 같은 색 배치를 보도록, 서버가 정한 시드로 단자를 섞는다.
        Random.State previousRandomState = Random.state;
        try
        {
            Random.InitState(owner.PuzzleSeed);
            _state = new CCTVRepairPuzzleState();
        }
        finally
        {
            Random.state = previousRandomState;
        }

        SyncStateFromStore();

        _cctvHub.OnAnyPointStateChanged += HandleSharedConnectionStateChanged;
        RefreshStatus();
        RefreshBoard();
    }

    // 화면을 다시 열면 그동안 서버에서 바뀐 연결 상태를 복구하고 구독을 되살립니다.
    private void OnEnable()
    {
        // 이 화면의 문구는 코드가 계산해 넣는 값이라 LocalizeStringEvent 의 자동 갱신을 받지 못한다.
        // 패널을 연 채로 언어를 바꾸면 이미 찍힌 문구가 그대로 남으므로 여기서 다시 그린다.
        LocalizationSettings.SelectedLocaleChanged += HandleLocaleChanged;

        if (_cctvHub == null)
        {
            return;
        }

        _cctvHub.OnAnyPointStateChanged += HandleSharedConnectionStateChanged;
        SyncStateFromStore();
        RefreshStatus();
        RefreshBoard();
    }

    // 화면이 비활성화되면 서버 상태 변경 구독을 해제합니다.
    private void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= HandleLocaleChanged;

        if (_cctvHub == null)
        {
            return;
        }

        _cctvHub.OnAnyPointStateChanged -= HandleSharedConnectionStateChanged;
    }

    // 연결 가능한 전선이면 드래그 선 표시를 시작한다.
    public void BeginWireDrag(int wireIndex, PointerEventData eventData)
    {
        ContinueWireDrag(wireIndex, eventData);
    }

    // 드래그 중인 전선을 시작 단자부터 포인터 위치까지 그린다.
    public void ContinueWireDrag(int wireIndex, PointerEventData eventData)
    {
        // 완료된 CCTV이거나 이미 연결한 전선은 다시 드래그하지 않는다.
        if (_state == null || _state.IsRepaired || _state.ConnectedTargets[wireIndex] >= 0)
        {
            return;
        }

        _wireImages[wireIndex].color = _wireColors[wireIndex];

        // 화면 좌표를 배선 보드 좌하단 기준 좌표로 바꾼다.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_connectionBoard, eventData.position, eventData.pressEventCamera, out Vector2 pointerPosition);
        pointerPosition += Vector2.Scale(_connectionBoard.rect.size, _connectionBoard.pivot);
        DrawWire(_wireImages[wireIndex].rectTransform, GetLocalCenter(_sourceTerminals[wireIndex]), pointerPosition);
        _wireImages[wireIndex].gameObject.SetActive(true);
    }

    // 드롭한 위치의 색상이 맞으면 전선을 연결하고 완료 여부를 검사한다.
    public void EndWireDrag(int wireIndex, PointerEventData eventData)
    {
        if (_state == null || _state.IsRepaired || _state.ConnectedTargets[wireIndex] >= 0)
        {
            return;
        }

        int matchedTarget = -1;

        // 포인터가 올라간 도착 단자와 전선 색상이 모두 일치해야 연결된다.
        for (int targetIndex = 0; targetIndex < WireCount; targetIndex++)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(_targetTerminals[targetIndex], eventData.position, eventData.pressEventCamera)
            && _state.TargetColors[targetIndex] == wireIndex)
            {
                matchedTarget = targetIndex;
                break;
            }
        }

        _state.ConnectedTargets[wireIndex] = matchedTarget;
        if (matchedTarget >= 0)
        {
            // 연결된 색상 정보가 유지되도록 전선 개수가 아닌 4비트 마스크를 동기화합니다.
            SetSharedConnectionMask(_state.ConnectionMask);
        }

        RefreshStatus();
        RefreshBoard();
        TryCompleteMission();
    }

    // 담당 CCTV의 배선을 모두 이으면 미션을 완료 처리하고 결과 문구를 띄운다.
    private void TryCompleteMission()
    {
        if (!_state.IsRepaired)
        {
            return;
        }

        MissionUIController controller = GetComponent<MissionUIController>();
        if (controller == null)
        {
            return;
        }

        controller.MarkCompletionReady();
        controller.ShowCompletedState();
    }

    // 미션에서 바뀐 연결 마스크를 서버에 보내 모든 클라이언트의 CCTV에 반영합니다.
    private void SetSharedConnectionMask(int connectionMask)
    {
        if (CCTVConnectionNetworkState.Instance != null)
        {
            CCTVConnectionNetworkState.Instance.RequestConnectionMask(
                _cameraIndex,
                connectionMask);
            return;
        }

        // 네트워크 없이 프리팹만 테스트하는 경우에는 로컬 상태만 갱신합니다.
        _cctvHub.ApplyConnectionMask(_cameraIndex, connectionMask);
    }

    // 서버에서 CCTV가 다시 끊기거나 복구되면 보관 중인 퍼즐 배선도 같은 수로 맞춥니다.
    private void HandleSharedConnectionStateChanged(
        int cameraIndex,
        CCTVConnectionState _)
    {
        if (cameraIndex != _cameraIndex)
        {
            return;
        }

        SyncStateFromStore();
        RefreshStatus();
        RefreshBoard();
    }

    // 공용 저장소의 연결 수를 담당 CCTV 퍼즐에 반영합니다.
    private void SyncStateFromStore()
    {
        _state.SyncConnectionMask(_cctvHub.GetPoint(_cameraIndex).ConnectionMask);
    }

    // 담당 CCTV의 이름표, 연결 상태, 배선 진행도를 갱신한다.
    private void RefreshStatus()
    {
        bool repaired = _state.IsRepaired;
        int connectionCount = _state.ConnectedCount;

        if (_cameraLabel != null)
        {
            _cameraLabel.text = $"CCTV {_cameraIndex + 1}";
        }

        if (_cameraFeed != null)
        {
            _cameraFeed.text = repaired ? "● CONNECTED" : connectionCount > 0 ? "◐ PARTIAL" : "× DISCONNECTED";
            _cameraFeed.color = repaired ? new Color(0.2f, 1f, 0.55f) : connectionCount > 0 ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.28f, 0.28f);
        }

        _progressText.text = $"WIRES  {connectionCount} / {WireCount}";
        RefreshAuxiliaryRails(repaired, connectionCount);
    }

    // 보조 레일도 실제 CCTV 연결 상태와 같은 색과 문구를 사용한다.
    private void RefreshAuxiliaryRails(bool repaired, int connectionCount)
    {
        bool partiallyConnected = !repaired && connectionCount > 0;
        Color statusColor = repaired
            ? new Color(0.2f, 1f, 0.55f)
            : partiallyConnected
                ? new Color(1f, 0.75f, 0.2f)
                : new Color(1f, 0.28f, 0.28f);

        if (_leftRailLabel != null)
        {
            _leftRailLabel.text = $"AUX\n{_cameraIndex + 1:00}";
        }

        if (_rightRailLabel != null)
        {
            _rightRailLabel.text = repaired ? "LINK\nOK" : partiallyConnected ? "LINK\nSYNC" : "LINK\nERR";
            _rightRailLabel.color = statusColor;
        }

        if (_faultBar != null)
        {
            _faultBar.color = new Color(statusColor.r, statusColor.g, statusColor.b, 0.65f);
        }

        if (_signalBar != null)
        {
            _signalBar.color = new Color(statusColor.r, statusColor.g, statusColor.b, 0.65f);
        }

        foreach (Image lamp in _warningLamp)
        {
            if (lamp != null)
            {
                lamp.gameObject.SetActive(!repaired);
                lamp.color = new Color(statusColor.r, statusColor.g, statusColor.b, 0.9f);
            }
        }

        foreach (Image link in _leftBoardLink)
        {
            if (link != null)
            {
                link.color = new Color(0.15f, 0.85f, 0.82f, repaired ? 0.75f : 0.48f);
            }
        }

        foreach (Image link in _rightBoardLink)
        {
            if (link != null)
            {
                link.color = new Color(statusColor.r, statusColor.g, statusColor.b, repaired ? 0.75f : 0.48f);
            }
        }
    }

    // 담당 CCTV의 단자 색상과 현재 연결된 전선을 배선 보드에 표시한다.
    private void HandleLocaleChanged(Locale locale) => RefreshBoard();

    private void RefreshBoard()
    {
        _objectiveText.text = (_state.IsRepaired ? _objectiveDone : _objectiveTodo)
            .GetLocalizedString(_cameraIndex + 1);

        for (int wireIndex = 0; wireIndex < WireCount; wireIndex++)
        {
            _sourceGraphics[wireIndex].color = _wireColors[wireIndex];
            _wireImages[wireIndex].color = _wireColors[wireIndex];

            int targetIndex = _state.ConnectedTargets[wireIndex];
            if (targetIndex < 0)
            {
                // 아직 연결되지 않은 전선은 보드에서 숨긴다.
                _wireImages[wireIndex].gameObject.SetActive(false);
                continue;
            }

            _wireImages[wireIndex].gameObject.SetActive(true);
            DrawWire(_wireImages[wireIndex].rectTransform, GetLocalCenter(_sourceTerminals[wireIndex]), GetLocalCenter(_targetTerminals[targetIndex]));
        }

        // 섞인 정답 배열을 기준으로 오른쪽 단자 색상을 표시한다.
        for (int targetIndex = 0; targetIndex < WireCount; targetIndex++)
        {
            _targetGraphics[targetIndex].color = _wireColors[_state.TargetColors[targetIndex]];
        }
    }

    // 단자의 중심점을 배선 보드 좌하단 기준 로컬 좌표로 변환한다.
    private Vector2 GetLocalCenter(RectTransform terminal)
    {
        Vector3 worldCenter = terminal.TransformPoint(terminal.rect.center);
        Vector2 localCenter = _connectionBoard.InverseTransformPoint(worldCenter);

        // 전선 앵커는 좌하단이므로 보드 피벗만큼 좌표를 보정한다.
        return localCenter + Vector2.Scale(_connectionBoard.rect.size, _connectionBoard.pivot);
    }

    // 시작점과 끝점 사이의 길이와 각도를 계산해 전선 이미지를 배치한다.
    private static void DrawWire(RectTransform wire, Vector2 start, Vector2 end)
    {
        Vector2 delta = end - start;
        wire.anchorMin = wire.anchorMax = Vector2.zero;
        wire.pivot = new Vector2(0f, 0.5f);
        wire.anchoredPosition = start;
        wire.sizeDelta = new Vector2(delta.magnitude, 22f);
        wire.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }
}
