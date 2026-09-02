using Character;
using CharacterArchive;
using Common;
using Skill;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CharacterArchiveEditor
{
    /// <summary>13D CharacterInfo와 독립 스킬 행 프리팹의 Inspector 연결을 재현한다.</summary>
    public static class CharacterInfoUiPrefabSetup
    {
        private const string PanelPrefabPath = "Assets/Art/UI/Prefab/panel/pn_CharacterArchive.prefab";
        private const string SkillPrefabPath = "Assets/Art/UI/Prefab/Skill/list_Skill.prefab";

        [MenuItem("Tools/Keybuddy/Character Archive/Setup Character Info UI", priority = 121)]
        public static void Setup()
        {
            SetupSkillItem();
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(SkillPrefabPath, ImportAssetOptions.ForceUpdate);
            SetupPanel();
            AssetDatabase.SaveAssets();
        }

        private static void SetupSkillItem()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(SkillPrefabPath);
            try
            {
                SkillListItemView view = root.GetComponent<SkillListItemView>();
                if (view == null) view = root.AddComponent<SkillListItemView>();
                Image icon = Find(root.transform, "mask_portrait/sp_portrait").GetComponent<Image>();
                SerializedObject serialized = new SerializedObject(view);
                Set(serialized, "iconImage", icon);
                Set(serialized, "nameText", Find(root.transform, "sp_name/lb_SkillName").GetComponent<TMP_Text>());
                Set(serialized, "descriptionText", Find(root.transform, "sp_name/lb_SkillDescription").GetComponent<TMP_Text>());
                Set(serialized, "cooldownText", Find(root.transform, "sp_cooldown/lb_level").GetComponent<TMP_Text>());
                Set(serialized, "placeholderIcon", icon.sprite);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, SkillPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void SetupPanel()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PanelPrefabPath);
            try
            {
                CharacterArchivePanel panel = root.GetComponent<CharacterArchivePanel>();
                if (panel == null) throw new System.InvalidOperationException("CharacterArchivePanel을 찾을 수 없습니다.");

                Transform characterInfo = Find(root.transform, "pn_right/CharacterInfo");
                Transform baseInfo = Find(characterInfo, "BaseInfo");
                Transform baseFields = Find(baseInfo, "base_info");
                Transform characterModel = Find(baseInfo, "CharacterModel");
                Transform skillInfo = Find(characterInfo, "SkillInfo");
                Transform skillContent = FindDescendant(skillInfo, "list_SkillInfo");
                Transform empty = FindDescendant(skillContent, "lb_empty");
                Transform title = FindDescendant(skillInfo, "lb_title");
                SkillListItemView template = skillContent.GetComponentInChildren<SkillListItemView>(true);
                if (template == null) throw new System.InvalidOperationException("list_Skill 템플릿에 SkillListItemView가 없습니다.");
                template.gameObject.SetActive(false);

                ScrollRect scroll = skillInfo.GetComponent<ScrollRect>();
                if (scroll == null) throw new System.InvalidOperationException("SkillInfo ScrollRect가 없습니다.");
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                // ScrollRect는 list_SkillInfo의 위치를 직접 움직이면 안 된다. 그 자식은 아래 Content의
                // VerticalLayoutGroup이 배치하므로, ScrollRect가 부모 Content를 움직여야 두 소유자가
                // 같은 anchoredPosition을 되돌려 쓰지 않는다.
                scroll.content = skillContent.parent as RectTransform;
                if (scroll.content == null)
                    throw new System.InvalidOperationException("SkillInfo Content 부모 RectTransform이 없습니다.");

                VerticalLayoutGroup layout = skillContent.GetComponent<VerticalLayoutGroup>();
                if (layout == null) layout = skillContent.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                ContentSizeFitter fitter = skillContent.GetComponent<ContentSizeFitter>();
                if (fitter == null) fitter = skillContent.gameObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                CharacterInfoController controller = characterInfo.GetComponent<CharacterInfoController>();
                if (controller == null) controller = characterInfo.gameObject.AddComponent<CharacterInfoController>();
                SerializedObject serialized = new SerializedObject(controller);
                Set(serialized, "characterCatalog", AssetDatabase.LoadAssetAtPath<CharacterCatalog>(
                    "Assets/Generated/TableData/Character/CharacterCatalog.asset"));
                Set(serialized, "skillCatalog", AssetDatabase.LoadAssetAtPath<SkillCatalog>(
                    "Assets/Generated/TableData/Skill/SkillCatalog.asset"));
                Set(serialized, "characterSkillCatalog", AssetDatabase.LoadAssetAtPath<CharacterSkillCatalog>(
                    "Assets/Generated/TableData/CharacterSkill/CharacterSkillCatalog.asset"));
                Set(serialized, "characterModelImage", characterModel.GetComponentInChildren<Image>(true));
                Set(serialized, "characterNameText", Find(baseFields, "lb_Name").GetComponent<TMP_Text>());
                Set(serialized, "levelText", Find(baseFields, "lb_level").GetComponent<TMP_Text>());
                Set(serialized, "originWorldText", Find(baseFields, "lb_originWorld").GetComponent<TMP_Text>());
                Set(serialized, "skillTitleText", title.GetComponent<TMP_Text>());
                Set(serialized, "skillTitleLocalizer", title.GetComponent<LocalizedTMPText>());
                Set(serialized, "emptyState", empty.gameObject);
                Set(serialized, "skillContent", skillContent.GetComponent<RectTransform>());
                Set(serialized, "skillTemplate", template);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                LocalizedTMPText titleLocalizer = title.GetComponent<LocalizedTMPText>();
                if (titleLocalizer == null) throw new System.InvalidOperationException("스킬 제목의 LocalizedTMPText가 없습니다.");
                titleLocalizer.enabled = false;

                SerializedObject panelSerialized = new SerializedObject(panel);
                Set(panelSerialized, "characterInfoUi", controller);
                panelSerialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Transform Find(Transform root, string path)
        {
            Transform found = root.Find(path);
            if (found == null) throw new System.InvalidOperationException("프리팹 경로를 찾지 못했습니다: " + path);
            return found;
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            if (root.name == objectName) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDescendantOrNull(child, objectName);
                if (found != null) return found;
            }
            throw new System.InvalidOperationException("프리팹 하위 오브젝트를 찾지 못했습니다: " + objectName);
        }

        private static Transform FindDescendantOrNull(Transform root, string objectName)
        {
            if (root.name == objectName) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDescendantOrNull(child, objectName);
                if (found != null) return found;
            }
            return null;
        }

        private static void Set(SerializedObject target, string propertyName, Object value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null) throw new System.InvalidOperationException("직렬화 필드가 없습니다: " + propertyName);
            property.objectReferenceValue = value;
        }
    }
}
