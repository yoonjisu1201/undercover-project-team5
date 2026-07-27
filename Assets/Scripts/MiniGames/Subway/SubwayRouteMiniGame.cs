using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// 문제 생성, CSV 파싱, 정답 판정하는 코드
public sealed partial class SubwayRouteMiniGame : MonoBehaviour
{
    private const int SegmentLength = 5;
    private const int MaximumQuestionCount = 4;

    [SerializeField] private TextAsset _subwayCsv;

    private readonly List<string> _answers = new();
    private readonly List<StationCardDragHandler> _stationCards = new();
    private readonly StationCardDragHandler[] _placedCards = new StationCardDragHandler[SegmentLength];
    private readonly StationDropSlot[] _dropSlots = new StationDropSlot[SegmentLength];

    private Transform _routeMap;
    private Text _progressText;
    private Text _resultTitle;
    private Text _resultMessage;
    private GameObject _resultOverlay;
    private int _questionCount;
    private int _completedQuestionCount;
    private Font _font;

    // UI를 초기화하고 첫 번째 노선 문제를 생성한다.
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

    // 프리팹 UI 참조를 찾고 기존 미리보기 오브젝트를 숨긴다.
    private void CacheUi()
    {
        _routeMap = FindChild(transform, "RouteMap");
        _progressText = FindChild(transform, "ProgressText").GetComponent<Text>();
        _resultOverlay = FindChild(transform, "ResultOverlay").gameObject;
        _resultTitle = FindChild(_resultOverlay.transform, "ResultTitle").GetComponent<Text>();
        _resultMessage = FindChild(_resultOverlay.transform, "ResultMessage").GetComponent<Text>();
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Transform csvDataPanel = FindChild(transform, "CSVDataPanel");
        csvDataPanel.gameObject.SetActive(false);

        RectTransform routeMapRect = (RectTransform)_routeMap;
        routeMapRect.anchorMin = new Vector2(0.02f, 0.04f);
        routeMapRect.anchorMax = new Vector2(0.98f, 0.95f);
        routeMapRect.offsetMin = Vector2.zero;
        routeMapRect.offsetMax = Vector2.zero;

        FindChild(transform, "Title").GetComponent<Text>().text = "지하철 노선 복구";
        FindChild(transform, "ObjectiveText").GetComponent<Text>().text =
            "공개된 1번 역을 기준으로 나머지 역을 올바른 순서에 배치하세요.";

        foreach (Transform child in _routeMap)
        {
            child.gameObject.SetActive(false);
        }
    }

    // CSV에서 무작위 연속 역 구간을 골라 새 문제를 구성한다.
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

        Color routeColor = GetRouteColor(route.LineName);
        CreateRouteHeader(route, routeColor);
        CreateRouteDiagram(stations, routeColor);
        CreateStationCards(stations, routeColor);
        CreateCheckButton(routeColor);
        UpdateProgress();
        return true;
    }

    // 이전 문제에서 동적으로 생성된 UI와 상태를 제거한다.
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

        _answers.Clear();
        _stationCards.Clear();
        Array.Clear(_placedCards, 0, _placedCards.Length);
        Array.Clear(_dropSlots, 0, _dropSlots.Length);
    }

    // 모든 슬롯 입력 여부와 역 순서를 검사해 다음 문제로 진행한다.
    private void CheckAnswers()
    {
        if (_placedCards.Skip(1).Any(card => card == null))
        {
            foreach (StationDropSlot slot in _dropSlots.Skip(1))
            {
                slot.ShowError();
            }
            return;
        }

        bool isCorrect = _placedCards
            .Skip(1)
            .Select(card => NormalizeAnswer(card.StationName))
            .SequenceEqual(_answers.Skip(1));

        Color resultColor = isCorrect
            ? new Color(0.08f, 0.28f, 0.2f)
            : new Color(0.32f, 0.09f, 0.1f);
        foreach (StationCardDragHandler card in _placedCards.Skip(1))
        {
            card.SetBackgroundColor(resultColor);
        }

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

    // 완료한 문제 수를 진행도 문구에 반영한다.
    private void UpdateProgress()
    {
        _progressText.text = $"진행도  {_completedQuestionCount} / {_questionCount}";
    }

    // 결과 오버레이에 제목과 메시지를 표시한다.
    private void ShowResult(string title, string message)
    {
        _resultTitle.text = title;
        _resultMessage.text = message;
        _resultOverlay.SetActive(true);
    }

    // 노선 이름에 대응하는 대표 색상을 반환한다.
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

    // HEX 문자열을 Unity 색상 값으로 변환한다.
    private static Color FromHex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }

    // CSV 원본을 플레이 가능한 노선별 순서 데이터로 변환한다.
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

    // 역 이름 뒤의 괄호형 부가 명칭을 제거한다.
    private static string RemoveParenthetical(string stationName)
    {
        string trimmedName = stationName.Trim();
        int parenthesisIndex = trimmedName.IndexOf('(');
        return parenthesisIndex >= 0
            ? trimmedName.Substring(0, parenthesisIndex).Trim()
            : trimmedName;
    }

    // 미니게임에서 사용할 수도권 운영사인지 확인한다.
    private static bool IsSeoulOrIncheonOperator(string operatorName)
    {
        return operatorName == "서울교통공사"
            || operatorName == "서울시메트로9호선주식회사"
            || operatorName == "네오트랜스주식회사"
            || operatorName == "인천교통공사"
            || operatorName == "공항철도주식회사";
    }

    // 역 이름 비교를 위해 모든 공백을 제거한다.
    private static string NormalizeAnswer(string answer)
    {
        return string.Concat(answer.Where(character => !char.IsWhiteSpace(character))).Trim();
    }

    // 비활성 오브젝트를 포함해 이름이 같은 첫 번째 자식을 찾는다.
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

        // 노선의 기본 지역과 노선명을 저장한다.
        public Route(string region, string lineName)
        {
            Region = region;
            LineName = lineName;
        }
    }
}
