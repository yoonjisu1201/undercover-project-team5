using System;
using UnityEngine;

[Serializable]
public class InventoryItem
{
    public string ItemName { get; }
    public Sprite sprite; // 아이템의 스프라이트를 저장할 수 있는 필드 추가
    public GameObject WorldPrefab; // 아이템의 프리팹을 저장할 수 있는 필드 추가

    public InventoryItem(string itemName, Sprite icon, GameObject worldPrefab)
    {
        ItemName = itemName;
        sprite = icon;
        WorldPrefab = worldPrefab;
    }
}