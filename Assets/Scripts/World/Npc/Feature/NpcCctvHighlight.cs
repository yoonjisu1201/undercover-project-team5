using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// CCTV 화면에서 이 NPC에 외곽선을 그리고, 커서를 올리면 착용 의상 이미지를 보여준다.
// 본부에서 CCTV만 보고도 용의자 후보를 좁히게 하는 것이 목적이다.
[RequireComponent(typeof(NpcFeatureController))]
public sealed class NpcCctvHighlight : MonoBehaviour, ICctvHighlightTarget, ICctvOutfitPreview
{
	// 한 NPC에서 공개할 의상 종류 수.
	[SerializeField, Min(1)] private int _revealCount = 2;

	private NpcFeatureController _featureController;
	private NetworkObject _networkObject;
	private Renderer[] _renderers;

	// 공개할 부위는 NPC마다 한 번만 정한다. 호버를 반복해도 바뀌지 않아야 한다.
	private List<Sprite> _thumbnails;

	private bool _hasOutline;

	private void Awake()
	{
		_featureController = GetComponent<NpcFeatureController>();
		_networkObject = GetComponent<NetworkObject>();
	}

	// 의상 파츠는 ApplyOutfit으로 런타임에 생성되므로 Awake 시점에는 렌더러가 없다.
	// 그래서 NpcFeatureController가 의상을 적용한 뒤 이것을 불러 준다.
	// 두 번 부르면 외곽선이 두 개 생기고, 앞의 것은 SetOutlineEnabled의 통제를 벗어나 계속 켜져 있는다.
	public void BuildOutline()
	{
		if (_hasOutline)
		{
			return;
		}

		_hasOutline = true;
		_renderers = GetComponentsInChildren<Renderer>(true);
		CctvHighlight.CreateOutline(this, transform, gameObject.layer, _renderers, CctvHighlightKind.Npc);
	}

	private void OnDestroy()
	{
		CctvHighlight.Unregister(this);
	}

	public CctvHighlightKind CctvKind => CctvHighlightKind.Npc;
	public Bounds CctvBounds => CctvHighlight.GetWorldBounds(_renderers, transform.position);
	public bool IsVisibleOnCctv => true;

	// 이름 대신 의상 이미지를 띄우므로 표시할 텍스트가 없다.
	public string CctvDisplayName => null;

	// ICctvOutfitPreview — 착용 의상 썸네일. 몽타주 선택 UI가 쓰는 그 이미지를 그대로 쓴다.
	public IReadOnlyList<Sprite> CctvOutfitThumbnails => _thumbnails ??= BuildThumbnails();

	// 착용한 부위 중 _revealCount 개를 뽑아 썸네일을 모은다.
	// NetworkObjectId를 시드로 써서 모든 클라이언트가 같은 부위를 본다. 별도 동기화가 필요 없다.
	private List<Sprite> BuildThumbnails()
	{
		List<Sprite> thumbnails = new();

		OutfitFeature outfit = _featureController.Outfit;
		if (outfit == null)
		{
			// 아직 서버에서 의상이 내려오지 않았다. 다음 호버에서 다시 만든다.
			return null;
		}

		List<ClothPart> equipped = CollectEquippedParts(outfit);
		if (equipped.Count == 0)
		{
			return thumbnails;
		}

		ulong seed = _networkObject != null ? _networkObject.NetworkObjectId : (ulong)GetInstanceID();
		System.Random random = new(unchecked((int)seed));

		// 앞에서 _revealCount 개만 쓰므로 전체를 섞는다.
		for (int i = equipped.Count - 1; i > 0; i--)
		{
			int swapIndex = random.Next(i + 1);
			(equipped[i], equipped[swapIndex]) = (equipped[swapIndex], equipped[i]);
		}

		foreach (ClothPart part in equipped)
		{
			if (thumbnails.Count >= _revealCount)
			{
				break;
			}

			ClothData data = ClothCatalog.Find(part, GetPartNumber(outfit, part));
			if (data == null || data.Thumbnail == null)
			{
				continue;
			}

			thumbnails.Add(data.Thumbnail);
		}

		return thumbnails;
	}

	// 착용하지 않은 부위는 번호가 -1이다(NpcOutfitController.GetRandomPartNumber).
	private static List<ClothPart> CollectEquippedParts(OutfitFeature outfit)
	{
		List<ClothPart> equipped = new();

		foreach (ClothPart part in AllParts)
		{
			if (GetPartNumber(outfit, part) >= 0)
			{
				equipped.Add(part);
			}
		}

		return equipped;
	}

	private static int GetPartNumber(OutfitFeature outfit, ClothPart part) => part switch
	{
		ClothPart.Beard => outfit.BeardNumber,
		ClothPart.Eyebrow => outfit.EyebrowsNumber,
		ClothPart.Glasses => outfit.GlassesNumber,
		ClothPart.Hair => outfit.HairNumber,
		ClothPart.Hat => outfit.HatNumber,
		ClothPart.Headphone => outfit.HeadphoneNumber,
		ClothPart.Arm => outfit.ArmNumber,
		ClothPart.Mask => outfit.MaskNumber,
		ClothPart.Pants => outfit.PantsNumber,
		ClothPart.Shoes => outfit.ShoesNumber,
		ClothPart.Torso => outfit.TorsoNumber,
		_ => -1
	};

	private static readonly ClothPart[] AllParts =
	{
		ClothPart.Beard, ClothPart.Eyebrow, ClothPart.Glasses, ClothPart.Hair, ClothPart.Hat,
		ClothPart.Headphone, ClothPart.Arm, ClothPart.Mask, ClothPart.Pants, ClothPart.Shoes, ClothPart.Torso
	};
}
