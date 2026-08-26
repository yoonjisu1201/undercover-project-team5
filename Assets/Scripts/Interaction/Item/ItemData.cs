using UnityEngine;
using UnityEngine.Localization;

[CreateAssetMenu(menuName = "Inventory/ItemData", fileName = "NewItemData")]
public class ItemData : ScriptableObject
{
    // 표시 이름과 사용법은 이 에셋에 두지 않는다. 현지화 테이블에서만 관리하고,
    // 여기서는 아이템 종류로 키를 만들어 읽기만 한다. 문구를 고칠 곳이 한 군데로 모인다.
    private const string LocalizationTable = "Language Table";

    [SerializeField] private ItemType _itemId;
    [SerializeField] private Sprite _icon;
    [SerializeField] private GameObject _worldPrefab;
    [SerializeField] private AudioClip _audioClip;

    [Header("=== 오른손에 들었을 때 ===")]
    [SerializeField] private Vector3 _holdPositionOffset;
    [SerializeField] private Vector3 _holdRotationOffset;
    [SerializeField, Range(0.1f, 1f)] private float _holdScale = 1f;

    public ItemType ItemId => _itemId;

    // 테이블 키 규칙: item_display_<ItemType> / item_desc_<ItemType>
    public string DisplayName => Localize("item_display_" + _itemId);

    // 사용법이 없는 아이템은 테이블에 키를 두지 않는다. 그때는 안내 문구를 붙이지 않는다.
    public string UsageDescription => Localize("item_desc_" + _itemId);

    public Sprite Icon => _icon;
    public GameObject WorldPrefab => _worldPrefab;
    public AudioClip AudioClip => _audioClip;
    public Vector3 HoldPositionOffset => _holdPositionOffset;
    public Vector3 HoldRotationOffset => _holdRotationOffset;
    public float HoldScale => _holdScale;

    // 키가 테이블에 없으면 빈 문자열이 온다. 호출부가 "문구 없음"으로 다룰 수 있게 null로 바꿔 돌려준다.
    private static string Localize(string key)
    {
        string text = new LocalizedString(LocalizationTable, key).GetLocalizedString();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
