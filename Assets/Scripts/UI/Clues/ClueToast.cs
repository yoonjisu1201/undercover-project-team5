using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

// 단서를 주운 순간 좌측에서 슬라이드로 나타났다가 잠시 뒤 사라지는 획득 알림.
public sealed class ClueToast : MonoBehaviour
{
    [SerializeField] private RectTransform _card;
    [SerializeField] private TMP_Text _label;

    [Header("현지화 문구")]
    [Tooltip("{0} 에 부위명이 들어간다.")]
    [SerializeField] private LocalizedString _clueAcquired;

    [Tooltip("부위명을 모를 때. {0} 에 단서 번호가 들어간다.")]
    [SerializeField] private LocalizedString _clueAcquiredNumber;

    [Tooltip("단서를 아직 하나도 얻지 않았을 때 보여줄 문구.")]
    [SerializeField] private LocalizedString _clueIdle;
    [SerializeField] private RawImage _thumbnail;

    [Header("슬라이드")]
    [SerializeField] private float _hiddenX = -420f;         // 화면 왼쪽 밖
    [SerializeField] private float _shownX = 24f;
    [SerializeField, Min(0f)] private float _slideDuration = 0.3f;
    [SerializeField, Min(0f)] private float _holdSeconds = 2.5f;
	[SerializeField, Min(0f)] private float _tabRestoreDelay = 0.25f;

    // 알림이 떠 있는 동안에는 같은 자리에 있는 단서 목록 버튼이 비켜 준다. (자리를 주고받는 연출)
    public static event System.Action<bool> ToastVisibilityChanged;

    private PlayerClueBook _boundClueBook;
    private Sequence _sequence;

	private void OnEnable()
	{
		InfoHubController.HubStateChanged += HandleHubStateChanged;
	}

    private void Awake()
    {
        _card.anchoredPosition = new Vector2(_hiddenX, _card.anchoredPosition.y);
		_card.gameObject.SetActive(false);

        // 프리팹에 남아 있던 문구를 대신한다. 이 라벨은 단서를 얻을 때 HandleClueAdded 가 덮어쓰므로
        // LocalizeStringEvent 를 붙이면 서로 지운다. 그래서 첫 문구도 여기서 넣는다.
        if (_label != null)
        {
            _label.text = _clueIdle.GetLocalizedString();
        }
    }

    private void OnDisable()
    {
		StopNotification(true);
		InfoHubController.HubStateChanged -= HandleHubStateChanged;
        UnbindClueBook();
    }

	private void HandleHubStateChanged(bool hubOpen)
	{
		if (!hubOpen)
		{
			return;
		}

		StopNotification(true);
	}

    private void Update()
    {
        // 내 플레이어는 접속 이후에 스폰되므로 매번 최신 것을 확인해 연결한다.
        PlayerClueBook local = PlayerClueBook.Local;
        if (_boundClueBook == local)
        {
            return;
        }

        UnbindClueBook();
        _boundClueBook = local;

        if (_boundClueBook != null)
        {
            _boundClueBook.OnClueAdded += HandleClueAdded;
        }
    }

    private void UnbindClueBook()
    {
        if (_boundClueBook != null)
        {
            _boundClueBook.OnClueAdded -= HandleClueAdded;
            _boundClueBook = null;
        }
    }

    private void HandleClueAdded(int clueNumber)
    {
        ClueModulePreview preview = FindFirstObjectByType<ClueModulePreview>(FindObjectsInactive.Include);

        Texture2D thumbnail = null;
        string partLabel = null;
        bool hasCapture = preview != null && preview.TryGetCapture(clueNumber, out thumbnail, out partLabel);
        if (!hasCapture)
        {
            return;
        }

        // 강조 서식은 코드에 두고 부위명만 인자로 넘긴다. 번역문에 색상 코드가 섞이지 않게 하려는 것이다.
        _label.text = !string.IsNullOrEmpty(partLabel)
            ? _clueAcquired.GetLocalizedString($"<color=#54E6D4>{partLabel}</color>")
            : _clueAcquiredNumber.GetLocalizedString(clueNumber);

        _thumbnail.texture = thumbnail;
        _thumbnail.gameObject.SetActive(thumbnail != null);

		SoundManager.Instance?.Play(SoundKey.Ui_ClueToast);
		PlayNotification();
    }

	private void PlayNotification()
	{
		// 연속으로 주우면 진행 중인 연출을 끊고 새 내용으로 다시 보여준다.
		_sequence?.Kill();
        _card.anchoredPosition = new Vector2(_hiddenX, _card.anchoredPosition.y);
		_card.gameObject.SetActive(true);

        ToastVisibilityChanged?.Invoke(true);

        _sequence = DOTween.Sequence()
            .Append(_card.DOAnchorPosX(_shownX, _slideDuration).SetEase(Ease.OutCubic))
            .AppendInterval(_holdSeconds)
            .Append(_card.DOAnchorPosX(_hiddenX, _slideDuration).SetEase(Ease.InCubic))
			.AppendCallback(() => _card.gameObject.SetActive(false))
			.AppendInterval(_tabRestoreDelay)
			.OnComplete(() =>
			{
				ToastVisibilityChanged?.Invoke(false);
			});
	}

	private void StopNotification(bool restoreTab)
	{
		_sequence?.Kill();
		_sequence = null;
		_card.anchoredPosition = new Vector2(_hiddenX, _card.anchoredPosition.y);
		_card.gameObject.SetActive(false);

		if (restoreTab)
		{
			ToastVisibilityChanged?.Invoke(false);
		}
	}
}
