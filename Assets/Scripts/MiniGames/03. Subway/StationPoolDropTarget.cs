using UnityEngine.Scripting;

// 배치한 지하철 카드를 다시 보관 영역으로 돌려놓는 드롭 대상을 관리합니다.
[Preserve]
public sealed class StationPoolDropTarget : UIDropPool
{
    public void Initialize(SubwayRouteMiniGame owner)
    {
        InitializePool(owner);
    }
}
