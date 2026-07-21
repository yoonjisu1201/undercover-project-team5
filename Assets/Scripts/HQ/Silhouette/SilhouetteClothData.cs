using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
	fileName = "SilhouetteClothData",
	menuName = "Montage")]
public class SilhouetteClothData : ScriptableObject {
	public int id;
	public SilhouetteParts Part;
	public Sprite ClothThumbnail;
	public GameObject ClothPrefabs;
}