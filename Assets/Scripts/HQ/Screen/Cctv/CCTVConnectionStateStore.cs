using System;
using UnityEngine;

// CCTV 한 대의 영상 연결 상태를 나타냅니다.
public enum CCTVConnectionState
{
    // 연결된 전선이 없어 영상 신호가 완전히 끊긴 상태입니다.
    Disconnected,

    // 전선 일부만 연결되어 영상은 나오지만 글리치가 발생하는 상태입니다.
    Partial,

    // 네 전선이 모두 연결되어 정상 영상을 출력하는 상태입니다.
    Connected
}

// 미니게임 UI와 본부 CCTV가 같은 연결 수를 참조하도록 로컬 상태를 보관합니다.
public static class CCTVConnectionStateStore
{
    public const int CameraCount = 5;
    public const int RequiredConnectionCount = 4;
    public const int FullConnectionMask = (1 << RequiredConnectionCount) - 1;

    private static readonly int[] ConnectionMasks = new int[CameraCount];
    private static bool _isInitialized;

    // 전선 연결 상태는 아래처럼 4비트 마스크로 저장합니다.
    // 파랑만 연결      → 0010
    // 빨강 + 노랑 연결 → 0101
    // 전부 연결        → 1111
    // 전부 끊김        → 0000

    // 특정 CCTV의 연결 수가 변경되면 카메라 인덱스와 새 상태를 전달합니다.
    public static event Action<int, CCTVConnectionState> OnCameraConnectionStateChanged;

    // Domain Reload 비활성 설정에서도 새 실행마다 이전 상태가 남지 않도록 초기화합니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Array.Clear(ConnectionMasks, 0, ConnectionMasks.Length);
        _isInitialized = false;
        OnCameraConnectionStateChanged = null;
    }

    // 실제 초기 연결 수는 서버의 CCTVConnectionNetworkState가 결정하므로 저장 공간만 준비합니다.
    public static void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
    }

    // 네트워크에서 받은 연결 마스크를 저장하고 모든 로컬 화면에 변경을 알립니다.
    public static void SetConnectionMask(int cameraIndex, int connectionMask)
    {
        EnsureInitialized();
        if (!IsValidCameraIndex(cameraIndex))
        {
            return;
        }

        int sanitizedConnectionMask = connectionMask & FullConnectionMask;
        if (ConnectionMasks[cameraIndex] == sanitizedConnectionMask)
        {
            return;
        }

        ConnectionMasks[cameraIndex] = sanitizedConnectionMask;
        OnCameraConnectionStateChanged?.Invoke(cameraIndex, GetState(cameraIndex));
    }

    // 지정한 CCTV의 4비트 연결 마스크를 반환합니다.
    public static int GetConnectionMask(int cameraIndex)
    {
        EnsureInitialized();
        return IsValidCameraIndex(cameraIndex) ? ConnectionMasks[cameraIndex] : 0;
    }

    // 지정한 CCTV의 연결 마스크에서 연결된 전선 수를 계산합니다.
    public static int GetConnectionCount(int cameraIndex)
    {
        return CountConnectedWires(GetConnectionMask(cameraIndex));
    }

    // 4비트 연결 마스크에서 켜진 비트 수를 계산합니다.
    public static int CountConnectedWires(int connectionMask)
    {
        int sanitizedConnectionMask = connectionMask & FullConnectionMask;
        int connectedCount = 0;

        while (sanitizedConnectionMask != 0)
        {
            connectedCount += sanitizedConnectionMask & 1;
            sanitizedConnectionMask >>= 1;
        }

        return connectedCount;
    }

    // 연결된 전선 수를 화면에서 사용하는 세 단계 상태로 변환합니다.
    public static CCTVConnectionState GetState(int cameraIndex)
    {
        int connectionMask = GetConnectionMask(cameraIndex);
        if (connectionMask == 0)
        {
            return CCTVConnectionState.Disconnected;
        }

        return connectionMask == FullConnectionMask ? CCTVConnectionState.Connected : CCTVConnectionState.Partial;
    }

    // 외부에서 전달된 인덱스가 CCTV 1~5 범위 안에 있는지 확인합니다.
    private static bool IsValidCameraIndex(int cameraIndex)
    {
        return cameraIndex >= 0 && cameraIndex < CameraCount;
    }
}
