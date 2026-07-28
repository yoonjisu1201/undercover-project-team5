using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// 문제 생성, 진행 상태, 정답 판정을 관리한다.
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
    private Transform _stationPool;
    private Text _progressText;
    private Text _resultTitle;
    private Text _resultMessage;
    private GameObject _resultOverlay;
    private int _questionCount;
    private int _completedQuestionCount;

    // UI를 연결하고 첫 번째 문제를 생성한다.
    private void Awake()
    {
        CacheUi();
        _questionCount = UnityEngine.Random.Range(1, MaximumQuestionCount + 1);

        if (!TryCreateQuestion())
        {
            ShowResult("데이터 오류", "지하철 노선 CSV를 불러오지 못했습니다.");
        }

    }

    // 프리팹 UI 참조와 버튼 이벤트를 연결한다.
    private void CacheUi()
    {
        _routeMap = FindChild(transform, "RouteMap");
        _progressText = FindChild(transform, "ProgressText").GetComponent<Text>();
        _resultOverlay = FindChild(transform, "ResultOverlay").gameObject;
        _resultTitle = FindChild(_resultOverlay.transform, "ResultTitle").GetComponent<Text>();
        _resultMessage = FindChild(_resultOverlay.transform, "ResultMessage").GetComponent<Text>();

        FindChild(transform, "CSVDataPanel").gameObject.SetActive(false);
        RectTransform routeMapRect = (RectTransform)_routeMap;
        routeMapRect.anchorMin = new Vector2(0.02f, 0.04f);
        routeMapRect.anchorMax = new Vector2(0.98f, 0.95f);
        routeMapRect.offsetMin = routeMapRect.offsetMax = Vector2.zero;

        FindChild(transform, "Title").GetComponent<Text>().text = "지하철 노선 복구";
        FindChild(transform, "ObjectiveText").GetComponent<Text>().text =
            "공개된 1번 역을 기준으로 나머지 역을 올바른 순서에 배치하세요.";

        foreach (string name in new[]
                 {
                     "PreviewLineName", "PreviewGuide", "StaticDragDropPreview",
                     "ResetCardsButton", "CheckAnswer"
                 })
        {
            FindChild(_routeMap, name).gameObject.SetActive(true);
        }

        SetButton("ResetCardsButton", ResetAllCards);
        SetButton("CheckAnswer", CheckAnswers);
    }

    // 지정한 프리팹 버튼에 클릭 동작을 연결한다.
    private void SetButton(string name, UnityEngine.Events.UnityAction action)
    {
        Button button = FindChild(_routeMap, name).GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    // CSV에서 무작위 연속 역 구간을 골라 새 문제를 구성한다.
    private bool TryCreateQuestion()
    {
        ClearCurrentQuestion();
        if (_subwayCsv == null)
        {
            return false;
        }

        List<SubwayRoute> routes = SubwayRouteData.Parse(_subwayCsv.bytes, SegmentLength);
        if (routes.Count == 0)
        {
            return false;
        }

        SubwayRoute route = routes[UnityEngine.Random.Range(0, routes.Count)];
        int start = UnityEngine.Random.Range(0, route.Stations.Count - SegmentLength + 1);
        List<string> stations = route.Stations.GetRange(start, SegmentLength);
        Color routeColor = SubwayRouteData.GetColor(route.LineName);

        SetRouteHeader(route, routeColor);
        SetRouteGraphic(routeColor);
        SetSlots(stations, routeColor);
        SetCards(stations, routeColor);
        RefreshCardPositions();
        UpdateProgress();
        return true;
    }

    // 이전 문제의 카드 배치와 정답 데이터를 초기화한다.
    private void ClearCurrentQuestion()
    {
        _answers.Clear();
        _stationCards.Clear();
        Array.Clear(_placedCards, 0, _placedCards.Length);
        Array.Clear(_dropSlots, 0, _dropSlots.Length);
    }

    // 배치된 카드의 역 순서를 검사한다.
    private void CheckAnswers()
    {
        if (_placedCards.Skip(1).Any(card => card == null)) // 2~5번 슬롯에 카드가 모두 배치되지 않은 경우
        {
            foreach (StationDropSlot slot in _dropSlots.Skip(1))
            {
                slot.ShowError();
            }
            return;
        }

        bool correct = _placedCards.Skip(1).Select(card => NormalizeAnswer(card.StationName)).SequenceEqual(_answers.Skip(1));
        Color resultColor = correct ? new Color(0.08f, 0.28f, 0.2f) : new Color(0.32f, 0.09f, 0.1f);    // 정답이면 초록, 오답이면 빨강

        foreach (StationCardDragHandler card in _placedCards.Skip(1))
        {
            card.SetBackgroundColor(resultColor);
        }

        if (!correct)
        {
            return;
        }

        _completedQuestionCount++;
        UpdateProgress();

        if (_completedQuestionCount >= _questionCount)
        {
            GetComponent<MiniGameUIController>().MarkCompletionReady();
            ShowResult("노선 복구 완료", $"{_questionCount}개 노선 구간을 모두 복구했습니다.");
        }
        else
        {
            TryCreateQuestion();
        }
    }

    // 완료한 문제 수를 진행도 문구에 반영한다.
    private void UpdateProgress()
    {
        _progressText.text = $"진행도  {_completedQuestionCount} / {_questionCount}";
    }

    // 결과 오버레이를 표시한다.
    private void ShowResult(string title, string message)
    {
        _resultTitle.text = title;
        _resultMessage.text = message;
        _resultOverlay.SetActive(true);
    }

    // 역 이름 비교를 위해 공백을 제거한다.
    private static string NormalizeAnswer(string answer)
    {
        return string.Concat(answer.Where(character => !char.IsWhiteSpace(character)));
    }

    // 비활성 오브젝트를 포함해 이름이 같은 자식을 찾는다.
    private static Transform FindChild(Transform parent, string childName)
    {
        return parent.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(child => child.name == childName);
    }
}
