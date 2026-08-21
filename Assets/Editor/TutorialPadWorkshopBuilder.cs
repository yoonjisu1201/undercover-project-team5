#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

/// <summary>
/// Builds the tutorial pad's editable, localized text layer in the workshop scene.
/// </summary>
public static class TutorialPadWorkshopBuilder
{
    private const string TableName = "Language Table";
    private const string CanvasName = "TutorialPadLocalizedCanvas";

    private static readonly (string key, string english, string korean)[] Entries =
    {
        ("tutorial_pad_title", "MISSION BRIEFING", "작전 브리핑"),
        ("tutorial_pad_step_1", "Gather clues in the alien facility.", "외계인 시설에서\n단서를 모으세요"),
        ("tutorial_pad_step_2", "There may be dangers inside.", "시설 내엔\n위험한 게 있을지도?"),
        ("tutorial_pad_step_3", "Find the culprit on the surface.", "지상에서\n범인을 찾으세요"),
        ("tutorial_pad_step_4", "Use the HQ control system.", "본부 내 관제\n시스템을 활용하세요"),
        ("tutorial_pad_guide", "For details, see the Field Agent Guide (H).", "자세한 내용은 현장 요원 가이드(H)을 참고하세요"),
    };

    [MenuItem("Tools/Tutorial Pad/Rebuild Localized Workshop Pad")]
    public static void Rebuild()
    {
        var pad = GameObject.Find("TutorialPad");
        if (pad == null)
        {
            Debug.LogError("[TutorialPad] TutorialPad object was not found in the active scene.");
            return;
        }

        SeedStringTable();

        DestroyChildIfPresent(pad.transform, "TutorialPadDesignSurface");
        DestroyChildIfPresent(pad.transform, CanvasName);

        var canvasObject = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasObject, "Create localized tutorial pad canvas");
        canvasObject.transform.SetParent(pad.transform, false);

        var canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(720f, 480f);
        canvasRect.localPosition = new Vector3(0f, 2.65f, -0.48f);
        canvasRect.localRotation = Quaternion.Euler(0f, 180f, 0f);
        canvasRect.localScale = new Vector3(-0.01f, 0.01f, 0.01f);

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 10;

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/Imported/GUI-BlueSky/ResourcesData/Fonts/NanumSquareRoundOTFEB SDF.asset");
        if (font == null)
        {
            Debug.LogError("[TutorialPad] Korean TMP font asset was not found.");
            Object.DestroyImmediate(canvasObject);
            return;
        }

        ConfigurePhysicalCards(pad.transform);
        CreateLocalizedText(canvasObject.transform, "Title", "tutorial_pad_title", new Vector2(0f, 198f), new Vector2(520f, 52f), 42f, font, TextAlignmentOptions.Center);
        CreateStepsColumn(canvasObject.transform, font);
        CreateLocalizedText(canvasObject.transform, "Guide", "tutorial_pad_guide", new Vector2(0f, -202f), new Vector2(620f, 36f), 20f, font, TextAlignmentOptions.Center);


        EditorUtility.SetDirty(canvasObject);
        EditorSceneManager.MarkSceneDirty(canvasObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[TutorialPad] Localized tutorial pad rebuilt.");
    }

