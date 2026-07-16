using System;
using Unity.Netcode;

public class NpcFeature : INetworkSerializable, IEquatable<NpcFeature> {
	// 처음부터 new로 생성해줘야 초기화 및 직렬화 시에 문제가 발생하지 않음
	// NetworkSpawn에서 초기화하려 했는데, 그렇게 하면 초기화보다 먼저 직렬화가 발생해서 문제 발생
	public OutfitFeature Outfit = new OutfitFeature();

	public void NetworkSerialize<T>(BufferSerializer<T> serializer)
		where T : IReaderWriter {
		serializer.SerializeValue(ref Outfit);
	}

	public bool Equals(NpcFeature other) {
		if (other == null) { return false; }
		if (ReferenceEquals(this, other)) return true;
		return Outfit?.Equals(other.Outfit) ?? other.Outfit == null;
	}
}