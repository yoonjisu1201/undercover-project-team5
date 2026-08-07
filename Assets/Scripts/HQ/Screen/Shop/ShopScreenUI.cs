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

public sealed class ShopScreenUI : ScreenBase, IClosableUi
{
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

	[Header("=== 구매 결과 팝업 ===")]
	[SerializeField] private GameObject _purchaseResultDimmer;
	[SerializeField] private GameObject _purchaseResultPopup;
	[SerializeField] private TMP_Text _purchaseResultMessageText;
	[SerializeField] private Button _purchaseResultConfirmButton;

	private const float InventoryFullWarningSeconds = 2f;
	private const string PurchaseCompleteMessageFormat = "{0} 구매 완료\n잔액: {1}";
	private const string PurchaseUnavailableMessageFormat = "구매 불가\n{0}";
	private const string InventoryFullReason = "인벤토리가 가득 찼습니다.";
	private CancellationTokenSource _inventoryFullWarningCts;

	private readonly List<ShopItemSlotUI> _spawnedSlots = new List<ShopItemSlotUI>();

	private ShopItemData _selectedItem;

	private void OnEnable()
	{
		_medicalTabButton.onClick.AddListener(ShowMedical);
		_consumablesTabButton.onClick.AddListener(ShowConsumables);
		_equipmentTabButton.onClick.AddListener(ShowEquipment);
		_purchaseButton.onClick.AddListener(HandlePurchaseClicked);
		_purchaseResultConfirmButton?.onClick.AddListener(HidePurchaseResultPopup);

		_shopManager.CreditsChanged += HandleCreditsChanged;
		_shopManager.InventoryFull += HandleInventoryFull;
		_shopManager.PurchaseCompleted += HandlePurchaseCompleted;
		_shopManager.PurchaseFailed += HandlePurchaseFailed;

		SetCategory(ShopCategory.Medical);
		HandleCreditsChanged(_shopManager.Credits);
		HidePurchaseResultPopup();
	}

	private void OnDisable()
	{
		_medicalTabButton.onClick.RemoveListener(ShowMedical);
		_consumablesTabButton.onClick.RemoveListener(ShowConsumables);
		_equipmentTabButton.onClick.RemoveListener(ShowEquipment);
		_purchaseButton.onClick.RemoveListener(HandlePurchaseClicked);
		_purchaseResultConfirmButton?.onClick.RemoveListener(HidePurchaseResultPopup);

		_shopManager.CreditsChanged -= HandleCreditsChanged;
		_shopManager.InventoryFull -= HandleInventoryFull;
		_shopManager.PurchaseCompleted -= HandlePurchaseCompleted;
		_shopManager.PurchaseFailed -= HandlePurchaseFailed;

		HideInventoryFullWarning();
		HidePurchaseResultPopup();
	}

