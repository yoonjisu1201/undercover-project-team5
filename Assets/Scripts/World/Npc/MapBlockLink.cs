using Unity.AI.Navigation;
using UnityEngine;

public sealed class MapBlockLink : MonoBehaviour
{
    [SerializeField] private MapBlock _firstBlock;
    [SerializeField] private MapBlock _secondBlock;
    [SerializeField] private NavMeshLink _navMeshLink;

    /// <summary>
    /// 연결된 두 Block이 모두 해제된 경우에만 NavMeshLink를 활성화합니다.
    /// </summary>
    /// <param name="controller">Block 해제 상태를 관리하는 Controller입니다.</param>
    public void ApplyAvailability(MapBlockController controller)
    {
        bool shouldEnable = controller != null &&
            _firstBlock != null &&
            _secondBlock != null &&
            controller.IsBlockAvailable(_firstBlock) &&
            controller.IsBlockAvailable(_secondBlock);

        if (_navMeshLink != null)
        {
            _navMeshLink.enabled = shouldEnable;
        }
    }
}
