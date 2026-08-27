using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 작전 가이드(클립보드)의 페이지를 실제 종이를 넘기듯 상단 집게 기준으로 넘긴다.
// 페이지는 같은 위치에 겹쳐 있고 1번이 맨 앞(형제 순서상 마지막)에 렌더된다.
// 피벗이 상단 중앙(0.5, 1)이라 X축 회전만으로 집게를 축으로 접혔다 펴진다.
//   - 다음(BottomNext) : 현재 페이지가 위로 접혀 올라가며 다음 페이지를 드러낸다.
//   - 이전(TopPrevious): 이전 페이지가 위에서 펴져 내려와 현재 페이지를 덮는다.
[DisallowMultipleComponent]
public class GuideBook : MonoBehaviour, IClosableUi
{
    private readonly string _headerText = "요원 가이드북";
    private readonly string _subtitleText = "요원들의 활동을 지원하기 위해 제공된 문서";

    [Header("=== 가이드북 열리면 사라져야 할 UI들 ===")]
    [SerializeField] private List<GameObject> _uisToHide;
    
    [Header("페이지 (논리 순서대로: Page_01 ~ Page_05)")]
    [SerializeField] private List<GuideBookPage> _pages = new List<GuideBookPage>();

    [Header("네비게이션 버튼")]
    [SerializeField] private Button _topPrevious;   // 위 버튼 = 이전
    [SerializeField] private Button _bottomNext;    // 아래 버튼 = 다음

    [Header("목차 탭")]
    [SerializeField] private RectTransform _navigationTabContainer;
    [SerializeField] private GuideBookNavigationTab _navigationTabPrefab;
    [SerializeField] private List<string> _navigationTitles = new List<string>();

    [Header("장식")]
    [SerializeField] private GameObject _pageUpIndicator; // 첫 페이지가 아닐 때만 보이는 넘긴 종이 표시
    [SerializeField, Range(0f, 1f)] private float _pageUpRevealFrac = 0.6f; // 페이지가 이 비율만큼 젖혀져 위로 올라오면 Page_Up 표시

    [Header("애니메이션")]
    [SerializeField] private float _flipDuration = 0.45f;
    [SerializeField, Range(80f, 160f)] private float _flipAngle = 110f; // 상단 집게 기준 접히는 각도
    [SerializeField] private Ease _liftEase = Ease.InQuad;   // 종이가 접혀 올라갈 때
    [SerializeField] private Ease _dropEase = Ease.OutQuad;  // 종이가 펴져 내려올 때

    private int _index;
    private Tween _flip;
    private CustomInputActions _actions;
    private bool _initialized;
    private readonly List<GuideBookNavigationTab> _navigationTabs = new List<GuideBookNavigationTab>();
    
    public event Action OnClose;

    private void Awake()
    {
        InitializePages();
        InitializeNavigationTabs();
        UpdateButtons();
    }

    private void InitializePages()
    {
        if (_initialized) return;
        _initialized = true;

        // 열리는 순간 RectTransform 회전을 강제로 건드리면 일부 빌드에서 네이티브 크래시가 날 수 있어,
        // 초기화 시에는 활성 페이지만 정하고 회전은 실제 페이지 전환 때만 조정한다.
        for (int i = 0; i < _pages.Count; i++)
        {
            if (_pages[i] == null) continue;
            _pages[i].gameObject.SetActive(i == _index);
            _pages[i].Initialize(
                _headerText,
                _subtitleText,
                (uint)i + 1,
                (uint)_pages.Count
            );
        }

        if (_pageUpIndicator != null) _pageUpIndicator.SetActive(_index > 0);
    }

    private void InitializeNavigationTabs()
    {
        if (_navigationTabContainer == null || _navigationTabPrefab == null) return;

        for (int i = 0; i < _pages.Count; i++)
        {
            GuideBookNavigationTab tab = Instantiate(_navigationTabPrefab, _navigationTabContainer);
            string title = i < _navigationTitles.Count ? _navigationTitles[i] : string.Empty;
            tab.Initialize(i, title, GoToPage);
            _navigationTabs.Add(tab);
        }

        UpdateNavigationTabs();
    }

    // 버튼 onClick은 인스펙터에서 위=GoPrevious / 아래=GoNext로 연결한다.

    private void OnEnable()
    {
        InitializePages();

        GameplayUiMode.Instance?.RegisterUi(this);
        GameplayUiMode.Instance?.ActivateCursor();

        _actions ??= new CustomInputActions();
        _actions.Enable();
        
        // 숨길 UI 꺼주기
        foreach (var ui  in _uisToHide) {
            if (ui == null) continue;
			ui.SetActive(false);
        }
    }

