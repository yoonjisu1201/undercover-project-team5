using System;
using System.Linq;
using System.Reflection;
using DG.Tweening;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class WaitingRoomWorldInteractionTests
{
    private const string WaitingRoomScenePath = "Assets/Scenes/WaitingRoom.unity";
    private const string BatteryMissionDisplayPrefabPath =
        "Assets/Prefabs/WaitingRoom/TutorialLayoutTemp/04_AlternatingGallery/ImportedCopies/MS_04_BreakerRepair_Display.prefab";

    private static readonly string[] LayoutPrefabPaths =
    {
        "Assets/Prefabs/WaitingRoom/TutorialLayoutTemp/03_SkillIslands/Created/03_SkillIslands.prefab",
        "Assets/Prefabs/WaitingRoom/TutorialLayoutTemp/04_AlternatingGallery/Created/04_AlternatingGallery.prefab",
        "Assets/Prefabs/WaitingRoom/TutorialLayoutTemp/05_DemoClassroom/Created/05_DemoClassroom.prefab",
    };

    private static readonly string[] EmissionMaterialPaths =
    {
        "Assets/Prefabs/WaitingRoom/TutorialLayoutTemp/03_SkillIslands/Materials/MAT_ReadyBlue.mat",
        "Assets/Prefabs/WaitingRoom/TutorialLayoutTemp/03_SkillIslands/Materials/MAT_NicknameNeutral.mat",
        "Assets/Prefabs/WaitingRoom/TutorialLayoutTemp/04_AlternatingGallery/Materials/MAT_ReadyBlue.mat",
        "Assets/Prefabs/WaitingRoom/TutorialLayoutTemp/04_AlternatingGallery/Materials/MAT_NicknameNeutral.mat",
        "Assets/Prefabs/WaitingRoom/TutorialLayoutTemp/05_DemoClassroom/Materials/MAT_ReadyBlue.mat",
        "Assets/Prefabs/WaitingRoom/TutorialLayoutTemp/05_DemoClassroom/Materials/MAT_NicknameNeutral.mat",
    };

    [Test]
    public void WorldInteractionTypes_UseExistingInteractableContract()
    {
        Type buttonBaseType = RequireType("WaitingRoomButtonBase");
        Type readyButtonType = RequireType("ReadyStartWorldButton");
        Type nicknameButtonType = RequireType("NicknameWorldButton");
        Type exitType = RequireType("WaitingRoomExitInteractable");

        Assert.That(buttonBaseType.IsAbstract, Is.True);
        Assert.That(buttonBaseType.IsSubclassOf(typeof(InteractableBase)), Is.True);
        Assert.That(readyButtonType.IsSubclassOf(buttonBaseType), Is.True);
        Assert.That(nicknameButtonType.IsSubclassOf(buttonBaseType), Is.True);
        Assert.That(exitType.IsSubclassOf(typeof(InteractableBase)), Is.True);

        MethodInfo interactMethod = buttonBaseType.GetMethod(
            nameof(InteractableBase.Interact),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.That(interactMethod, Is.Not.Null);
        Assert.That(interactMethod.IsFinal, Is.True);

        Assert.That(
            typeof(WaitingRoomUI).GetMethod("HandleLeaveButtonClicked", BindingFlags.Instance | BindingFlags.Public),
            Is.Not.Null);
        Assert.That(
            typeof(WaitingRoomUI).GetMethod("InteractReadyStart", BindingFlags.Instance | BindingFlags.Public),
            Is.Not.Null);
        Assert.That(typeof(WaitingRoomUI).GetEvent("ReadyStartStateChanged"), Is.Not.Null);
    }

    [Test]
    public void WaitingRoomButton_InteractPressesAndReturns()
    {
        GameObject roomObject = new GameObject("WaitingRoomUI_Test");
        GameObject stationObject = new GameObject("Station_Test");
        GameObject buttonObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        buttonObject.transform.SetParent(stationObject.transform);
        Vector3 releasedPosition = new Vector3(1f, 2f, 3f);
        Quaternion localRotation = Quaternion.Euler(-12f, 0f, 0f);
        buttonObject.transform.localPosition = releasedPosition;
        buttonObject.transform.localRotation = localRotation;

        TestWaitingRoomButton button = null;
        Tween pressTween = null;

        try
        {
            roomObject.AddComponent<WaitingRoomUI>();
            button = stationObject.AddComponent<TestWaitingRoomButton>();

            SerializedObject serializedButton = new SerializedObject(button);
            SerializedProperty buttonTransformProperty =
                serializedButton.FindProperty("_buttonTransform");
            Assert.That(buttonTransformProperty, Is.Not.Null);
            buttonTransformProperty.objectReferenceValue = buttonObject.transform;
            serializedButton.ApplyModifiedPropertiesWithoutUndo();

            button.InitializeForTest();

            button.Interact(null);

            FieldInfo tweenField = typeof(WaitingRoomButtonBase).GetField(
                "_pressTween",
                BindingFlags.Instance | BindingFlags.NonPublic);
            pressTween = (Tween)tweenField.GetValue(button);
            pressTween.Goto(0.08f, false);

            Vector3 expectedPressedPosition =
                releasedPosition + localRotation * Vector3.back * 0.025f;

            Assert.That(button.ActionCount, Is.EqualTo(1));
            Assert.That(stationObject.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(
                Vector3.Distance(buttonObject.transform.localPosition, expectedPressedPosition),
                Is.LessThan(0.0001f));

            pressTween.Complete();

            Assert.That(
                Vector3.Distance(buttonObject.transform.localPosition, releasedPosition),
                Is.LessThan(0.0001f));
        }
        finally
        {
            pressTween?.Kill();
            UnityEngine.Object.DestroyImmediate(stationObject);
            UnityEngine.Object.DestroyImmediate(roomObject);
        }
    }

    [Test]
    public void WaitingRoomScene_HidesCursorAndProvidesWorldInteractionPrompt()
    {
        Scene scene = SceneManager.GetSceneByPath(WaitingRoomScenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;

        if (!wasLoaded)
        {
            scene = EditorSceneManager.OpenScene(WaitingRoomScenePath, OpenSceneMode.Additive);
        }

        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            SceneCursorSettings cursorSettings = roots
                .SelectMany(root => root.GetComponentsInChildren<SceneCursorSettings>(true))
                .Single();
            InteractionPromptUI promptUI = roots
                .SelectMany(root => root.GetComponentsInChildren<InteractionPromptUI>(true))
                .SingleOrDefault();
            GameObject exitDoor = FindInRoots(roots, "Double Door");
            Type exitType = RequireType("WaitingRoomExitInteractable");
            Type outlinableType = RequireType("EPOOutline.Outlinable");

            Assert.That(cursorSettings.CursorVisibleByDefault, Is.False);
            Assert.That(promptUI, Is.Not.Null);
            Assert.That(FindInRoots(roots, "StartButtonArea").activeSelf, Is.False);
            Assert.That(FindInRoots(roots, "exit").activeSelf, Is.False);
            Assert.That(exitDoor.GetComponent(exitType), Is.Not.Null);
            Assert.That(exitDoor.GetComponent(outlinableType), Is.Not.Null);
        }
        finally
        {
            if (!wasLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    [Test]
    public void WaitingRoomPlayerInstantiation_UsesWaitingRoomSpawnPointPose()
    {
        Scene scene = SceneManager.GetSceneByPath(WaitingRoomScenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;

        if (!wasLoaded)
        {
            scene = EditorSceneManager.OpenScene(WaitingRoomScenePath, OpenSceneMode.Additive);
        }

        GameObject playerPrefab = new GameObject("PlayerPrefab_Test");
        GameObject playerInstance = null;

        try
        {
            Transform spawnPoint = FindInRoots(
                scene.GetRootGameObjects(),
                "WaitingRoomSpawnPoint").transform;
            MethodInfo instantiateMethod = typeof(GameSessionManager).GetMethod(
                "InstantiatePlayerAtWaitingRoomSpawn",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(instantiateMethod, Is.Not.Null);

            playerInstance = (GameObject)instantiateMethod.Invoke(
                null,
                new object[] { playerPrefab });

            Assert.That(
                Vector3.Distance(playerInstance.transform.position, spawnPoint.position),
                Is.LessThan(0.0001f));
            Assert.That(
                Quaternion.Angle(playerInstance.transform.rotation, spawnPoint.rotation),
                Is.LessThan(0.0001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(playerInstance);
            UnityEngine.Object.DestroyImmediate(playerPrefab);

            if (!wasLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    [TestCaseSource(nameof(LayoutPrefabPaths))]
    public void LayoutPrefab_HasReadyNicknameAndExitInteractions(string prefabPath)
    {
        Type readyButtonType = RequireType("ReadyStartWorldButton");
        Type nicknameButtonType = RequireType("NicknameWorldButton");
        Type outlinableType = RequireType("EPOOutline.Outlinable");
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

        try
        {
            GameObject readyStation = FindChild(root, "ReadyStartStation");
            GameObject nicknameStation = FindChild(root, "NicknameChangeStation");
            GameObject readyButton = FindChild(root, "GalleryButton_READY_START");
            GameObject nicknameButton = FindChild(root, "GalleryButton_NICKNAME");
            GameObject statusLamp = FindChild(root, "StatusLamp_Cylinder_Lamp07");

            Component readyComponent = readyStation.GetComponent(readyButtonType);
            Component nicknameComponent = nicknameStation.GetComponent(nicknameButtonType);

            Assert.That(readyComponent, Is.Not.Null);
            Assert.That(readyStation.GetComponent(outlinableType), Is.Not.Null);
            Assert.That(readyButton.GetComponent(readyButtonType), Is.Null);
            Assert.That(readyButton.GetComponent(outlinableType), Is.Null);

            Assert.That(nicknameComponent, Is.Not.Null);
            Assert.That(nicknameStation.GetComponent(outlinableType), Is.Not.Null);
            Assert.That(nicknameButton.GetComponent(nicknameButtonType), Is.Null);
            Assert.That(nicknameButton.GetComponent(outlinableType), Is.Null);

            foreach (Collider collider in readyStation.GetComponentsInChildren<Collider>(true))
            {
                Assert.That(collider.GetComponentInParent(readyButtonType, true), Is.SameAs(readyComponent));
            }

            foreach (Collider collider in nicknameStation.GetComponentsInChildren<Collider>(true))
            {
                Assert.That(collider.GetComponentInParent(nicknameButtonType, true), Is.SameAs(nicknameComponent));
            }

            SerializedObject readySerializedObject = new SerializedObject(readyComponent);
            SerializedProperty readyButtonTransformProperty =
                readySerializedObject.FindProperty("_buttonTransform");
            SerializedProperty readyButtonRendererProperty =
                readySerializedObject.FindProperty("_buttonRenderer");
            SerializedProperty lampProperty =
                readySerializedObject.FindProperty("_statusLampRenderer");

            Assert.That(readyButtonTransformProperty, Is.Not.Null);
            Assert.That(readyButtonRendererProperty, Is.Not.Null);
            Assert.That(readyButtonTransformProperty.objectReferenceValue, Is.SameAs(readyButton.transform));
            Assert.That(readyButtonRendererProperty.objectReferenceValue, Is.SameAs(readyButton.GetComponent<Renderer>()));
            Assert.That(lampProperty.objectReferenceValue, Is.SameAs(statusLamp.GetComponent<Renderer>()));

            AssertColor(
                readySerializedObject.FindProperty("_inactiveBaseColor").colorValue,
                new Color(0.025f, 0.16f, 0.7f, 1f));
            AssertColor(
                readySerializedObject.FindProperty("_inactiveEmissionColor").colorValue,
                new Color(0.05f, 1.2f, 7f, 1f));
            AssertColor(
                readySerializedObject.FindProperty("_activeBaseColor").colorValue,
                new Color(0.7f, 0.025f, 0.015f, 1f));
            AssertColor(
                readySerializedObject.FindProperty("_activeEmissionColor").colorValue,
                new Color(7f, 0.08f, 0.03f, 1f));

            SerializedProperty nicknameButtonTransformProperty =
                new SerializedObject(nicknameComponent).FindProperty("_buttonTransform");
            Assert.That(nicknameButtonTransformProperty, Is.Not.Null);
            Assert.That(nicknameButtonTransformProperty.objectReferenceValue, Is.SameAs(nicknameButton.transform));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [TestCaseSource(nameof(EmissionMaterialPaths))]
    public void WorldButtonMaterial_HasEmissionEnabled(string materialPath)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        Assert.That(material, Is.Not.Null);
        Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True);
    }

    [Test]
    public void WaitingRoomBatteryMissionDisplay_IsNonInteractiveDisplayPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BatteryMissionDisplayPrefabPath);
        Assert.That(prefab, Is.Not.Null);

        GameObject root = PrefabUtility.LoadPrefabContents(BatteryMissionDisplayPrefabPath);

        try
        {
            Assert.That(root.name, Is.EqualTo("MS_04_BreakerRepair_Display"));
            Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<NetworkBehaviour>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<InteractableBase>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void LayoutPrefab_PlacesBatteryMissionDisplayOnBatteryExhibit()
    {
        const string layoutPrefabPath =
            "Assets/Prefabs/WaitingRoom/TutorialLayoutTemp/04_AlternatingGallery/Created/04_AlternatingGallery.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(layoutPrefabPath);

        try
        {
            GameObject batteryExhibit = FindChild(root, "Battery_Exhibit");
            GameObject missionDisplay = FindChild(root, "MS_04_BreakerRepair_Display");

            Assert.That(missionDisplay.transform.parent.gameObject, Is.SameAs(batteryExhibit));
            Assert.That(missionDisplay.transform.localPosition.y, Is.GreaterThan(0.24f));
            Assert.That(Mathf.Abs(missionDisplay.transform.localPosition.x), Is.LessThan(1.2f));
            Assert.That(Mathf.Abs(missionDisplay.transform.localPosition.z), Is.LessThan(1.2f));
            Assert.That(
                Quaternion.Angle(
                    missionDisplay.transform.localRotation,
                    Quaternion.Euler(0f, 180f, 0f)),
                Is.LessThan(0.01f));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Type RequireType(string fullName)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(fullName))
            .FirstOrDefault(candidate => candidate != null);

        Assert.That(type, Is.Not.Null, $"{fullName} 타입이 아직 구현되지 않았습니다.");
        return type;
    }

    private static GameObject FindInRoots(GameObject[] roots, string objectName)
    {
        GameObject result = roots
            .Select(root => FindChild(root, objectName))
            .FirstOrDefault(candidate => candidate != null);

        Assert.That(result, Is.Not.Null, $"{objectName} 오브젝트를 찾지 못했습니다.");
        return result;
    }

    private static GameObject FindChild(GameObject root, string objectName)
    {
        Transform match = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(child => child.name == objectName);
        return match == null ? null : match.gameObject;
    }

    private static void AssertColor(Color actual, Color expected)
    {
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
        Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
    }

    private sealed class TestWaitingRoomButton : WaitingRoomButtonBase
    {
        public int ActionCount { get; private set; }

        public override string InteractionText => "테스트";
        public override bool CanInteract(GameObject interactor) => true;

        public void InitializeForTest()
        {
            base.Awake();
        }

        protected override void ExecuteButtonAction()
        {
            ActionCount++;
        }
    }
}
