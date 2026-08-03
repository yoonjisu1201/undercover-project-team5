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

// CCTV 한 지점이 자기 카메라 번호와 연결 상태를 직접 소유한다.
public sealed class CCTVPoint : MonoBehaviour
{
    public const int CameraCount = 5;
    public const int RequiredConnectionCount = 4;
    public const int FullConnectionMask = (1 << RequiredConnectionCount) - 1;

    public int CameraNumber { get; private set; }

    public CCTVConnectionState ConnectionState
        => ConnectionMask == FullConnectionMask
            ? CCTVConnectionState.Connected
            : ConnectionMask == 0
                ? CCTVConnectionState.Disconnected
                : CCTVConnectionState.Partial;
    public int ConnectionMask { get; private set; } = FullConnectionMask;

    public event Action<CCTVConnectionState> OnConnectionStateChanged;

    // CCTVHub가 등록 시점에 카메라 번호를 부여한다.
    public void Initialize(int cameraNumber)
    {
        CameraNumber = cameraNumber;
    }

    // 4비트 연결 마스크를 반영하고, 실제로 값이 바뀐 경우에만 이벤트를 쏜다.
    public void ApplyConnectionMask(int connectionMask)
    {
        int sanitizedMask = connectionMask & FullConnectionMask;
        if (sanitizedMask == ConnectionMask)
        {
            return;
        }

        ConnectionMask = sanitizedMask;
        OnConnectionStateChanged?.Invoke(ConnectionState);
    }
}
