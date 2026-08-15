using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 단서를 주운 순간 좌측에서 슬라이드로 나타났다가 잠시 뒤 사라지는 획득 알림.
public sealed class ClueToast : MonoBehaviour
{
    [SerializeField] private RectTransform _card;
    [SerializeField] private TMP_Text _label;
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

        _label.text = !string.IsNullOrEmpty(partLabel)
            ? $"단서 획득 — <color=#54E6D4>{partLabel}</color>"
            : $"단서 {clueNumber} 획득";

        _thumbnail.texture = thumbnail;
        _thumbnail.gameObject.SetActive(thumbnail != null);

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
