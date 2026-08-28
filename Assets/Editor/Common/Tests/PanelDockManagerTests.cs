using System.Collections.Generic;
using Common;
using DesktopWindow;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CommonEditor.Tests
{
    /// <summary>Panel_UI 공용 패널의 1:1 좌우 도킹 규칙을 Play Mode 없이 확인한다.</summary>
    public sealed class PanelDockManagerTests
    {
        private readonly List<Object> created = new List<Object>();
        private GameObject canvasObject;
        private RectTransform panelUi;
        private PanelDockManager manager;

        [SetUp]
        public void SetUp()
        {
            canvasObject = Create("Canvas", null);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(1000f, 800f);

            panelUi = Create("Panel_UI", canvasObject.transform).GetComponent<RectTransform>();
            panelUi.anchorMin = Vector2.zero;
            panelUi.anchorMax = Vector2.one;
            panelUi.offsetMin = Vector2.zero;
            panelUi.offsetMax = Vector2.zero;
            manager = panelUi.gameObject.AddComponent<PanelDockManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
            canvasObject = null;
            created.Clear();
        }

        [Test]
        public void 좌우_스냅은_상단과_공용_엣지를_정확히_맞춘다()
        {
            PanelDragHandle a = CreatePanel("A", new Vector2(90f, 60f));
            PanelDragHandle b = CreatePanel("B", new Vector2(200f, 0f));

            DragToEnd(a);

            Assert.AreEqual(1, manager.LinkCount);
            AssertBoundsTouchAndTopAlign(a, b);
        }

        [Test]
        public void SnapDistance_밖이면_연결하지_않는다()
        {
            PanelDragHandle a = CreatePanel("A", new Vector2(0f, 0f));
            CreatePanel("B", new Vector2(180f, 0f));

            DragToEnd(a);

            Assert.AreEqual(0, manager.LinkCount);
        }

        [Test]
        public void 연결된_AB에는_C를_추가할수_없고_CD는_독립적으로_연결된다()
        {
            PanelDragHandle a = CreatePanel("A", new Vector2(90f, 60f));
            PanelDragHandle b = CreatePanel("B", new Vector2(200f, 0f));
            PanelDragHandle c = CreatePanel("C", new Vector2(310f, 0f));
            PanelDragHandle d = CreatePanel("D", new Vector2(600f, 0f));

            DragToEnd(a);
            Assert.AreEqual(1, manager.LinkCount);

            DragToEnd(c);
            Assert.AreEqual(1, manager.LinkCount, "C는 이미 연결된 B에 추가로 붙을 수 없다.");

            d.TargetPanel.anchoredPosition = new Vector2(420f, 60f);
            c.TargetPanel.anchoredPosition = new Vector2(310f, 0f);
            DragToEnd(c);
            Assert.AreEqual(2, manager.LinkCount, "C-D는 A-B와 독립적인 1:1 링크가 된다.");
            Assert.IsTrue(manager.IsDocked(b));
            Assert.IsTrue(manager.IsDocked(d));
        }

        [Test]
        public void 개별_드래그는_DetachDistance를_넘긴_뒤_점프없이_링크를_해제한다()
        {
            PanelDragHandle a = CreatePanel("A", new Vector2(90f, 60f));
            PanelDragHandle b = CreatePanel("B", new Vector2(200f, 0f));
            DragToEnd(a);
            Vector2 snapped = a.TargetPanel.anchoredPosition;

            manager.BeginPanelDrag(a);
            manager.MovePanelDuringDrag(a, snapped + new Vector2(47f, 0f));
            Assert.AreEqual(snapped, a.TargetPanel.anchoredPosition, "임계값 전에는 붙은 위치를 유지한다.");

            Vector2 detached = snapped + new Vector2(49f, 0f);
            a.TargetPanel.anchoredPosition = detached;
            manager.MovePanelDuringDrag(a, detached);
            manager.EndPanelDrag(a);

            Assert.AreEqual(0, manager.LinkCount);
            Assert.AreEqual(detached, a.TargetPanel.anchoredPosition, "링크만 해제하고 계산된 개별 위치는 보존한다.");
            Assert.IsFalse(manager.IsDocked(b));
        }

        [Test]
        public void DockHandle은_두_패널에_같은_이동량을_적용하고_제3패널과_스냅하지_않는다()
        {
            PanelDragHandle a = CreatePanel("A", new Vector2(90f, 60f));
            PanelDragHandle b = CreatePanel("B", new Vector2(200f, 0f));
            CreatePanel("C", new Vector2(330f, 0f));
            DragToEnd(a);
            DockHandleDrag dockHandle = panelUi.GetComponentInChildren<DockHandleDrag>();
            Vector2 aStart = a.TargetPanel.anchoredPosition;
            Vector2 bStart = b.TargetPanel.anchoredPosition;

            manager.BeginDockHandleDrag(dockHandle);
            manager.MoveDockHandleDrag(dockHandle, new Vector2(30f, -20f));
            manager.EndDockHandleDrag(dockHandle);

            Assert.AreEqual(new Vector2(30f, -20f), a.TargetPanel.anchoredPosition - aStart);
            Assert.AreEqual(a.TargetPanel.anchoredPosition - aStart, b.TargetPanel.anchoredPosition - bStart);
            RectTransform anchor = panelUi.Find("DockHandleAnchor") as RectTransform;
            Assert.AreEqual(180f, anchor.anchoredPosition.x, 0.01f);
            Assert.AreEqual(-20f, anchor.anchoredPosition.y, 0.01f,
                "페어 이동 중 anchor도 동일한 공용 엣지를 계속 추적한다.");
            Assert.AreEqual(1, manager.LinkCount, "페어 이동은 다른 패널을 탐색하거나 스냅하지 않는다.");
        }

        [Test]
        public void DockHandle_연속_누적드래그는_시작위치_기준으로_실제경계까지_따라간다()
        {
            PanelDragHandle a = CreatePanel("A", new Vector2(90f, 60f));
            PanelDragHandle b = CreatePanel("B", new Vector2(200f, 0f));
            DragToEnd(a);
            DockHandleDrag dockHandle = panelUi.GetComponentInChildren<DockHandleDrag>();
            Vector2 aStart = a.TargetPanel.anchoredPosition;
            Vector2 bStart = b.TargetPanel.anchoredPosition;

            manager.BeginDockHandleDrag(dockHandle);
            foreach (float deltaX in new[] { 50f, 100f, 150f, 200f, 250f })
            {
                manager.MoveDockHandleDrag(dockHandle, new Vector2(deltaX, 0f));
                Assert.AreEqual(deltaX, a.TargetPanel.anchoredPosition.x - aStart.x, 0.01f,
                    "같은 드래그의 누적 요청은 시작 위치 기준으로 적용되어야 한다.");
                Assert.AreEqual(a.TargetPanel.anchoredPosition - aStart, b.TargetPanel.anchoredPosition - bStart);
            }

            manager.MoveDockHandleDrag(dockHandle, new Vector2(1000f, 0f));
            Assert.AreEqual(250f, a.TargetPanel.anchoredPosition.x - aStart.x, 0.01f,
                "오른쪽 실제 Canvas 경계에서만 제한한다.");

            manager.MoveDockHandleDrag(dockHandle, new Vector2(200f, 0f));
            Assert.AreEqual(200f, a.TargetPanel.anchoredPosition.x - aStart.x, 0.01f,
                "경계에 닿은 뒤 반대 방향 요청은 같은 드래그에서 즉시 반영되어야 한다.");
            Assert.AreEqual(a.TargetPanel.anchoredPosition - aStart, b.TargetPanel.anchoredPosition - bStart);
            manager.EndDockHandleDrag(dockHandle);
        }

        [Test]
        public void 비활성화_시_링크와_핸들을_즉시_정리한다()
        {
            PanelDragHandle a = CreatePanel("A", new Vector2(90f, 60f));
            CreatePanel("B", new Vector2(200f, 0f));
            DragToEnd(a);
            Assert.IsNotNull(panelUi.GetComponentInChildren<DockHandleDrag>());

            a.gameObject.SetActive(false);
            // EditMode에서는 일반 MonoBehaviour의 OnDisable 메시지가 실행 루프 밖에서 호출되지 않을 수
            // 있으므로, PanelDragHandle이 실제로 전달하는 즉시 정리 신호를 직접 보낸다.
            manager.NotifyPanelUnavailable(a);

            Assert.AreEqual(0, manager.LinkCount);
            Assert.IsNull(panelUi.GetComponentInChildren<DockHandleDrag>(), "파괴 예약 대신 즉시 비활성화하여 입력을 남기지 않는다.");
        }

        [Test]
        public void fallback_핸들은_프리팹없이_Image와_입력영역을_만든다()
        {
            PanelDragHandle a = CreatePanel("A", new Vector2(90f, 60f));
            CreatePanel("B", new Vector2(200f, 0f));
            DragToEnd(a);

            DockHandleDrag handle = panelUi.GetComponentInChildren<DockHandleDrag>();
            Assert.IsNotNull(handle.GetComponent<Image>());
            Assert.IsTrue(handle.GetComponent<WindowInputRegion>().ReceiveMouseInput);
            Assert.AreEqual(new Vector2(16f, 48f), (handle.transform as RectTransform).sizeDelta);
            Assert.AreEqual(new Color(0.25f, 0.8f, 1f, 0.9f), handle.GetComponent<Image>().color);
            RectTransform anchor = panelUi.Find("DockHandleAnchor") as RectTransform;
            Assert.AreEqual(150f, anchor.anchoredPosition.x, 0.01f);
            Assert.AreEqual(0f, anchor.anchoredPosition.y, 0.01f,
                "기본 alignment=0.5는 기존 공용 엣지 중앙을 유지한다.");
        }

        [Test]
        public void DockHandleAnchor는_공용엣지_정렬과_관리자_오프셋을_적용한다()
        {
            SetDockHandleLayout(alignment: 1f, offset: new Vector2(7f, 13f));
            PanelDragHandle a = CreatePanel("A", new Vector2(90f, 60f));
            PanelDragHandle b = CreatePanel("B", new Vector2(200f, 0f));
            DragToEnd(a);

            RectTransform anchor = panelUi.Find("DockHandleAnchor") as RectTransform;
            Assert.IsNotNull(anchor);
            Assert.AreEqual(157f, anchor.anchoredPosition.x, 0.01f,
                "alignment=1은 공용 세로 구간의 상단에 manager offset을 더한다.");
            Assert.AreEqual(63f, anchor.anchoredPosition.y, 0.01f,
                "alignment=1은 공용 세로 구간의 상단에 manager offset을 더한다.");
        }

        [Test]
        public void CustomDockHandlePrefab의_RectTransform_디자인값과_기존입력컴포넌트를_보존한다()
        {
            GameObject prefabObject = Create("DesignedHandle", null);
            RectTransform prefab = prefabObject.GetComponent<RectTransform>();
            prefab.anchorMin = prefab.anchorMax = new Vector2(0.25f, 0.75f);
            prefab.pivot = new Vector2(0.2f, 0.8f);
            prefab.sizeDelta = new Vector2(31f, 73f);
            prefab.anchoredPosition = new Vector2(11f, -19f);
            prefab.localScale = new Vector3(1.5f, 0.75f, 1f);
            prefab.localRotation = Quaternion.Euler(0f, 0f, 15f);
            DockHandleDrag authoredDrag = prefabObject.AddComponent<DockHandleDrag>();
            prefabObject.AddComponent<WindowInputRegion>();
            SetDockHandlePrefab(prefab);

            PanelDragHandle a = CreatePanel("A", new Vector2(90f, 60f));
            CreatePanel("B", new Vector2(200f, 0f));
            DragToEnd(a);

            DockHandleDrag handle = panelUi.GetComponentInChildren<DockHandleDrag>();
            RectTransform instance = handle.transform as RectTransform;
            Assert.AreNotSame(authoredDrag, handle);
            Assert.AreEqual(prefab.anchorMin, instance.anchorMin);
            Assert.AreEqual(prefab.anchorMax, instance.anchorMax);
            Assert.AreEqual(prefab.pivot, instance.pivot);
            Assert.AreEqual(prefab.sizeDelta, instance.sizeDelta);
            Assert.AreEqual(prefab.anchoredPosition, instance.anchoredPosition);
            Assert.AreEqual(prefab.localScale, instance.localScale);
            Assert.AreEqual(prefab.localRotation, instance.localRotation);
            Assert.AreEqual(1, instance.GetComponents<WindowInputRegion>().Length,
                "프리팹의 입력 영역을 재사용하고 중복 생성하지 않는다.");
            Assert.IsTrue(instance.GetComponent<WindowInputRegion>().ReceiveMouseInput);

            manager.NotifyPanelUnavailable(a);
            Assert.IsNull(panelUi.Find("DockHandleAnchor"), "링크 해제 시 anchor와 handle을 함께 제거한다.");
        }

        [Test]
        public void 기존_핸들_포커스와_화면제한은_유지된다()
        {
            GameObject eventSystemObject = Create("EventSystem", null);
            EventSystem eventSystem = eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();
            PanelDragHandle back = CreatePanel("Back", new Vector2(0f, 0f));
            PanelDragHandle front = CreatePanel("Front", new Vector2(0f, 0f));

            back.OnPointerDown(new PointerEventData(eventSystem));

            Assert.Greater(back.TargetPanel.GetSiblingIndex(), front.TargetPanel.GetSiblingIndex());
            Vector2 limited = back.ClampMoveDelta(new Vector2(10000f, 0f));
            Assert.Less(limited.x, 10000f, "기존 화면 이탈 제한은 도킹에서도 재사용된다.");
        }

        private PanelDragHandle CreatePanel(string name, Vector2 position)
        {
            GameObject panel = Create(name, panelUi);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(100f, 100f);
            rect.anchoredPosition = position;
            PanelDragHandle drag = panel.AddComponent<PanelDragHandle>();
            SerializedObject serialized = new SerializedObject(drag);
            serialized.FindProperty("targetPanel").objectReferenceValue = rect;
            serialized.FindProperty("keepInsideRect").objectReferenceValue = rect;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return drag;
        }

        private void DragToEnd(PanelDragHandle handle)
        {
            manager.BeginPanelDrag(handle);
            manager.EndPanelDrag(handle);
        }

        private void SetDockHandleLayout(float alignment, Vector2 offset)
        {
            SerializedObject serialized = new SerializedObject(manager);
            serialized.FindProperty("dockHandleVerticalAlignment").floatValue = alignment;
            serialized.FindProperty("dockHandlePositionOffset").vector2Value = offset;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private void SetDockHandlePrefab(RectTransform prefab)
        {
            SerializedObject serialized = new SerializedObject(manager);
            serialized.FindProperty("dockHandlePrefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssertBoundsTouchAndTopAlign(PanelDragHandle a, PanelDragHandle b)
        {
            Vector3[] aCorners = new Vector3[4];
            Vector3[] bCorners = new Vector3[4];
            a.TargetPanel.GetWorldCorners(aCorners);
            b.TargetPanel.GetWorldCorners(bCorners);
            Assert.AreEqual(aCorners[2].y, bCorners[2].y, 0.01f);
            bool aOnLeft = Mathf.Abs(aCorners[2].x - bCorners[0].x) < 0.01f;
            bool bOnLeft = Mathf.Abs(bCorners[2].x - aCorners[0].x) < 0.01f;
            Assert.IsTrue(aOnLeft || bOnLeft);
        }

        private GameObject Create(string name, Transform parent)
        {
            GameObject value = new GameObject(name, typeof(RectTransform));
            value.transform.SetParent(parent, false);
            created.Add(value);
            return value;
        }
    }
}
