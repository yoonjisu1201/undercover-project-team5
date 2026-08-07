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

    public RegionId RegionId => _regionId;
    public Vector3 RegionCenter => _regionCenter;
    public Vector3 RegionSize => _regionSize;
    public Placement[] Barriers => _barriers;
    public Placement[] Fogs => _fogs;
}
