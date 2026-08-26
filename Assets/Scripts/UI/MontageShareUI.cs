using System.Collections.Generic;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;

public class MontageShareUI : MonoBehaviour, IClosableUi
{
	[Header("=== MontageShareManager 등록 ===")]
	[SerializeField] private MontageShareManager _montageShareManager;

	[Header("=== 접혔을 때의 UI와 열렸을 때의 UI ===")]
	[SerializeField] private GameObject _notificationUI;
	[SerializeField] private GameObject _tabUI;
	[SerializeField] private GameObject _expandedUI;
	[SerializeField] private RectTransform _expandedBody;
	[SerializeField] private RectTransform _expandedContent;

	[Header("=== 접혔을 때 보일 Text UI ===")]
	[SerializeField] private LocalizeStringEvent _stateText;

	[Header("=== 열었을 때 보이는 뱃지 텍스트 ===")]
	[SerializeField] private LocalizeStringEvent _sentSecondsAgoText;

	[Header("=== 전송된 적이 없다면, RawImage를 비활성화해두기 위해 저장 ===")]
	[SerializeField] private GameObject _montageImage;

	[Header("=== 내부에서 사용할 LocalizationText ===")]
	[SerializeField] private LocalizedString _noMontageShared;
	[SerializeField] private LocalizedString _newMontageShared;
	[SerializeField] private LocalizedString _montageAlreadyViewed;
	[SerializeField] private LocalizedString _sentSecondsAgo;

	[Header("=== 펼침 연출 ===")]
	// 단서 목록처럼 접힌 카드 높이에서 시작해 아래로 늘어나며 펼쳐진다. (확대/축소가 아니다)
	[SerializeField, Min(0f)] private float _slideDuration = 0.24f;
	[SerializeField, Min(0f)] private float _compactRevealOffset = 120f;

	[Header("=== 몽타주 갱신 알림 ===")]
	[SerializeField] private float _notificationHiddenX = 460f;
	[SerializeField] private float _notificationShownX = -24f;
	[SerializeField, Min(0f)] private float _notificationSlideDuration = 0.3f;
	[SerializeField, Min(0f)] private float _notificationHoldSeconds = 2.5f;

	public bool IsExpanded => _expandedUI != null && _expandedUI.activeSelf;

	// 내가 확인하지 않은 새 몽타주가 있는가?
	private bool _isMontageRenewed = false;

	private float _expandedRestHeight;
	private CanvasGroup _expandedContentGroup;
	private Vector2 _expandedContentRestPosition;
	private Tween _slide;
	private Tween _compactSlide;
	private Sequence _notificationSequence;
	private bool _isNotificationPlaying;
	// 미션 패널처럼 밖에서 Tab 카드를 숨겨둔 상태. 알림이 끝나도 이 상태를 덮어쓰지 않는다.
	private bool _isTabCardAllowed = true;

	private void Awake()
	{
		// 기존 프리팹의 NotificationState를 TabState로 이름 변경한 뒤 새 NotificationState를 추가했으므로,
		// 이전 SerializedField 참조를 신뢰하지 않고 현재 계층 이름으로 두 상태를 명확히 구분한다.
		GameObject notificationState = transform.Find("NotificationState")?.gameObject;
		GameObject tabState = transform.Find("TabState")?.gameObject;
		if (notificationState != null)
		{
			_notificationUI = notificationState;
		}
		if (tabState != null)
		{
			_tabUI = tabState;
		}

		if (_expandedBody != null)
		{
			_expandedRestHeight = _expandedBody.sizeDelta.y;
		}

		if (_expandedContent != null)
		{
			_expandedContentRestPosition = _expandedContent.anchoredPosition;
			_expandedContentGroup = _expandedContent.GetComponent<CanvasGroup>();
		}

		ResetToCompactState();
	}

