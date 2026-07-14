using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// 두 Block의 해금 상태를 NavMeshLink 활성화 상태로 반영합니다.
/// NPC 이동이나 경로 계산 자체는 담당하지 않습니다.
/// </summary>
public sealed class MapBlockLink : MonoBehaviour
{
    [SerializeField] private MapBlock _firstBlock;
    [SerializeField] private MapBlock _secondBlock;
    [SerializeField] private NavMeshLink _navMeshLink;

    /// <summary>
    /// 양쪽 Block이 모두 해금된 경우에만 NavMeshLink를 활성화합니다.
    /// </summary>
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
