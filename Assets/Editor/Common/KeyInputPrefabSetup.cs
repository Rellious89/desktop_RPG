using Common;
using DesktopWindow;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CommonEditor
{
    /// <summary>타이핑 패드 프리팹과 desktopScene_ReSize의 버튼/필드 표시 연결을 반복 실행 가능하게 구성한다.</summary>
    public static class KeyInputPrefabSetup
    {
        private const string PanelPrefabPath = "Assets/Art/UI/Prefab/panel/pn_KeyInput.prefab";
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";

        [MenuItem("Tools/Keybuddy/Common/Setup Key Input Panel", priority = 140)]
        public static void Setup()
        {
            SetupPanelPrefab();
            AssetDatabase.SaveAssets();
            SetupScene();
            AssetDatabase.SaveAssets();
        }

        private static void SetupPanelPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PanelPrefabPath);
            try
            {
                TMP_InputField inputField = FindDescendant(root.transform, "InputField (TMP)")
                    .GetComponent<TMP_InputField>();
                if (inputField == null)
                    throw new System.InvalidOperationException("InputField (TMP)에 TMP_InputField가 없습니다.");

                inputField.lineType = TMP_InputField.LineType.MultiLineSubmit;
                inputField.richText = false;
                if (inputField.textComponent == null)
                    throw new System.InvalidOperationException("TMP_InputField의 Text Component가 연결되지 않았습니다.");
                inputField.textComponent.enableWordWrapping = true;
                inputField.textComponent.alignment = TextAlignmentOptions.TopLeft;

                RectMask2D viewportMask = inputField.textViewport != null
                    ? inputField.textViewport.GetComponent<RectMask2D>()
                    : null;
                if (viewportMask == null)
                    throw new System.InvalidOperationException("TMP_InputField의 Text Viewport에 RectMask2D가 필요합니다.");

                KeyInputPanel panel = root.GetComponent<KeyInputPanel>();
                if (panel == null) panel = root.AddComponent<KeyInputPanel>();
                SerializedObject serialized = new SerializedObject(panel);
                Set(serialized, "inputField", inputField);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                WindowInputRegion region = root.GetComponent<WindowInputRegion>();
                if (region == null) region = root.AddComponent<WindowInputRegion>();
                region.ReceiveMouseInput = true;

                root.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void SetupScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            GameObject buttonRoot = FindUniqueSceneObject("btn_KeyInput");
            Button button = buttonRoot.GetComponent<Button>();
            if (button == null) throw new System.InvalidOperationException("btn_KeyInput에 Button이 없습니다.");

            WindowInputRegion buttonRegion = buttonRoot.GetComponent<WindowInputRegion>();
            if (buttonRegion == null) buttonRegion = buttonRoot.AddComponent<WindowInputRegion>();
            buttonRegion.ReceiveMouseInput = true;

            KeyInputPanel[] panels = Object.FindObjectsOfType<KeyInputPanel>(true);
            if (panels.Length != 1)
                throw new System.InvalidOperationException("씬의 KeyInputPanel은 정확히 하나여야 합니다: " + panels.Length);
            KeyInputPanel panel = panels[0];
            panel.gameObject.SetActive(false);

            ModalPanelOpener opener = buttonRoot.GetComponent<ModalPanelOpener>();
            if (opener == null) opener = buttonRoot.AddComponent<ModalPanelOpener>();
            SerializedObject openerSerialized = new SerializedObject(opener);
            Set(openerSerialized, "panel", panel);
            openerSerialized.ApplyModifiedPropertiesWithoutUndo();

            FieldModeMenuButtonVisibilityController[] visibilityControllers =
                Object.FindObjectsOfType<FieldModeMenuButtonVisibilityController>(true);
            if (visibilityControllers.Length != 1)
                throw new System.InvalidOperationException(
                    "씬의 FieldModeMenuButtonVisibilityController는 정확히 하나여야 합니다: " +
                    visibilityControllers.Length);
            ConfigureVisibilityEntry(visibilityControllers[0], buttonRoot, panel);

            // 최초 OnEnable에서 현재 필드 상태로 동기화되기 전에도 마을 화면에 잠깐 나타나지 않게 한다.
            buttonRoot.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureVisibilityEntry(
            FieldModeMenuButtonVisibilityController controller,
            GameObject buttonRoot,
            KeyInputPanel panel)
        {
            SerializedObject serialized = new SerializedObject(controller);
            SerializedProperty entries = serialized.FindProperty("buttons");
            if (entries == null) throw new System.InvalidOperationException("buttons 직렬화 필드를 찾지 못했습니다.");

            SerializedProperty target = null;
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty candidate = entries.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative("buttonRoot").objectReferenceValue == buttonRoot)
                {
                    target = candidate;
                    break;
                }
            }

            if (target == null)
            {
                int index = entries.arraySize;
                entries.InsertArrayElementAtIndex(index);
                target = entries.GetArrayElementAtIndex(index);
            }

            target.FindPropertyRelative("label").stringValue = "Key Input";
            target.FindPropertyRelative("buttonRoot").objectReferenceValue = buttonRoot;
            target.FindPropertyRelative("showInTown").boolValue = false;
            target.FindPropertyRelative("showInDungeon").boolValue = true;
            target.FindPropertyRelative("panelToCloseWhenHidden").objectReferenceValue = panel;
            target.FindPropertyRelative("requiredBuildingId").stringValue = string.Empty;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject FindUniqueSceneObject(string objectName)
        {
            Transform[] all = Object.FindObjectsOfType<Transform>(true);
            GameObject result = null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name != objectName) continue;
                if (result != null)
                    throw new System.InvalidOperationException("씬에 같은 이름의 오브젝트가 둘 이상입니다: " + objectName);
                result = all[i].gameObject;
            }

            if (result == null) throw new System.InvalidOperationException("씬 오브젝트를 찾지 못했습니다: " + objectName);
            return result;
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == objectName) return all[i];
            throw new System.InvalidOperationException(root.name + " 아래에서 찾지 못했습니다: " + objectName);
        }

        private static void Set(SerializedObject target, string propertyName, Object value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null) throw new System.InvalidOperationException("직렬화 필드가 없습니다: " + propertyName);
            property.objectReferenceValue = value;
        }
    }
}