	private void OnEnable()
	{
		InfoHubController.HubStateChanged += HandleHubStateChanged;

		// 상태 문구는 UpdateUiState 가 StringReference 를 갈아끼우는 방식이라, 상태가 바뀌지 않는 한
		// 다시 계산되지 않는다. 그래서 언어를 바꿔도 이미 표시된 문구는 그대로 남는다.
		// 언어 변경 때마다, 그리고 꺼져 있는 동안 언어가 바뀐 경우를 위해 켜질 때도 다시 적용한다.
		LocalizationSettings.SelectedLocaleChanged += HandleLocaleChanged;
		if (_montageShareManager != null)
		{
			UpdateUiState();
		}
	}
	private void OnDisable()
	{
		InfoHubController.HubStateChanged -= HandleHubStateChanged;
		LocalizationSettings.SelectedLocaleChanged -= HandleLocaleChanged;
		StopNotification();
		_slide?.Kill();
		_compactSlide?.Kill();
		GameplayUiMode.Instance?.UnregisterUi(this);    // 펼친 채로 비활성화될 때 스택 정리
	}

	private void Start()
	{
		RoundManager.Instance.OnRoundStarted += OnRoundStarted;
		_montageShareManager.OnMontageStateChanged += HandleMontageStateChanged;

		UpdateUiState();
	}

	public void OnDestroy()
	{
		RoundManager.Instance.OnRoundStarted -= OnRoundStarted;
		_montageShareManager.OnMontageStateChanged -= HandleMontageStateChanged;
	}

