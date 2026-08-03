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
public class OperationGuidePageFlipper : MonoBehaviour
{
    [Header("페이지 (논리 순서대로: Page_01 ~ Page_05)")]
    [SerializeField] private List<RectTransform> _pages = new List<RectTransform>();

    [Header("네비게이션 버튼")]
    [SerializeField] private Button _topPrevious;   // 위 버튼 = 이전
    [SerializeField] private Button _bottomNext;    // 아래 버튼 = 다음

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

    private void Awake()
    {
        // 첫 페이지만 펼친 상태로 두고 나머지는 회전 초기화 후 꺼둔다.
        for (int i = 0; i < _pages.Count; i++)
        {
            if (_pages[i] == null) continue;
            _pages[i].localEulerAngles = Vector3.zero;
            _pages[i].gameObject.SetActive(i == _index);
        }
        if (_pageUpIndicator != null) _pageUpIndicator.SetActive(_index > 0);
        UpdateButtons();
    }

    // 버튼 onClick은 인스펙터에서 위=GoPrevious / 아래=GoNext로 연결한다.

    private void OnDisable()
    {
        _flip?.Kill();
    }

    // 다음 페이지: 현재 페이지(맨 앞)가 위로 접혀 올라가며 뒤의 다음 페이지를 드러낸다.
    public void GoNext()
    {
        if (IsFlipping() || _index >= _pages.Count - 1) return;

        RectTransform current = _pages[_index];
        RectTransform incoming = _pages[_index + 1];
        _index++;

        // 다음 페이지를 뒤에 평평하게 깔아둔다.
        incoming.gameObject.SetActive(true);
        incoming.localEulerAngles = Vector3.zero;

        _flip = current.DOLocalRotate(new Vector3(-_flipAngle, 0f, 0f), _flipDuration)
            .SetEase(_liftEase)
            .OnUpdate(() => RevealPageUpDuringLift(current))
            .OnComplete(() =>
            {
                current.gameObject.SetActive(false);
                current.localEulerAngles = Vector3.zero; // 되돌아올 때를 위해 복구
                _flip = null;
                UpdateButtons();
            });

        UpdateButtons();
    }

    // 이전 페이지: 이전 페이지(맨 앞)가 접힌 상태에서 아래로 펴지며 현재 페이지를 덮는다.
    public void GoPrevious()
    {
        if (IsFlipping() || _index <= 0) return;

        RectTransform current = _pages[_index];
        RectTransform incoming = _pages[_index - 1];
        _index--;

        incoming.gameObject.SetActive(true);
        incoming.localEulerAngles = new Vector3(-_flipAngle, 0f, 0f); // 접힌 상태에서 시작

        _flip = incoming.DOLocalRotate(Vector3.zero, _flipDuration)
            .SetEase(_dropEase)
            .OnUpdate(() => HidePageUpDuringDrop(incoming))
            .OnComplete(() =>
            {
                current.gameObject.SetActive(false);
                _flip = null;
                UpdateButtons();
            });

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
}
