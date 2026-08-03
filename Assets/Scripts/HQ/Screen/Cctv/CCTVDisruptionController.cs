using Unity.Netcode;
using UnityEngine;

// 라운드 중 정기적으로, 또는 오체포 시 CCTV 연결 상태를 저하시킵니다.
public sealed class CCTVDisruptionController : NetworkBehaviour
{
    [SerializeField, Min(1f)] private float _minimumInterval = 180f;
    [SerializeField, Min(1f)] private float _maximumInterval = 300f;

    [Header("=== CCTV Hub 등록 ===")]
    [SerializeField] private CCTVHub _cctvHub;

    private double _nextDisruptionTime;

    public static CCTVDisruptionController Instance { get; private set; }

    // 오체포 판정 시스템이 서버 방해 기능을 호출할 수 있도록 인스턴스를 등록합니다.
    private void Awake()
    {
        Instance = this;
    }

    // 라운드 중 서버 시간으로 다음 방해 시점을 확인하고 한 대의 CCTV를 고장 냅니다.
    private void Update()
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }

        if (RoundManager.Instance == null || RoundManager.Instance.CurrentState != RoundState.InRound)
        {
            _nextDisruptionTime = 0d;
            return;
        }

        if (_nextDisruptionTime <= 0d)
        {
            ScheduleNextDisruption();
            return;
        }

        if (NetworkManager.ServerTime.Time < _nextDisruptionTime)
        {
            return;
        }

        DisruptRandomCameras(1);
        ScheduleNextDisruption();
    }

    // 네트워크 오브젝트가 해제되면 정적 인스턴스 참조를 정리합니다.
    public override void OnNetworkDespawn()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // 시민 오체포 시 CCTV 한두 개를 추가로 방해합니다.
    public void TriggerWrongArrestDisruption()
    {
        if (!IsServer)
        {
            return;
        }

        DisruptRandomCameras(Random.Range(1, 3));
    }

    // 현재 설정된 최소·최대 간격 사이에서 다음 서버 방해 시각을 정합니다.
    private void ScheduleNextDisruption()
    {
        float minimum = Mathf.Min(_minimumInterval, _maximumInterval);
        float maximum = Mathf.Max(_minimumInterval, _maximumInterval);
        _nextDisruptionTime = NetworkManager.ServerTime.Time + Random.Range(minimum, maximum);
    }

    // 정상 CCTV를 우선으로 고르고 남는 수량은 Partial CCTV에서 골라 연결 상태를 낮춥니다.
    private void DisruptRandomCameras(int disruptionCount)
    {
        CCTVConnectionNetworkState networkState = CCTVConnectionNetworkState.Instance;
        if (networkState == null)
        {
            return;
        }

        int[] connectedCameraIndexes = new int[_cctvHub.CameraCount];
        int[] partialCameraIndexes = new int[_cctvHub.CameraCount];
        int connectedCount = 0;
        int partialCount = 0;

        for (int cameraIndex = 0; cameraIndex < _cctvHub.CameraCount; cameraIndex++)
        {
            int connectionMask = networkState.GetServerConnectionMask(cameraIndex);
            if (connectionMask == CCTVPoint.FullConnectionMask)
            {
                connectedCameraIndexes[connectedCount++] = cameraIndex;
            }
            else if (connectionMask != 0)
            {
                partialCameraIndexes[partialCount++] = cameraIndex;
            }
        }

        Shuffle(connectedCameraIndexes, connectedCount);
        Shuffle(partialCameraIndexes, partialCount);

        int targetCount = Mathf.Min(Mathf.Max(disruptionCount, 0), connectedCount + partialCount);
        int disruptedCount = 0;

        // Connected CCTV는 Disconnected 또는 Partial 중 하나로 낮춥니다.
        for (int index = 0; index < connectedCount && disruptedCount < targetCount; index++)
        {
            int disruptedConnectionMask = Random.value < 0.5f ? 0 : CreateRandomPartialMask();
            networkState.SetServerConnectionMask(connectedCameraIndexes[index], disruptedConnectionMask);
            disruptedCount++;
        }

        // 이미 Partial인 CCTV는 더 악화되는 방향인 Disconnected로만 변경합니다.
        for (int index = 0; index < partialCount && disruptedCount < targetCount; index++)
        {
            networkState.SetServerConnectionMask(partialCameraIndexes[index], 0);
            disruptedCount++;
        }
    }

    // Partial 고장 상태에 사용할 한두 가닥의 무작위 연결 마스크를 만듭니다.
    private static int CreateRandomPartialMask()
    {
        int firstWireIndex = Random.Range(0, CCTVPoint.RequiredConnectionCount);
        int connectionMask = 1 << firstWireIndex;

        if (Random.value < 0.5f)
        {
            int secondWireIndex;
            do
            {
                secondWireIndex = Random.Range(0, CCTVPoint.RequiredConnectionCount);
            }
            while (secondWireIndex == firstWireIndex);

            connectionMask |= 1 << secondWireIndex;
        }

        return connectionMask;
    }

    // 배열의 유효한 앞부분만 섞어 같은 상태 안에서 대상 순서를 무작위로 만듭니다.
    private static void Shuffle(int[] cameraIndexes, int count)
    {
        for (int index = count - 1; index > 0; index--)
        {
            int swapIndex = Random.Range(0, index + 1);
            (cameraIndexes[index], cameraIndexes[swapIndex]) = (cameraIndexes[swapIndex], cameraIndexes[index]);
        }
    }
}
