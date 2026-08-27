using Building;
using Common;
using Field;
using Inventory;
using Shop.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Shop.Editor
{
    /// <summary>12C의 직렬화 연결을 재현 가능하게 적용하는 일회성 저작 도구.</summary>
    public static class ShopSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";
        private const string PanelPath = "Assets/Art/UI/Prefab/panel/pn_Shop.prefab";

        public static void Apply()
        {
            ConfigurePanel();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject interaction = Find("Interaction_Shop");
            GameObject shopButton = Find("btn_shop");
            GameObject shopSlot = Find("ShopSlot");
            ShopPanel panel = Find("pn_Shop").GetComponent<ShopPanel>();

            var construction = interaction.GetComponent<TownBuildingInteractionController>() ??
                               interaction.AddComponent<TownBuildingInteractionController>();
            var so = new SerializedObject(construction);
            Set(so, "fieldModeManager", Object.FindObjectOfType<FieldModeManager>());
            Set(so, "transitionSequencer", Object.FindObjectOfType<FieldTransitionSequencer>());
            Set(so, "stageCamera", Find("Main Camera").GetComponent<Camera>());
            Set(so, "uiAnchor", shopSlot.transform.Find("UIAnchor"));
            Set(so, "interactionRoot", Find("TownInteractionLayer"));
            Set(so, "interactionParent", interaction.GetComponent<RectTransform>());
            Set(so, "buildButton", Child(interaction.transform, "btn_Build_Shop").GetComponent<Button>());
            Set(so, "openInnButton", Child(interaction.transform, "btn_Open_Shop"));
            Set(so, "completionButton", Child(interaction.transform, "btn_Open_Shop").GetComponent<Button>());
            GameObject timer = Child(interaction.transform, "pn_ConstructionTimer");
            Set(so, "constructionTimerRoot", timer);
            Set(so, "constructionTimerText", Child(timer.transform, "lb_ConstructionTimer").GetComponent<TMPro.TextMeshProUGUI>());
            Set(so, "constructionTimerAnimator", timer.GetComponentInChildren<Animator>(true));
            Set(so, "buildingPopup", Object.FindObjectOfType<BuildingPopupPanel>(true));
            Set(so, "building", AssetDatabase.LoadAssetAtPath<BuildingDefinition>("Assets/Generated/TableData/Building/Building_3.asset"));
            Set(so, "inventoryManager", Find("InventoryManager").GetComponent<InventoryManager>());
            so.ApplyModifiedPropertiesWithoutUndo();

            var opener = shopButton.GetComponent<ModalPanelOpener>() ?? shopButton.AddComponent<ModalPanelOpener>();
            so = new SerializedObject(opener); Set(so, "panel", panel); so.ApplyModifiedPropertiesWithoutUndo();

            FieldModeMenuButtonVisibilityController menu = Find("MainMenu").GetComponent<FieldModeMenuButtonVisibilityController>();
            so = new SerializedObject(menu);
            SerializedProperty entries = so.FindProperty("buttons");
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("buttonRoot").objectReferenceValue == shopButton)
                    entry.FindPropertyRelative("panelToCloseWhenHidden").objectReferenceValue = panel;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigurePanel()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PanelPath);
            var panel = root.GetComponent<ShopPanel>() ?? root.AddComponent<ShopPanel>();
            var so = new SerializedObject(panel);
            Set(so, "shopCatalog", AssetDatabase.LoadAssetAtPath<ShopCatalog>("Assets/Generated/TableData/Shop/ShopCatalog.asset"));
            Set(so, "productCatalog", AssetDatabase.LoadAssetAtPath<ShopProductCatalog>("Assets/Generated/TableData/ShopProduct/ShopProductCatalog.asset"));
            Set(so, "itemCatalog", AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/Generated/TableData/Item/ItemCatalog.asset"));
            Set(so, "currencyCatalog", AssetDatabase.LoadAssetAtPath<CurrencyCatalog>("Assets/Generated/TableData/Currency/CurrencyCatalog.asset"));
            Set(so, "itemRowPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/UI/Prefab/Shop/list_ShopItem.prefab"));
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PanelPath); PrefabUtility.UnloadPrefabContents(root);
        }

        private static void Set(SerializedObject so, string field, Object value) => so.FindProperty(field).objectReferenceValue = value;
        private static GameObject Find(string name)
        {
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Transform t = Child(root.transform, name); if (t != null) return t.gameObject;
            }
            throw new System.InvalidOperationException("Missing scene object: " + name);
        }
        private static GameObject Child(Transform root, string name)
        {
            if (root.name == name) return root.gameObject;
            for (int i = 0; i < root.childCount; i++) { GameObject found = Child(root.GetChild(i), name); if (found != null) return found; }
            return null;
        }
    }
}
