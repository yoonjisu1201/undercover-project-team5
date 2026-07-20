using UnityEngine;

[CreateAssetMenu(
	fileName = "SilhouetteClothData",
	menuName = "Silhouette")]
public class SilhouetteClothData : ScriptableObject {
	public int id;
	public SilhouetteParts Part;
	public Sprite ClothThumbnail;
	public GameObject ClothPrefab;
}