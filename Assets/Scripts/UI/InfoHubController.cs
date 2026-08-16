using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// Tab으로 여는 정보 허브. 화면이 어두워지고 좌측에서 단서 목록 버튼이 밀려 들어온다.
// 버튼을 누르면 아래로 길어지면서 목록이 펼쳐진다.
// 우측 몽타주 TabState의 이동은 MontageShareUI에 위임한다.
// 단서는 인벤토리에 들어가지 않으므로(PlayerClueBook) 단서 열람 창구는 여기뿐이다.
public sealed class InfoHubController : MonoBehaviour, IClosableUi
{
    [Header("Dimmer")]
    [SerializeField] private Image _dimmer;
    [SerializeField, Range(0f, 1f)] private float _dimAlpha = 0.72f;

    [Header("단서 목록 (좌측: 왼쪽에서 오른쪽으로)")]
    [SerializeField] private RectTransform _cluePanel;
    [SerializeField] private Button _clueButton;
    [SerializeField] private RectTransform _clueBody;         // 펼쳐질 영역. 높이 0에서 늘린다
    [SerializeField] private Transform _entryParent;
    [SerializeField] private ClueBookEntry _entryTemplate;    // 비활성 상태로 두는 항목 원본
    [SerializeField] private float _cluePeekX = -300f;        // 닫혀 있을 때 왼쪽에 살짝 걸쳐 둔다
    [SerializeField] private float _clueShownX = 24f;
	[SerializeField] private float _clueRestoreHiddenX = -448f;

    [Header("몽타주 (우측: 오른쪽에서 왼쪽으로)")]
    [SerializeField] private MontageShareUI _montageShareUI;
    [SerializeField] private float _montagePeekX = 300f;      // 닫혀 있을 때 오른쪽에 살짝 걸쳐 둔다
    [SerializeField] private float _montageShownX = -24f;

    [Header("연출")]
	[SerializeField, Min(0f)] private float _slideDuration = 0.35f;
	[SerializeField, Min(0f)] private float _tabRestoreSlideDuration = 0.35f;
	[SerializeField, Min(0f)] private float _buttonTextFadeDuration = 0.14f;
	[SerializeField, Range(0f, 1f)] private float _buttonTextFadeStartRatio = 0.75f;
	[SerializeField, Range(0f, 1f)] private float _autoExpandStartRatio = 0.82f;
    [SerializeField, Min(0f)] private float _expandDuration = 0.28f;
    [SerializeField, Min(0f)] private float _entryStagger = 0.05f;   // 항목이 하나씩 들어오는 간격

    // 다른 캔버스에 있는 허브 버튼(몽타주 알림 카드)이 같은 타이밍에 Dimmer 위로 올라오도록 알린다.
    public static event Action<bool> HubStateChanged;
    public static bool IsHubOpen { get; private set; }

    private readonly List<ClueBookEntry> _entries = new();
    private CustomInputActions _actions;
    private PlayerClueBook _boundClueBook;
    private Tween _clueSlide;
    private Tween _expand;
    private Tween _fade;
	private Tween _buttonTextSwitch;
	private Tween _buttonTextFade;
	private Tween _autoExpand;
    private bool _isOpen;
    private bool _isClueExpanded;
	private GameObject _clueIdleText;
	private GameObject _clueCopy;
	private GameObject _montageIdleText;
    private GameObject _montageCopy;
	private CanvasGroup _clueIdleTextGroup;
	private CanvasGroup _clueCopyGroup;
	private CanvasGroup _montageIdleTextGroup;
	private CanvasGroup _montageCopyGroup;

    private void Awake()
    {
        _entryTemplate.gameObject.SetActive(false);
		EnsureMontageShareUI();
		CacheButtonTextStates();
		ResetClosedTabState();
    }

