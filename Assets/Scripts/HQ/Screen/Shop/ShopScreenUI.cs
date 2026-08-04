using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

public sealed class ShopScreenUI : MonoBehaviour {
	[Header("=== 화면 ===")]
	[SerializeField] private GameObject _shopScreen;

	[Header("=== 상점 관리 ===")]
	[SerializeField] private ShopManager _shopManager;

	[Header("=== 카테고리 탭 ===")]
	[SerializeField] private Button _medicalTabButton;
	[SerializeField] private Button _consumablesTabButton;
	[SerializeField] private Button _equipmentTabButton;
	[SerializeField] private Image _medicalTabBackground;
	[SerializeField] private Image _consumablesTabBackground;
	[SerializeField] private Image _equipmentTabBackground;
	[SerializeField] private Color _tabActiveColor = new Color(0.12f, 0.38f, 0.34f, 1f);
	[SerializeField] private Color _tabInactiveColor = new Color(0.03f, 0.12f, 0.13f, 1f);

	[Header("=== 카테고리 부제 ===")]
	[SerializeField] private LocalizeStringEvent _headerSubtitle;
	[SerializeField] private LocalizedString _medicalSubtitle;
	[SerializeField] private LocalizedString _consumablesSubtitle;
	[SerializeField] private LocalizedString _equipmentSubtitle;

	[Header("=== 아이템 목록 ===")]
	[SerializeField] private Transform _itemContent;
	[SerializeField] private ShopItemSlotUI _itemSlotPrefab;

	[Header("=== 상세 정보 ===")]
	[SerializeField] private Image _previewIcon;
	[SerializeField] private GameObject _previewHint;
	[SerializeField] private LocalizeStringEvent _detailName;
	[SerializeField] private LocalizeStringEvent _detailDescription;
	[SerializeField] private TMP_Text _priceValue;
	[SerializeField] private LocalizedString _detailNamePlaceholder;
	[SerializeField] private LocalizedString _detailDescriptionPlaceholder;

	[Header("=== 버튼 ===")]
	[SerializeField] private Button _purchaseButton;
	[SerializeField] private Button _backButton;

	[Header("=== 공용 코인 표시 ===")]
	[SerializeField] private TMP_Text _creditsText;

	private readonly List<ShopItemSlotUI> _spawnedSlots = new List<ShopItemSlotUI>();

	private ShopItemData _selectedItem;

	private void OnEnable() 
	{
		_medicalTabButton.onClick.AddListener(ShowMedical);
		_consumablesTabButton.onClick.AddListener(ShowConsumables);
		_equipmentTabButton.onClick.AddListener(ShowEquipment);
		_purchaseButton.onClick.AddListener(HandlePurchaseClicked);
		_backButton.onClick.AddListener(HandleBackClicked);

		_shopManager.CreditsChanged += HandleCreditsChanged;

		SetCategory(ShopCategory.Medical);
		HandleCreditsChanged(_shopManager.Credits);
	}

	private void OnDisable() 
	{
		_medicalTabButton.onClick.RemoveListener(ShowMedical);
		_consumablesTabButton.onClick.RemoveListener(ShowConsumables);
		_equipmentTabButton.onClick.RemoveListener(ShowEquipment);
		_purchaseButton.onClick.RemoveListener(HandlePurchaseClicked);
		_backButton.onClick.RemoveListener(HandleBackClicked);

		_shopManager.CreditsChanged -= HandleCreditsChanged;
	}

	private void Update() 
	{
		Keyboard keyboard = Keyboard.current;

		if (keyboard == null) 
		{
			return;
		}

		if (keyboard.rightBracketKey.wasPressedThisFrame) 
		{
			SetShopActive(!_shopScreen.activeSelf);
		}

		if (keyboard.leftBracketKey.wasPressedThisFrame && _shopManager.IsSpawned) 
		{
			_shopManager.RequestAddCreditsRpc();
		}
	}

