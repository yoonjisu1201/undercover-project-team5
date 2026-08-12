using System.Collections.Generic;
using UnityEngine;


public class ItemCatalog : MonoBehaviour {
    public static ItemCatalog Instance;
    [SerializeField] private ItemData[] _items;

    private Dictionary<ItemType, ItemData> _itemById;

    private void Awake()
    {
        if (Instance == null) { Instance = this; }
        else { Destroy(gameObject); }

        _itemById = new Dictionary<ItemType, ItemData>();
        foreach (ItemData item in _items) {
            if (item == null) continue;

            _itemById.Add(item.ItemId, item);
        }
    }

    public bool TryGet(ItemType itemId, out ItemData itemData)
    {
        return _itemById.TryGetValue(itemId, out itemData);
    }

    // NetworkSerialize에서 out 파라미터를 못 쓰는 호출부용 편의 오버로드
    public ItemData TryGet(ItemType itemId)
    {
        _itemById.TryGetValue(itemId, out ItemData itemData);
        return itemData;
    }
}