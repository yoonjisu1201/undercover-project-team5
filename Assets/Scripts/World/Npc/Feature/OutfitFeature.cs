using System;
using Unity.Netcode;

public class OutfitFeature : INetworkSerializable, IEquatable<OutfitFeature> {
	public int BeardNumber;
	public int EyebrowsNumber;
	public int GlassesNumber;
	public int HairNumber;
	public int HatNumber;
	public int HeadphoneNumber;
	public int ArmNumber;
	public int MaskNumber;
	public int PantsNumber;
	public int ShoesNumber;
	public int TorsoNumber;

	public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
		serializer.SerializeValue(ref BeardNumber);
		serializer.SerializeValue(ref EyebrowsNumber);
		serializer.SerializeValue(ref GlassesNumber);
		serializer.SerializeValue(ref HairNumber);
		serializer.SerializeValue(ref HatNumber);
		serializer.SerializeValue(ref HeadphoneNumber);
		serializer.SerializeValue(ref ArmNumber);
		serializer.SerializeValue(ref MaskNumber);
		serializer.SerializeValue(ref PantsNumber);
		serializer.SerializeValue(ref ShoesNumber);
		serializer.SerializeValue(ref TorsoNumber);
	}

	public bool Equals(OutfitFeature other) {
		if (other == null) { return false; }
		if (ReferenceEquals(this, other)) return true;

		return BeardNumber == other.BeardNumber
		       && EyebrowsNumber == other.EyebrowsNumber
		       && GlassesNumber == other.GlassesNumber
		       && HairNumber == other.HairNumber
		       && HatNumber == other.HatNumber
		       && HeadphoneNumber == other.HeadphoneNumber
		       && ArmNumber == other.ArmNumber
		       && MaskNumber == other.MaskNumber
		       && PantsNumber == other.PantsNumber
		       && ShoesNumber == other.ShoesNumber
		       && TorsoNumber == other.TorsoNumber;
	}
}