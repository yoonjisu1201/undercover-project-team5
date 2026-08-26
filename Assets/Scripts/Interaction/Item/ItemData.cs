using UnityEngine;
using UnityEngine.Localization;

[CreateAssetMenu(menuName = "Inventory/ItemData", fileName = "NewItemData")]
public class ItemData : ScriptableObject
{
    [SerializeField] private ItemType _itemId;
    [SerializeField] private LocalizedString _displayName;
    [SerializeField] private LocalizedString _usageDescription;
    [SerializeField] private Sprite _icon;
    [SerializeField] private GameObject _worldPrefab;
    [SerializeField] private AudioClip _audioClip;

    public ItemType ItemId => _itemId;
    // 반환 타입은 string 그대로 둔다. 이 이름은 줍기 안내·인벤토리·CCTV·미션 안내 등
    // 여러 화면이 문자열로 조립해 쓰고 있어서, 타입을 바꾸면 호출부가 전부 흔들린다.
    public string DisplayName => _displayName.GetLocalizedString();
    // 비워 두면 안내 문구를 붙이지 않는다. 지정하지 않은 아이템은 지금처럼 이름만 보인다.
    public string UsageDescription => _usageDescription.IsEmpty ? null : _usageDescription.GetLocalizedString();
    public Sprite Icon => _icon;
    public GameObject WorldPrefab => _worldPrefab;
    public AudioClip AudioClip => _audioClip;
}
