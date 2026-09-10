using System.Linq;
using Common;
using Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace InventoryEditor.Tests
{
    /// <summary>인벤토리 슬롯 호버 표시의 프리팹 배선과 생명주기를 고정한다.</summary>
    public sealed class InventorySlotHoverEffectTests
    {
        private const string SlotPrefabPath = "Assets/Art/UI/Prefab/Inventory/list_item.prefab";
        private const string InventoryPrefabPath = "Assets/Art/UI/Prefab/panel/pn_Inventory.prefab";
        private const string HoverClipPath =
            "Assets/Art/UI/PixelDesign/Pixel UI & HUD/Sprites/Selectors/Reticle_Hover_UI.anim";

        private GameObject slotInstance;
        private ItemDefinition firstItem;
        private ItemDefinition secondItem;

        [TearDown]
        public void TearDown()
        {
            if (slotInstance != null) Object.DestroyImmediate(slotInstance);
            if (firstItem != null) Object.DestroyImmediate(firstItem);
            if (secondItem != null) Object.DestroyImmediate(secondItem);
        }

        [Test]
        public void InventoryPrefab_UsesTheSharedSlotPrefabForAllThirtyTwoSlots()
        {
            GameObject panel = LoadPrefab(InventoryPrefabPath);
            InventorySlotView[] slots = panel.GetComponentsInChildren<InventorySlotView>(true);

            Assert.AreEqual(32, slots.Length, "pn_Inventory의 고정 슬롯 수가 달라졌습니다.");
            foreach (InventorySlotView slot in slots)
            {
                Object source = PrefabUtility.GetCorrespondingObjectFromSource(slot.gameObject);
                Assert.IsNotNull(source, $"'{slot.name}'이 원본 프리팹과 연결된 중첩 인스턴스가 아닙니다.");
                Assert.AreEqual(SlotPrefabPath, AssetDatabase.GetAssetPath(source),
                    $"'{slot.name}'이 공용 list_item 원본을 사용하지 않습니다.");
            }
        }

        [Test]
        public void SlotPrefab_HasAnInactiveNonBlockingUiHoverAnimator()
        {
            GameObject prefab = LoadPrefab(SlotPrefabPath);
            InventorySlotView slot = prefab.GetComponent<InventorySlotView>();
            var serialized = new SerializedObject(slot);
            var hover = serialized.FindProperty("hoverEffect").objectReferenceValue as GameObject;

            Assert.IsNotNull(hover, "InventorySlotView.hoverEffect가 연결되지 않았습니다.");
            Assert.AreEqual("sp_HoverEffect", hover.name);
            Assert.IsFalse(hover.activeSelf, "호버 효과는 슬롯의 초기 상태에서 꺼져 있어야 합니다.");

            Graphic[] graphics = hover.GetComponentsInChildren<Graphic>(true);
            Assert.IsNotEmpty(graphics);
            Assert.IsTrue(graphics.All(graphic => !graphic.raycastTarget),
                "호버 효과 Graphic이 포인터 입력을 가로채면 Exit/클릭 동작이 깨집니다.");

            Animator animator = hover.GetComponent<Animator>();
            Assert.IsNotNull(animator);
            Assert.AreEqual(AnimatorUpdateMode.UnscaledTime, animator.updateMode,
                "게임 시간이 멈춘 UI에서도 호버 애니메이션이 재생되어야 합니다.");
            Assert.IsNotNull(animator.runtimeAnimatorController);
            Assert.AreEqual("Reticle_Hover_UI", animator.runtimeAnimatorController.name);
        }

        [Test]
        public void HoverClip_AnimatesTheUiImageWithFourFramesAtTwelveFpsAndLoops()
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(HoverClipPath);
            Assert.IsNotNull(clip);
            Assert.AreEqual(12f, clip.frameRate);
            Assert.IsTrue(clip.isLooping);

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            Assert.AreEqual(1, bindings.Length);
            Assert.AreEqual(typeof(Image), bindings[0].type,
                "SpriteRenderer가 아니라 Unity UI Image의 sprite를 애니메이션해야 합니다.");
            Assert.AreEqual("m_Sprite", bindings[0].propertyName);

            ObjectReferenceKeyframe[] frames = AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]);
            Assert.AreEqual(4, frames.Length);
            CollectionAssert.AreEqual(new[] { 0f, 1f / 12f, 2f / 12f, 3f / 12f },
                frames.Select(frame => frame.time).ToArray(), new FloatComparer(0.00001f));
            Assert.IsTrue(frames.All(frame => frame.value is Sprite));
        }

        [Test]
        public void HoverEffect_OnlyShowsForAnItemAndStopsOnEveryRequiredExitPath()
        {
            slotInstance = Object.Instantiate(LoadPrefab(SlotPrefabPath));
            InventorySlotView slot = slotInstance.GetComponent<InventorySlotView>();
            GameObject hover = FindChild(slotInstance.transform, "sp_HoverEffect").gameObject;
            Image hoverImage = hover.GetComponent<Image>();
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(HoverClipPath);
            EditorCurveBinding binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
            ObjectReferenceKeyframe[] frames = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            firstItem = ScriptableObject.CreateInstance<ItemDefinition>();
            secondItem = ScriptableObject.CreateInstance<ItemDefinition>();

            slot.SetEmpty();
            slot.OnPointerEnter(null);
            Assert.IsFalse(hover.activeSelf, "빈 슬롯의 호버 효과는 표시되면 안 됩니다.");

            slot.SetItem(firstItem, 1);
            hoverImage.sprite = (Sprite)frames[3].value;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "부모에서 ItemTooltipController를 찾지 못해"));
            slot.OnPointerEnter(null);
            Assert.IsTrue(hover.activeSelf, "아이템이 있는 슬롯은 포인터 진입 시 효과를 표시해야 합니다.");
            Assert.AreSame(frames[0].value, hoverImage.sprite,
                "다시 진입할 때 이전 진행 위치가 아니라 첫 프레임부터 재생해야 합니다.");

            slot.OnPointerExit(null);
            Assert.IsFalse(hover.activeSelf, "포인터 이탈 시 효과를 즉시 숨겨야 합니다.");

            slot.OnPointerEnter(null);
            Assert.IsTrue(hover.activeSelf);
            slot.SetItem(secondItem, 1);
            Assert.IsFalse(hover.activeSelf, "호버 중 슬롯 내용이 바뀌면 이전 효과가 남으면 안 됩니다.");

            slot.OnPointerEnter(null);
            slot.SetEmpty();
            Assert.IsFalse(hover.activeSelf, "빈 슬롯으로 바뀌면 효과가 남으면 안 됩니다.");

            slot.SetItem(firstItem, 1);
            slot.OnPointerEnter(null);
            slotInstance.SetActive(false);
            Assert.IsFalse(hover.activeSelf, "슬롯이 비활성화될 때 효과의 self-active 상태도 정리해야 합니다.");
        }

        private static GameObject LoadPrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"'{path}'를 찾지 못했습니다.");
            return prefab;
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChild(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private sealed class FloatComparer : System.Collections.IComparer
        {
            private readonly float tolerance;

            public FloatComparer(float tolerance)
            {
                this.tolerance = tolerance;
            }

            public int Compare(object x, object y)
            {
                float difference = (float)x - (float)y;
                if (Mathf.Abs(difference) <= tolerance) return 0;
                return difference < 0f ? -1 : 1;
            }
        }
    }
}
