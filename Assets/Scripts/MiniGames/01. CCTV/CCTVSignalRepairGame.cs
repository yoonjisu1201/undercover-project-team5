using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// CCTV별 배선 입력을 처리하고 연결 상태와 화면을 동기화합니다.
public sealed class CCTVSignalRepairGame : MonoBehaviour
{
    private const int CameraCount = CCTVConnectionStateStore.CameraCount;
    private const int WireCount = CCTVConnectionStateStore.RequiredConnectionCount;

    [Header("CCTV 선택")]
    [SerializeField] private Button[] _cameraButtons;
    [SerializeField] private TMP_Text[] _cameraLabels;
    [SerializeField] private TMP_Text[] _cameraFeeds;

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

    private readonly CCTVRepairPuzzleState[] _states = new CCTVRepairPuzzleState[CameraCount];
    private readonly Color[] _wireColors =
    {
        new Color(0.95f, 0.18f, 0.18f), // 빨강
        new Color(0.15f, 0.45f, 1f),    // 파랑
        new Color(1f, 0.82f, 0.12f),    // 노랑
        new Color(0.95f, 0.18f, 0.75f)  // 분홍
    };

    private int _selectedCamera;

    // CCTV별 퍼즐 상태와 버튼, 드래그 단자를 초기화한다.
    private void Awake()
    {
        // 본부 오브젝트의 활성 여부와 무관한 공용 상태를 기준으로 초기 배선을 맞춥니다.
        CCTVConnectionStateStore.EnsureInitialized();

        // 각 CCTV가 서로 다른 단자 배치를 갖도록 퍼즐 상태를 따로 만듭니다.
        for (int cameraIndex = 0; cameraIndex < CameraCount; cameraIndex++)
        {
            _states[cameraIndex] = new CCTVRepairPuzzleState();
            SyncCameraStateFromStore(cameraIndex);
        }

        // 프리팹에 복제된 버튼도 배열 순서와 같은 CCTV를 선택하도록 클릭 이벤트를 연결한다.
        for (int cameraIndex = 0; cameraIndex < CameraCount; cameraIndex++)
        {
            int capturedCameraIndex = cameraIndex;
            _cameraButtons[cameraIndex].onClick.RemoveAllListeners();
            _cameraButtons[cameraIndex].onClick.AddListener(() => SelectCamera(capturedCameraIndex));
        }

        // 왼쪽 시작 단자에 해당 전선 번호와 게임 참조를 전달한다.
        for (int wireIndex = 0; wireIndex < WireCount; wireIndex++)
        {
            CCTVWireTerminal terminal = _sourceTerminals[wireIndex].GetComponent<CCTVWireTerminal>();
            terminal.Configure(this, wireIndex);
        }

        _resultOverlay.SetActive(false);
        SelectCamera(0);
    }

    // 화면이 활성화되면 서버 상태 변경을 구독하고 최신 배선 수를 복구합니다.
    private void OnEnable()
    {
        CCTVConnectionStateStore.OnCameraConnectionStateChanged +=
            HandleSharedConnectionStateChanged;

        // UI를 다시 열었을 때 그동안 서버에서 바뀐 연결 상태를 복구합니다.
        if (_states[0] != null)
        {
            for (int cameraIndex = 0; cameraIndex < CameraCount; cameraIndex++)
            {
                SyncCameraStateFromStore(cameraIndex);
            }

            RefreshCameraCards();
            RefreshBoard();
        }
    }

    // 화면이 비활성화되면 서버 상태 변경 구독을 해제합니다.
    private void OnDisable()
    {
        CCTVConnectionStateStore.OnCameraConnectionStateChanged -=
            HandleSharedConnectionStateChanged;
    }

    // Unity Button의 On Click 이벤트에서 선택한 CCTV 번호를 전달받는다.
    public void OnCameraButtonClick(int cameraIndex)
    {
        SelectCamera(cameraIndex);
    }

    // 선택한 CCTV를 현재 작업 대상으로 바꾸고 화면을 갱신한다.
    private void SelectCamera(int cameraIndex)
    {
        _selectedCamera = cameraIndex;
        RefreshCameraCards();
        RefreshBoard();
    }

    // 연결 가능한 전선이면 드래그 선 표시를 시작한다.
    public void BeginWireDrag(int wireIndex, PointerEventData eventData)
    {
        CCTVRepairPuzzleState state = _states[_selectedCamera];

        // 완료된 CCTV이거나 이미 연결한 전선은 다시 드래그하지 않는다.
        if (state.IsRepaired || state.ConnectedTargets[wireIndex] >= 0)
        {
            return;
        }

        ContinueWireDrag(wireIndex, eventData);
    }

