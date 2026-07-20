using UnityEngine;

// 단서가 랜덤으로 생성될 수 있는 하나의 맵 구역을 나타내는 클래스
[RequireComponent(typeof(BoxCollider))]
public sealed class ClueSpawnArea : MonoBehaviour
{
    [SerializeField] private bool _isUnlocked = true;

    private BoxCollider _bounds;

    public bool IsUnlocked => _isUnlocked && gameObject.activeInHierarchy;

    private void Awake()
    {
        _bounds = GetComponent<BoxCollider>();
    }

    public void SetUnlocked(bool isUnlocked)
    {
        //--- 구역 해금 상태 갱신 ---//
        _isUnlocked = isUnlocked;
    }

    //--- 회전과 크기가 적용된 BoxCollider 내부의 랜덤 지점 계산 ---//
    public Vector3 GetRandomPointOnTop()
    {
        _bounds ??= GetComponent<BoxCollider>();

        Vector3 halfSize = _bounds.size * 0.5f;
        Vector3 localPoint = _bounds.center + new Vector3(Random.Range(-halfSize.x, halfSize.x), halfSize.y, Random.Range(-halfSize.z, halfSize.z));

        return transform.TransformPoint(localPoint);
    }
}
