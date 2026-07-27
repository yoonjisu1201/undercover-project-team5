using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public sealed class SubwayRouteMiniGame : MonoBehaviour
{
    private const int SegmentLength = 5;
    private const int MissingStationCount = 1;
    private const int MaximumQuestionCount = 4;

    [SerializeField] private TextAsset _subwayCsv;

    private readonly InputField[] _answerInputs = new InputField[MissingStationCount];
    private readonly string[] _answers = new string[MissingStationCount];

    private Transform _routeMap;
    private Text _progressText;
    private Text _resultTitle;
    private Text _resultMessage;
    private GameObject _resultOverlay;
    private int _questionCount;
    private int _completedQuestionCount;
    private Sprite _circleSprite;
    private Font _font;

    private void Awake()
    {
        CacheUi();
        _questionCount = UnityEngine.Random.Range(1, MaximumQuestionCount + 1);

        if (!TryCreateQuestion())
        {
            ShowResult("데이터 오류", "지하철 노선 CSV를 불러오지 못했습니다.");
        }

        Button closeResultButton = FindChild(transform, "CloseResultButton").GetComponent<Button>();
        closeResultButton.onClick.AddListener(GetComponent<MiniGameUIController>().Close);
    }

    private void CacheUi()
    {
        _routeMap = FindChild(transform, "RouteMap");
        _progressText = FindChild(transform, "ProgressText").GetComponent<Text>();
        _resultOverlay = FindChild(transform, "ResultOverlay").gameObject;
        _resultTitle = FindChild(_resultOverlay.transform, "ResultTitle").GetComponent<Text>();
        _resultMessage = FindChild(_resultOverlay.transform, "ResultMessage").GetComponent<Text>();
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _circleSprite = CreateCircleSprite();

        Transform csvDataPanel = FindChild(transform, "CSVDataPanel");
        csvDataPanel.gameObject.SetActive(false);

        RectTransform routeMapRect = (RectTransform)_routeMap;
        routeMapRect.anchorMin = new Vector2(0.02f, 0.04f);
        routeMapRect.anchorMax = new Vector2(0.98f, 0.95f);
        routeMapRect.offsetMin = Vector2.zero;
        routeMapRect.offsetMax = Vector2.zero;

        FindChild(transform, "Title").GetComponent<Text>().text = "지하철 노선 복구";
        FindChild(transform, "ObjectiveText").GetComponent<Text>().text =
            "노선도에서 비어 있는 역 이름을 입력하세요.";

        foreach (Transform child in _routeMap)
        {
            child.gameObject.SetActive(false);
        }
    }

    private bool TryCreateQuestion()
    {
        ClearCurrentQuestion();

        if (_subwayCsv == null)
        {
            return false;
        }

        List<Route> routes = ParseRoutes(_subwayCsv.bytes);
        if (routes.Count == 0)
        {
            return false;
        }

        Route route = routes[UnityEngine.Random.Range(0, routes.Count)];
        int segmentStart = UnityEngine.Random.Range(0, route.Stations.Count - SegmentLength + 1);
        List<string> stations = route.Stations.GetRange(segmentStart, SegmentLength);

        List<int> missingStationIndices = new()
        {
            UnityEngine.Random.Range(0, SegmentLength)
        };

        Color routeColor = GetRouteColor(route.LineName);
        CreateRouteHeader(route, routeColor);
        CreateRouteDiagram(stations, missingStationIndices, routeColor);
        CreateCheckButton(routeColor);
        UpdateProgress();

        _answerInputs[0].Select();
        _answerInputs[0].ActivateInputField();
        return true;
    }

    private void ClearCurrentQuestion()
    {
        foreach (Transform child in _routeMap)
        {
            if (!child.gameObject.activeSelf)
            {
                continue;
            }

            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        _answerInputs[0] = null;
        _answers[0] = null;
    }

    private void CreateRouteHeader(Route route, Color routeColor)
    {
        Text routeName = CreateText("RouteName", _routeMap, 32, FontStyle.Bold, TextAnchor.MiddleCenter);
        SetAnchors(routeName.rectTransform, new Vector2(0.1f, 0.83f), new Vector2(0.9f, 0.98f));
        routeName.color = routeColor;
        routeName.text = $"{route.Region}  |  {route.LineName}";

        Text guide = CreateText("Guide", _routeMap, 19, FontStyle.Normal, TextAnchor.MiddleCenter);
        SetAnchors(guide.rectTransform, new Vector2(0.1f, 0.72f), new Vector2(0.9f, 0.84f));
        guide.color = new Color(0.76f, 0.83f, 0.87f);
        guide.text = "공개된 노선 구간의 빈 역 이름을 입력하세요.";
    }

    private void CreateRouteDiagram(
        IReadOnlyList<string> stations,
        IReadOnlyList<int> missingStationIndices,
        Color routeColor)
    {
        const float firstNodeX = 0.08f;
        const float lastNodeX = 0.92f;
        const float nodeY = 0.5f;
        float spacing = (lastNodeX - firstNodeX) / (stations.Count - 1);

        for (int index = 0; index < stations.Count - 1; index++)
        {
            float left = firstNodeX + spacing * index;
            float right = firstNodeX + spacing * (index + 1);
            Image line = CreateImage($"Line{index}", _routeMap, routeColor);
            RectTransform lineRect = line.rectTransform;
            lineRect.anchorMin = new Vector2(left, nodeY);
            lineRect.anchorMax = new Vector2(right, nodeY);
            lineRect.offsetMin = new Vector2(14f, -5f);
            lineRect.offsetMax = new Vector2(-14f, 5f);
        }

        int answerIndex = 0;
        for (int stationIndex = 0; stationIndex < stations.Count; stationIndex++)
        {
            float nodeX = firstNodeX + spacing * stationIndex;
            int missingIndex = IndexOf(missingStationIndices, stationIndex);
            bool isMissing = missingIndex >= 0;

            RectTransform node = CreateRect($"Station{stationIndex}", _routeMap);
            node.anchorMin = new Vector2(nodeX, nodeY);
            node.anchorMax = new Vector2(nodeX, nodeY);
            node.sizeDelta = new Vector2(150f, 180f);
            node.anchoredPosition = Vector2.zero;

            CreateStationCircle(node, routeColor, isMissing);

            if (isMissing)
            {
                _answers[answerIndex] = NormalizeAnswer(stations[stationIndex]);
                _answerInputs[answerIndex] = CreateStationInput(node, answerIndex, routeColor);
                answerIndex++;
            }
            else
            {
                Text stationName = CreateText("StationName", node, 18, FontStyle.Bold, TextAnchor.UpperCenter);
                stationName.rectTransform.anchorMin = new Vector2(0f, 0f);
                stationName.rectTransform.anchorMax = new Vector2(1f, 0.3f);
                stationName.rectTransform.offsetMin = Vector2.zero;
                stationName.rectTransform.offsetMax = Vector2.zero;
                stationName.color = Color.white;
                stationName.text = stations[stationIndex];
            }
        }
    }

    private void CreateStationCircle(RectTransform node, Color routeColor, bool isMissing)
    {
        Image outerCircle = CreateImage("StationCircle", node, routeColor);
        outerCircle.sprite = _circleSprite;
        outerCircle.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        outerCircle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        outerCircle.rectTransform.sizeDelta = isMissing ? new Vector2(54f, 54f) : new Vector2(34f, 34f);
        outerCircle.rectTransform.anchoredPosition = Vector2.zero;

        Image innerCircle = CreateImage("StationCenter", outerCircle.transform, Color.white);
        innerCircle.sprite = _circleSprite;
        innerCircle.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        innerCircle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        innerCircle.rectTransform.sizeDelta = isMissing ? new Vector2(38f, 38f) : new Vector2(20f, 20f);
        innerCircle.rectTransform.anchoredPosition = Vector2.zero;

        if (!isMissing)
        {
            return;
        }

    }

    private InputField CreateStationInput(RectTransform node, int answerIndex, Color routeColor)
    {
        GameObject inputObject = new($"Answer{answerIndex}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
        inputObject.transform.SetParent(node, false);

        RectTransform inputRect = inputObject.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0.5f, 0f);
        inputRect.anchorMax = new Vector2(0.5f, 0f);
        inputRect.sizeDelta = new Vector2(138f, 42f);
        inputRect.anchoredPosition = new Vector2(0f, 22f);

        Image background = inputObject.GetComponent<Image>();
        background.color = new Color(0.08f, 0.13f, 0.17f, 1f);

        Text text = CreateText("Text", inputObject.transform, 17, FontStyle.Bold, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform, 8f);
        text.color = Color.white;

        Text placeholder = CreateText("Placeholder", inputObject.transform, 15, FontStyle.Normal, TextAnchor.MiddleCenter);
        Stretch(placeholder.rectTransform, 8f);
        placeholder.color = new Color(routeColor.r, routeColor.g, routeColor.b, 0.72f);
        placeholder.text = $"빈칸 {answerIndex + 1}";

        InputField input = inputObject.GetComponent<InputField>();
        input.textComponent = text;
        input.placeholder = placeholder;
        input.lineType = InputField.LineType.SingleLine;
        input.characterLimit = 12;
        input.onEndEdit.AddListener(_ => CheckAnswers());
        return input;
    }

    private void CreateCheckButton(Color routeColor)
    {
        GameObject buttonObject = new("CheckAnswer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(_routeMap, false);

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.04f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.04f);
        buttonRect.sizeDelta = new Vector2(240f, 52f);
        buttonRect.anchoredPosition = Vector2.zero;

        buttonObject.GetComponent<Image>().color = routeColor;
        buttonObject.GetComponent<Button>().onClick.AddListener(CheckAnswers);

        Text label = CreateText("Label", buttonObject.transform, 20, FontStyle.Bold, TextAnchor.MiddleCenter);
        Stretch(label.rectTransform);
        label.color = new Color(0.03f, 0.06f, 0.08f);
        label.text = "노선 복구 확인";
    }

    private void CheckAnswers()
    {
        bool isCorrect = NormalizeAnswer(_answerInputs[0].text) == _answers[0];
        _answerInputs[0].GetComponent<Image>().color = isCorrect
            ? new Color(0.08f, 0.28f, 0.2f)
            : new Color(0.32f, 0.09f, 0.1f);

        if (!isCorrect)
        {
            return;
        }

        _completedQuestionCount++;
        UpdateProgress();

        if (_completedQuestionCount >= _questionCount)
        {
            ShowResult("노선 복구 완료", $"{_questionCount}개 노선 구간을 모두 복구했습니다.");
            return;
        }

        TryCreateQuestion();
    }

    private void UpdateProgress()
    {
        _progressText.text = $"진행도  {_completedQuestionCount} / {_questionCount}";
    }

    private void ShowResult(string title, string message)
    {
        _resultTitle.text = title;
        _resultMessage.text = message;
        _resultOverlay.SetActive(true);
    }

    private static Color GetRouteColor(string lineName)
    {
        Dictionary<string, Color> routeColors = new()
        {
            ["1호선"] = FromHex("#004A85"),
            ["2호선"] = FromHex("#00A23F"),
            ["3호선"] = FromHex("#ED6C00"),
            ["4호선"] = FromHex("#009BCE"),
            ["5호선"] = FromHex("#794698"),
            ["6호선"] = FromHex("#7C4932"),
            ["7호선"] = FromHex("#6E7E31"),
            ["8호선"] = FromHex("#D11D70"),
            ["9호선"] = FromHex("#A49D87"),
            ["경의중앙"] = FromHex("#6AC2B3"),
            ["수인분당"] = FromHex("#ECA300"),
            ["신분당"] = FromHex("#B81B30"),
            ["인천1호선"] = FromHex("#B4C7E7"),
            ["공항"] = FromHex("#0079AC"),
            ["우이신설"] = FromHex("#BACC50"),
            ["신림선"] = FromHex("#5E7DBB"),
            ["의정부"] = FromHex("#F0831E"),
            ["에버라인"] = FromHex("#44A436"),
            ["인천2호선"] = FromHex("#F4A462"),
            ["김포골드라인"] = FromHex("#957326"),
            ["경춘"] = FromHex("#007A62"),
            ["경강"] = FromHex("#0B318F"),
            ["서해선"] = FromHex("#5EAC41"),
            ["GTX-A"] = FromHex("#9A6292")
        };

        if (routeColors.TryGetValue(lineName, out Color color))
        {
            return color;
        }

        int stableHash = 17;
        foreach (char character in lineName)
        {
            stableHash = stableHash * 31 + character;
        }

        return Color.HSVToRGB(Mathf.Abs(stableHash % 360) / 360f, 0.72f, 0.9f);
    }

    private static Color FromHex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }

    private Sprite CreateCircleSprite()
    {
        const int size = 64;
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
        texture.name = "GeneratedStationCircle";
        texture.wrapMode = TextureWrapMode.Clamp;

        float center = (size - 1) * 0.5f;
        float radiusSquared = center * center;
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float deltaX = x - center;
                float deltaY = y - center;
                pixels[y * size + x] = deltaX * deltaX + deltaY * deltaY <= radiusSquared
                    ? Color.white
                    : Color.clear;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private Text CreateText(string objectName, Transform parent, int fontSize, FontStyle style, TextAnchor alignment)
    {
        GameObject textObject = new(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = _font;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 10;
        text.resizeTextMaxSize = fontSize;
        return text;
    }

    private static Image CreateImage(string objectName, Transform parent, Color color)
    {
        GameObject imageObject = new(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject rectObject = new(objectName, typeof(RectTransform));
        rectObject.transform.SetParent(parent, false);
        return rectObject.GetComponent<RectTransform>();
    }

    private static void SetAnchors(RectTransform rect, Vector2 minimum, Vector2 maximum)
    {
        rect.anchorMin = minimum;
        rect.anchorMax = maximum;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static int IndexOf(IReadOnlyList<int> values, int target)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (values[index] == target)
            {
                return index;
            }
        }

        return -1;
    }

    private static List<Route> ParseRoutes(byte[] csvBytes)
    {
        Encoding encoding;
        try
        {
            encoding = Encoding.GetEncoding(949);
        }
        catch (ArgumentException)
        {
            encoding = Encoding.UTF8;
        }

        string csv = encoding.GetString(csvBytes);
        Dictionary<string, Route> routes = new();

        foreach (string line in csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            string[] columns = line.Split(',');
            if (columns.Length < 6 || !int.TryParse(columns[4].Trim(), out int order))
            {
                continue;
            }

            string region = columns[1].Trim();
            string operatorName = columns[2].Trim();
            string lineName = columns[3].Trim();
            string stationName = RemoveParenthetical(columns[5]);

            bool isSuinBundangLine = operatorName == "코레일" && lineName == "수인분당";
            if ((!IsSeoulOrIncheonOperator(operatorName) && !isSuinBundangLine)
                || string.IsNullOrEmpty(stationName))
            {
                continue;
            }

            string key = $"{region}|{operatorName}|{lineName}";

            if (!routes.TryGetValue(key, out Route route))
            {
                route = new Route(region, lineName);
                routes.Add(key, route);
            }

            route.OrderedStations.Add((order, stationName));
        }

        foreach (Route route in routes.Values)
        {
            route.Stations.AddRange(route.OrderedStations
                .OrderBy(station => station.Order)
                .Select(station => station.Name));
        }

        return routes.Values.Where(route => route.Stations.Count >= SegmentLength).ToList();
    }

    private static string RemoveParenthetical(string stationName)
    {
        string trimmedName = stationName.Trim();
        int parenthesisIndex = trimmedName.IndexOf('(');
        return parenthesisIndex >= 0
            ? trimmedName.Substring(0, parenthesisIndex).Trim()
            : trimmedName;
    }

    private static bool IsSeoulOrIncheonOperator(string operatorName)
    {
        return operatorName == "서울교통공사"
            || operatorName == "서울시메트로9호선주식회사"
            || operatorName == "네오트랜스주식회사"
            || operatorName == "인천교통공사"
            || operatorName == "공항철도주식회사";
    }

    private static string NormalizeAnswer(string answer)
    {
        return string.Concat(answer.Where(character => !char.IsWhiteSpace(character))).Trim();
    }

    private static Transform FindChild(Transform parent, string childName)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
            {
                return child;
            }
        }

        return null;
    }

    private sealed class Route
    {
        public readonly string Region;
        public readonly string LineName;
        public readonly List<(int Order, string Name)> OrderedStations = new();
        public readonly List<string> Stations = new();

        public Route(string region, string lineName)
        {
            Region = region;
            LineName = lineName;
        }
    }
}
