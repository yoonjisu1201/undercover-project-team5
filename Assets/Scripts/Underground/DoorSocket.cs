using Unity.AI.Navigation;
using UnityEngine;

// 지하 맵 모듈 프리팹의 문 위치를 표시하는 마커. 생성기가 GetComponentsInChildren로 찾아서 사용한다.
// forward가 이 문이 열리는 바깥쪽 방향이므로, 모듈을 만들 때 이 방향을 기준으로 맞춰야 한다.
public class DoorSocket : MonoBehaviour
{
    private const float GizmoSphereRadius = 0.15f;
    private const float GizmoForwardLength = 1f;

    // 실제로 문으로 쓰일 때 켜는 문 모델, 안 쓰이고 막힐 때 켜는 벽 모델.
    [SerializeField] private GameObject _door;
    [SerializeField] private GameObject _wall;
    [SerializeField] private GameObject _frontChecker;

    // 생성기가 이 소켓을 다른 모듈과 이었는지 표시하는 런타임 상태. 프리팹에 저장할 값이 아니다.
    public bool IsConnected { get; set; }

    private void Awake()
    {
        _door.SetActive(false);
        _wall.SetActive(false);
        _frontChecker.SetActive(false);

        // 문 모델은 열려있든 닫혀있든 NavMesh를 굽을 때 막힌 물체로 잡히면 안 되니 제외시킨다.
        if (!_door.TryGetComponent(out NavMeshModifier modifier))
        {
            modifier = _door.AddComponent<NavMeshModifier>();
        }

        modifier.ignoreFromBuild = true;
    }

    // 이 소켓이 실제로 다른 모듈과 이어져 문으로 쓰이게 됐을 때 부른다.
    public void ShowAsDoor()
    {
        _door.SetActive(true);
        _wall.SetActive(false);
    }

    // 이 소켓이 끝내 안 이어져서 막힌 벽으로 남을 때 부른다.
    public void ShowAsWall()
    {
        _door.SetActive(false);
        _wall.SetActive(true);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position, GizmoSphereRadius);
        Gizmos.DrawRay(transform.position, transform.forward * GizmoForwardLength);
    }
}
