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

    public ItemType ItemId => _itemId;
    public string DisplayName => _displayName;
    public string UsageDescription => _usageDescription;
    public Sprite Icon => _icon;
    public GameObject WorldPrefab => _worldPrefab;
    public AudioClip AudioClip => _audioClip;
}
