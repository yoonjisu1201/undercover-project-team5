using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Tables;

public class WaitingRoomUIRoomNameTests
{
	[TestCase(
		"Assets/Tables/Language Table_ko.asset",
		"← 튜토리얼 종료     aaa     튜토리얼 시작 →")]
	[TestCase(
		"Assets/Tables/Language Table_en.asset",
		"← TUTORIAL END     aaa     TUTORIAL START →")]
	public void TutorialRoute_FormatsFinalSessionRoomName(string tablePath, string expected)
	{
		StringTable table = AssetDatabase.LoadAssetAtPath<StringTable>(tablePath);
		StringTableEntry entry = table.GetEntry("waiting_room_tutorial_route");

		Assert.That(entry.GetLocalizedString("aaa"), Is.EqualTo(expected));
	}

	[Test]
	public void SetRoomNameArgument_UsesFinalSessionRoomName()
	{
		MethodInfo method = typeof(WaitingRoomUI).GetMethod(
			"SetRoomNameArgument",
			BindingFlags.NonPublic | BindingFlags.Static);

		Assert.That(method, Is.Not.Null, "WaitingRoomUI must bind the final session room name to the localized guide text.");

		var gameObject = new GameObject("RoomNameLocalizerTest");
		try
		{
			var localizer = gameObject.AddComponent<LocalizeStringEvent>();
			const string roomName = "방 1234";

			method.Invoke(null, new object[] { localizer, roomName });

			Assert.That(localizer.StringReference.Arguments, Is.Not.Null);
			Assert.That(localizer.StringReference.Arguments.Count, Is.EqualTo(1));
			Assert.That(localizer.StringReference.Arguments[0], Is.EqualTo(roomName));
		}
		finally
		{
			Object.DestroyImmediate(gameObject);
		}
	}
}