	private void Update()
	{
		// Tab은 InfoHubController가 받는다. 몽타주는 허브의 버튼으로 연다.
		if (_sentSecondsAgoText == null || _montageShareManager == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
		{
			return;
		}

		_sentSecondsAgoText.StringReference = _sentSecondsAgo;
		_sentSecondsAgoText.StringReference.Arguments = new List<object> { (int)(NetworkManager.Singleton.ServerTime.Time - _montageShareManager.LastSharedTime.Value) };
		_sentSecondsAgoText.RefreshString();
	}

	// 패널을 열거나 닫을 때 사용. 패널을 열거나 닫으면 새 메세지 상태 갱신
	private void TogglePanelState(bool playOpenSound = true)
	{
		bool willExpand = !_expandedUI.activeSelf;

		// 접을 때는 다 줄어든 뒤에 카드를 되돌린다. (PlayExpandSlide의 OnComplete)
		if (willExpand)
		{
			SetCompactCardsActive(false, false);
		}

		PlayExpandSlide(willExpand);

		// 펼쳐졌을 때만 ESC 닫기 스택에 등록한다. (접힌 HUD 상태는 ESC 대상이 아님)
		if (willExpand)
		{
			GameplayUiMode.Instance?.RegisterUi(this, playOpenSound);
		}
		else
		{
			GameplayUiMode.Instance?.UnregisterUi(this);
		}

		_isMontageRenewed = false;

		UpdateUiState();
	}

	// 카드 높이에서 시작해 아래로 늘어나며 펼쳐지고, 접을 때는 다시 카드 높이로 말려 올라간다.
	// (단서 목록이 버튼 아래로 펼쳐지는 것과 같은 방식이다)
	private void PlayExpandSlide(bool willExpand)
	{
		_slide?.Kill();

		if (_expandedBody == null || _expandedContent == null || _expandedContentGroup == null)
		{
			_expandedUI.gameObject.SetActive(willExpand);
			if (!willExpand)
			{
				RefreshCompactState();
			}
			return;
		}

		if (willExpand)
		{
			_expandedUI.gameObject.SetActive(true);
			SetExpandedHeight(0f);
			_expandedContentGroup.alpha = 1f;
			_expandedContent.anchoredPosition = _expandedContentRestPosition;

			Sequence open = DOTween.Sequence()
				.Join(DOTween.To(GetExpandedHeight, SetExpandedHeight, _expandedRestHeight, _slideDuration).SetEase(Ease.OutCubic));

			_slide = open;
			return;
		}

		// Body 높이만 줄여 버튼 아래에서 위로 접히게 한다.
		Sequence close = DOTween.Sequence()
			.Join(DOTween.To(GetExpandedHeight, SetExpandedHeight, 0f, _slideDuration).SetEase(Ease.InCubic));

		_slide = close.OnComplete(() =>
		{
			_expandedUI.gameObject.SetActive(false);
			SetExpandedHeight(_expandedRestHeight);
			ResetContent();
			RefreshCompactState();
		});
	}

	// 다음 연출을 위해 내용물의 위치와 투명도를 제자리로 되돌린다.
	private void ResetContent()
	{
		_expandedContentGroup.alpha = 1f;
		_expandedContent.anchoredPosition = _expandedContentRestPosition;
	}

	private void ResetToCompactState()
	{
		_slide?.Kill();

		if (_expandedBody != null)
		{
			SetExpandedHeight(_expandedRestHeight);
		}

		if (_expandedContentGroup != null)
		{
			ResetContent();
		}

		_expandedUI.SetActive(false);
		RefreshCompactState();
	}

	private void HandleHubStateChanged(bool hubOpen)
	{
		if (hubOpen)
		{
			StopNotification();
		}

		if (!IsExpanded)
		{
			RefreshCompactState();
		}
	}

	public void ShowTabState(float hiddenX, float shownX, float duration)
	{
		StopNotification();
		if (IsExpanded || _tabUI == null)
		{
			return;
		}

		SetCompactCardsActive(false, true);
		RectTransform tabRect = _tabUI.transform as RectTransform;
		tabRect.anchoredPosition = new Vector2(hiddenX, tabRect.anchoredPosition.y);

		_compactSlide?.Kill();
		_compactSlide = tabRect.DOAnchorPosX(shownX, duration).SetEase(Ease.OutCubic);
	}

	public float HideTabState(float hiddenX, float duration)
	{
		if (_tabUI == null)
		{
			return 0f;
		}

		RectTransform tabRect = _tabUI.transform as RectTransform;
		_compactSlide?.Kill();

		if (!IsExpanded || _expandedBody == null)
		{
			_compactSlide = tabRect.DOAnchorPosX(hiddenX, duration).SetEase(Ease.InCubic);
			return 0f;
		}

		float currentHeight = GetExpandedHeight();
		float revealHeight = tabRect.rect.height + _compactRevealOffset;
		float revealDelay = GetCollapseDelayAtHeight(currentHeight, revealHeight);

		_compactSlide = DOTween.Sequence()
			.AppendInterval(revealDelay)
			.AppendCallback(() => SetCompactCardsActive(false, true))
			.Append(tabRect.DOAnchorPosX(hiddenX, duration).SetEase(Ease.InCubic));

		return revealDelay;
	}

	private float GetCollapseDelayAtHeight(float startHeight, float targetHeight)
	{
		if (_slideDuration <= 0f || startHeight <= 0f || targetHeight >= startHeight)
		{
			return 0f;
		}

		// 접힘 트윈의 Ease.InCubic 진행률을 역산해 버튼 높이와 여유 공간이 남는 순간을 구한다.
		float normalizedHeight = Mathf.Clamp01(targetHeight / startHeight);
		float normalizedTime = Mathf.Pow(1f - normalizedHeight, 1f / 3f);
		return _slideDuration * normalizedTime;
	}

	private void RefreshCompactState()
	{
		bool showNotification = _isNotificationPlaying && !InfoHubController.IsHubOpen;
		SetCompactCardsActive(showNotification, !showNotification && _isTabCardAllowed);
	}

	private void PlayNotification()
	{
		if (InfoHubController.IsHubOpen || IsExpanded)
		{
			return;
		}

		StopNotification();
		_compactSlide?.Kill();

		RectTransform notificationRect = _notificationUI != null
			? _notificationUI.transform as RectTransform
			: null;
		if (notificationRect == null)
		{
			Debug.LogError("[MontageShareUI] NotificationState의 RectTransform을 찾지 못했습니다.", this);
			RefreshCompactState();
			return;
		}

		_isNotificationPlaying = true;
		SetCompactCardsActive(true, false);

		notificationRect.anchoredPosition = new Vector2(_notificationHiddenX, notificationRect.anchoredPosition.y);
		_notificationSequence = DOTween.Sequence()
			.Append(notificationRect.DOAnchorPosX(_notificationShownX, _notificationSlideDuration).SetEase(Ease.OutCubic))
			.AppendInterval(_notificationHoldSeconds)
			.Append(notificationRect.DOAnchorPosX(_notificationHiddenX, _notificationSlideDuration).SetEase(Ease.InCubic))
			// 알림이 나가면 그대로 끝낸다. Tab 카드를 다시 밀어 넣으면 화면 구석에 칩이 남는다.
			.AppendCallback(() => SetCompactCardsActive(false, false))
			.OnComplete(() =>
			{
				_isNotificationPlaying = false;
			});
	}

	private void StopNotification()
	{
		_notificationSequence?.Kill();
		_notificationSequence = null;
		_isNotificationPlaying = false;
	}

	// 미니게임 화면 위에 Tab 안내가 겹치지 않도록 밖에서 숨긴다.
	// 알림(NotificationState)은 건드리지 않아 미션 중에도 새 몽타주는 알려준다.
	public void SetTabCardVisible(bool visible)
	{
		_isTabCardAllowed = visible;

		if (_tabUI != null)
		{
			_tabUI.SetActive(visible);
		}
	}

	private void SetCompactCardsActive(bool notificationActive, bool tabActive)
	{
		if (_notificationUI != null)
		{
			_notificationUI.SetActive(notificationActive);
		}
		if (_tabUI != null)
		{
			_tabUI.SetActive(tabActive && _isTabCardAllowed);
		}
	}

	private float GetExpandedHeight() => _expandedBody.sizeDelta.y;

	private void SetExpandedHeight(float height)
	{
		_expandedBody.sizeDelta = new Vector2(_expandedBody.sizeDelta.x, height);
	}

	// 정보 허브의 몽타주 버튼에서 호출한다. 이미 펼쳐져 있으면 그대로 둔다.
	// 허브가 Tab으로 함께 펼칠 때는 열림음을 내지 않는다. Tab 한 번에 소리가 두 번 나기 때문이다.
	public void Expand(bool playOpenSound = true)
	{
		if (!_expandedUI.activeSelf)
		{
			TogglePanelState(playOpenSound);
		}
	}

	// ESC 등으로 닫으면 펼쳐진 패널을 접는다. (IClosableUi)
	public void Close()
	{
		if (_expandedUI.activeSelf)
		{
			TogglePanelState();
		}
	}


	// 새 라운드가 시작되면, UI 관련 정보 모두 없앤다.
	private void OnRoundStarted(int round)
	{
		_isMontageRenewed = false;
		StopNotification();
		ResetToCompactState();

		UpdateUiState();
	}

	// ShareManager를 구독하면서 본부가 몽타주 공유하면 바로 renewed값 갱신
	private void HandleMontageStateChanged(MontageState state)
	{
		_isMontageRenewed = true;

		UpdateUiState();
		if (!IsExpanded)
		{
			PlayNotification();
		}
	}

	private void HandleLocaleChanged(Locale locale) => UpdateUiState();

	private void UpdateUiState()
	{
		// 1. 몽타주가 전송된 적이 없다? 그럼 전송된 적 없다는 메세지 출력 및 몽타주 이미지 비활성화해두기
		if (!_montageShareManager.IsMontageShared.Value)
		{
			_montageImage.SetActive(false);
			_stateText.StringReference = _noMontageShared;
			_stateText.RefreshString();

			return;
		}

		// 위에 걸리지 않았으면 몽타주 전송된 것. 몽타주 이미지 활성화한다
		_montageImage.SetActive(true);

		// 확인하지 않은 새 몽타주가 있는 경우 메세지
		if (_isMontageRenewed)
		{
			_stateText.StringReference = _newMontageShared;
		}
		// 확인하지 않은 새 몽타주가 없는 경우 메세지
		else
		{
			_stateText.StringReference = _montageAlreadyViewed;
		}

		// 메시지 적용
		_stateText.RefreshString();
	}
}
