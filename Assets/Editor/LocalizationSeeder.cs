#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

/// <summary>
/// Language Table(StringTable)에 항목을 코드로 추가/갱신하는 개발용 도구.
/// 메뉴: Tools ▸ Localization ▸ Seed Language Table
/// 아래 Entries 배열만 수정한 뒤 메뉴를 실행하면 en/ko 값이 함께 채워진다.
/// (일회성 도구이므로 다 쓰면 이 파일을 삭제해도 된다.)
/// </summary>
public static class LocalizationSeeder
{
    // 스크린샷의 컬렉션 이름과 동일해야 한다.
    private const string CollectionName = "Language Table";

    // ▼▼▼ 여기만 수정하세요: (Key, English, Korean) ▼▼▼
    private static readonly (string key, string en, string ko)[] Entries =
    {
        ("montage_part_torso",       "Top",       "상의"),
        ("montage_part_arm",         "Arm",       "팔"),
        ("montage_part_pants",       "Pants",     "바지"),
        ("montage_part_shoes",       "Shoes",     "신발"),
        ("montage_part_hair",        "Hair",      "헤어"),
        ("montage_part_hat",         "Hat",       "모자"),
        ("montage_part_glasses",     "Glasses",   "안경"),
        ("montage_part_eyebrow",     "Eyebrow",   "눈썹"),
        ("montage_part_beard",       "Beard",     "수염"),
        ("montage_part_mask",        "Mask",      "마스크"),
        ("montage_part_headphone",   "Headphone", "헤드폰"),
        ("shop_purchase_unavailable",  "Purchase unavailable\n{0}", "구매 불가\n{0}"),
        ("shop_insufficient_credits",  "Not enough credits.",       "돈이 부족합니다."),
    };
    // ▲▲▲ 여기까지 ▲▲▲

    [MenuItem("Tools/Localization/Seed Language Table")]
    public static void Seed()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(CollectionName);
        if (collection == null)
        {
            Debug.LogError($"[LocalizationSeeder] '{CollectionName}' 컬렉션을 찾을 수 없습니다. 이름을 확인하세요.");
            return;
        }

        var shared = collection.SharedData;
        var en = collection.GetTable(new LocaleIdentifier("en")) as StringTable;
        var ko = collection.GetTable(new LocaleIdentifier("ko")) as StringTable;

        if (en == null || ko == null)
        {
            Debug.LogError("[LocalizationSeeder] en/ko StringTable을 찾을 수 없습니다.");
            return;
        }

        int count = 0;
        foreach (var (key, enVal, koVal) in Entries)
        {
            // 공유 키가 없으면 생성 (AddEntry가 내부적으로 처리하지만 명시적으로 보장)
            if (shared.GetEntry(key) == null)
                shared.AddKey(key);

            // 값 설정 (있으면 갱신, 없으면 추가)
            en.AddEntry(key, enVal);
            ko.AddEntry(key, koVal);
            count++;
        }

        EditorUtility.SetDirty(shared);
        EditorUtility.SetDirty(en);
        EditorUtility.SetDirty(ko);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[LocalizationSeeder] 완료: {count}개 항목을 '{CollectionName}'에 반영했습니다.");
    }
}
#endif
