using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Building;
using Common;
using Field;
using Inventory;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BuildingEditor.Tests
{
    /// <summary>
    /// 마을 상호작용 UI의 좌표 계산과 표시/숨김 정책, 그리고 건설 타이머/완성 안내 시험.
    ///
    /// <b>Play Mode를 쓰지 않는다.</b> 카메라의 픽셀 사각형을 직접 지정하고 프레임 한 번분의 갱신
    /// (<c>UpdateInteraction</c>)을 직접 불러 확인하므로, 실행 환경의 화면 크기나 프레임 수에
    /// 결과가 달라지지 않는다.
    ///
    /// <b>시각도 진짜를 쓰지 않는다.</b> 컨트롤러의 시계를 고정값으로 갈아 끼우고 시험이 직접 밀기
    /// 때문에, 남은 시간과 완성 경계가 시험을 언제 돌리느냐에 따라 달라지지 않는다.
    /// </summary>
    public sealed class TownBuildingInteractionTests
    {
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";
        private const string PopupPrefabPath = "Assets/Art/UI/Prefab/dialog/dialog_BuildingPopup.prefab";
        private const string BuildingAssetPath = "Assets/Generated/TableData/Building/Building_1.asset";

        private const int PixelWidth = 256;
        private const int PixelHeight = 256;

        /// <summary>01_UI 테이블 GUID.</summary>
        private const string UiTableGuid = "GUID:32fd067a20b754a50b20446b9c78d2ae";

        /// <summary>01_UI / 42(건설 시작 안내)의 실제 Entry Id.</summary>
        private const long ConstructionStartedKeyId = 8970130103984129L;

        /// <summary>01_UI / 43(건설 완성 안내)의 실제 Entry Id.</summary>
        private const long ConstructionCompletedKeyId = 8970130103984130L;

        /// <summary>타이머 연출 클립. 자리도 크기도 건드리지 않는 <b>스프라이트 교체</b>여야 한다.</summary>
        private const string TimerClipPath = "Assets/Art/UI/Building/LoadingTimer.anim";

        /// <summary>시험이 쓰는 고정 시각. 남은 시간과 완성 경계를 글자 그대로 확인하기 위해
        /// 컨트롤러의 시계를 이 값으로 갈아 끼운다 - 실제 시각에 따라 결과가 달라지지 않는다.</summary>
        private static readonly DateTime FixedNowUtc =
            new DateTime(2026, 8, 22, 10, 30, 0, DateTimeKind.Utc);

        private static readonly MethodInfo ConfigureSaveMethod = typeof(SaveSystem).GetMethod(
            "ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<Object> created = new List<Object>();
        private FakeStorage storage;
        private InventoryManager testInventory;
        private ToastManager toastManager;

        /// <summary>컨트롤러가 읽는 지금 시각. 시험이 이 값을 밀어 시간을 흘려보낸다.</summary>
        private DateTime nowUtc;

        [SetUp]
        public void SetUp()
        {
            nowUtc = FixedNowUtc;

            // 컨트롤러가 건설 기록을 보려면 저장 문서를 읽는다 - 실제 저장 파일 근처에도 가지 않도록
            // 메모리 위의 가짜 저장소를 끼워 넣는다(건물 팝업 시험과 같은 방식이다).
            Assert.IsNotNull(ConfigureSaveMethod,
                "SaveSystem.ConfigureForTests를 찾지 못했습니다 - 그대로 두면 시험이 실제 저장 파일을 읽고 씁니다.");

            storage = new FakeStorage();
            ConfigureSaveMethod.Invoke(null, new object[] { storage, null, null });
        }

        [TearDown]
        public void TearDown()
        {
            if (testInventory != null) EditModeLifecycle.Invoke(testInventory, "OnDestroy");
            testInventory = null;

            if (toastManager != null) EditModeLifecycle.Invoke(toastManager, "OnDestroy");
            toastManager = null;

            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();

            ConfigureSaveMethod.Invoke(null, new object[] { null, null, null });
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
        public void 짓지_않은_건물의_입장_버튼은_꺼진_채로_남는다()
        {
            Fixture fixture = CreateFixture();
            Assert.IsFalse(fixture.OpenInnButton.activeSelf);

            // 저작 실수로 켜져 있어도 되돌린다 - "지어지지 않은 건물에 들어가는 버튼"은 보이지 않는다.
            fixture.OpenInnButton.SetActive(true);
            Update(fixture);

            Assert.IsFalse(fixture.OpenInnButton.activeSelf);
        }

        // ---- 단계별 표시(건설 버튼 / 타이머 / 입장 버튼) ----

        [Test]
        public void 아직_짓지_않았으면_건설_버튼만_보인다()
        {
            Fixture fixture = CreateFixture();

            AssertOnlyOneVisible(fixture, build: true, timer: false, open: false);
            Assert.AreEqual(BuildingConstructionPhase.NotStarted, fixture.Controller.ConstructionPhase);
        }

        [Test]
        public void 짓는_중이면_타이머만_보인다()
        {
            AddConstruction(BuildingIdOfAsset(), FixedNowUtc.AddSeconds(60));

            Fixture fixture = CreateFixture();

            AssertOnlyOneVisible(fixture, build: false, timer: true, open: false);
            Assert.AreEqual(BuildingConstructionPhase.InProgress, fixture.Controller.ConstructionPhase);
        }

        [Test]
        public void 완성되면_타이머가_사라지고_입장_버튼만_보인다()
        {
            AddConstruction(BuildingIdOfAsset(), FixedNowUtc.AddSeconds(60));
            Fixture fixture = CreateFixture();
            Assert.IsTrue(fixture.TimerRoot.activeSelf);

            // 시간만 흘렀다 - 저장 기록은 그대로다.
            nowUtc = FixedNowUtc.AddSeconds(60);
            Update(fixture);

            AssertOnlyOneVisible(fixture, build: false, timer: false, open: true);
            Assert.AreEqual(BuildingConstructionPhase.Completed, fixture.Controller.ConstructionPhase);
        }

        [Test]
        public void 완성_시각을_읽을_수_없으면_셋_다_보이지_않는다()
        {
            AddUnreadableConstruction(BuildingIdOfAsset());

            Fixture fixture = CreateFixture();

            AssertOnlyOneVisible(fixture, build: false, timer: false, open: false);
            Assert.AreEqual(BuildingConstructionPhase.Unreadable, fixture.Controller.ConstructionPhase);
            Assert.IsTrue(fixture.Controller.IsConstructionStarted,
                "손상된 기록도 '이미 시작했다'이다 - 건설 버튼이 돌아오면 두 번 짓게 된다");
        }

        [Test]
        public void 타이머는_건설_버튼과_같은_자리에_놓인다()
        {
            AddConstruction(BuildingIdOfAsset(), FixedNowUtc.AddSeconds(60));
            Fixture fixture = CreateFixture();

            fixture.Anchor.position = new Vector3(1f, 0.5f, 0f);
            Update(fixture);

            Assert.Greater(fixture.TimerRect.anchoredPosition.x, 0f, "타이머도 월드 앵커를 따라간다");
            Assert.AreEqual(fixture.BuildButtonRect.anchoredPosition, fixture.TimerRect.anchoredPosition,
                "건설 버튼과 타이머는 서로 자리를 물려받는 사이다 - 좌표를 따로 계산하지 않는다");
        }

        [Test]
        public void 던전이면_타이머도_입장_버튼도_함께_사라진다()
        {
            AddConstruction(BuildingIdOfAsset(), FixedNowUtc.AddSeconds(60));
            Fixture fixture = CreateFixture();
            Assert.IsTrue(fixture.InteractionRoot.activeSelf);

            SetFieldMode(fixture.FieldModeManager, FieldMode.Dungeon);
            Update(fixture);

            Assert.IsFalse(fixture.InteractionRoot.activeSelf,
                "루트 하나가 꺼지면 그 안의 건설 버튼도 타이머도 입장 버튼도 함께 사라진다");
            Assert.IsFalse(fixture.TimerRoot.activeInHierarchy);
        }

        [Test]
        public void 전환_연출_중에도_타이머가_보이지_않는다()
        {
            AddConstruction(BuildingIdOfAsset(), FixedNowUtc.AddSeconds(60));
            Fixture fixture = CreateFixture();

            SetSequencerPlaying(fixture.Sequencer, true);
            Update(fixture);

            Assert.IsFalse(fixture.InteractionRoot.activeSelf);
            Assert.IsFalse(fixture.TimerRoot.activeInHierarchy);
        }

        // ---- 남은 시간 표시 ----

        [Test]
        public void 남은_시간은_HH_mm_ss로_표시된다()
        {
            AddConstruction(BuildingIdOfAsset(), FixedNowUtc.AddSeconds(60));
            Fixture fixture = CreateFixture();

            Assert.AreEqual("00:01:00", fixture.TimerText.text);

            nowUtc = FixedNowUtc.AddSeconds(1);
            Update(fixture);
            Assert.AreEqual("00:00:59", fixture.TimerText.text);
        }

        [Test]
        public void 하루를_넘는_남은_시간도_되감기지_않는다()
        {
            AddConstruction(BuildingIdOfAsset(), FixedNowUtc.AddHours(25));

            Fixture fixture = CreateFixture();

            Assert.AreEqual("25:00:00", fixture.TimerText.text,
                "24시간에서 되감긴 값은 '곧 끝난다'로 잘못 읽힌다");
        }

        [Test]
        public void 한_초보다_짧게_남으면_1초로_보인다()
        {
            AddConstruction(BuildingIdOfAsset(), FixedNowUtc.AddMilliseconds(400));

            Fixture fixture = CreateFixture();

            Assert.AreEqual("00:00:01", fixture.TimerText.text,
                "0.4초 남았는데 00:00:00이 보이면 이미 끝난 것으로 읽힌다");
            Assert.IsTrue(fixture.TimerRoot.activeSelf, "아직 완성 시각이 오지 않았다");
        }

        [Test]
        public void 표시_초가_그대로면_텍스트를_다시_쓰지_않는다()
        {
            AddConstruction(BuildingIdOfAsset(), FixedNowUtc.AddSeconds(60));
            Fixture fixture = CreateFixture();
            Assert.AreEqual("00:01:00", fixture.TimerText.text);

            // 같은 초 안에서 여러 프레임이 지나가도 문자열을 다시 만들지 않는다는 것을, 밖에서 써 둔
            // 표식이 살아남는지로 확인한다.
            fixture.TimerText.text = "표식";
            nowUtc = FixedNowUtc.AddMilliseconds(200);
            Update(fixture);
            nowUtc = FixedNowUtc.AddMilliseconds(400);
            Update(fixture);
            Assert.AreEqual("표식", fixture.TimerText.text, "1초에 한 번만 고쳐 쓴다");

            nowUtc = FixedNowUtc.AddSeconds(1);
            Update(fixture);
            Assert.AreEqual("00:00:59", fixture.TimerText.text, "초가 바뀌면 그때 고쳐 쓴다");
        }

        [Test]
        public void 남은_시간을_보여도_저장하지_않는다()
        {
            AddConstruction(BuildingIdOfAsset(), FixedNowUtc.AddSeconds(60));
            Fixture fixture = CreateFixture();

            int before = storage.WriteCalls;
            for (int i = 1; i <= 5; i++)
            {
                nowUtc = FixedNowUtc.AddSeconds(i);
                Update(fixture);
            }

            Assert.AreEqual(before, storage.WriteCalls, "타이머가 흐를 때마다 파일을 쓰면 안 된다");
        }

        [Test]
        public void 타이머_연출은_시간_배속과_무관하게_돈다()
        {
            Fixture fixture = CreateFixture();

            Assert.AreEqual(AnimatorUpdateMode.UnscaledTime, fixture.TimerAnimator.updateMode,
                "화면이 멈춰도(timeScale 0) 타이머 연출은 돌아야 한다");
        }

        // ---- 완성 안내 토스트 ----

        [Test]
        public void 완성되면_안내를_한_번만_띄우고_그_사실이_파일에_남는다()
        {
            AddConstruction(BuildingIdOfAsset(), FixedNowUtc.AddSeconds(60));
            Fixture fixture = CreateFixture();
            CreateToastManager();
            DeliverCompletedMessage(fixture, "건설이 완료되었습니다.");

            nowUtc = FixedNowUtc.AddSeconds(60);
            Update(fixture);
            Update(fixture);
            nowUtc = FixedNowUtc.AddSeconds(120);
            Update(fixture);

            Assert.AreEqual(1, fixture.Controller.CompletedToastCount, "완성 한 번에 안내도 한 번이다");
            Assert.IsTrue(SaveSystem.Data.buildingConstructions[0].completionNotified);
        }

        [Test]
        public void 앱을_다시_켜도_완성_안내는_다시_뜨지_않는다()
        {
            // 이미 안내를 마친 기록 그대로 - 컨트롤러는 새로 만들어진다.
            AddConstruction(BuildingIdOfAsset(), FixedNowUtc.AddDays(-1), completionNotified: true);

            Fixture fixture = CreateFixture();
            CreateToastManager();
            DeliverCompletedMessage(fixture, "건설이 완료되었습니다.");

            int before = storage.WriteCalls;
            Update(fixture);

            Assert.AreEqual(0, fixture.Controller.CompletedToastCount);
            Assert.AreEqual(before, storage.WriteCalls, "다시 저장하지도 않는다");
            Assert.IsTrue(fixture.OpenInnButton.activeSelf, "안내는 끝났어도 입장 버튼은 그대로 켜진다");
        }

        [Test]
        public void 완성_저장이_실패하면_안내하지_않고_다음_갱신에서_다시_시도한다()
        {
            AddConstruction(BuildingIdOfAsset(), FixedNowUtc.AddSeconds(60));
            Fixture fixture = CreateFixture();
            CreateToastManager();
            DeliverCompletedMessage(fixture, "건설이 완료되었습니다.");

            storage.WritesFail = true;
            LogAssert.Expect(LogType.Error, new Regex("완성 표식을 저장하지"));
            nowUtc = FixedNowUtc.AddSeconds(60);
            Update(fixture);

            Assert.AreEqual(0, fixture.Controller.CompletedToastCount, "실패한 확정은 안내하지 않는다");
            Assert.IsFalse(SaveSystem.Data.buildingConstructions[0].completionNotified);

            storage.WritesFail = false;
            Update(fixture);

            Assert.AreEqual(1, fixture.Controller.CompletedToastCount, "다음 갱신에서 다시 시도한다");
            Assert.IsTrue(SaveSystem.Data.buildingConstructions[0].completionNotified);
        }

        [Test]
        public void 완성_안내_문구가_오기_전에는_토스트를_띄우지_않고_한_번만_알린다()
        {
            AddConstruction(BuildingIdOfAsset(), FixedNowUtc.AddSeconds(60));
            Fixture fixture = CreateFixture();

            // 문구가 도착하지 않은 상태에서 완성됐다 - 코드가 대체 문구를 지어내지 않는다.
            LogAssert.Expect(LogType.Warning, new Regex("아직 없어 토스트를 띄우지 않습니다"));
            nowUtc = FixedNowUtc.AddSeconds(60);
            Update(fixture);
            Update(fixture);

            Assert.AreEqual(0, fixture.Controller.CompletedToastCount);
            Assert.IsNull(fixture.Controller.CompletedToastMessage);
        }

        // ---- 건설 기록에 따른 표시 ----

        [Test]
        public void 기록이_없으면_건설_버튼은_그대로_보인다()
        {
            Fixture fixture = CreateFixture();

            Assert.IsTrue(fixture.BuildButton.gameObject.activeSelf);
            Assert.IsFalse(fixture.Controller.IsConstructionStarted);
            Assert.IsTrue(fixture.InteractionRoot.activeSelf);
        }

        [Test]
        public void 같은_buildingId_기록이_있으면_건설_버튼이_숨는다()
        {
            AddConstruction(BuildingIdOfAsset());

            Fixture fixture = CreateFixture();

            Assert.IsTrue(fixture.Controller.IsConstructionStarted);
            Assert.IsFalse(fixture.BuildButton.gameObject.activeSelf,
                "이미 시작된 건물의 건설 버튼은 나오지 않는다");
            Assert.IsFalse(fixture.OpenInnButton.activeSelf,
                "아직 짓는 중이므로 여관 입장 버튼은 나오지 않는다");
        }

        [Test]
        public void 완성_시각이_지난_기록도_건설_버튼을_계속_숨긴다()
        {
            // 앱을 껐다 켠 상황 그대로 - 기록만 저장 문서에 남아 있다.
            SaveSystem.Data.buildingConstructions.Add(new BuildingConstructionSaveState
            {
                buildingId = BuildingIdOfAsset(),
                startedAtUtc = "2020-01-01T00:00:00.0000000Z",
                completeAtUtc = "2020-01-01T00:01:00.0000000Z",
            });

            Fixture fixture = CreateFixture();
            Update(fixture);

            Assert.IsFalse(fixture.BuildButton.gameObject.activeSelf,
                "완성 시각이 지났다고 다시 지을 수 있게 되면 안 된다");
        }

        [Test]
        public void 다른_buildingId_기록은_이_버튼을_숨기지_않는다()
        {
            AddConstruction(BuildingIdOfAsset() + "_다른건물");

            Fixture fixture = CreateFixture();

            Assert.IsFalse(fixture.Controller.IsConstructionStarted);
            Assert.IsTrue(fixture.BuildButton.gameObject.activeSelf);
        }

        [Test]
        public void 건설이_시작되면_숨김이_그_자리에서_반영된다()
        {
            Fixture fixture = CreateFixture();
            Assert.IsTrue(fixture.BuildButton.gameObject.activeSelf);

            // 서비스가 기록을 남긴 것과 같은 상태를 만든다(판정의 근거는 언제나 저장 기록이다).
            AddConstruction(BuildingIdOfAsset());
            Update(fixture);

            Assert.IsFalse(fixture.BuildButton.gameObject.activeSelf);
        }

        // ---- 시작 안내 토스트 ----

        [Test]
        public void 안내_문구가_오기_전에는_토스트를_띄우지_않고_한_번만_알린다()
        {
            Fixture fixture = CreateFixture();

            // 문구가 도착하지 않은 상태에서 시작 신호가 왔다 - 코드가 대체 문구를 지어내지 않는다.
            LogAssert.Expect(LogType.Warning, new Regex("건설 시작 안내 문구"));
            RaiseConstructionStarted(fixture);
            RaiseConstructionStarted(fixture);

            Assert.AreEqual(0, fixture.Controller.StartedToastCount);
            Assert.IsNull(fixture.Controller.StartedToastMessage);
        }

        [Test]
        public void 문구가_도착하면_시작마다_안내를_한_번씩_띄운다()
        {
            Fixture fixture = CreateFixture();
            CreateToastManager();

            DeliverStartedMessage(fixture, "건설을 시작했습니다.");
            Assert.AreEqual("건설을 시작했습니다.", fixture.Controller.StartedToastMessage,
                "문구는 표에서 온다 - 코드가 짓지 않는다");

            RaiseConstructionStarted(fixture);

            Assert.AreEqual(1, fixture.Controller.StartedToastCount, "한 번의 시작에 안내도 한 번이다");
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

                // 건설 타이머 배선. 셋 다 같은 묶음 안에 있어야 한 번에 켜고 끌 수 있다.
                var timerRoot = so.FindProperty("constructionTimerRoot").objectReferenceValue as GameObject;
                var timerText = so.FindProperty("constructionTimerText").objectReferenceValue
                    as TextMeshProUGUI;
                var timerAnimator = so.FindProperty("constructionTimerAnimator").objectReferenceValue
                    as Animator;

                Assert.IsNotNull(timerRoot, "constructionTimerRoot");
                Assert.IsNotNull(timerText, "constructionTimerText");
                Assert.IsNotNull(timerAnimator, "constructionTimerAnimator");

                Assert.AreEqual("pn_ConstructionTimer", timerRoot.name);
                Assert.AreEqual("lb_ConstructionTimer", timerText.name);
                Assert.AreEqual("ani_Timer", timerAnimator.name);

                Assert.AreSame(interactionParent, timerRoot.transform.parent,
                    "타이머도 건설 버튼과 같은 사각형을 기준으로 자리를 잡는다");
                Assert.IsTrue(timerText.transform.IsChildOf(timerRoot.transform));
                Assert.IsTrue(timerAnimator.transform.IsChildOf(timerRoot.transform));

                Assert.IsFalse(timerRoot.activeSelf, "타이머는 시작 시 꺼져 있어야 한다");

                Assert.AreEqual(AnimatorUpdateMode.UnscaledTime, timerAnimator.updateMode,
                    "화면이 멈춰도(timeScale 0) 타이머 연출은 돌아야 한다");
                Assert.IsNotNull(timerAnimator.runtimeAnimatorController,
                    "ani_Timer에 Animator Controller가 연결되어 있어야 한다");

                // 타이머는 <b>보여 주기만</b> 한다 - 클릭을 받아 그 아래의 입력을 가리면 안 된다.
                Assert.IsFalse(timerText.raycastTarget, "남은 시간 텍스트는 클릭을 받지 않는다");
                foreach (Graphic graphic in timerRoot.GetComponentsInChildren<Graphic>(true))
                {
                    Assert.IsFalse(graphic.raycastTarget,
                        $"타이머의 '{graphic.name}'이 클릭을 받으면 그 아래가 눌리지 않는다");
                }

                // 이 묶음에서 상호작용하는 것은 두 버튼뿐이다(그중 입장 버튼은 완성 뒤에만 보인다).
                Selectable[] selectables = interactionParent.GetComponentsInChildren<Selectable>(true);
                Assert.AreEqual(2, selectables.Length,
                    "건설 버튼과 여관 입장 버튼 말고 상호작용하는 것이 있으면 안 된다");
                foreach (Selectable selectable in selectables)
                {
                    Assert.IsTrue(
                        selectable.name == "btn_Build_Inn" || selectable.name == "btn_Open_Inn",
                        $"'{selectable.name}'은 상호작용 대상이 아니다");
                }

                var openInnButton = openInn.GetComponent<Button>();
                Assert.IsNotNull(openInnButton, "btn_Open_Inn에는 Button이 있어야 한다");
                Assert.AreEqual(0, openInnButton.onClick.GetPersistentEventCount(),
                    "이번 단계에서 입장 버튼은 켜지기만 한다 - 누를 때 할 일은 다음 단계의 몫이다");

                Assert.AreEqual(0, buildButton.onClick.GetPersistentEventCount(),
                    "클릭은 런타임 리스너로만 걸린다 - 버튼에 영구 호출을 저작하지 않는다");

                // 건설 배선. 서비스는 이 인벤토리 하나로 만들어지고, 안내 문구는 표에서 온다.
                var sceneInventory = so.FindProperty("inventoryManager").objectReferenceValue as InventoryManager;
                Assert.IsNotNull(sceneInventory,
                    "InventoryManager가 연결되지 않으면 확인 버튼을 눌러도 건설이 시작되지 않는다");
                Assert.AreEqual(scene, sceneInventory.gameObject.scene, "같은 씬의 InventoryManager여야 한다");

                Assert.AreEqual(UiTableGuid,
                    so.FindProperty("constructionStartedMessage.m_TableReference.m_TableCollectionName")
                        .stringValue);
                Assert.AreEqual(ConstructionStartedKeyId,
                    so.FindProperty("constructionStartedMessage.m_TableEntryReference.m_KeyId").longValue,
                    "건설 시작 안내는 01_UI / 42여야 한다");

                Assert.AreEqual(UiTableGuid,
                    so.FindProperty("constructionCompletedMessage.m_TableReference.m_TableCollectionName")
                        .stringValue);
                Assert.AreEqual(ConstructionCompletedKeyId,
                    so.FindProperty("constructionCompletedMessage.m_TableEntryReference.m_KeyId").longValue,
                    "건설 완성 안내는 01_UI / 43이어야 한다");

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
        public void 타이머_연출은_자리도_크기도_건드리지_않는다()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(TimerClipPath);
            Assert.IsNotNull(clip, TimerClipPath);

            // 자리/크기/회전은 전부 float 곡선으로 저작된다 - 하나라도 있으면 연출이 UI를 움직인다.
            Assert.AreEqual(0, AnimationUtility.GetCurveBindings(clip).Length,
                "타이머 연출은 스프라이트만 갈아 끼운다 - 자리나 크기를 움직이면 앵커 계산과 싸운다");

            EditorCurveBinding[] pptr = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            Assert.Greater(pptr.Length, 0, "스프라이트 교체 곡선은 있어야 한다");
            foreach (EditorCurveBinding binding in pptr)
            {
                Assert.AreEqual("m_Sprite", binding.propertyName);
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
            public GameObject TimerRoot;
            public RectTransform TimerRect;
            public TextMeshProUGUI TimerText;
            public Animator TimerAnimator;
            public Transform Anchor;
            public BuildingPopupPanel Popup;
            public BuildingDefinition Building;
            public InventoryManager Inventory;
        }

        /// <summary>씬에 하나 있는 InventoryManager를 흉내낸다. EditMode에서는 Awake가 오지 않으므로
        /// 정적 Instance 등록을 직접 재현한다 - 저장 파일은 가짜 저장소가 이미 막고 있다.</summary>
        private InventoryManager CreateInventory()
        {
            var go = new GameObject("TestInventoryManager");
            go.SetActive(false);
            created.Add(go);

            var manager = go.AddComponent<InventoryManager>();
            EditModeLifecycle.Invoke(manager, "Awake");
            testInventory = manager;
            return manager;
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

            // 실제 씬과 같은 모양으로 세운다 - 타이머 묶음은 꺼진 채로 시작하고, 그 안에 남은 시간
            // 텍스트와 회전 연출이 하나씩 들어 있다.
            var timerGo = new GameObject("TestConstructionTimer", typeof(RectTransform));
            timerGo.SetActive(false);
            timerGo.transform.SetParent(parent, false);
            fixture.TimerRoot = timerGo;
            fixture.TimerRect = (RectTransform)timerGo.transform;
            fixture.TimerRect.anchorMin = new Vector2(0.5f, 0.5f);
            fixture.TimerRect.anchorMax = new Vector2(0.5f, 0.5f);

            var timerTextGo = new GameObject("TestConstructionTimerText",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            timerTextGo.transform.SetParent(timerGo.transform, false);
            fixture.TimerText = timerTextGo.GetComponent<TextMeshProUGUI>();

            var timerAnimatorGo = new GameObject("TestConstructionTimerAnimation",
                typeof(RectTransform), typeof(Animator));
            timerAnimatorGo.transform.SetParent(timerGo.transform, false);
            fixture.TimerAnimator = timerAnimatorGo.GetComponent<Animator>();

            // 저작이 잘못돼 있어도 컨트롤러가 되돌린다는 것을 확인하려고 일부러 어긋나게 둔다.
            fixture.TimerAnimator.updateMode = AnimatorUpdateMode.Normal;

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

            fixture.Inventory = CreateInventory();

            var controllerGo = new GameObject("TestBuildingController");
            controllerGo.SetActive(false);
            created.Add(controllerGo);
            fixture.Controller = controllerGo.AddComponent<TownBuildingInteractionController>();

            var so = new SerializedObject(fixture.Controller);
            so.FindProperty("inventoryManager").objectReferenceValue = fixture.Inventory;
            so.FindProperty("fieldModeManager").objectReferenceValue = fixture.FieldModeManager;
            so.FindProperty("transitionSequencer").objectReferenceValue = fixture.Sequencer;
            so.FindProperty("stageCamera").objectReferenceValue = camera;
            so.FindProperty("uiAnchor").objectReferenceValue = fixture.Anchor;
            so.FindProperty("interactionRoot").objectReferenceValue = fixture.InteractionRoot;
            so.FindProperty("interactionParent").objectReferenceValue = parent;
            so.FindProperty("buildButton").objectReferenceValue = fixture.BuildButton;
            so.FindProperty("openInnButton").objectReferenceValue = fixture.OpenInnButton;
            so.FindProperty("constructionTimerRoot").objectReferenceValue = fixture.TimerRoot;
            so.FindProperty("constructionTimerText").objectReferenceValue = fixture.TimerText;
            so.FindProperty("constructionTimerAnimator").objectReferenceValue = fixture.TimerAnimator;
            so.FindProperty("buildingPopup").objectReferenceValue = fixture.Popup;
            so.FindProperty("building").objectReferenceValue = fixture.Building;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 시계를 갈아 끼운다 - 남은 시간과 완성 경계를 실제 시각과 무관하게 확인하기 위함이며,
            // 서비스가 만들어지기 <b>전에</b> 넣어야 첫 갱신부터 이 시계를 쓴다.
            SetClock(fixture.Controller);

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

        /// <summary>단계마다 <b>셋 중 하나만</b> 보인다는 규칙을 한 줄로 확인한다 - 하나를 켜면서
        /// 다른 하나를 끄는 것을 빠뜨리면 여기서 드러난다.</summary>
        private static void AssertOnlyOneVisible(Fixture fixture, bool build, bool timer, bool open)
        {
            Assert.AreEqual(build, fixture.BuildButton.gameObject.activeSelf, "건설 버튼");
            Assert.AreEqual(timer, fixture.TimerRoot.activeSelf, "건설 타이머");
            Assert.AreEqual(open, fixture.OpenInnButton.activeSelf, "여관 입장 버튼");
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

        /// <summary>씬에 연결된 Building_1의 Building Id. 표가 바뀌어도 시험이 같은 값을 본다.</summary>
        private static string BuildingIdOfAsset()
        {
            var building = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(BuildingAssetPath);
            Assert.IsNotNull(building, BuildingAssetPath);
            return building.BuildingId;
        }

        /// <summary>건설 기록 한 줄을 저장 문서에 직접 넣는다(시작 경로를 거치지 않는다) - 여기서
        /// 확인하려는 것은 "기록이 있으면 어떻게 보이는가"뿐이다. 기본값은 고정 시각에서 1분 뒤에
        /// 끝나는 기록이라 <b>짓는 중</b>이다.</summary>
        private static void AddConstruction(string buildingId)
        {
            AddConstruction(buildingId, FixedNowUtc.AddSeconds(60));
        }

        private static void AddConstruction(
            string buildingId, DateTime completeAtUtc, bool completionNotified = false)
        {
            SaveSystem.Data.buildingConstructions.Add(new BuildingConstructionSaveState
            {
                buildingId = buildingId,
                startedAtUtc = SaveData.FormatTimestamp(FixedNowUtc),
                completeAtUtc = SaveData.FormatTimestamp(completeAtUtc),
                completionNotified = completionNotified,
            });
        }

        /// <summary>완성 시각을 읽을 수 없는 손상된 기록.</summary>
        private static void AddUnreadableConstruction(string buildingId)
        {
            SaveSystem.Data.buildingConstructions.Add(new BuildingConstructionSaveState
            {
                buildingId = buildingId,
                startedAtUtc = SaveData.FormatTimestamp(FixedNowUtc),
                completeAtUtc = "읽을 수 없는 값",
            });
        }

        /// <summary>컨트롤러의 시계를 시험이 미는 값으로 바꾼다. 실제 실행에서는 UTC 시계 하나뿐이며,
        /// 이 이음매는 "지금 몇 시인가"에 따라 결과가 달라지는 시험을 없애기 위한 것이다.</summary>
        private void SetClock(TownBuildingInteractionController controller)
        {
            FieldInfo field = typeof(TownBuildingInteractionController).GetField(
                "utcNowProvider", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "utcNowProvider를 찾지 못했습니다 - 시험이 실제 시각을 읽게 됩니다.");
            field.SetValue(controller, new Func<DateTime>(() => nowUtc));
        }

        /// <summary>완성 안내 번역이 도착한 상황을 그대로 재현한다.</summary>
        private static void DeliverCompletedMessage(Fixture fixture, string value)
        {
            MethodInfo method = typeof(TownBuildingInteractionController).GetMethod(
                "ApplyCompletedMessage", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            method.Invoke(fixture.Controller, new object[] { value });
        }

        /// <summary>서비스가 시작을 알린 것과 같은 경로를 재현한다.</summary>
        private static void RaiseConstructionStarted(Fixture fixture)
        {
            MethodInfo method = typeof(TownBuildingInteractionController).GetMethod(
                "HandleConstructionStarted", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            method.Invoke(fixture.Controller, new object[] { fixture.Building, null });
        }

        /// <summary>번역이 도착한 상황을 그대로 재현한다(Locale이 선택되어 있지 않아도 확인할 수 있다).</summary>
        private static void DeliverStartedMessage(Fixture fixture, string value)
        {
            MethodInfo method = typeof(TownBuildingInteractionController).GetMethod(
                "ApplyStartedMessage", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            method.Invoke(fixture.Controller, new object[] { value });
        }

        /// <summary>토스트 관리자 하나를 세운다. 템플릿이 없어 실제로 그리지는 않으므로 코루틴도 돌지
        /// 않는다 - 여기서 확인하는 것은 "안내를 몇 번 요청했는가"다.</summary>
        private ToastManager CreateToastManager()
        {
            var go = new GameObject("TestToastManager");
            created.Add(go);

            var manager = go.AddComponent<ToastManager>();
            LogAssert.Expect(LogType.Error, new Regex("template"));
            EditModeLifecycle.Invoke(manager, "Awake");
            toastManager = manager;
            return manager;
        }

        /// <summary>메모리 위의 가짜 저장소. 이 시험은 실제 저장 파일을 읽거나 쓰지 않는다.</summary>
        private sealed class FakeStorage : ISaveStorage
        {
            public int WriteCalls;

            /// <summary>쓰기를 실패시킨다 - "저장하지 못한 완성"의 경로를 확인하기 위한 스위치다.</summary>
            public bool WritesFail;

            public bool WritesBlocked => false;

            public string BlockedReason => null;

            public SaveReadResult ReadPrimary() => SaveReadResult.Missing("fake://primary");

            public SaveReadResult ReadBackup() => SaveReadResult.Missing("fake://backup");

            public SaveWriteResult Write(string text)
            {
                WriteCalls++;
                return WritesFail
                    ? SaveWriteResult.Failed("시험이 저장을 실패시켰습니다.")
                    : SaveWriteResult.Written(false);
            }

            public SaveQuarantineResult QuarantinePrimary(string reason) =>
                SaveQuarantineResult.Moved("fake://corrupted/primary");
        }
    }
}
