using System;
using Unity.Netcode;

/// 본부에서 조합한 몽타주의 파츠별 착용 상태입니다.
/// 값은 MontageClothData.id이고, -1은 미착용을 의미합니다.
public struct MontageState : INetworkSerializable, IEquatable<MontageState> {
	public const int None = -1;

	public int BeardId;
	public int EyebrowsId;
	public int GlassesId;
	public int HairId;
	public int HatId;
	public int HeadphoneId;
	public int ArmId;
	public int PantsId;
	public int MaskId;
	public int ShoesId;
	public int TorsoId;

	// struct는 기본값이 0이므로, 아무것도 입지 않은 상태는 반드시 이걸로 만들어야 한다
	public static MontageState Empty => new MontageState {
		BeardId = None,
		EyebrowsId = None,
		GlassesId = None,
		HairId = None,
		HatId = None,
		HeadphoneId = None,
		ArmId = None,
		PantsId = None,
		MaskId = None,
		ShoesId = None,
		TorsoId = None
	};

	public int Get(MontageParts part) {
		switch (part) {
			case MontageParts.Beard: return BeardId;
			case MontageParts.Eyebrows: return EyebrowsId;
			case MontageParts.Glasses: return GlassesId;
			case MontageParts.Hair: return HairId;
			case MontageParts.Hats: return HatId;
			case MontageParts.Headphones: return HeadphoneId;
			case MontageParts.Arms: return ArmId;
			case MontageParts.Pants: return PantsId;
			case MontageParts.Masks: return MaskId;
			case MontageParts.Shoes: return ShoesId;
			case MontageParts.Torso: return TorsoId;
			default:
				throw new ArgumentOutOfRangeException(nameof(part), part, "[MontageState] 알 수 없는 파츠입니다.");
		}
	}

	// struct이므로 원본을 바꾸지 않고 해당 파츠만 교체한 복사본을 돌려준다
	public MontageState WithCloth(MontageParts part, int clothId) {
		MontageState changed = this;

		switch (part) {
			case MontageParts.Beard: changed.BeardId = clothId; break;
			case MontageParts.Eyebrows: changed.EyebrowsId = clothId; break;
			case MontageParts.Glasses: changed.GlassesId = clothId; break;
			case MontageParts.Hair: changed.HairId = clothId; break;
			case MontageParts.Hats: changed.HatId = clothId; break;
			case MontageParts.Headphones: changed.HeadphoneId = clothId; break;
			case MontageParts.Arms: changed.ArmId = clothId; break;
			case MontageParts.Pants: changed.PantsId = clothId; break;
			case MontageParts.Masks: changed.MaskId = clothId; break;
			case MontageParts.Shoes: changed.ShoesId = clothId; break;
			case MontageParts.Torso: changed.TorsoId = clothId; break;
			default:
				throw new ArgumentOutOfRangeException(nameof(part), part, "[MontageState] 알 수 없는 파츠입니다.");
		}
		return changed;
	}

	
	// NetworkVariable로 기본 자료형이 아닌 클래스를 관리하려면 아래의 두 내용을 적어둬야 함,.. 무슨 뜻인지는 저도 잘 몰라요
	public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
		serializer.SerializeValue(ref BeardId);
		serializer.SerializeValue(ref EyebrowsId);
		serializer.SerializeValue(ref GlassesId);
		serializer.SerializeValue(ref HairId);
		serializer.SerializeValue(ref HatId);
		serializer.SerializeValue(ref HeadphoneId);
		serializer.SerializeValue(ref ArmId);
		serializer.SerializeValue(ref PantsId);
		serializer.SerializeValue(ref MaskId);
		serializer.SerializeValue(ref ShoesId);
		serializer.SerializeValue(ref TorsoId);
	}

	public bool Equals(MontageState other) {
		return BeardId == other.BeardId
		       && EyebrowsId == other.EyebrowsId
		       && GlassesId == other.GlassesId
		       && HairId == other.HairId
		       && HatId == other.HatId
		       && HeadphoneId == other.HeadphoneId
		       && ArmId == other.ArmId
		       && PantsId == other.PantsId
		       && MaskId == other.MaskId
		       && ShoesId == other.ShoesId
		       && TorsoId == other.TorsoId;
	}
}
