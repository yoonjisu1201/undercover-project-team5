using System;
using UnityEngine;

[CreateAssetMenu(fileName = "MapBoundaryLayout", menuName = "Undercover/Map Boundary Layout")]
public sealed class MapBoundaryLayout : ScriptableObject
{
    [Serializable]
    public struct Placement
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
    }

    [SerializeField] private RegionId _regionId;
    [SerializeField] private Vector3 _regionCenter;
    [SerializeField] private Vector3 _regionSize;
    [SerializeField] private Placement[] _barriers = Array.Empty<Placement>();
    [SerializeField] private Placement[] _fogs = Array.Empty<Placement>();

    // 바리게이트만으로는 지형이 꺼진 곳이나 모서리에 틈이 남는다. 그런 구간을 보이지 않는
    // 콜라이더로 통째로 막는다. Scale이 곧 벽의 크기(m)다.
    [SerializeField] private Placement[] _invisibleWalls = Array.Empty<Placement>();

    public RegionId RegionId => _regionId;
    public Vector3 RegionCenter => _regionCenter;
    public Vector3 RegionSize => _regionSize;
    public Placement[] Barriers => _barriers;
    public Placement[] Fogs => _fogs;
    public Placement[] InvisibleWalls => _invisibleWalls;
}
