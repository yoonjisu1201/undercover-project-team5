// 단서로 캡쳐 가능한 파츠인지 판정하는 순수 로직.
public static class ClueCaptureRules
{
    public static bool IsCapturable(ClothPart part, int clothId, out ClothData data)
    {
        if (clothId < 0)
        {
            data = null;
            return false;
        }

        data = ClothCatalog.Find(part, clothId);
        return data != null && data.MontagePrefab != null;
    }

    public static int CalculateCapturableClueCount(NpcFeature criminalFeature)
    {
        if (criminalFeature?.Outfit == null)
        {
            return 0;
        }

        OutfitFeature outfit = criminalFeature.Outfit;
        int count = 0;

        count += IsCapturable(ClothPart.Beard, outfit.BeardNumber, out _) ? 1 : 0;
        count += IsCapturable(ClothPart.Eyebrow, outfit.EyebrowsNumber, out _) ? 1 : 0;
        count += IsCapturable(ClothPart.Glasses, outfit.GlassesNumber, out _) ? 1 : 0;
        count += IsCapturable(ClothPart.Hair, outfit.HairNumber, out _) ? 1 : 0;
        count += IsCapturable(ClothPart.Hat, outfit.HatNumber, out _) ? 1 : 0;
        count += IsCapturable(ClothPart.Headphone, outfit.HeadphoneNumber, out _) ? 1 : 0;
        count += IsCapturable(ClothPart.Mask, outfit.MaskNumber, out _) ? 1 : 0;
        count += IsCapturable(ClothPart.Pants, outfit.PantsNumber, out _) ? 1 : 0;
        count += IsCapturable(ClothPart.Shoes, outfit.ShoesNumber, out _) ? 1 : 0;
        count += IsCapturable(ClothPart.Torso, outfit.TorsoNumber, out _) ? 1 : 0;

        return count;
    }
}