    private void OnDisable()
    {
        _flip?.Kill();  // 페이지 넘기는 중에 꺼지면 Tween이 남아있어도 화면에 표시되지 않으므로 강제 종료
        GameplayUiMode.Instance?.UnregisterUi(this);
        GameplayUiMode.Instance?.DeactivateCursor();
        _actions?.Disable();
        
        // 숨길 UI 켜주기
        foreach (var ui  in _uisToHide) {
            if (ui == null) continue;
            ui.SetActive(true);
        }
    }

    // 가이드북도 E로 닫는다. 창이 열려 있는 동안에는 플레이어 상호작용이 잠기므로 E가 겹치지 않는다.
    private void Update()
    {
        if (_actions == null)
        {
            return;
        }

        // 이제 가이드북 아이템 아니다! 그래서 E로 닫는 것은 막고, H(여는 키)로만 닫히게 함
        if (_actions.UI.OpenGuideBook.WasPressedThisFrame()) {
            Close();
        }
    }

    // 다음 페이지: 현재 페이지(맨 앞)가 위로 접혀 올라가며 뒤의 다음 페이지를 드러낸다.
    public void GoNext()
    {
        GoToPage(_index + 1);
    }

    // 이전 페이지: 이전 페이지(맨 앞)가 접힌 상태에서 아래로 펴지며 현재 페이지를 덮는다.
    public void GoPrevious()
    {
        GoToPage(_index - 1);
    }

    public void GoToPage(int targetIndex)
    {
        if (IsFlipping() || targetIndex < 0 || targetIndex >= _pages.Count || targetIndex == _index) return;

        RectTransform current = _pages[_index].RectTransform;
        RectTransform incoming = _pages[targetIndex].RectTransform;
        bool movingForward = targetIndex > _index;
        _index = targetIndex;

        if (movingForward)
        {
            incoming.gameObject.SetActive(true);
            incoming.localRotation = Quaternion.identity;

            _flip = current.DOLocalRotate(new Vector3(-_flipAngle, 0f, 0f), _flipDuration)
                .SetEase(_liftEase)
                .OnUpdate(() => RevealPageUpDuringLift(current))
                .OnComplete(() =>
                {
                    current.gameObject.SetActive(false);
                    current.localRotation = Quaternion.identity;
                    _flip = null;
                    UpdateButtons();
                });

            UpdateNavigationTabs();
            UpdateButtons();
            return;
        }

        incoming.gameObject.SetActive(true);
        incoming.localRotation = Quaternion.Euler(-_flipAngle, 0f, 0f); // 접힌 상태에서 시작

        _flip = incoming.DOLocalRotate(Vector3.zero, _flipDuration)
            .SetEase(_dropEase)
            .OnUpdate(() => HidePageUpDuringDrop(incoming))
            .OnComplete(() =>
            {
                current.gameObject.SetActive(false);
                _flip = null;
                UpdateButtons();
            });

        UpdateNavigationTabs();
        UpdateButtons();
    }

    private bool IsFlipping() => _flip != null && _flip.IsActive() && _flip.IsPlaying();

    // 넘기는 중이거나 양 끝 페이지에서는 해당 버튼을 눌러도 소용없으므로 잠근다.
    private void UpdateButtons()
    {
        bool busy = IsFlipping();
        if (_topPrevious != null) _topPrevious.interactable = !busy && _index > 0;                 // 위=이전
        if (_bottomNext != null) _bottomNext.interactable = !busy && _index < _pages.Count - 1;    // 아래=다음
    }

    private void UpdateNavigationTabs()
    {
        for (int i = 0; i < _navigationTabs.Count; i++)
        {
            _navigationTabs[i].SetSelected(i == _index);
        }
    }

    // 다음으로 넘길 때: 현재 페이지가 위로 충분히 젖혀져(TopPrevious 위치까지 올라와) 있으면 Page_Up을 켠다.
    private void RevealPageUpDuringLift(RectTransform lifting)
    {
        if (_pageUpIndicator == null || _pageUpIndicator.activeSelf || _index <= 0) return;
        if (Quaternion.Angle(Quaternion.identity, lifting.localRotation) >= _flipAngle * _pageUpRevealFrac)
            _pageUpIndicator.SetActive(true);
    }

    // 첫 페이지로 되돌아올 때: 내려오는 페이지가 그 위치 아래로 내려가면 Page_Up을 끈다.
    private void HidePageUpDuringDrop(RectTransform dropping)
    {
        if (_pageUpIndicator == null || !_pageUpIndicator.activeSelf || _index != 0) return;
        if (Quaternion.Angle(Quaternion.identity, dropping.localRotation) <= _flipAngle * _pageUpRevealFrac)
            _pageUpIndicator.SetActive(false);
    }

    public void Show()
    {
        gameObject.SetActive(true);
    }

    public void Close()
    {
        gameObject.SetActive(false);
        OnClose?.Invoke();
    }

    // X 버튼을 눌러 ui를 닫는다
    public void OnButtonClick()
    {
        Close();
    }
}