    private void OnEnable()
    {
        _actions ??= new CustomInputActions();
        _actions.Enable();
        _clueButton.onClick.AddListener(ToggleClueList);
        ClueToast.ToastVisibilityChanged += HandleToastVisibilityChanged;

		if (!_isOpen)
		{
			ResetClosedTabState();
		}
    }

    private void OnDisable()
    {
        _actions?.Disable();
        KillTweens();
        SetButtonTextStates(false);
        _clueButton.onClick.RemoveListener(ToggleClueList);
        ClueToast.ToastVisibilityChanged -= HandleToastVisibilityChanged;
        UnbindClueBook();

        if (_isOpen)
        {
            // 씬 전환 등으로 꺼질 때 커서 카운트와 허브 상태가 남지 않도록 정리한다.
            SetOpenState(false);
            GameplayUiMode.Instance?.UnregisterUi(this);
            GameplayUiMode.Instance?.DeactivateCursor();
        }
    }

    private void Update()
    {
        // 내 플레이어는 접속 이후에 스폰되므로 매번 최신 것을 확인해 연결한다.
        BindClueBookIfNeeded();

        if (_actions.UI.Montage.WasPressedThisFrame())
        {
            if (_isOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }
    }

    private void BindClueBookIfNeeded()
    {
        PlayerClueBook local = PlayerClueBook.Local;
        if (_boundClueBook == local)
        {
            return;
        }

        UnbindClueBook();
        _boundClueBook = local;

        if (_boundClueBook != null)
        {
            _boundClueBook.OnClueBookChanged += HandleClueBookChanged;
        }
    }

    private void UnbindClueBook()
    {
        if (_boundClueBook != null)
        {
            _boundClueBook.OnClueBookChanged -= HandleClueBookChanged;
            _boundClueBook = null;
        }
    }

    // 목록을 펼쳐 둔 채로 단서를 새로 주우면 즉시 반영한다.
    private void HandleClueBookChanged()
    {
        if (_isOpen && _isClueExpanded)
        {
            RebuildEntries();
            PlayExpandTo(GetExpandedHeight());
        }
    }

    private void Open()
    {
        SetOpenState(true);
		_buttonTextSwitch?.Kill();
		SetButtonTextStates(true);

        GameplayUiMode.Instance?.RegisterUi(this);
        GameplayUiMode.Instance?.ActivateCursor();

        _fade?.Kill();
        _dimmer.gameObject.SetActive(true);
        Color from = _dimmer.color;
        from.a = 0f;
        _dimmer.color = from;
        _fade = _dimmer.DOFade(_dimAlpha, 0.2f).SetEase(Ease.OutQuad);

        // 단서 목록은 왼쪽에서, 몽타주 카드는 오른쪽에서 밀려 들어온다.
		_cluePanel.gameObject.SetActive(true);
		ApplyPanelX(_cluePanel, _cluePeekX);
        _clueSlide?.Kill();
		_clueSlide = _cluePanel.DOAnchorPosX(_clueShownX, _slideDuration).SetEase(Ease.OutCubic);

		EnsureMontageShareUI();
		_montageShareUI?.ShowTabState(_montagePeekX, _montageShownX, _slideDuration);
		_autoExpand?.Kill();
		_autoExpand = DOVirtual.DelayedCall(_slideDuration * _autoExpandStartRatio, ExpandHubPanels);
    }

    // ESC(스택)와 Tab 모두 이 경로로 닫는다.
    public void Close()
    {
        if (!_isOpen)
        {
            return;
        }

        SetOpenState(false);
		CollapseClueList();

		_buttonTextSwitch?.Kill();
		_autoExpand?.Kill();
		float textSwitchDelay = _slideDuration * _buttonTextFadeStartRatio;
		_buttonTextSwitch = DOVirtual.DelayedCall(textSwitchDelay, PlayButtonTextCloseFade);

        GameplayUiMode.Instance?.UnregisterUi(this);
        GameplayUiMode.Instance?.DeactivateCursor();

        _fade?.Kill();
        _fade = _dimmer.DOFade(0f, 0.15f).SetEase(Ease.InQuad)
            .OnComplete(() => _dimmer.gameObject.SetActive(false));

		// 열어 둔 몽타주 창도 Tab으로 같이 닫는다.
        if (_montageShareUI == null)
        {
            _montageShareUI = FindFirstObjectByType<MontageShareUI>(FindObjectsInactive.Include);
        }

        _montageShareUI?.Close();

        // 열어 둔 단서 창도 Tab으로 같이 닫는다.
        FindFirstObjectByType<ClueUI>(FindObjectsInactive.Include)?.Close();

		float buttonExitDelay = _montageShareUI?.HideTabState(_montagePeekX, _slideDuration) ?? 0f;

		// 목록은 먼저 접고, 좌우 버튼은 같은 시점부터 함께 퇴장시킨다.
		_clueSlide?.Kill();
		_clueSlide = DOTween.Sequence()
			.AppendInterval(buttonExitDelay)
			.Append(_cluePanel.DOAnchorPosX(_cluePeekX, _slideDuration).SetEase(Ease.InCubic))
			.OnComplete(() =>
			{
				SetBodyHeight(0f);
				ApplyPanelX(_cluePanel, _cluePeekX);
			});
    }

    // 단서 목록 버튼: 버튼 아래가 길어지면서 목록이 나온다.
    private void ToggleClueList()
    {
		if (!_isOpen)
		{
			return;
		}

		if (_isClueExpanded)
        {
			CollapseClueList();
            return;
        }

		ExpandClueList();
    }

	private void ExpandClueList()
	{
		if (!_isOpen || _isClueExpanded)
		{
			return;
		}

		SetBodyHeight(0f);
		RebuildEntries();
		_isClueExpanded = true;
		PlayExpandTo(GetExpandedHeight());
		PlayEntriesIn();
	}

	private void ExpandHubPanels()
	{
		if (!_isOpen)
		{
			return;
		}

		ExpandClueList();
		EnsureMontageShareUI();
		_montageShareUI?.Expand();
	}

	private void CollapseClueList()
    {
        if (_isClueExpanded)
        {
            foreach (ClueBookEntry entry in _entries)
            {
                entry.PlayOut();
            }
        }

        _isClueExpanded = false;
		PlayExpandTo(0f);
    }

    // 레이아웃이 자리를 잡은 다음 프레임에 항목을 하나씩 들여보낸다.
    private void PlayEntriesIn()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_entryParent);

