using System;
using UnityEngine;
using UnityEngine.Serialization;

public enum RegionId {
    A,
    B,
    C,
    D,
    E,
    // 지상 A~E의 배타적 추첨과 무관하게 항상 별도로 관리되는 지하 구역.
    Basement
}

// Box Collider로 정의된 맵 구역의 해금 상태와 관련 오브젝트를 관리합니다.
[RequireComponent(typeof(BoxCollider))]
public sealed class MapRegion : MonoBehaviour
{
    [Header("구역 설정")]
    [SerializeField] private RegionId _regionId;
    [SerializeField] private BoxCollider _bounds;

    [Header("구역 콘텐츠")]
    [FormerlySerializedAs("_clueSpawnArea")]
    [SerializeField] private MapSpawnArea _spawnArea;

    [Header("이 구역에서 플레이할 때 시작 지점(캠핑카)을 놓을 위치")]
    [Tooltip("StartPoint 오브젝트가 이 Transform의 위치·회전으로 옮겨집니다. 본부 입구, 플레이어 스폰, 카트 스폰이 모두 그 자식이라 함께 따라옵니다.")]
    [SerializeField] private Transform _startPointAnchor;

    [Header("잠금 상태에서 활성화할 오브젝트")]
    [Tooltip("벽, 출입 차단물, NavMeshObstacle이 포함된 오브젝트 등을 등록합니다.")]
    [SerializeField] private GameObject[] _lockObjects = Array.Empty<GameObject>();

    public RegionId RegionId => _regionId;
    public BoxCollider Bounds => _bounds;
    public MapSpawnArea SpawnArea => _spawnArea;
    public Transform StartPointAnchor => _startPointAnchor;
    public bool IsUnlocked { get; private set; } = false;


    // 구역 해금 상태가 실제로 변경된 뒤 호출됩니다.
    public event Action<MapRegion, bool> UnlockStateChanged;

    private void Reset()
    {
        _bounds = GetComponent<BoxCollider>();
        _bounds.isTrigger = true;
        TryGetComponent(out _spawnArea);
    }

    private void OnValidate()
    {
        if (_bounds == null)
        {
            _bounds = GetComponent<BoxCollider>();
        }

        if (_spawnArea == null)
        {
            TryGetComponent(out _spawnArea);
        }
    }


    // 현재 구역의 해금 상태를 변경합니다.
    public void SetUnlocked(bool unlocked)
    {
        if (IsUnlocked == unlocked)
        {
            return;
        }

        ApplyUnlockState(unlocked, notify: true);
    }

    public void Unlock()
    {
        SetUnlocked(true);
    }

    public void Lock()
    {
        SetUnlocked(false);
    }


    // 구역 경계와 스폰 영역 Box를 월드 좌표 기준 경계에 맞춰 다시 설정합니다.
    // 절차적으로 생성되어 매번 크기가 달라지는 맵(지하 등)에서, 생성 결과에 맞춰 구역을 갱신할 때 씁니다.
    public void SetBoundsFromWorld(Bounds worldBounds)
    {
        if (_bounds != null)
        {
            MapSpawnArea.ApplyWorldBounds(_bounds, worldBounds);
        }

        _spawnArea?.SetBounds(worldBounds);
    }

    // 월드 좌표가 이 구역의 Box Collider 안에 있는지 확인합니다.
    public bool Contains(Vector3 worldPosition)
    {
        if (_bounds == null)
        {
            return false;
        }

        Vector3 localPosition = _bounds.transform.InverseTransformPoint(worldPosition) - _bounds.center;
        Vector3 halfSize = _bounds.size * 0.5f;

        return Mathf.Abs(localPosition.x) <= halfSize.x && Mathf.Abs(localPosition.y) <= halfSize.y && Mathf.Abs(localPosition.z) <= halfSize.z;
    }

    // 구역 해금 상태를 적용합니다.
    private void ApplyUnlockState(bool unlocked, bool notify)
    {
        IsUnlocked = unlocked;

        if (_spawnArea != null)
        {
            _spawnArea.enabled = unlocked;
        }

        // 잠금 상태에서 활성화할 오브젝트를 설정합니다.
        foreach (GameObject lockObject in _lockObjects)
        {
            if (lockObject != null)
            {
                lockObject.SetActive(!unlocked);
            }
        }
        if (notify)
        {
            UnlockStateChanged?.Invoke(this, unlocked);
        }
    }
}
