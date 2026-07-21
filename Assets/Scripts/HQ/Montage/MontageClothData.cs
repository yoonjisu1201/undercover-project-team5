using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
	fileName = "MontageClothData",
	menuName = "Montage")]
public class MontageClothData : ScriptableObject {
	public int id;
	public MontageParts Part;
	public Sprite ClothThumbnail;
	public GameObject ClothPrefabs;
}