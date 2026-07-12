using UnityEngine;


[CreateAssetMenu(menuName = "Inventory/ItemData", fileName = "NewItemData")]

public class ItemData : ScriptableObject
{
    [SerializeField] private string _itemId;
    [SerializeField] private string _displayName;
    [SerializeField] private Sprite _icon;
    [SerializeField] private GameObject _worldPrefab;

    public string ItemId => _itemId;
    public string DisplayName => _displayName;
    public Sprite Icon => _icon;
    public GameObject WorldPrefab => _worldPrefab;

}