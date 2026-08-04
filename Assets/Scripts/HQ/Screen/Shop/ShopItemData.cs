using UnityEngine;
using UnityEngine.Localization;

public enum ShopCategory {
	Medical,
	Consumables,
	Equipment
}

[CreateAssetMenu(menuName = "Shop/Item Data", fileName = "NewShopItemData")]
public sealed class ShopItemData : ScriptableObject {
	[SerializeField] private ItemData _itemData;
	[SerializeField] private ShopCategory _category;
	[SerializeField] private LocalizedString _displayName;
	[SerializeField] private LocalizedString _description;
	[SerializeField] private int _price;

	public ItemData ItemData => _itemData;
	public ShopCategory Category => _category;
	public Sprite Icon => _itemData.Icon;
	public LocalizedString DisplayName => _displayName;
	public LocalizedString Description => _description;
	public int Price => _price;
}
