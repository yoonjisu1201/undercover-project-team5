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

    [SerializeField] private string _regionId;
    [SerializeField] private Vector3 _regionCenter;
    [SerializeField] private Vector3 _regionSize;
    [SerializeField] private bool _unlockedAtStart;
    [SerializeField] private Placement[] _barriers = Array.Empty<Placement>();
    [SerializeField] private Placement[] _fogs = Array.Empty<Placement>();

    public string RegionId => _regionId;
    public Vector3 RegionCenter => _regionCenter;
    public Vector3 RegionSize => _regionSize;
    public bool UnlockedAtStart => _unlockedAtStart;
    public Placement[] Barriers => _barriers;
    public Placement[] Fogs => _fogs;
}
