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
                Set(serialized, "cooldownText", Find(root.transform, "sp_cooldown/lb_colldown").GetComponent<TMP_Text>());
                Set(serialized, "unlockedNameRoot", Find(root.transform, "sp_name").gameObject);
                Set(serialized, "cooldownRoot", Find(root.transform, "sp_cooldown").gameObject);
                Transform lockInfo = Find(root.transform, "LockInfo");
                Set(serialized, "lockedInfoRoot", lockInfo.gameObject);
                Transform lockContent = Find(lockInfo, "LockInfo");
                Set(serialized, "lockedNameText", Find(lockContent, "lb_SkillName_Lock").GetComponent<TMP_Text>());
                Transform lockedDescription = Find(lockContent, "lb_SkillDescription_Lock");
                Set(serialized, "lockedDescriptionText", lockedDescription.GetComponent<TMP_Text>());
                LocalizedTMPText lockedDescriptionLocalizer = lockedDescription.GetComponent<LocalizedTMPText>();
                if (lockedDescriptionLocalizer == null) lockedDescriptionLocalizer = lockedDescription.gameObject.AddComponent<LocalizedTMPText>();
                SetLocalizedReference(lockedDescriptionLocalizer, "32fd067a20b754a50b20446b9c78d2ae", 14349990280290304);
                lockedDescriptionLocalizer.enabled = false;
                Set(serialized, "lockedDescriptionLocalizer", lockedDescriptionLocalizer);
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
                Transform count = FindDescendant(skillInfo, "lb_count");
                SkillListItemView template = skillContent.GetComponentInChildren<SkillListItemView>(true);
                if (template == null) throw new System.InvalidOperationException("list_Skill 템플릿에 SkillListItemView가 없습니다.");
                template.gameObject.SetActive(false);

                // 제목/카운트는 목록의 상태가 아니라 SkillInfo 자체의 고정 헤더다. Content의
                // VerticalLayoutGroup에서 분리해 ScrollRect가 움직이는 영역에 포함되지 않게 한다.
                title.SetParent(skillInfo, false);
                RectTransform titleRect = title as RectTransform;
                if (titleRect == null) throw new System.InvalidOperationException("스킬 제목 RectTransform이 없습니다.");
                titleRect.anchorMin = new Vector2(0f, 1f);
                titleRect.anchorMax = new Vector2(0f, 1f);
                titleRect.pivot = new Vector2(0f, 1f);
                titleRect.anchoredPosition = new Vector2(12f, -2f);
                title.SetAsLastSibling();

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

                RectTransform viewport = scroll.viewport;
                if (viewport == null) throw new System.InvalidOperationException("SkillInfo Viewport가 없습니다.");
                // 고정 헤더(12)와 간격(4)을 비워 목록 viewport가 제목과 겹치지 않게 한다.
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.pivot = new Vector2(.5f, .5f);
                viewport.offsetMin = Vector2.zero;
                viewport.offsetMax = new Vector2(0f, -16f);

                // Viewport는 mask만 소유한다. 남아 있던 중첩 ScrollRect는 다른 content를
                // 가리켜 pointer/scroll ownership을 혼동시키므로 제거한다.
                ScrollRect nestedScroll = viewport.GetComponent<ScrollRect>();
                if (nestedScroll != null) Object.DestroyImmediate(nestedScroll);

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
                Set(serialized, "skillCountText", count.GetComponent<TMP_Text>());
                Set(serialized, "skillCountLocalizer", count.GetComponent<LocalizedTMPText>());
                Set(serialized, "emptyState", empty.gameObject);
                Set(serialized, "skillContent", skillContent.GetComponent<RectTransform>());
                Set(serialized, "skillTemplate", template);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                LocalizedTMPText titleLocalizer = title.GetComponent<LocalizedTMPText>();
                if (titleLocalizer == null) throw new System.InvalidOperationException("스킬 제목의 LocalizedTMPText가 없습니다.");
                titleLocalizer.enabled = true;
                LocalizedTMPText countLocalizer = count.GetComponent<LocalizedTMPText>();
                if (countLocalizer == null) throw new System.InvalidOperationException("스킬 카운트의 LocalizedTMPText가 없습니다.");
                countLocalizer.enabled = false;

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

        private static void SetLocalizedReference(LocalizedTMPText target, string tableGuid, long keyId)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty text = serialized.FindProperty("text");
            if (text == null) throw new System.InvalidOperationException("LocalizedTMPText.text가 없습니다.");
            text.FindPropertyRelative("m_TableReference").FindPropertyRelative("m_TableCollectionName").stringValue =
                "GUID:" + tableGuid;
            SerializedProperty entry = text.FindPropertyRelative("m_TableEntryReference");
            entry.FindPropertyRelative("m_KeyId").longValue = keyId;
            entry.FindPropertyRelative("m_Key").stringValue = string.Empty;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