	private void ShowMedical() 
	{
		SetCategory(ShopCategory.Medical);
	}

	private void ShowConsumables() 
	{
		SetCategory(ShopCategory.Consumables);
	}

	private void ShowEquipment() 
	{
		SetCategory(ShopCategory.Equipment);
	}

	private void SetCategory(ShopCategory category) 
	{
		_medicalTabBackground.color = 
			category == ShopCategory.Medical ? _tabActiveColor : _tabInactiveColor;

		_consumablesTabBackground.color = 
			category == ShopCategory.Consumables ? _tabActiveColor : _tabInactiveColor;

		_equipmentTabBackground.color = 
			category == ShopCategory.Equipment ? _tabActiveColor : _tabInactiveColor;

		switch (category) 
		{
			case ShopCategory.Medical:
				_headerSubtitle.StringReference = _medicalSubtitle;
				break;

			case ShopCategory.Consumables:
				_headerSubtitle.StringReference = _consumablesSubtitle;
				break;

			case ShopCategory.Equipment:
				_headerSubtitle.StringReference = _equipmentSubtitle;
				break;
		}

		_headerSubtitle.RefreshString();
		RefreshSlots(category);
		ResetDetail();
	}

	private void RefreshSlots(ShopCategory category)
	{
		foreach (ShopItemSlotUI slot in _spawnedSlots) 
		{
			Destroy(slot.gameObject);
		}

		_spawnedSlots.Clear();

		foreach (ShopItemData item in _shopManager.ShopItems)
		{
			if (item.Category != category) 
			{
				continue;
			}

			ShopItemSlotUI slot = Instantiate(_itemSlotPrefab, _itemContent);
			slot.Setup(item, SelectItem);
			_spawnedSlots.Add(slot);
		}
	}

	private void SelectItem(ShopItemData item) {
		_selectedItem = item;

		foreach (ShopItemSlotUI slot in _spawnedSlots) 
		{
			slot.SetSelected(slot.Data == item);
		}

		_previewIcon.sprite = item.Icon;
		_previewIcon.enabled = true;
		_previewHint.SetActive(false);

		_detailName.StringReference = item.DisplayName;
		_detailName.RefreshString();

		_detailDescription.StringReference = item.Description;
		_detailDescription.RefreshString();

		_priceValue.text = item.Price.ToString("N0");

		RefreshPurchaseButton();
	}

	private void ResetDetail()
	{
		_selectedItem = null;

		_previewIcon.enabled = false;
		_previewHint.SetActive(true);

		_detailName.StringReference = _detailNamePlaceholder;
		_detailName.RefreshString();

		_detailDescription.StringReference = _detailDescriptionPlaceholder;
		_detailDescription.RefreshString();

		_priceValue.text = "000,000";

		RefreshPurchaseButton();
	}

	private void RefreshPurchaseButton() 
	{
		_purchaseButton.interactable =
			_selectedItem != null &&
			_shopManager.IsSpawned &&
			_shopManager.Credits >= _selectedItem.Price;
	}

	private void HandlePurchaseClicked() 
	{
		if (_selectedItem == null || !_shopManager.IsSpawned) 
		{
			return;
		}

		_shopManager.RequestPurchaseRpc(_selectedItem.ItemData.ItemId);
	}

	private void HandleCreditsChanged(int credits)
	{
		_creditsText.text = credits.ToString("N0");
		RefreshPurchaseButton();
	}

	private void SetShopActive(bool active) {
		if (_shopScreen.activeSelf == active) 
		{
			return;
		}

		_shopScreen.SetActive(active);

		if (active) 
		{
			GameplayUiMode.Instance?.ActivateCursor();
		} 
		else 
		{
			GameplayUiMode.Instance?.DeactivateCursor();
		}
	}

	private void HandleBackClicked() 
	{
		SetShopActive(false);
	}
}
