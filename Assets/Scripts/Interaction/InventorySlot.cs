using System;
using Unity.Netcode;

// NetworkList<T>에 담기 위해 struct + IEquatable + INetworkSerializeByMemcpy로 구성한다
// (WaitingRoomReadyManager.PlayerSlot과 같은 패턴). 슬롯은 "종류"가 아니라 실제 ItemBase
// 인스턴스를 가리킨다 - 그래서 주웠다 버려도 같은 오브젝트라 인스턴스 상태가 유지된다.
[Serializable]
public struct InventorySlot : IEquatable<InventorySlot>, INetworkSerializeByMemcpy
{
    public NetworkBehaviourReference ItemRef;

    public static readonly InventorySlot Empty = new InventorySlot { ItemRef = new NetworkBehaviourReference(null) };

    public bool IsEmpty => !TryGetItem(out _);

    public bool TryGetItem(out ItemBase item) => ItemRef.TryGet(out item);

    // 기존 소비 코드(슬롯의 "종류"만 필요한 곳)가 그대로 쓸 수 있도록 남겨둔 편의 프로퍼티.
    public ItemType ItemId => TryGetItem(out ItemBase item) ? item.ItemId : ItemType.None;

    public bool Equals(InventorySlot other) => ItemRef.Equals(other.ItemRef);
}