        for (int i = 0; i < _entries.Count; i++)
        {
            _entries[i].PlayIn(i * _entryStagger);
        }
    }

	private void PlayExpandTo(float height)
    {
        _expand?.Kill();
		_expand = _clueBody.DOSizeDelta(new Vector2(_clueBody.sizeDelta.x, height), _expandDuration)
			.SetEase(height > 0f ? Ease.OutCubic : Ease.InCubic);
    }

    private void SetBodyHeight(float height)
    {
        _clueBody.sizeDelta = new Vector2(_clueBody.sizeDelta.x, height);
    }

    // 항목 수로 높이를 직접 계산하면 레이아웃 그룹의 여백·간격 설정과 어긋나서
    // 위아래 공간이 짝이 안 맞는다. 레이아웃이 실제로 요구하는 높이를 그대로 쓴다.
    private float GetExpandedHeight()
    {
        if (_entries.Count == 0)
        {
            return 0f;
        }

        RectTransform entryParentRect = (RectTransform)_entryParent;
        LayoutRebuilder.ForceRebuildLayoutImmediate(entryParentRect);
        return LayoutUtility.GetPreferredHeight(entryParentRect);
    }

	// 단서 쪽은 ExpandedState 하나가 접힌 탭과 펼친 목록을 모두 담당한다.
	// 알림 중에는 숨기고, 알림이 완전히 끝난 뒤 왼쪽에서 원래 대기 위치로 복귀시킨다.
    private void HandleToastVisibilityChanged(bool toastVisible)
    {
        if (_isOpen)
        {
            return;
        }

        _clueSlide?.Kill();
		if (toastVisible)
		{
			_cluePanel.gameObject.SetActive(false);
			return;
		}

		ApplyPanelX(_cluePanel, _clueRestoreHiddenX);
		_cluePanel.gameObject.SetActive(true);
		_clueSlide = _cluePanel
			.DOAnchorPosX(_cluePeekX, _tabRestoreSlideDuration)
			.SetEase(Ease.OutCubic);
    }

    private void SetOpenState(bool open)
    {
        _isOpen = open;
        IsHubOpen = open;
		SetClueButtonClickable(open);
        HubStateChanged?.Invoke(open);
    }

	private void CacheButtonTextStates()
	{
		Transform clueBackground = _cluePanel.Find("Background");
		Transform montageTab = _montageShareUI != null
			? _montageShareUI.transform.Find("TabState")
			: null;
		Transform montageBackground = montageTab != null ? montageTab.Find("Background") : null;

		// 단서 ExpandedState 안에서 접힌 상태와 Tab 상태의 텍스트만 교체한다.
		_clueIdleText = FindChild(clueBackground, "IdleText", "IdelText");
		_clueCopy = FindChild(clueBackground, "Copy");
		_montageIdleText = FindChild(montageBackground, "IdleText");
		_montageCopy = FindChild(montageBackground, "Copy");
		_clueIdleTextGroup = GetOrAddCanvasGroup(_clueIdleText);
		_clueCopyGroup = GetOrAddCanvasGroup(_clueCopy);
		_montageIdleTextGroup = GetOrAddCanvasGroup(_montageIdleText);
		_montageCopyGroup = GetOrAddCanvasGroup(_montageCopy);

		if (_clueIdleText == null)
		{
			Debug.LogWarning("[InfoHubController] 단서 ExpandedState의 IdleText를 찾지 못했습니다.");
		}
	}

	private void SetButtonTextStates(bool hubOpen)
	{
		_buttonTextFade?.Kill();
		SetTextPairImmediate(_clueIdleText, _clueIdleTextGroup, _clueCopy, _clueCopyGroup, hubOpen);
		SetTextPairImmediate(_montageIdleText, _montageIdleTextGroup, _montageCopy, _montageCopyGroup, hubOpen);
	}

	private void PlayButtonTextCloseFade()
	{
		_buttonTextFade?.Kill();
		Sequence fade = DOTween.Sequence();
		AddCloseTextFade(fade, _clueIdleText, _clueIdleTextGroup, _clueCopy, _clueCopyGroup);
		AddCloseTextFade(fade, _montageIdleText, _montageIdleTextGroup, _montageCopy, _montageCopyGroup);
		_buttonTextFade = fade.OnComplete(() =>
		{
			_clueCopy?.SetActive(false);
			_montageCopy?.SetActive(false);
		});
	}

	private void AddCloseTextFade(
		Sequence fade,
		GameObject idleText,
		CanvasGroup idleGroup,
		GameObject copy,
		CanvasGroup copyGroup)
	{
		if (idleText == null || idleGroup == null || copy == null || copyGroup == null)
		{
			idleText?.SetActive(true);
			copy?.SetActive(false);
			return;
		}

		idleText.SetActive(true);
		copy.SetActive(true);
		idleGroup.alpha = 0f;
		copyGroup.alpha = 1f;
		fade.Join(idleGroup.DOFade(1f, _buttonTextFadeDuration).SetEase(Ease.OutQuad));
		fade.Join(copyGroup.DOFade(0f, _buttonTextFadeDuration).SetEase(Ease.InQuad));
	}

	private static void SetTextPairImmediate(
		GameObject idleText,
		CanvasGroup idleGroup,
		GameObject copy,
		CanvasGroup copyGroup,
		bool hubOpen)
	{
		if (idleGroup != null)
		{
			idleGroup.alpha = 1f;
		}
		if (copyGroup != null)
		{
			copyGroup.alpha = 1f;
		}

		idleText?.SetActive(!hubOpen);
		copy?.SetActive(hubOpen);
	}

	private static CanvasGroup GetOrAddCanvasGroup(GameObject target)
	{
		if (target == null)
		{
			return null;
		}

		CanvasGroup group = target.GetComponent<CanvasGroup>();
		return group != null ? group : target.AddComponent<CanvasGroup>();
	}

	private void EnsureMontageShareUI()
	{
		if (_montageShareUI == null)
		{
			_montageShareUI = FindFirstObjectByType<MontageShareUI>(FindObjectsInactive.Include);
		}
	}

	private static GameObject FindChild(Transform parent, params string[] names)
	{
		if (parent == null)
		{
			return null;
		}

		foreach (string name in names)
		{
			Transform child = parent.Find(name);
			if (child != null)
			{
				return child.gameObject;
			}
		}

		return null;
	}

	private void SetClueButtonClickable(bool clickable)
	{
		_clueButton.interactable = clickable;

		if (_clueButton.targetGraphic != null)
		{
			_clueButton.targetGraphic.raycastTarget = clickable;
		}
	}

	private void ResetClosedTabState()
	{
		KillTweens();
		_isOpen = false;
		_isClueExpanded = false;
		IsHubOpen = false;
		SetBodyHeight(0f);
		_cluePanel.gameObject.SetActive(true);
		ApplyPanelX(_cluePanel, _cluePeekX);
		_dimmer.gameObject.SetActive(false);
		SetClueButtonClickable(false);
		SetButtonTextStates(false);
		EnsureMontageShareUI();
		_montageShareUI?.HideTabState(_montagePeekX, 0f);
	}

    private static void ApplyPanelX(RectTransform panel, float x)
    {
        panel.anchoredPosition = new Vector2(x, panel.anchoredPosition.y);
    }

    private void KillTweens()
    {
        _clueSlide?.Kill();
        _expand?.Kill();
		_fade?.Kill();
		_buttonTextSwitch?.Kill();
		_buttonTextFade?.Kill();
		_autoExpand?.Kill();
    }

    private void RebuildEntries()
    {
        foreach (ClueBookEntry entry in _entries)
        {
            Destroy(entry.gameObject);
        }

        _entries.Clear();

        ClueModulePreview preview = FindFirstObjectByType<ClueModulePreview>(FindObjectsInactive.Include);

        if (_boundClueBook != null)
        {
            foreach (int clueNumber in _boundClueBook.ClueNumbers)
            {
                int captured = clueNumber;
                Texture2D thumbnail = null;
                string partLabel = null;
                if (preview == null || !preview.TryGetCapture(captured, out thumbnail, out partLabel))
                {
                    continue;
                }

                AddEntry($"단서 {captured}", partLabel ?? string.Empty, thumbnail, () => ShowClue(captured));
            }
        }

        if (_entries.Count == 0)
        {
            AddEntry("아직 획득한 단서가 없습니다", string.Empty, null, null);
        }
    }

    private void AddEntry(string label, string subLabel, Texture thumbnail, Action onClick)
    {
        ClueBookEntry entry = Instantiate(_entryTemplate, _entryParent);
        entry.gameObject.SetActive(true);
        entry.Bind(label, subLabel, thumbnail, onClick);
        _entries.Add(entry);
    }

    // 단서 UI는 씬에 하나뿐인 오브젝트라 프리팹이 참조를 들고 있을 수 없다. 열 때 찾는다.
    private static void ShowClue(int clueNumber)
    {
        ClueUI clueUi = FindFirstObjectByType<ClueUI>(FindObjectsInactive.Include);
        if (clueUi == null)
        {
            Debug.LogError("[InfoHubController] 씬에서 단서 UI(ClueUI)를 찾지 못했습니다.");
            return;
        }

        clueUi.ShowClue(clueNumber);
    }
}
