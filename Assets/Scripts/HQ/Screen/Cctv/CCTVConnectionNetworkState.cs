using Unity.Netcode;
using UnityEngine;

// CCTV 1~5의 4비트 연결 마스크를 서버에서 관리하고 모든 클라이언트에 동일하게 복제합니다.
public sealed class CCTVConnectionNetworkState : NetworkBehaviour
{
    private readonly NetworkList<int> _connectionMasks = new();

    [Header("=== CCTV Hub 등록 ===")]
    [SerializeField] private CCTVHub _cctvHub;
    private bool _serverStateInitialized;
    private bool _isRebuildingServerState;

    public static CCTVConnectionNetworkState Instance { get; private set; }

    // 다른 CCTV 시스템이 현재 네트워크 상태를 찾을 수 있도록 인스턴스를 등록합니다.
    private void Awake()
    {
        Instance = this;
    }

    // 서버는 초기 상태를 만들고 모든 클라이언트는 복제 목록의 변경을 구독합니다.
    public override void OnNetworkSpawn()
    {
        _connectionMasks.OnListChanged += HandleConnectionMaskChanged;
        if (_cctvHub != null)
        {
            _cctvHub.OnCctvPointsActivated += HandleCctvPointsActivated;
        }

        if (IsServer && !_serverStateInitialized && _cctvHub != null && _cctvHub.CameraCount > 0)
        {
            InitializeServerState();
        }

        ApplyAllStatesLocally();
    }

    private void Update()
    {
        if (!IsServer || _serverStateInitialized || _cctvHub == null || _cctvHub.CameraCount == 0)
        {
            return;
        }

        InitializeServerState();
        ApplyAllStatesLocally();
    }

    // 네트워크 오브젝트가 해제되면 목록 이벤트와 정적 참조를 정리합니다.
    public override void OnNetworkDespawn()
    {
        _connectionMasks.OnListChanged -= HandleConnectionMaskChanged;
        if (_cctvHub != null)
        {
            _cctvHub.OnCctvPointsActivated -= HandleCctvPointsActivated;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    // 수리 UI 클라이언트에서 한 CCTV의 현재 연결 마스크를 서버에 요청합니다.
    public void RequestConnectionMask(int cameraIndex, int connectionMask)
    {
        // 조작한 클라이언트 화면은 RPC 왕복을 기다리지 않고 즉시 갱신합니다.
        _cctvHub.ApplyConnectionMask(cameraIndex, connectionMask);

        if (IsSpawned)
        {
            SetConnectionMaskRpc(cameraIndex, connectionMask);
        }
    }

    // 서버의 CCTV 방해 시스템이 연결 마스크를 직접 변경할 때 사용합니다.
    public void SetServerConnectionMask(int cameraIndex, int connectionMask)
    {
        if (!IsServer || cameraIndex < 0 || cameraIndex >= _connectionMasks.Count)
        {
            return;
        }

        _connectionMasks[cameraIndex] = connectionMask & CCTVPoint.FullConnectionMask;
    }

    // 서버가 관리하는 지정 CCTV의 연결 마스크를 반환합니다.
    public int GetServerConnectionMask(int cameraIndex)
    {
        if (!IsServer || cameraIndex < 0 || cameraIndex >= _connectionMasks.Count)
        {
            return 0;
        }

        return _connectionMasks[cameraIndex];
    }

    // 클라이언트가 요청한 연결 마스크를 4비트로 제한한 뒤 서버 복제 목록에 반영합니다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetConnectionMaskRpc(int cameraIndex, int connectionMask)
    {
        if (cameraIndex < 0 || cameraIndex >= _connectionMasks.Count)
        {
            return;
        }

        _connectionMasks[cameraIndex] = connectionMask & CCTVPoint.FullConnectionMask;
    }

    // 서버가 Partial CCTV 두 개와 각 초기 연결 수를 결정합니다.
    private void InitializeServerState()
    {
        _isRebuildingServerState = true;
        try
        {
            _connectionMasks.Clear();

            for (int cameraIndex = 0; cameraIndex < _cctvHub.CameraCount; cameraIndex++)
            {
                _connectionMasks.Add(0);
            }

            if (_cctvHub.CameraCount < 2)
            {
                _serverStateInitialized = true;
                return;
            }

            int[] cameraIndexes = new int[_cctvHub.CameraCount];
            for (int index = 0; index < cameraIndexes.Length; index++)
            {
                cameraIndexes[index] = index;
            }

            for (int index = cameraIndexes.Length - 1; index > 0; index--)
            {
                int swapIndex = Random.Range(0, index + 1);
                (cameraIndexes[index], cameraIndexes[swapIndex]) = (cameraIndexes[swapIndex], cameraIndexes[index]);
            }

            _connectionMasks[cameraIndexes[0]] = CreateRandomConnectionMask(Random.Range(1, 3));
            _connectionMasks[cameraIndexes[1]] = CreateRandomConnectionMask(Random.Range(1, 3));
            _serverStateInitialized = true;
        }
        finally
        {
            _isRebuildingServerState = false;
        }
    }

    private void HandleCctvPointsActivated()
    {
        _serverStateInitialized = false;

        if (IsServer)
        {
            InitializeServerState();
        }

        ApplyAllStatesLocally();
    }

    // 지정한 수만큼 서로 다른 전선 비트를 무작위로 켠 초기 Partial 마스크를 만듭니다.
    private static int CreateRandomConnectionMask(int connectionCount)
    {
        int[] wireIndexes = { 0, 1, 2, 3 };
        for (int index = wireIndexes.Length - 1; index > 0; index--)
        {
            int swapIndex = Random.Range(0, index + 1);
            (wireIndexes[index], wireIndexes[swapIndex]) = (wireIndexes[swapIndex], wireIndexes[index]);
        }

        int connectionMask = 0;
        int clampedConnectionCount = Mathf.Clamp(connectionCount, 0, CCTVPoint.RequiredConnectionCount);
        for (int index = 0; index < clampedConnectionCount; index++)
        {
            connectionMask |= 1 << wireIndexes[index];
        }

        return connectionMask;
    }

    // 복제 목록의 한 항목이 바뀌면 로컬 공용 저장소에도 같은 연결 마스크를 기록합니다.
    private void HandleConnectionMaskChanged(NetworkListEvent<int> changeEvent)
    {
        if (_isRebuildingServerState
            || changeEvent.Index < 0
            || changeEvent.Index >= _connectionMasks.Count
            || changeEvent.Index >= _cctvHub.CameraCount)
        {
            return;
        }

        _cctvHub.ApplyConnectionMask(changeEvent.Index, _connectionMasks[changeEvent.Index]);
    }

    // 처음 스폰된 클라이언트가 현재 복제 목록 전체를 로컬 화면에 반영하도록 합니다.
    private void ApplyAllStatesLocally()
    {
        int count = Mathf.Min(_connectionMasks.Count, _cctvHub.CameraCount);
        for (int cameraIndex = 0; cameraIndex < count; cameraIndex++)
        {
            _cctvHub.ApplyConnectionMask(cameraIndex, _connectionMasks[cameraIndex]);
        }
    }
}
