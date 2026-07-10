using System.Collections.Generic;
using UnityEngine;


public class ItemCatalog : MonoBehaviour
{
    [SerializeField] private ItemData[] _items;

    private Dictionary<string, ItemData> _itemById;

    private void Awake()
    {
        _itemById = new Dictionary<string, ItemData>();

        foreach (ItemData item in _items)
        {
            if (item == null) continue;

            _itemById.Add(item.ItemId, item);
        }
    }

    public bool TryGet(string itemId, out ItemData itemData)
    {
        return _itemById.TryGetValue(itemId, out itemData);
    }


}