    // 드래그 중인 전선을 시작 단자부터 포인터 위치까지 그린다.
    public void ContinueWireDrag(int wireIndex, PointerEventData eventData)
    {
        CCTVRepairPuzzleState state = _states[_selectedCamera];
        if (state.IsRepaired || state.ConnectedTargets[wireIndex] >= 0)
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
        CCTVRepairPuzzleState state = _states[_selectedCamera];
        if (state.IsRepaired || state.ConnectedTargets[wireIndex] >= 0)
        {
            return;
        }

        int matchedTarget = -1;

        // 포인터가 올라간 도착 단자와 전선 색상이 모두 일치해야 연결된다.
        for (int targetIndex = 0; targetIndex < WireCount; targetIndex++)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(_targetTerminals[targetIndex], eventData.position, eventData.pressEventCamera)
            && state.TargetColors[targetIndex] == wireIndex)
            {
                matchedTarget = targetIndex;
                break;
            }
        }

        state.ConnectedTargets[wireIndex] = matchedTarget;
        if (matchedTarget >= 0)
        {
            // 연결된 색상 정보가 유지되도록 전선 개수가 아닌 4비트 마스크를 동기화합니다.
            SetSharedConnectionMask(_selectedCamera, state.ConnectionMask);
        }
        RefreshCameraCards();
        RefreshBoard();
    }

    // 미니게임에서 바뀐 연결 마스크를 서버에 보내 모든 클라이언트의 CCTV에 반영합니다.
    private static void SetSharedConnectionMask(int cameraIndex, int connectionMask)
    {
        if (CCTVConnectionNetworkState.Instance != null)
        {
            CCTVConnectionNetworkState.Instance.RequestConnectionMask(
                cameraIndex,
                connectionMask);
            return;
        }

        // 네트워크 없이 프리팹만 테스트하는 경우에는 로컬 상태만 갱신합니다.
        CCTVConnectionStateStore.SetConnectionMask(cameraIndex, connectionMask);
    }

    // 서버에서 CCTV가 다시 끊기거나 복구되면 보관 중인 퍼즐 배선도 같은 수로 맞춥니다.
    private void HandleSharedConnectionStateChanged(
        int cameraIndex,
        CCTVConnectionState _)
    {
        SyncCameraStateFromStore(cameraIndex);
        RefreshCameraCards();

        if (cameraIndex == _selectedCamera)
        {
            RefreshBoard();
        }
    }

    // 공용 저장소의 연결 수를 지정한 CCTV 퍼즐에 반영합니다.
    private void SyncCameraStateFromStore(int cameraIndex)
    {
        _states[cameraIndex].SyncConnectionMask(CCTVConnectionStateStore.GetConnectionMask(cameraIndex));
    }

    // CCTV 선택 카드의 문구, 색상, 선택 가능 상태와 진행도를 갱신한다.
    private void RefreshCameraCards()
    {
        int repairedCount = 0;
        for (int i = 0; i < CameraCount; i++)
        {
            bool repaired = _states[i].IsRepaired;
            if (repaired) repairedCount++;

            // 연결 완료 후에도 다시 끊길 수 있으므로 모든 CCTV는 계속 선택할 수 있습니다.
            _cameraLabels[i].text = $"CCTV {i + 1}";
            int connectionCount = _states[i].ConnectedCount;
            // 배선 수에 맞춰 선택 카드에도 Disconnected/Partial/Connected를 동일하게 표시합니다.
            _cameraFeeds[i].text = repaired ? "● CONNECTED" : connectionCount > 0 ? "◐ PARTIAL" : "× DISCONNECTED";
            _cameraFeeds[i].color = repaired ? new Color(0.2f, 1f, 0.55f) : connectionCount > 0 ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.28f, 0.28f);
            _cameraButtons[i].interactable = true;
        }

        _progressText.text = $"ONLINE  {repairedCount} / {CameraCount}";
    }

    // 선택한 CCTV의 단자 색상과 현재 연결된 전선을 배선 보드에 표시한다.
    private void RefreshBoard()
    {
        CCTVRepairPuzzleState state = _states[_selectedCamera];
        _objectiveText.text = state.IsRepaired ? $"CCTV {_selectedCamera + 1} 연결 완료 — 다른 오프라인 CCTV를 선택하세요."
        : $"CCTV {_selectedCamera + 1}: 같은 색 단자를 드래그해서 연결하세요.";

        for (int wireIndex = 0; wireIndex < WireCount; wireIndex++)
        {
            _sourceGraphics[wireIndex].color = _wireColors[wireIndex];
            _wireImages[wireIndex].color = _wireColors[wireIndex];

            int targetIndex = state.ConnectedTargets[wireIndex];
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
            _targetGraphics[targetIndex].color = _wireColors[state.TargetColors[targetIndex]];
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
