using System.Collections.Generic;
using System.Reflection;
using Building;
using Field;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BuildingEditor.Tests
{
    /// <summary>
    /// 마을 상호작용 UI의 좌표 계산과 표시/숨김 정책 시험.
    ///
    /// <b>Play Mode를 쓰지 않는다.</b> 카메라의 픽셀 사각형을 직접 지정하고 프레임 한 번분의 갱신
    /// (<c>UpdateInteraction</c>)을 직접 불러 확인하므로, 실행 환경의 화면 크기나 프레임 수에
    /// 결과가 달라지지 않는다.
    /// </summary>
    public sealed class TownBuildingInteractionTests
    {
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";
        private const string PopupPrefabPath = "Assets/Art/UI/Prefab/dialog/dialog_BuildingPopup.prefab";
        private const string BuildingAssetPath = "Assets/Generated/TableData/Building/Building_1.asset";

        private const int PixelWidth = 256;
        private const int PixelHeight = 256;

        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();
        }

        // ---- 좌표 계산 ----

        [Test]
        public void 카메라_정면의_앵커는_사각형_좌표로_변환된다()
        {
            Camera camera = CreateCamera();
            RectTransform parent = CreateIdentityRect();

            // 직교 카메라의 정중앙 - 화면 좌표는 픽셀 사각형의 한가운데가 된다.
            bool projected = TownBuildingInteractionController.TryProjectAnchor(
                camera, new Vector3(0f, 0f, 0f), parent, null,
                PixelWidth, PixelHeight, out Vector2 localPoint);

            Assert.IsTrue(projected);
            Assert.AreEqual(0f, localPoint.x, 0.01f, "화면 한가운데는 사각형의 로컬 원점이다");
            Assert.AreEqual(0f, localPoint.y, 0.01f);
        }

        [Test]
        public void 앵커가_옆으로_움직이면_변환된_좌표도_같은_방향으로_움직인다()
        {
            Camera camera = CreateCamera();
            RectTransform parent = CreateIdentityRect();

            TownBuildingInteractionController.TryProjectAnchor(
                camera, Vector3.zero, parent, null, PixelWidth, PixelHeight, out Vector2 center);
            bool projected = TownBuildingInteractionController.TryProjectAnchor(
                camera, new Vector3(1f, 0.5f, 0f), parent, null,
                PixelWidth, PixelHeight, out Vector2 moved);

            Assert.IsTrue(projected);
            Assert.Greater(moved.x, center.x);
            Assert.Greater(moved.y, center.y);
        }

        [Test]
        public void 카메라_뒤의_앵커는_변환하지_않는다()
        {
            Camera camera = CreatePerspectiveCamera();
            RectTransform parent = CreateIdentityRect();

            // 카메라는 원점에서 +Z를 본다 - 뒤쪽(-Z)은 화면 좌표가 뒤집혀 반대편에 그려진다.
            bool projected = TownBuildingInteractionController.TryProjectAnchor(
                camera, new Vector3(0f, 0f, -5f), parent, null,
                PixelWidth, PixelHeight, out _);

            Assert.IsFalse(projected);
        }

        [Test]
        public void 화면_밖의_앵커는_변환하지_않는다()
        {
            Camera camera = CreateCamera();
            RectTransform parent = CreateIdentityRect();

            Assert.IsFalse(TownBuildingInteractionController.TryProjectAnchor(
                camera, new Vector3(1000f, 0f, 0f), parent, null,
                PixelWidth, PixelHeight, out _), "오른쪽 밖");
            Assert.IsFalse(TownBuildingInteractionController.TryProjectAnchor(
                camera, new Vector3(-1000f, 0f, 0f), parent, null,
                PixelWidth, PixelHeight, out _), "왼쪽 밖");
            Assert.IsFalse(TownBuildingInteractionController.TryProjectAnchor(
                camera, new Vector3(0f, 1000f, 0f), parent, null,
                PixelWidth, PixelHeight, out _), "위쪽 밖");
            Assert.IsFalse(TownBuildingInteractionController.TryProjectAnchor(
                camera, new Vector3(0f, -1000f, 0f), parent, null,
                PixelWidth, PixelHeight, out _), "아래쪽 밖");
        }

        [Test]
        public void 참조가_없으면_변환하지_않는다()
        {
            Assert.IsFalse(TownBuildingInteractionController.TryProjectAnchor(
                null, Vector3.zero, CreateIdentityRect(), null, PixelWidth, PixelHeight, out _));
            Assert.IsFalse(TownBuildingInteractionController.TryProjectAnchor(
                CreateCamera(), Vector3.zero, null, null, PixelWidth, PixelHeight, out _));
        }

        // ---- 이벤트 카메라 ----

        [Test]
        public void Overlay_캔버스는_이벤트_카메라를_null로_넘긴다()
        {
            var go = new GameObject("OverlayCanvas", typeof(Canvas));
            created.Add(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = CreateCamera();

            Assert.IsNull(TownBuildingInteractionController.ResolveEventCamera(canvas),
                "Overlay 캔버스에 worldCamera를 넘기면 좌표가 어긋난다");
        }

        [Test]
        public void 카메라_모드_캔버스는_지정된_카메라를_넘긴다()
        {
            var go = new GameObject("CameraCanvas", typeof(Canvas));
            created.Add(go);
            var canvas = go.GetComponent<Canvas>();
            Camera camera = CreateCamera();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;

            Assert.AreSame(camera, TownBuildingInteractionController.ResolveEventCamera(canvas));
        }

        [Test]
        public void 캔버스가_없으면_이벤트_카메라도_없다()
        {
            Assert.IsNull(TownBuildingInteractionController.ResolveEventCamera(null));
        }

        // ---- 표시/숨김 정책 ----

        [Test]
        public void 마을이면_상호작용_UI가_켜지고_버튼_위치가_맞춰진다()
        {
            Fixture fixture = CreateFixture();

            Assert.IsTrue(fixture.InteractionRoot.activeSelf);
            Assert.IsTrue(fixture.Controller.IsInteractionVisible);
            Assert.AreEqual(0f, fixture.BuildButtonRect.anchoredPosition.x, 0.01f);
            Assert.AreEqual(0f, fixture.BuildButtonRect.anchoredPosition.y, 0.01f);
        }

        [Test]
        public void 던전이면_상호작용_UI가_꺼진다()
        {
            Fixture fixture = CreateFixture();

            SetFieldMode(fixture.FieldModeManager, FieldMode.Dungeon);
            Update(fixture);

            Assert.IsFalse(fixture.InteractionRoot.activeSelf);
            Assert.IsFalse(fixture.Controller.IsInteractionVisible);
        }

        [Test]
        public void 전환_연출_중에는_상호작용_UI가_꺼진다()
        {
            Fixture fixture = CreateFixture();

            SetSequencerPlaying(fixture.Sequencer, true);
            Update(fixture);

            Assert.IsFalse(fixture.InteractionRoot.activeSelf);
        }

        [Test]
        public void 전환이_끝나면_버튼과_위치가_되돌아온다()
        {
            Fixture fixture = CreateFixture();

            SetSequencerPlaying(fixture.Sequencer, true);
            Update(fixture);
            Assert.IsFalse(fixture.InteractionRoot.activeSelf);

            // 연출 중에 앵커가 움직였다고 두고, 끝난 뒤 그 위치로 되돌아오는지 본다.
            fixture.Anchor.position = new Vector3(1f, 0f, 0f);
            SetSequencerPlaying(fixture.Sequencer, false);
            Update(fixture);

            Assert.IsTrue(fixture.InteractionRoot.activeSelf);
            Assert.Greater(fixture.BuildButtonRect.anchoredPosition.x, 0f,
                "연출 뒤에는 지금 앵커 위치로 다시 맞춰져야 한다");
        }

        [Test]
        public void 앵커가_화면_밖이면_숨기지만_팝업은_닫지_않는다()
        {
            Fixture fixture = CreateFixture();
            OpenPopup(fixture);

            fixture.Anchor.position = new Vector3(1000f, 0f, 0f);
            Update(fixture);

            Assert.IsFalse(fixture.InteractionRoot.activeSelf);
            Assert.IsTrue(fixture.Popup.gameObject.activeSelf,
                "마을 안에서 앵커가 잠깐 화면 밖으로 밀렸다고 보고 있던 정보 창을 닫으면 안 된다");
        }

        [Test]
        public void 앵커가_카메라_뒤면_숨긴다()
        {
            Fixture fixture = CreateFixture(perspective: true);

            fixture.Anchor.position = new Vector3(0f, 0f, -5f);
            Update(fixture);

            Assert.IsFalse(fixture.InteractionRoot.activeSelf);
        }

        // ---- 팝업 ----

        [Test]
        public void 건설_버튼을_누르면_Building_1을_바인딩하고_팝업을_연다()
        {
            Fixture fixture = CreateFixture();
            Assert.IsFalse(fixture.Popup.gameObject.activeSelf, "시작 시 팝업은 꺼져 있어야 한다");

            fixture.BuildButton.onClick.Invoke();

            Assert.IsTrue(fixture.Popup.gameObject.activeSelf);
            Assert.AreSame(fixture.Building, fixture.Popup.BoundBuilding);
        }

        [Test]
        public void 건설_버튼을_누르면_버튼_사각형도_함께_넘긴다()
        {
            Fixture fixture = CreateFixture();
            Assert.IsNull(fixture.Popup.SourceRect, "누르기 전에는 기준 버튼이 없다");

            fixture.BuildButton.onClick.Invoke();

            Assert.AreSame(fixture.BuildButtonRect, fixture.Popup.SourceRect,
                "팝업이 어디에 뜰지는 팝업이 정한다 - 컨트롤러는 기준이 되는 버튼만 알려 준다");
        }

        [Test]
        public void 던전으로_나가면_열린_팝업이_정상_닫기_경로로_닫힌다()
        {
            Fixture fixture = CreateFixture();
            OpenPopup(fixture);

            SetFieldMode(fixture.FieldModeManager, FieldMode.Dungeon);
            Update(fixture);

            Assert.IsFalse(fixture.Popup.gameObject.activeSelf);

            // 실제 실행에서는 Close가 켠 비활성화가 곧바로 OnDisable로 이어진다 - 그 뒤에 구독이
            // 남아 있지 않은지까지가 "정상 닫기 경로로 닫았다"의 뜻이다.
            EditModeLifecycle.RaiseDisable(fixture.Popup);
            Assert.IsFalse(fixture.Popup.HasLocalizationSubscriptions,
                "정상 Close 경로를 지났다면 구독도 함께 끊겨야 한다");
            Assert.IsFalse(fixture.Popup.HasInventorySubscription,
                "인벤토리 구독도 같은 경로에서 끊겨야 한다 - 닫힌 팝업이 계속 판정하면 안 된다");
        }

        [Test]
        public void 전환_연출이_시작되면_열린_팝업이_닫힌다()
        {
            Fixture fixture = CreateFixture();
            OpenPopup(fixture);

            SetSequencerPlaying(fixture.Sequencer, true);
            Update(fixture);

            Assert.IsFalse(fixture.Popup.gameObject.activeSelf);
        }

        // ---- 여관 입장 버튼 ----

        [Test]
        public void 여관_입장_버튼은_언제나_꺼진_채로_남는다()
        {
            Fixture fixture = CreateFixture();
            Assert.IsFalse(fixture.OpenInnButton.activeSelf);

            // 저작 실수로 켜져 있어도 되돌린다.
            fixture.OpenInnButton.SetActive(true);
            Update(fixture);

            Assert.IsFalse(fixture.OpenInnButton.activeSelf);
        }

        // ---- 씬 ----

        [Test]
        public void 씬은_항상_켜져_있는_관리자_오브젝트에_컨트롤러를_하나만_둔다()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                TownBuildingInteractionController[] all =
                    Object.FindObjectsOfType<TownBuildingInteractionController>(true);
                var inScene = new List<TownBuildingInteractionController>();
                foreach (TownBuildingInteractionController controller in all)
                {
                    if (controller.gameObject.scene == scene) inScene.Add(controller);
                }

                Assert.AreEqual(1, inScene.Count);
                TownBuildingInteractionController target = inScene[0];

                Assert.AreEqual("FieldSystem", target.gameObject.name,
                    "상호작용 루트를 끄는 컴포넌트는 그 바깥의 항상 켜진 오브젝트에 있어야 한다");
                Assert.IsTrue(target.gameObject.activeInHierarchy);

                var so = new SerializedObject(target);
                var interactionRoot = so.FindProperty("interactionRoot").objectReferenceValue as GameObject;
                var interactionParent = so.FindProperty("interactionParent").objectReferenceValue as RectTransform;
                var buildButton = so.FindProperty("buildButton").objectReferenceValue as Button;
                var openInn = so.FindProperty("openInnButton").objectReferenceValue as GameObject;
                var popup = so.FindProperty("buildingPopup").objectReferenceValue as BuildingPopupPanel;
                var anchor = so.FindProperty("uiAnchor").objectReferenceValue as Transform;
                var stageCamera = so.FindProperty("stageCamera").objectReferenceValue as Camera;
                var modeManager = so.FindProperty("fieldModeManager").objectReferenceValue as FieldModeManager;
                var sequencer = so.FindProperty("transitionSequencer").objectReferenceValue
                    as FieldTransitionSequencer;
                var building = so.FindProperty("building").objectReferenceValue as BuildingDefinition;

                Assert.IsNotNull(interactionRoot, "interactionRoot");
                Assert.IsNotNull(interactionParent, "interactionParent");
                Assert.IsNotNull(buildButton, "buildButton");
                Assert.IsNotNull(openInn, "openInnButton");
                Assert.IsNotNull(popup, "buildingPopup");
                Assert.IsNotNull(anchor, "uiAnchor");
                Assert.IsNotNull(stageCamera, "stageCamera");
                Assert.IsNotNull(modeManager, "fieldModeManager");
                Assert.IsNotNull(sequencer, "transitionSequencer");
                Assert.IsNotNull(building, "building");

                Assert.AreEqual("TownInteractionLayer", interactionRoot.name);
                Assert.AreEqual("Interaction", interactionParent.name);
                Assert.AreEqual("btn_Build_Inn", buildButton.name);
                Assert.AreEqual("btn_Open_Inn", openInn.name);
                Assert.AreEqual("dialog_BuildingPopup", popup.name);
                Assert.AreEqual("UIAnchor", anchor.name);
                Assert.AreEqual("Building_1", building.name);

                Assert.AreSame(modeManager.gameObject, target.gameObject,
                    "FieldModeManager는 같은 관리자 오브젝트의 것을 쓴다");
                Assert.IsFalse(interactionRoot.transform.IsChildOf(target.transform),
                    "자기가 끄는 루트 안에 있으면 한 번 숨긴 뒤 다시 켤 수 없다");
                Assert.AreSame(interactionParent, buildButton.transform.parent,
                    "버튼 좌표는 그 부모 사각형 기준으로 계산된다");

                Assert.IsFalse(popup.gameObject.activeSelf, "팝업은 시작 시 꺼져 있어야 한다");
                Assert.IsFalse(openInn.activeSelf, "여관 입장 버튼은 시작 시 꺼져 있어야 한다");
                Assert.IsTrue(buildButton.gameObject.activeSelf);

                Assert.AreEqual(0, buildButton.onClick.GetPersistentEventCount(),
                    "클릭은 런타임 리스너로만 걸린다 - 버튼에 영구 호출을 저작하지 않는다");

                Canvas canvas = interactionParent.GetComponentInParent<Canvas>();
                Assert.IsNotNull(canvas);
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode,
                    "Overlay라면 좌표 변환에 null 카메라를 넘겨야 한다");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void 씬의_팝업_인스턴스는_프리팹의_배선을_그대로_쓴다()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                BuildingPopupPanel[] all = Object.FindObjectsOfType<BuildingPopupPanel>(true);
                var inScene = new List<BuildingPopupPanel>();
                foreach (BuildingPopupPanel panel in all)
                {
                    if (panel.gameObject.scene == scene) inScene.Add(panel);
                }

                Assert.AreEqual(1, inScene.Count);
                Assert.AreEqual(PrefabSource(inScene[0]), PopupPrefabPath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void 씬의_팝업은_경고와_자리_사각형까지_그대로_물려받는다()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                BuildingPopupPanel target = null;
                foreach (BuildingPopupPanel panel in Object.FindObjectsOfType<BuildingPopupPanel>(true))
                {
                    if (panel.gameObject.scene == scene) target = panel;
                }

                Assert.IsNotNull(target, "씬에 건물 팝업이 있어야 한다");

                var so = new SerializedObject(target);
                var warning = so.FindProperty("warningText").objectReferenceValue as TMPro.TextMeshProUGUI;
                var placement = so.FindProperty("placementRect").objectReferenceValue as RectTransform;

                Assert.IsNotNull(warning, "경고 TMP 참조가 씬 인스턴스까지 닿아야 한다");
                Assert.AreEqual("lb_warningMSG", warning.name);
                Assert.IsFalse(warning.gameObject.activeSelf, "씬에서도 경고는 꺼진 채로 시작한다");
                Assert.IsFalse(warning.raycastTarget);

                Assert.IsNotNull(placement, "자리를 옮길 사각형 참조가 씬 인스턴스까지 닿아야 한다");
                Assert.AreEqual("bg", placement.name);
                Assert.AreNotSame(target.transform, placement,
                    "전체 화면을 덮는 팝업 루트를 옮기면 입력 영역까지 화면 밖으로 나간다");
                Assert.AreEqual("Dialog_UI", target.transform.parent.name,
                    "팝업이 붙는 부모는 그대로 Dialog_UI다");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ---- 도우미 ----

        private static string PrefabSource(Component component)
        {
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(component);
            return source == null ? null : AssetDatabase.GetAssetPath(source);
        }

        private sealed class Fixture
        {
            public TownBuildingInteractionController Controller;
            public FieldModeManager FieldModeManager;
            public FieldTransitionSequencer Sequencer;
            public GameObject InteractionRoot;
            public GameObject OpenInnButton;
            public Button BuildButton;
            public RectTransform BuildButtonRect;
            public Transform Anchor;
            public BuildingPopupPanel Popup;
            public BuildingDefinition Building;
        }

        private Fixture CreateFixture(bool perspective = false)
        {
            var fixture = new Fixture();

            Camera camera = perspective ? CreatePerspectiveCamera() : CreateCamera();

            var anchorGo = new GameObject("TestUIAnchor");
            created.Add(anchorGo);
            fixture.Anchor = anchorGo.transform;
            fixture.Anchor.position = Vector3.zero;

            RectTransform parent = CreateIdentityRect();
            fixture.InteractionRoot = parent.gameObject;

            var buildGo = new GameObject("TestBuildButton", typeof(RectTransform), typeof(Button));
            buildGo.transform.SetParent(parent, false);
            fixture.BuildButton = buildGo.GetComponent<Button>();
            fixture.BuildButtonRect = (RectTransform)buildGo.transform;
            fixture.BuildButtonRect.anchorMin = new Vector2(0.5f, 0.5f);
            fixture.BuildButtonRect.anchorMax = new Vector2(0.5f, 0.5f);

            var openGo = new GameObject("TestOpenInnButton", typeof(RectTransform));
            openGo.transform.SetParent(parent, false);
            openGo.SetActive(false);
            fixture.OpenInnButton = openGo;

            var modeGo = new GameObject("TestFieldModeManager");
            modeGo.SetActive(false);
            created.Add(modeGo);
            fixture.FieldModeManager = modeGo.AddComponent<FieldModeManager>();

            var sequencerGo = new GameObject("TestSequencer");
            sequencerGo.SetActive(false);
            created.Add(sequencerGo);
            fixture.Sequencer = sequencerGo.AddComponent<FieldTransitionSequencer>();

            fixture.Popup = CreatePopupInstance();
            fixture.Building = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(BuildingAssetPath);
            Assert.IsNotNull(fixture.Building, BuildingAssetPath);

            var controllerGo = new GameObject("TestBuildingController");
            controllerGo.SetActive(false);
            created.Add(controllerGo);
            fixture.Controller = controllerGo.AddComponent<TownBuildingInteractionController>();

            var so = new SerializedObject(fixture.Controller);
            so.FindProperty("fieldModeManager").objectReferenceValue = fixture.FieldModeManager;
            so.FindProperty("transitionSequencer").objectReferenceValue = fixture.Sequencer;
            so.FindProperty("stageCamera").objectReferenceValue = camera;
            so.FindProperty("uiAnchor").objectReferenceValue = fixture.Anchor;
            so.FindProperty("interactionRoot").objectReferenceValue = fixture.InteractionRoot;
            so.FindProperty("interactionParent").objectReferenceValue = parent;
            so.FindProperty("buildButton").objectReferenceValue = fixture.BuildButton;
            so.FindProperty("openInnButton").objectReferenceValue = fixture.OpenInnButton;
            so.FindProperty("buildingPopup").objectReferenceValue = fixture.Popup;
            so.FindProperty("building").objectReferenceValue = fixture.Building;
            so.ApplyModifiedPropertiesWithoutUndo();

            // EditMode에서는 엔진이 OnEnable을 부르지 않으므로 활성화 직후 직접 재현한다
            // (리스너 연결 + 첫 갱신). 대상 씬을 Play Mode로 켜지 않기 위한 최소한의 이음매다.
            controllerGo.SetActive(true);
            EditModeLifecycle.RaiseEnable(fixture.Controller);
            return fixture;
        }

        private BuildingPopupPanel CreatePopupInstance()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PopupPrefabPath);
            Assert.IsNotNull(prefab, PopupPrefabPath);

            var popupParent = new GameObject("TestPopupParent", typeof(RectTransform));
            created.Add(popupParent);

            GameObject instance = Object.Instantiate(prefab, popupParent.transform, false);
            created.Add(instance);
            instance.SetActive(false);
            return instance.GetComponent<BuildingPopupPanel>();
        }

        private static void OpenPopup(Fixture fixture)
        {
            fixture.BuildButton.onClick.Invoke();
            Assert.IsTrue(fixture.Popup.gameObject.activeSelf);
            EditModeLifecycle.RaiseEnable(fixture.Popup);
            Assert.IsTrue(fixture.Popup.HasLocalizationSubscriptions);
        }

        private static void Update(Fixture fixture)
        {
            MethodInfo method = typeof(TownBuildingInteractionController).GetMethod(
                "UpdateInteraction", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            method.Invoke(fixture.Controller, null);
        }

        private Camera CreateCamera()
        {
            var go = new GameObject("TestStageCamera", typeof(Camera));
            created.Add(go);
            var camera = go.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;
            camera.pixelRect = new Rect(0f, 0f, PixelWidth, PixelHeight);
            return camera;
        }

        private Camera CreatePerspectiveCamera()
        {
            Camera camera = CreateCamera();
            camera.orthographic = false;
            camera.transform.position = Vector3.zero;
            return camera;
        }

        private RectTransform CreateIdentityRect()
        {
            var go = new GameObject("TestInteractionRoot", typeof(RectTransform));
            created.Add(go);
            var rect = (RectTransform)go.transform;
            rect.position = Vector3.zero;
            rect.rotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(PixelWidth, PixelHeight);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // 실제 씬의 Interaction 사각형과 같은 모양으로 둔다 - 화면 전체를 덮는 사각형의 로컬
            // 원점이 화면 한가운데다. 그래야 자식 버튼의 앵커(0.5, 0.5) 기준 anchoredPosition이
            // 변환된 로컬 좌표와 그대로 같아진다.
            rect.position = new Vector3(PixelWidth * 0.5f, PixelHeight * 0.5f, 0f);
            return rect;
        }

        private static void SetFieldMode(FieldModeManager manager, FieldMode mode)
        {
            FieldInfo field = typeof(FieldModeManager).GetField(
                "<CurrentMode>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(manager, mode);
        }

        private static void SetSequencerPlaying(FieldTransitionSequencer sequencer, bool value)
        {
            FieldInfo field = typeof(FieldTransitionSequencer).GetField(
                "<IsPlaying>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(sequencer, value);
        }
    }
}
