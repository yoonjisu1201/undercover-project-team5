using System;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;

// Box Collider로 정의된 맵 구역의 선택 상태와 관련 오브젝트를 관리합니다.
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

    [Header("사용하지 않는 구역에서 비활성화할 오브젝트")]
    [SerializeField] private GameObject[] _lockObjects = Array.Empty<GameObject>();

    [Header("바리게이트")]
    [SerializeField] private GameObject _barricadeGroup;

    public string RegionId => _regionId;
    public BoxCollider Bounds => _bounds;
    public MapSpawnArea SpawnArea => _spawnArea;
    public bool SelectedAtStart => _unlockedAtStart;
    public bool IsSelected { get; private set; }


    // 구역 선택 상태가 실제로 변경된 뒤 호출됩니다.
    public event Action<MapRegion, bool> SelectionStateChanged;

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


    // 현재 구역의 선택 상태를 변경합니다.
    public void SetSelected(bool selected)
    {
        bool changed = IsSelected != selected;
        ApplySelectionState(selected, notify: changed);
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

    // 선택한 구역에서만 스폰 영역과 외곽 바리케이드를 활성화합니다.
    private void ApplySelectionState(bool selected, bool notify)
    {
        IsSelected = selected;

        if (_spawnArea != null)
        {
            _spawnArea.enabled = selected;
        }

        foreach (GameObject lockObject in _lockObjects)
        {
            if (lockObject != null)
            {
                lockObject.SetActive(selected);
            }
        }

        _barricadeGroup?.SetActive(selected);

        if (notify)
        {
            SelectionStateChanged?.Invoke(this, selected);
        }
    }
}
