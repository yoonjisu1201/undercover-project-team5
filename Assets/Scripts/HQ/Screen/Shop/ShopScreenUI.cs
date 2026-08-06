using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

public sealed class ShopScreenUI : MonoBehaviour, IClosableUi
{
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

	[Header("=== 공용 코인 표시 ===")]
	[SerializeField] private TMP_Text _creditsText;

	[Header("=== 구매 경고 ===")]
	[SerializeField] private GameObject _inventoryFullWarning;
	[SerializeField] private TMP_Text _inventoryFullMessageText;
	[SerializeField] private LocalizedString _inventoryFullMessage;

	private const float InventoryFullWarningSeconds = 2f;
	private CancellationTokenSource _inventoryFullWarningCts;

	private readonly List<ShopItemSlotUI> _spawnedSlots = new List<ShopItemSlotUI>();

	private ShopItemData _selectedItem;

	private void OnEnable() 
	{
		_medicalTabButton.onClick.AddListener(ShowMedical);
		_consumablesTabButton.onClick.AddListener(ShowConsumables);
		_equipmentTabButton.onClick.AddListener(ShowEquipment);
		_purchaseButton.onClick.AddListener(HandlePurchaseClicked);

		_shopManager.CreditsChanged += HandleCreditsChanged;
		_shopManager.InventoryFull += HandleInventoryFull;

		SetCategory(ShopCategory.Medical);
		HandleCreditsChanged(_shopManager.Credits);
	}

	private void OnDisable() 
	{
		_medicalTabButton.onClick.RemoveListener(ShowMedical);
		_consumablesTabButton.onClick.RemoveListener(ShowConsumables);
		_equipmentTabButton.onClick.RemoveListener(ShowEquipment);
		_purchaseButton.onClick.RemoveListener(HandlePurchaseClicked);

		_shopManager.CreditsChanged -= HandleCreditsChanged;
		_shopManager.InventoryFull -= HandleInventoryFull;

		HideInventoryFullWarning();
	}

	private void Update() 
	{

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

		_priceValue.text = "0";

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

		HideInventoryFullWarning();
		_shopManager.RequestPurchaseRpc(_selectedItem.ItemData.ItemId);
	}

	private void HandleCreditsChanged(int credits)
	{
		_creditsText.text = credits.ToString("N0");
		RefreshPurchaseButton();
	}

	private void HandleInventoryFull()
	{
		_inventoryFullWarningCts?.Cancel();
		_inventoryFullWarningCts?.Dispose();
		_inventoryFullWarningCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

		_inventoryFullMessageText.text = _inventoryFullMessage.GetLocalizedString();
		_inventoryFullWarning.SetActive(true);
		HideInventoryFullWarningAfterDelayAsync(_inventoryFullWarningCts.Token).Forget();
	}

	private async UniTaskVoid HideInventoryFullWarningAfterDelayAsync(CancellationToken cancellationToken)
	{
		await UniTask.Delay(
			TimeSpan.FromSeconds(InventoryFullWarningSeconds),
			ignoreTimeScale: true,
			cancellationToken: cancellationToken);

		_inventoryFullWarning.SetActive(false);
		_inventoryFullWarningCts?.Dispose();
		_inventoryFullWarningCts = null;
	}

	private void HideInventoryFullWarning()
	{
		_inventoryFullWarningCts?.Cancel();
		_inventoryFullWarningCts?.Dispose();
		_inventoryFullWarningCts = null;

		_inventoryFullWarning.SetActive(false);
	}

	public void Open()
	{
		HideInventoryFullWarning();
		SetShopActive(true);
	}

	public void Close()
	{
		HideInventoryFullWarning();
		SetShopActive(false);
	}

	private void SetShopActive(bool active)
	{
		if (_shopScreen.activeSelf == active)
		{
			return;
		}

		_shopScreen.SetActive(active);
	}
}
