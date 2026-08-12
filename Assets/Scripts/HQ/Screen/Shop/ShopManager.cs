using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public sealed class ShopManager : NetworkBehaviour {
	[Header("=== 상품 ===")]
	[SerializeField] private ShopItemData[] _shopItems;

	[Header("=== 공용 코인 ===")]
	[SerializeField] private int _initialCredits = 1000;

	[Header("=== 구매 아이템 생성 위치 ===")]
	[SerializeField] private Transform _itemDropPoint;

	private readonly NetworkVariable<int> _credits =
		new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

	public IReadOnlyList<ShopItemData> ShopItems => _shopItems;
	public int Credits => _credits.Value;

	public event Action<int> CreditsChanged;
	public event Action InventoryFull;
	public event Action<ItemType, int> PurchaseCompleted;
	public event Action<string> PurchaseFailed;

	public override void OnNetworkSpawn() 
	{
		if (IsServer) 
		{
			_credits.Value = _initialCredits;
		}

		_credits.OnValueChanged += HandleCreditsChanged;
		CreditsChanged?.Invoke(_credits.Value);
	}

	public override void OnNetworkDespawn() 
	{
		_credits.OnValueChanged -= HandleCreditsChanged;
	}

	public void AddCreditsOnServer(int amount)
	{
		if (!IsServer || amount <= 0) 
		{
			return;
		}

		_credits.Value += amount;
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	public void RequestPurchaseRpc(ItemType itemId, RpcParams rpcParams = default)
	{
		ShopItemData shopItem = FindShopItem(itemId);
		ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (shopItem == null)
        {
            Debug.LogWarning($"[ShopManager] 구매 실패: 상품을 찾을 수 없습니다. ItemId: {itemId}");
            return;
        }

        if (_credits.Value < shopItem.Price)
        {
            Debug.LogWarning($"[ShopManager] 구매 실패: 크레딧이 부족합니다. Credits: {_credits.Value}, Price: {shopItem.Price}");
			NotifyPurchaseFailedRpc("돈이 부족합니다.", RpcTarget.Single(senderClientId, RpcTargetUse.Temp));
            return;
        }

		ItemData itemData = shopItem.ItemData;

		if (itemData == null)
		{
			return;
		}

		if (shopItem.Category == ShopCategory.Consumables)
		{
			if (!NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient senderClient) ||
				senderClient.PlayerObject == null ||
				!senderClient.PlayerObject.TryGetComponent(out PlayerInventory inventory))
			{
				return;
			}

			if (!ItemBase.TrySpawnAndAddToInventory(itemData, inventory))
			{
				NotifyPurchaseFailedRpc("인벤토리가 가득 찼습니다.", RpcTarget.Single(senderClientId, RpcTargetUse.Temp));
				return;
			}

			_credits.Value -= shopItem.Price;
			NotifyPurchaseCompletedRpc(itemData.ItemId, _credits.Value, RpcTarget.Single(senderClientId, RpcTargetUse.Temp));
			return;
		}

		if (itemData.WorldPrefab == null ||
			!itemData.WorldPrefab.TryGetComponent(out ItemBase _) ||
			!itemData.WorldPrefab.TryGetComponent(out NetworkObject _)) 
		{
			return;
		}

		GameObject itemObject = Instantiate(
			itemData.WorldPrefab,
			_itemDropPoint.position,
			itemData.WorldPrefab.transform.rotation);

		ItemBase itemBase = itemObject.GetComponent<ItemBase>();
		NetworkObject networkObject = itemObject.GetComponent<NetworkObject>();

		itemBase.Configure(itemData);
		networkObject.Spawn(destroyWithScene: true);

		_credits.Value -= shopItem.Price;
		NotifyPurchaseCompletedRpc(itemData.ItemId, _credits.Value, RpcTarget.Single(senderClientId, RpcTargetUse.Temp));
	}

	[Rpc(SendTo.SpecifiedInParams)]
	private void NotifyPurchaseCompletedRpc(ItemType itemId, int remainingCredits, RpcParams rpcParams = default)
	{
		PurchaseCompleted?.Invoke(itemId, remainingCredits);
	}

	[Rpc(SendTo.SpecifiedInParams)]
	private void NotifyPurchaseFailedRpc(string reason, RpcParams rpcParams = default)
	{
		PurchaseFailed?.Invoke(reason);
	}

	private ShopItemData FindShopItem(ItemType itemId)
	{
		foreach (ShopItemData shopItem in _shopItems) 
		{
			if (shopItem != null && shopItem.ItemData != null && shopItem.ItemData.ItemId == itemId)
			{
				return shopItem;
			}
		}

		return null;
	}

	private void HandleCreditsChanged(int previousCredits, int currentCredits) 
	{
		CreditsChanged?.Invoke(currentCredits);
	}
}