    private static void CreateStepsColumn(Transform parent, TMP_FontAsset font)
    {
        var column = new GameObject("StepsColumn", typeof(RectTransform), typeof(VerticalLayoutGroup));
        column.transform.SetParent(parent, false);

        var rect = column.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(590f, 320f);

        var layout = column.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 6, 6);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        CreateStep(column.transform, "Step01", "1", "tutorial_pad_step_1", font);
        CreateStep(column.transform, "Step02", "2", "tutorial_pad_step_2", font);
        CreateStep(column.transform, "Step03", "3", "tutorial_pad_step_3", font);
        CreateStep(column.transform, "Step04", "4", "tutorial_pad_step_4", font);
    }

    private static void CreateStep(Transform parent, string name, string number, string key, TMP_FontAsset font)
    {
        var step = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
        step.transform.SetParent(parent, false);

        var element = step.GetComponent<LayoutElement>();
        element.minWidth = 570f;
        element.preferredWidth = 570f;
        element.minHeight = 70f;
        element.preferredHeight = 72f;
        element.flexibleHeight = 0f;

        var layout = step.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 4, 4);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        var numberText = CreateStaticText(step.transform, "Number", number, Vector2.zero, Vector2.zero, 34f, font, new Color(0.94f, 0.90f, 0.78f), TextAlignmentOptions.Center);
        var numberElement = numberText.gameObject.AddComponent<LayoutElement>();
        numberElement.minWidth = 72f;
        numberElement.preferredWidth = 72f;
        numberElement.flexibleWidth = 0f;

        var bodyText = CreateLocalizedText(step.transform, "Text", key, Vector2.zero, Vector2.zero, 21f, font, TextAlignmentOptions.Left);
        var bodyElement = bodyText.gameObject.AddComponent<LayoutElement>();
        bodyElement.minWidth = 420f;
        bodyElement.preferredWidth = 450f;
        bodyElement.flexibleWidth = 1f;
    }

    private static void ConfigurePhysicalCards(Transform pad)
    {
        var yPositions = new[] { 3.89f, 3.10f, 2.31f, 1.52f };
        for (var i = 0; i < yPositions.Length; i++)
        {
            var card = pad.Find($"Card0{i + 1}");
            var number = pad.Find($"Number0{i + 1}");
            if (card != null)
            {
                card.localPosition = new Vector3(0f, yPositions[i], -0.24f);
                card.localScale = new Vector3(5.9f, 0.72f, 0.08f);
            }

            if (number != null)
            {
                number.localPosition = new Vector3(-2.55f, yPositions[i], -0.32f);
                number.localScale = new Vector3(0.28f, 0.08f, 0.28f);
            }
        }
    }


    private static TextMeshProUGUI CreateLocalizedText(Transform parent, string name, string key, Vector2 position, Vector2 size, float fontSize, TMP_FontAsset font, TextAlignmentOptions alignment)
    {
        var text = CreateTextObject(parent, name, position, size, fontSize, font, new Color(0.015f, 0.012f, 0.01f), alignment);
        text.text = GetKoreanFallback(key);

        var localizer = text.gameObject.AddComponent<LocalizeStringEvent>();
        localizer.SetTable(TableName);
        localizer.SetEntry(key);
        localizer.OnUpdateString.AddListener(text.SetText);
        return text;
    }


    private static string GetKoreanFallback(string key)
    {
        foreach (var entry in Entries)
        {
            if (entry.key == key)
                return entry.korean;
        }

        return key;
    }

    private static TextMeshProUGUI CreateStaticText(Transform parent, string name, string value, Vector2 position, Vector2 size, float fontSize, TMP_FontAsset font, Color color, TextAlignmentOptions alignment)
    {
        var text = CreateTextObject(parent, name, position, size, fontSize, font, color, alignment);
        text.text = value;
        return text;
    }


    private static TextMeshProUGUI CreateTextObject(Transform parent, string name, Vector2 position, Vector2 size, float fontSize, TMP_FontAsset font, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    private static void SeedStringTable()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(TableName);
        if (collection == null)
        {
            throw new System.InvalidOperationException($"String table collection '{TableName}' was not found.");
        }

        var english = collection.GetTable(new LocaleIdentifier("en")) as StringTable;
        var korean = collection.GetTable(new LocaleIdentifier("ko")) as StringTable;
        if (english == null || korean == null)
        {
            throw new System.InvalidOperationException("English or Korean tutorial pad string table is missing.");
        }

        foreach (var (key, englishValue, koreanValue) in Entries)
        {
            if (collection.SharedData.GetEntry(key) == null)
                collection.SharedData.AddKey(key);

            english.AddEntry(key, englishValue);
            korean.AddEntry(key, koreanValue);
        }

        EditorUtility.SetDirty(collection.SharedData);
        EditorUtility.SetDirty(english);
        EditorUtility.SetDirty(korean);
    }

    private static void DestroyChildIfPresent(Transform parent, string name)
    {
        var child = parent.Find(name);
        if (child == null)
            return;

        Undo.DestroyObjectImmediate(child.gameObject);
    }
}
#endif
