using Unity.AI.Navigation;
using UnityEngine;

//추가----------------------
/// <summary>
/// 두 MapBlock이 모두 해금되었을 때만 NPC가 이용할 NavMesh 연결 지점을 활성화합니다.
/// </summary>
[RequireComponent(typeof(NavMeshLink))]
public sealed class MapBlockLink : MonoBehaviour
{
    [SerializeField] private MapBlock _blockA;
    [SerializeField] private MapBlock _blockB;
    [SerializeField] private NavMeshLink _navMeshLink;

    /// <summary>
    /// Component를 추가할 때 같은 GameObject의 NavMeshLink를 자동으로 연결합니다.
    /// </summary>
    private void Reset()
    {
        _navMeshLink = GetComponent<NavMeshLink>();
    }

    /// <summary>
    /// 연결된 양쪽 MapBlock이 모두 이용 가능할 때만 NavMesh 연결을 활성화합니다.
    /// </summary>
    /// <param name="blockController">현재 이용 가능한 MapBlock을 관리하는 Controller입니다.</param>
    public void RefreshAvailability(MapBlockController blockController)
    {
        if (_navMeshLink == null)
        {
            _navMeshLink = GetComponent<NavMeshLink>();
        }

        if (_navMeshLink == null)
        {
            return;
        }

        bool canUseLink = blockController != null &&
            blockController.IsBlockAvailable(_blockA) &&
            blockController.IsBlockAvailable(_blockB);

        _navMeshLink.enabled = canUseLink;
    }
}