	private void Update()
	{
		if (_purchaseResultPopup != null &&
			_purchaseResultPopup.activeSelf &&
			Keyboard.current != null &&
			Keyboard.current.escapeKey.wasPressedThisFrame)
		{
			HidePurchaseResultPopup();
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

	private void SelectItem(ShopItemData item)
	{
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
			_shopManager.IsSpawned;
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
		HandlePurchaseFailed(InventoryFullReason);
	}

	private void HandlePurchaseCompleted(string itemId, int remainingCredits)
	{
		HideInventoryFullWarning();

		string itemName = itemId;
		ShopItemData purchasedItem = FindShopItem(itemId);
		if (purchasedItem != null)
		{
			itemName = purchasedItem.DisplayName.GetLocalizedString();
		}

		ShowPurchaseResultPopup(string.Format(PurchaseCompleteMessageFormat, itemName, remainingCredits.ToString("N0")));
	}

	private void HandlePurchaseFailed(string reason)
	{
		HideInventoryFullWarning();
		ShowPurchaseResultPopup(string.Format(PurchaseUnavailableMessageFormat, reason));
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

	public void Close()
	{
		HidePurchaseResultPopup();
	}

	private void ShowPurchaseResultPopup(string message)
	{
		EnsurePurchaseResultPopup();

		if (_purchaseResultMessageText != null)
		{
			_purchaseResultMessageText.text = message;
		}

		_purchaseResultDimmer.SetActive(true);
		_purchaseResultPopup.SetActive(true);
		GameplayUiMode.Instance?.RegisterUi(this);
	}

	private void HidePurchaseResultPopup()
	{
		if (_purchaseResultPopup != null)
		{
			_purchaseResultPopup.SetActive(false);
		}

		if (_purchaseResultDimmer != null)
		{
			_purchaseResultDimmer.SetActive(false);
		}

		GameplayUiMode.Instance?.UnregisterUi(this);
	}

	private ShopItemData FindShopItem(string itemId)
	{
		foreach (ShopItemData item in _shopManager.ShopItems)
		{
			if (item != null && item.ItemData != null && item.ItemData.ItemId == itemId)
			{
				return item;
			}
		}

		return null;
	}

	private void EnsurePurchaseResultPopup()
	{
		if (_purchaseResultPopup != null && _purchaseResultDimmer != null)
		{
			return;
		}

		GameObject dimmer = new GameObject("PurchaseResultDimmer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		dimmer.transform.SetParent(transform, false);

		RectTransform dimmerRect = dimmer.GetComponent<RectTransform>();
		dimmerRect.anchorMin = Vector2.zero;
		dimmerRect.anchorMax = Vector2.one;
		dimmerRect.offsetMin = Vector2.zero;
		dimmerRect.offsetMax = Vector2.zero;

		Image dimmerImage = dimmer.GetComponent<Image>();
		dimmerImage.color = new Color(0f, 0f, 0f, 0.58f);
		_purchaseResultDimmer = dimmer;

		GameObject popup = _purchaseResultPopup;
		if (popup == null)
		{
			popup = new GameObject("PurchaseResultPopup", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			popup.transform.SetParent(dimmer.transform, false);
		}
		else
		{
			popup.transform.SetParent(dimmer.transform, false);
		}

		RectTransform popupRect = popup.GetComponent<RectTransform>();
		popupRect.anchorMin = new Vector2(0.5f, 0.5f);
		popupRect.anchorMax = new Vector2(0.5f, 0.5f);
		popupRect.pivot = new Vector2(0.5f, 0.5f);
		popupRect.sizeDelta = new Vector2(620f, 340f);
		popupRect.anchoredPosition = Vector2.zero;

		Image background = popup.GetComponent<Image>();
		if (background != null)
		{
			background.color = new Color(0.02f, 0.09f, 0.1f, 0.98f);
		}

		if (_purchaseResultMessageText == null)
		{
			_purchaseResultMessageText = CreatePopupText(popup.transform);
		}

		if (_purchaseResultConfirmButton == null)
		{
			_purchaseResultConfirmButton = CreatePopupButton(popup.transform);
			_purchaseResultConfirmButton.onClick.AddListener(HidePurchaseResultPopup);
		}

		_purchaseResultPopup = popup;
	}

	private TMP_Text CreatePopupText(Transform parent)
	{
		GameObject textObject = new GameObject("MessageText", typeof(RectTransform), typeof(TextMeshProUGUI));
		textObject.transform.SetParent(parent, false);

		RectTransform textRect = textObject.GetComponent<RectTransform>();
		textRect.anchorMin = new Vector2(0.08f, 0.42f);
		textRect.anchorMax = new Vector2(0.92f, 0.82f);
		textRect.offsetMin = Vector2.zero;
		textRect.offsetMax = Vector2.zero;

		TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
		text.alignment = TextAlignmentOptions.Center;
		text.fontSize = 40f;
		text.color = Color.white;
		text.enableAutoSizing = true;
		text.fontSizeMin = 24f;
		text.fontSizeMax = 40f;
		return text;
	}

	private Button CreatePopupButton(Transform parent)
	{
		GameObject buttonObject = new GameObject("ConfirmButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
		buttonObject.transform.SetParent(parent, false);

		RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
		buttonRect.anchorMin = new Vector2(0.32f, 0.12f);
		buttonRect.anchorMax = new Vector2(0.68f, 0.32f);
		buttonRect.offsetMin = Vector2.zero;
		buttonRect.offsetMax = Vector2.zero;

		Image buttonImage = buttonObject.GetComponent<Image>();
		buttonImage.color = new Color(0.12f, 0.38f, 0.34f, 1f);

		GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
		labelObject.transform.SetParent(buttonObject.transform, false);

		RectTransform labelRect = labelObject.GetComponent<RectTransform>();
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = Vector2.zero;
		labelRect.offsetMax = Vector2.zero;

		TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
		label.text = "확인";
		label.alignment = TextAlignmentOptions.Center;
		label.fontSize = 30f;
		label.color = Color.white;

		return buttonObject.GetComponent<Button>();
	}

	public override void ActivateScreen()
	{
		base.ActivateScreen();
		HideInventoryFullWarning();
		HidePurchaseResultPopup();
	}
}
