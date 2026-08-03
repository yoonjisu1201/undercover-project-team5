using System;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;

// Box Collider로 정의된 맵 구역의 해금 상태와 관련 오브젝트를 관리합니다.
[RequireComponent(typeof(BoxCollider))]
public sealed class MapRegion : MonoBehaviour
{
    [Header("구역 설정")]
    [SerializeField] private string _regionId;
    [SerializeField] private BoxCollider _bounds;
    [SerializeField] private bool _unlockedAtStart;

    [Header("구역 콘텐츠")]
    [FormerlySerializedAs("_clueSpawnArea")]
    [SerializeField] private MapSpawnArea _spawnArea;

    [Header("잠금 상태에서 활성화할 오브젝트")]
    [Tooltip("벽, 출입 차단물, NavMeshObstacle이 포함된 오브젝트 등을 등록합니다.")]
    [SerializeField] private GameObject[] _lockObjects = Array.Empty<GameObject>();

    [Header("바리게이트")]
    [SerializeField] private GameObject _barricadeGroup;

    public string RegionId => _regionId;
    public BoxCollider Bounds => _bounds;
    public MapSpawnArea SpawnArea => _spawnArea;
    public bool IsUnlocked { get; private set; }


    // 구역 해금 상태가 실제로 변경된 뒤 호출됩니다.
    public event Action<MapRegion, bool> UnlockStateChanged;

    private void Reset()
    {
        _bounds = GetComponent<BoxCollider>();
        _bounds.isTrigger = true;
        TryGetComponent(out _spawnArea);
    }

    private void Awake()
    {
        ApplyUnlockState(_unlockedAtStart, _barricadeGroup, notify: false);
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
    public void SetUnlocked(bool unlocked, GameObject barricadeGroup)
    {
        if (IsUnlocked == unlocked)
        {
            return;
        }

        ApplyUnlockState(unlocked, barricadeGroup, notify: true);
    }

    public void Unlock()
    {
        SetUnlocked(true, _barricadeGroup);
    }

    public void Lock()
    {
        SetUnlocked(false, _barricadeGroup);
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
    private void ApplyUnlockState(bool unlocked, GameObject barricadeGroup, bool notify)
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
            if (barricadeGroup != null)
            {
                barricadeGroup.SetActive(!unlocked);
            }
        }

        if (notify)
        {
            UnlockStateChanged?.Invoke(this, unlocked);
        }
    }
}
