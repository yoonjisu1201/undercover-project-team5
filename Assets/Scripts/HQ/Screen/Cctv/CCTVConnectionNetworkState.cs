using Unity.Netcode;
using UnityEngine;

// CCTV 1~5의 연결 수를 서버에서 관리하고 모든 클라이언트에 동일하게 복제합니다.
public sealed class CCTVConnectionNetworkState : NetworkBehaviour
{
    private readonly NetworkList<int> _connectionCounts = new();

    public static CCTVConnectionNetworkState Instance { get; private set; }

    // 다른 CCTV 시스템이 현재 네트워크 상태를 찾을 수 있도록 인스턴스를 등록합니다.
    private void Awake()
    {
        Instance = this;
    }

    // 서버는 초기 상태를 만들고 모든 클라이언트는 복제 목록의 변경을 구독합니다.
    public override void OnNetworkSpawn()
    {
        _connectionCounts.OnListChanged += HandleConnectionCountChanged;

        if (IsServer && _connectionCounts.Count == 0)
        {
            InitializeServerState();
        }

        ApplyAllStatesLocally();
    }

    // 네트워크 오브젝트가 해제되면 목록 이벤트와 정적 참조를 정리합니다.
    public override void OnNetworkDespawn()
    {
        _connectionCounts.OnListChanged -= HandleConnectionCountChanged;

        if (Instance == this)
        {
            Instance = null;
        }
    }

    // 미니게임 클라이언트에서 한 CCTV의 현재 연결 수를 서버에 요청합니다.
    public void RequestConnectionCount(int cameraIndex, int connectionCount)
    {
        // 조작한 클라이언트 화면은 RPC 왕복을 기다리지 않고 즉시 갱신합니다.
        CCTVConnectionStateStore.SetConnectionCount(cameraIndex, connectionCount);

        if (IsSpawned)
        {
            SetConnectionCountRpc(cameraIndex, connectionCount);
        }
    }

    // 서버의 CCTV 방해 시스템이 연결 수를 직접 변경할 때 사용합니다.
    public void SetServerConnectionCount(int cameraIndex, int connectionCount)
    {
        if (!IsServer || cameraIndex < 0 || cameraIndex >= _connectionCounts.Count)
        {
            return;
        }

        _connectionCounts[cameraIndex] = Mathf.Clamp(connectionCount, 0, CCTVConnectionStateStore.RequiredConnectionCount);
    }

    // 서버가 관리하는 지정 CCTV의 연결 수를 반환합니다.
    public int GetServerConnectionCount(int cameraIndex)
    {
        if (!IsServer || cameraIndex < 0 || cameraIndex >= _connectionCounts.Count)
        {
            return 0;
        }

        return _connectionCounts[cameraIndex];
    }

    // 클라이언트가 요청한 연결 수를 검증한 뒤 서버 복제 목록에 반영합니다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetConnectionCountRpc(int cameraIndex, int connectionCount)
    {
        if (cameraIndex < 0 || cameraIndex >= _connectionCounts.Count)
        {
            return;
        }

        _connectionCounts[cameraIndex] = Mathf.Clamp(connectionCount, 0, CCTVConnectionStateStore.RequiredConnectionCount);
    }

    // 서버가 Partial CCTV 두 개와 각 초기 연결 수를 한 번만 결정합니다.
    private void InitializeServerState()
    {
        for (int cameraIndex = 0; cameraIndex < CCTVConnectionStateStore.CameraCount; cameraIndex++)
        {
            _connectionCounts.Add(0);
        }

        int[] cameraIndexes = { 0, 1, 2, 3, 4 };
        for (int index = cameraIndexes.Length - 1; index > 0; index--)
        {
            int swapIndex = Random.Range(0, index + 1);
            (cameraIndexes[index], cameraIndexes[swapIndex]) = (cameraIndexes[swapIndex], cameraIndexes[index]);
        }

        _connectionCounts[cameraIndexes[0]] = Random.Range(1, 3);
        _connectionCounts[cameraIndexes[1]] = Random.Range(1, 3);
    }

    // 복제 목록의 한 항목이 바뀌면 로컬 공용 저장소에도 같은 연결 수를 기록합니다.
    private void HandleConnectionCountChanged(NetworkListEvent<int> changeEvent)
    {
        if (changeEvent.Index < 0 || changeEvent.Index >= CCTVConnectionStateStore.CameraCount)
        {
            return;
        }

        CCTVConnectionStateStore.SetConnectionCount(changeEvent.Index, _connectionCounts[changeEvent.Index]);
    }

    // 처음 스폰된 클라이언트가 현재 복제 목록 전체를 로컬 화면에 반영하도록 합니다.
    private void ApplyAllStatesLocally()
    {
        int count = Mathf.Min(_connectionCounts.Count, CCTVConnectionStateStore.CameraCount);
        for (int cameraIndex = 0; cameraIndex < count; cameraIndex++)
        {
            CCTVConnectionStateStore.SetConnectionCount(cameraIndex, _connectionCounts[cameraIndex]);
        }
    }
}
