using UnityEngine;

// CCTV 한 대의 단자 배치와 현재 연결된 전선을 관리합니다.
public sealed class CCTVRepairPuzzleState
{
    private const int WireCount = CCTVConnectionStateStore.RequiredConnectionCount;

    public readonly int[] TargetColors;
    public readonly int[] ConnectedTargets = { -1, -1, -1, -1 };

    // 현재 올바르게 연결된 전선 수를 배열 할당 없이 계산합니다.
    public int ConnectedCount
    {
        get
        {
            int connectedCount = 0;
            for (int wireIndex = 0; wireIndex < ConnectedTargets.Length; wireIndex++)
            {
                if (ConnectedTargets[wireIndex] >= 0)
                {
                    connectedCount++;
                }
            }

            return connectedCount;
        }
    }

    // 필요한 전선이 모두 연결되었는지 반환합니다.
    public bool IsRepaired => ConnectedCount == WireCount;

    // 현재 연결된 전선 번호를 4비트 마스크로 변환합니다.
    public int ConnectionMask
    {
        get
        {
            int connectionMask = 0;
            for (int wireIndex = 0; wireIndex < ConnectedTargets.Length; wireIndex++)
            {
                if (ConnectedTargets[wireIndex] >= 0)
                {
                    connectionMask |= 1 << wireIndex;
                }
            }

            return connectionMask;
        }
    }

    // 새 퍼즐마다 오른쪽 단자의 색상 순서를 무작위로 구성합니다.
    public CCTVRepairPuzzleState()
    {
        TargetColors = new[] { 0, 1, 2, 3 };

        for (int index = TargetColors.Length - 1; index > 0; index--)
        {
            int swapIndex = UnityEngine.Random.Range(0, index + 1);
            (TargetColors[index], TargetColors[swapIndex]) = (TargetColors[swapIndex], TargetColors[index]);
        }
    }

    // 서버 연결 마스크에 표시된 전선만 정확히 복구하고 나머지는 해제합니다.
    public void SyncConnectionMask(int connectionMask)
    {
        int sanitizedConnectionMask = connectionMask & CCTVConnectionStateStore.FullConnectionMask;
        for (int wireIndex = 0; wireIndex < WireCount; wireIndex++)
        {
            bool isConnected = (sanitizedConnectionMask & (1 << wireIndex)) != 0;
            ConnectedTargets[wireIndex] = isConnected ? FindTargetForWire(wireIndex) : -1;
        }
    }

    // 지정한 전선 색상과 같은 색을 가진 오른쪽 단자의 위치를 찾습니다.
    private int FindTargetForWire(int wireIndex)
    {
        for (int targetIndex = 0; targetIndex < WireCount; targetIndex++)
        {
            if (TargetColors[targetIndex] == wireIndex)
            {
                return targetIndex;
            }
        }

        return -1;
    }
}
