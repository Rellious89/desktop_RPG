using System.Collections.Generic;
using System.Reflection;
using Common;
using DesktopWindow;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CommonEditor.Tests
{
    public sealed class KeyInputPanelTests
    {
        private const string PanelPath = "Assets/Art/UI/Prefab/panel/pn_KeyInput.prefab";
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";

        [Test]
        public void Prefab_IsConfiguredForWrappedSubmitInput()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PanelPath);
            try
            {
                KeyInputPanel panel = root.GetComponent<KeyInputPanel>();
                Assert.IsNotNull(panel);
                Assert.IsTrue(panel.HasRequiredReferences);
                Assert.IsFalse(root.activeSelf, "입력 패널은 씬 시작 시 닫혀 있어야 합니다.");
                Assert.IsTrue(root.GetComponent<WindowInputRegion>()?.ReceiveMouseInput);

                TMP_InputField input = panel.InputField;
                Assert.AreEqual(TMP_InputField.LineType.MultiLineSubmit, input.lineType);
                Assert.IsFalse(input.richText);
                Assert.IsTrue(input.textComponent.enableWordWrapping);
                Assert.AreEqual(TextAlignmentOptions.TopLeft, input.textComponent.alignment);
                Assert.IsNotNull(input.textViewport.GetComponent<RectMask2D>());
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void Scene_WiresDungeonOnlyButtonToPanel()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid());

            KeyInputPanel[] panels = Object.FindObjectsOfType<KeyInputPanel>(true);
            Assert.AreEqual(1, panels.Length);
            GameObject buttonRoot = FindSceneObject("btn_KeyInput");
            Assert.IsNotNull(buttonRoot.GetComponent<Button>());
            Assert.IsTrue(buttonRoot.GetComponent<WindowInputRegion>()?.ReceiveMouseInput);

            ModalPanelOpener opener = buttonRoot.GetComponent<ModalPanelOpener>();
            Assert.IsNotNull(opener);
            SerializedObject openerSerialized = new SerializedObject(opener);
            Assert.AreSame(panels[0], openerSerialized.FindProperty("panel").objectReferenceValue);

            FieldModeMenuButtonVisibilityController[] controllers =
                Object.FindObjectsOfType<FieldModeMenuButtonVisibilityController>(true);
            Assert.AreEqual(1, controllers.Length);
            SerializedObject visibility = new SerializedObject(controllers[0]);
            SerializedProperty entries = visibility.FindProperty("buttons");
            SerializedProperty entry = FindEntry(entries, buttonRoot);
            Assert.IsNotNull(entry, "btn_KeyInput의 필드별 표시 항목이 필요합니다.");
            Assert.IsFalse(entry.FindPropertyRelative("showInTown").boolValue);
            Assert.IsTrue(entry.FindPropertyRelative("showInDungeon").boolValue);
            Assert.AreSame(panels[0], entry.FindPropertyRelative("panelToCloseWhenHidden").objectReferenceValue);
        }

        [Test]
        public void ScopedExclusion_IsReferenceCountedAndReversible()
        {
            Dictionary<KeyCode, int> counts = ScopedCounts();
            while (counts.ContainsKey(KeyCode.Return))
                GlobalKeyboardHook.ReleaseScopedExcludedKey(KeyCode.Return);

            try
            {
                GlobalKeyboardHook.AcquireScopedExcludedKey(KeyCode.Return);
                GlobalKeyboardHook.AcquireScopedExcludedKey(KeyCode.Return);
                Assert.AreEqual(2, counts[KeyCode.Return]);

                GlobalKeyboardHook.ReleaseScopedExcludedKey(KeyCode.Return);
                Assert.AreEqual(1, counts[KeyCode.Return]);

                GlobalKeyboardHook.ReleaseScopedExcludedKey(KeyCode.Return);
                Assert.IsFalse(counts.ContainsKey(KeyCode.Return));
            }
            finally
            {
                while (counts.ContainsKey(KeyCode.Return))
                    GlobalKeyboardHook.ReleaseScopedExcludedKey(KeyCode.Return);
            }
        }

        private static Dictionary<KeyCode, int> ScopedCounts()
        {
            FieldInfo field = typeof(GlobalKeyboardHook).GetField(
                "scopedExcludedKeyCounts", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            return (Dictionary<KeyCode, int>)field.GetValue(null);
        }

        private static SerializedProperty FindEntry(SerializedProperty entries, GameObject buttonRoot)
        {
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("buttonRoot").objectReferenceValue == buttonRoot) return entry;
            }
            return null;
        }

        private static GameObject FindSceneObject(string objectName)
        {
            Transform[] all = Object.FindObjectsOfType<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == objectName) return all[i].gameObject;
            return null;
        }
    }
}
