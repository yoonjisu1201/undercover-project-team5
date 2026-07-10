using System;
using Unity.Services.Multiplayer;
using UnityEngine;

/* 조인코드로만 입장 가능한 세션(방)을 만들고 참가하는 기능.
 * 랜덤 매칭, 빠른 시작(QuickJoin), 공개 세션 목록 조회는 사용하지 않는다.
 */
public class GameSessionManager : MonoBehaviour
{
	public static GameSessionManager Instance { get; private set; }

	[Header("세션 설정")]
	[SerializeField] private int _maxPlayers = 4;

	public ISession CurrentSession { get; private set; }
	public string JoinCode => CurrentSession?.Code;

	public event Action<string> OnSessionCreated; // 조인코드 발급 완료
	public event Action OnSessionJoined;          // 조인코드로 참가 완료
	public event Action<string> OnSessionError;   // 실패 사유 전달

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}
		Instance = this;
		DontDestroyOnLoad(gameObject);
	}

	// 호스트: 방을 만들고 조인코드를 발급받는다.
	public async void CreateSession()
	{
		try
		{
			var options = new SessionOptions
			{
				MaxPlayers = _maxPlayers,
				IsPrivate = true 
			}.WithRelayNetwork(); 

			CurrentSession = await MultiplayerService.Instance.CreateSessionAsync(options);
			OnSessionCreated?.Invoke(CurrentSession.Code);
		}
		catch (Exception e)
		{
			OnSessionError?.Invoke(e.Message);
		}
	}

	// 클라이언트: 조인코드로 방에 참가한다.
	public async void JoinSessionByCode(string joinCode)
	{
		try
		{
			CurrentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode);
			OnSessionJoined?.Invoke();
		}
		catch (Exception e)
		{
			OnSessionError?.Invoke(e.Message);
		}
	}

	public async void LeaveSession()
	{
		if (CurrentSession == null) return;

		await CurrentSession.LeaveAsync();
		CurrentSession = null;
	}
}
