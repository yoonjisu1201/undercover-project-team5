using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class ShopItemSlotUI : MonoBehaviour {
	[SerializeField] private Button _button;
	[SerializeField] private Image _background;
	[SerializeField] private Image _icon;

	[SerializeField] private Color _normalColor = new Color(0.018f, 0.09f, 0.095f, 1f);
	[SerializeField] private Color _selectedColor = new Color(0.12f, 0.38f, 0.34f, 1f);

	private ShopItemData _data;
	private Action<ShopItemData> _onSelected;

	public ShopItemData Data => _data;

	private void Awake() 
	{
		_button.onClick.AddListener(HandleClicked);
	}

	private void OnDestroy() 
	{
		_button.onClick.RemoveListener(HandleClicked);
	}

	public void Setup(ShopItemData data, Action<ShopItemData> onSelected) 
	{
		_data = data;
		_onSelected = onSelected;
		_icon.sprite = data.Icon;

		SetSelected(false);
	}

	public void SetSelected(bool selected) 
	{
		_background.color = selected ? _selectedColor : _normalColor;
	}

	private void HandleClicked() 
	{
		_onSelected?.Invoke(_data);
	}
}
