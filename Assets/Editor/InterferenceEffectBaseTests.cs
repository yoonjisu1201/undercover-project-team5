using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;


public sealed class InterferenceEffectBaseTests
{
    private Scene _previewScene;
    private GameObject _gameObject;
    private FieldVisionInterferenceEvent _effect;


    [SetUp]
    public void SetUp()
    {
        _previewScene = EditorSceneManager.NewPreviewScene();
        _gameObject = new GameObject(nameof(InterferenceEffectBaseTests));
        SceneManager.MoveGameObjectToScene(_gameObject, _previewScene);
        _effect = _gameObject.AddComponent<FieldVisionInterferenceEvent>();
    }


    [TearDown]
    public void TearDown()
    {
        EditorSceneManager.ClosePreviewScene(_previewScene);
    }


    [Test]
    public void Duration_ReturnsSerializedDuration()
    {
        SetBaseField("_duration", 7f);

        Assert.That(_effect.Duration, Is.EqualTo(7f));
    }


    [Test]
    public void Activate_InvokesStartedEvent()
    {
        bool invoked = false;
        GetBaseEvent("_onInterferenceStarted").AddListener(() => invoked = true);

        _effect.Activate();

        Assert.That(invoked, Is.True);
    }


    [Test]
    public void Deactivate_InvokesEndedEvent()
    {
        bool invoked = false;
        GetBaseEvent("_onInterferenceEnded").AddListener(() => invoked = true);

        _effect.Deactivate(InterferenceEndReason.Normal);

        Assert.That(invoked, Is.True);
    }


    private UnityEvent GetBaseEvent(string fieldName)
    {
        FieldInfo field = typeof(InterferenceEffectBase).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        return (UnityEvent)field.GetValue(_effect);
    }


    private void SetBaseField<T>(string fieldName, T value)
    {
        FieldInfo field = typeof(InterferenceEffectBase).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        field.SetValue(_effect, value);
    }
}
