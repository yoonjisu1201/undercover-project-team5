using System;
using Unity.Netcode;

// 본부 콘솔에서 지금 무엇을 보고 있는지. 관전자가 대상 화면을 그대로 따라가는 데 쓴다.
//
// 탭·CCTV 카메라·몽타주 항목·지도 모드를 따로 동기화하면 값 하나마다 NetworkVariable과
// 이벤트가 한 세트씩 붙는다. 하위 화면이 늘어날수록 그 비용이 커져 한 덩어리로 묶는다.
public struct HqConsoleState : INetworkSerializable, IEquatable<HqConsoleState>
{
    // 콘솔이 닫혀 있음을 뜻하는 탭 값.
    public const sbyte ClosedTab = -1;

    public sbyte Tab;          // HqScreenController.ConsoleTab
    public byte CctvCamera;
    public byte MontagePart;   // ClothPart
    public byte MinimapMode;   // MinimapScreenController.MinimapMode

    public bool IsOpen => Tab != ClosedTab;

    public static HqConsoleState Closed => new() { Tab = ClosedTab };

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tab);
        serializer.SerializeValue(ref CctvCamera);
        serializer.SerializeValue(ref MontagePart);
        serializer.SerializeValue(ref MinimapMode);
    }

    // NetworkVariable이 값이 실제로 바뀌었는지 판단할 때 쓴다. 없으면 매번 바뀐 것으로 보고 보낸다.
    public bool Equals(HqConsoleState other)
    {
        return Tab == other.Tab
            && CctvCamera == other.CctvCamera
            && MontagePart == other.MontagePart
            && MinimapMode == other.MinimapMode;
    }
}
