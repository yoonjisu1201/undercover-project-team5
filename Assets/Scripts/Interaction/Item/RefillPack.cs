using UnityEngine;

namespace Interaction.Item {
	public class RefillPack : ItemBase, IInteractionApplier {

		public string InteractionApplyText => "회복량 보충하기";
		
		public string ApplyCompletedMessage => "회복량 보충 완료";

		public bool CanApplyTo(GameObject user, InteractableBase target, out string failReason) {
			failReason = null;
			
			if (target is BasicCart) {
				return true;
			}
			
			return false;
		}
		
		public void ApplyToOnServer(GameObject user, InteractableBase target, PlayerInventory inventory, int selectedIndex) {
			// 카트 회복량 복구 이후 인벤토리에서 삭제 처리
			if (target is BasicCart cart) {
				cart.RefillHealingAmount();
				
				inventory.TryRemoveSelectedItemOnServer(ItemId, selectedIndex);
			}
		}
	}
}