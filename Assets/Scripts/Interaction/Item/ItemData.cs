using UnityEngine;

[CreateAssetMenu(menuName = "Inventory/ItemData", fileName = "NewItemData")]
public class ItemData : ScriptableObject
{
    [SerializeField] private ItemType _itemId;
    [SerializeField] private string _displayName;
    [SerializeField, TextArea] private string _usageDescription;
    [SerializeField] private Sprite _icon;
    [SerializeField] private GameObject _worldPrefab;
    [SerializeField] private AudioClip _audioClip;

    [Header("=== 오른손에 들었을 때 ===")]
    [SerializeField] private Vector3 _holdPositionOffset;
    [SerializeField] private Vector3 _holdRotationOffset;
    [SerializeField, Range(0.1f, 1f)] private float _holdScale = 1f;

    public ItemType ItemId => _itemId;
    public string DisplayName => _displayName;
    public string UsageDescription => _usageDescription;
    public Sprite Icon => _icon;
    public GameObject WorldPrefab => _worldPrefab;
    public AudioClip AudioClip => _audioClip;
    public Vector3 HoldPositionOffset => _holdPositionOffset;
    public Vector3 HoldRotationOffset => _holdRotationOffset;
    public float HoldScale => _holdScale;
}
