using System;
using System.Collections.Generic;
using System.IO;
using Enemy;
using Inventory;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace TableDataEditor
{
    /// <summary>이름/ID로 에셋을 찾은 결과. 개수를 함께 돌려주어 진단 문구에 "몇 개를 찾았는지"를 쓴다.</summary>
    public enum AssetLookupResult
    {
        NotFound = 0,
        Found = 1,
        Ambiguous = 2,
    }

    /// <summary>
    /// CSV의 키 문자열을 프로젝트의 실제 에셋으로 <b>정확히 일치하는 이름</b>으로만 찾아 주는 인덱스.
    /// 전부 <see cref="AssetDatabase"/> 정식 API로만 조회하며 <b>어떤 에셋도 수정하지 않는다</b> -
    /// MonsterMotionProfile / Sprite / 수동 ItemDefinition은 사람이 만든 자산이고 임포터의 입력일 뿐이다.
    ///
    /// <b>부분 일치를 절대 허용하지 않는다.</b> <c>FindAssets</c>의 이름 필터는 부분 일치라
    /// 후보를 좁히는 데만 쓰고, 최종 판정은 언제나 <c>name</c>의 Ordinal 완전 일치다. 같은 이름이
    /// 둘 이상이면 어느 쪽을 쓸지 임의로 정하지 않고 <see cref="AssetLookupResult.Ambiguous"/>로 돌려
    /// 호출하는 쪽이 오류로 보고하게 한다.
    ///
    /// <b>Sprite 이름은 파일 이름과 다를 수 있다.</b> 스프라이트 시트로 잘린 이미지는 하위 에셋마다
    /// 이름이 따로 있으므로, 파일 이름만 보고 판단하면 있는 것을 없다고 하게 된다. 그래서 인덱스는
    /// <see cref="TextureImporter"/>의 import 설정에서 이름을 읽는다 - 텍스처 본체를 메모리로 올리지
    /// 않으므로 프로젝트에 이미지가 수천 장 있어도 감당할 수 있고, 실제 <see cref="Sprite"/> 객체는
    /// 이름이 맞은 파일에서만 읽는다.
    /// </summary>
    public sealed class TableDataAssetIndex
    {
        private readonly Dictionary<string, List<MonsterMotionProfile>> motionProfiles =
            new Dictionary<string, List<MonsterMotionProfile>>(StringComparer.Ordinal);

        private readonly Dictionary<string, List<ItemDefinition>> manualItemsById =
            new Dictionary<string, List<ItemDefinition>>(StringComparer.Ordinal);

        /// <summary>Sprite 이름 -> 그 이름을 가진 Sprite가 들어 있는 에셋 경로들(프로젝트 전체).</summary>
        private readonly Dictionary<string, List<string>> spritePathsByName =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>같은 것을 <see cref="TableDataPaths.ItemIconRoot"/> 안에서만 모은 목록.</summary>
        private readonly Dictionary<string, List<string>> itemIconPathsByName =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        private readonly Dictionary<string, List<Sprite>> resolvedSprites =
            new Dictionary<string, List<Sprite>>(StringComparer.Ordinal);

        private readonly Dictionary<string, List<Sprite>> resolvedItemIcons =
            new Dictionary<string, List<Sprite>>(StringComparer.Ordinal);

        private SpriteDataProviderFactories spriteDataProviderFactories;

        private bool motionProfilesBuilt;
        private bool manualItemsBuilt;
        private bool spriteNamesBuilt;
        private bool itemIconNamesBuilt;

        // ---- MonsterMotionProfile ----

        public AssetLookupResult FindMotionProfile(string assetName, out MonsterMotionProfile profile, out int count)
        {
            EnsureMotionProfiles();
            return Resolve(motionProfiles, assetName, out profile, out count);
        }

        private void EnsureMotionProfiles()
        {
            if (motionProfilesBuilt) return;
            motionProfilesBuilt = true;

            foreach (string guid in AssetDatabase.FindAssets("t:MonsterMotionProfile"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<MonsterMotionProfile>(path);
                if (asset == null) continue;

                Append(motionProfiles, asset.name, asset);
            }
        }

        // ---- ItemDefinition ----

        /// <summary>
        /// 생성 폴더 <b>밖</b>에 있는(= 사람이 직접 만든) ItemDefinition을 <b>에셋 이름이 아니라
        /// ItemId</b>로 찾는다. 쓰임새는 하나다 - Item.csv의 item_id가 기존 수동 아이템의 저장 키와
        /// 겹치는지 확인하는 것. 겹친 채로 두면 같은 저장 키를 가진 정의가 둘이 되어 어느 쪽이
        /// 인벤토리에 그려질지가 실행 순서에 달리게 되므로, 호출하는 쪽이 오류로 막는다.
        ///
        /// 생성 폴더 안쪽을 빼는 이유는 그쪽이 <b>이 임포터가 방금 만든 것</b>이기 때문이다 - 자기가
        /// 만든 에셋을 충돌 상대로 세면 두 번째 Rebuild부터 모든 행이 오류가 된다.
        /// </summary>
        public AssetLookupResult FindManualItemByItemId(string itemId, out ItemDefinition item, out int count)
        {
            EnsureManualItems();
            return Resolve(manualItemsById, itemId, out item, out count);
        }

        private void EnsureManualItems()
        {
            if (manualItemsBuilt) return;
            manualItemsBuilt = true;

            string generatedPrefix = TableDataPaths.ItemOutputFolder + "/";

            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (path.StartsWith(generatedPrefix, StringComparison.Ordinal)) continue;

                var asset = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (asset == null) continue;

                // ItemId가 비어 있는 정의는 저장 키가 없어 무엇과도 겹칠 수 없다.
                string id = asset.ItemId;
                if (string.IsNullOrEmpty(id)) continue;

                Append(manualItemsById, id, asset);
            }
        }

        // ---- 아이템 아이콘 ----

        /// <summary>
        /// <c>icon_key</c>를 <see cref="TableDataPaths.ItemIconRoot"/> <b>안에서만</b> 찾는다.
        /// 판정 규칙(정확한 이름 일치, 여럿이면 Ambiguous)은 <see cref="FindSprite"/>와 같고 탐색 범위만
        /// 다르다. 폴더가 아직 없으면 아무것도 찾지 못한 것으로 다루며, 그 안내는 호출하는 쪽이 한다.
        ///
        /// 빈 이름을 따로 걸러 내지 않는다 - "icon_key가 비어 있다"는 <b>경고</b>이지 조회 실패가 아니고,
        /// 그 판단은 호출하는 쪽(TableDataValidator.ReadIcon)이 이미 하고 돌아간다. 여기서 같은 조건을
        /// 한 번 더 두면 어느 쪽이 실제로 동작하는 판정인지가 흐려지므로, 빈 이름의 안전 처리는
        /// <see cref="ResolveSpriteByName"/> 한 곳에만 둔다.
        /// </summary>
        public AssetLookupResult FindItemIcon(string spriteName, out Sprite sprite, out int count)
        {
            EnsureItemIconNames();
            return ResolveSpriteByName(spriteName, itemIconPathsByName, resolvedItemIcons, out sprite, out count);
        }

        private void EnsureItemIconNames()
        {
            if (itemIconNamesBuilt) return;
            itemIconNamesBuilt = true;

            if (!AssetDatabase.IsValidFolder(TableDataPaths.ItemIconRoot)) return;

            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { TableDataPaths.ItemIconRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                CollectSpriteNames(path, itemIconPathsByName);
            }
        }

        // ---- Sprite ----

        public AssetLookupResult FindSprite(string spriteName, out Sprite sprite, out int count)
        {
            sprite = null;
            count = 0;

            if (string.IsNullOrEmpty(spriteName)) return AssetLookupResult.NotFound;

            EnsureSpriteNames();
            return ResolveSpriteByName(spriteName, spritePathsByName, resolvedSprites, out sprite, out count);
        }

        /// <summary>이름 목록에서 후보 경로를 고르고, <b>이름이 맞은 파일만</b> 실제로 읽어 Sprite를
        /// 확정한다. 한 번 읽은 결과는 캐시한다. 이름이 비어 있으면 찾지 못한 것으로 다룬다 - 사전
        /// 조회에 null을 넘길 수 없으므로 안전 처리는 여기 한 곳에 모아 둔다.</summary>
        private static AssetLookupResult ResolveSpriteByName(
            string spriteName,
            Dictionary<string, List<string>> pathsByName,
            Dictionary<string, List<Sprite>> cache,
            out Sprite sprite,
            out int count)
        {
            sprite = null;
            count = 0;

            if (string.IsNullOrEmpty(spriteName)) return AssetLookupResult.NotFound;

            if (cache.TryGetValue(spriteName, out List<Sprite> cached))
            {
                return Take(cached, out sprite, out count);
            }

            var matches = new List<Sprite>();
            if (pathsByName.TryGetValue(spriteName, out List<string> paths))
            {
                foreach (string path in paths)
                {
                    foreach (UnityEngine.Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        if (candidate is Sprite found && string.Equals(found.name, spriteName, StringComparison.Ordinal))
                        {
                            matches.Add(found);
                        }
                    }
                }
            }

            cache[spriteName] = matches;
            return Take(matches, out sprite, out count);
        }

        private void EnsureSpriteNames()
        {
            if (spriteNamesBuilt) return;
            spriteNamesBuilt = true;

            string[] guids = AssetDatabase.FindAssets("t:Sprite");
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    if (i % 200 == 0 && EditorUtility.DisplayCancelableProgressBar(
                            "Table Data", $"Sprite 이름 목록을 읽는 중... ({i}/{guids.Length})",
                            guids.Length == 0 ? 1f : (float)i / guids.Length))
                    {
                        // 취소해도 인덱스는 "만들어진 것"으로 두지 않는다 - 반쪽짜리 인덱스로 "없음"을
                        // 판정하면 실제로 있는 에셋을 없다고 보고하게 된다.
                        spriteNamesBuilt = false;
                        spritePathsByName.Clear();
                        throw new OperationCanceledException("Sprite 인덱스 작성을 취소했습니다.");
                    }

                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrEmpty(path)) continue;

                    CollectSpriteNames(path, spritePathsByName);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void CollectSpriteNames(string path, Dictionary<string, List<string>> target)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
            {
                // 텍스처가 아닌 Sprite 에셋(드묾)은 메인 에셋 이름이 곧 Sprite 이름이다.
                Append(target, Path.GetFileNameWithoutExtension(path), path);
                return;
            }

            switch (importer.spriteImportMode)
            {
                case SpriteImportMode.None:
                    // Sprite로 import되지 않은 텍스처. 참조 대상이 아니다.
                    return;

                case SpriteImportMode.Multiple:
                    CollectSheetSpriteNames(importer, path, target);
                    return;

                default:
                    // Single / Polygon: Sprite 이름은 파일 이름과 같다.
                    Append(target, Path.GetFileNameWithoutExtension(path), path);
                    return;
            }
        }

        /// <summary>
        /// 스프라이트 시트로 자른 이미지의 하위 Sprite 이름을 읽는다. <see cref="ISpriteEditorDataProvider"/>는
        /// 텍스처마다 초기화 비용이 있어서 <b>Multiple 모드에서만</b> 쓴다 - 대부분의 이미지는 Single이라
        /// 파일 이름만으로 충분하고, 전부 이 경로로 보내면 인덱스 작성이 훨씬 느려진다.
        ///
        /// 옛 <c>TextureImporter.spritesheet</c>를 쓰지 않는 이유는 그쪽이 Deprecated이며 Sprite Editor로
        /// 편집한 최신 내용을 돌려주지 않을 수 있기 때문이다 - 있는 Sprite를 없다고 보고하게 된다.
        /// </summary>
        private void CollectSheetSpriteNames(
            TextureImporter importer, string path, Dictionary<string, List<string>> target)
        {
            if (spriteDataProviderFactories == null)
            {
                spriteDataProviderFactories = new SpriteDataProviderFactories();
                spriteDataProviderFactories.Init();
            }

            ISpriteEditorDataProvider provider =
                spriteDataProviderFactories.GetSpriteEditorDataProviderFromObject(importer);

            if (provider != null)
            {
                provider.InitSpriteEditorDataProvider();
                SpriteRect[] rects = provider.GetSpriteRects();

                if (rects != null && rects.Length > 0)
                {
                    foreach (SpriteRect rect in rects)
                    {
                        if (!string.IsNullOrEmpty(rect.name)) Append(target, rect.name, path);
                    }

                    return;
                }
            }

            // 자른 정보를 읽지 못하면 최소한 파일 이름으로라도 찾을 수 있게 남겨 둔다.
            Append(target, Path.GetFileNameWithoutExtension(path), path);
        }

        // ---- 생성 폴더 안의 기존 Definition ----

        /// <summary>
        /// 출력 폴더 <b>안에서만</b> 기존 생성 Definition을 모은다. 수동 에셋(Assets/Data 이하)은
        /// 탐색 범위에 넣지 않는다 - 같은 타입이라는 이유로 사람이 만든 던전 에셋을 임포터가 덮어쓰는
        /// 일이 절대 없어야 한다.
        ///
        /// 키는 <b>에셋이 실제로 들고 있는 ID</b>다(파일 이름이 아니다). 같은 ID가 둘 이상이면
        /// 목록에 그대로 담아 두고, 어느 쪽을 재사용할지는 호출하는 쪽이 오류로 처리한다.
        /// </summary>
        public static Dictionary<string, List<T>> LoadGeneratedById<T>(string folder, Func<T, string> idSelector)
            where T : ScriptableObject
        {
            var map = new Dictionary<string, List<T>>(StringComparer.Ordinal);
            if (!AssetDatabase.IsValidFolder(folder)) return map;

            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset == null) continue;

                string id = idSelector(asset) ?? string.Empty;
                Append(map, id, asset);
            }

            return map;
        }

        // ---- 공용 ----

        private static AssetLookupResult Resolve<T>(
            Dictionary<string, List<T>> map, string key, out T value, out int count) where T : class
        {
            value = null;
            count = 0;

            if (string.IsNullOrEmpty(key)) return AssetLookupResult.NotFound;

            return map.TryGetValue(key, out List<T> matches)
                ? Take(matches, out value, out count)
                : AssetLookupResult.NotFound;
        }

        private static AssetLookupResult Take<T>(List<T> matches, out T value, out int count) where T : class
        {
            count = matches?.Count ?? 0;
            value = count == 1 ? matches[0] : null;

            if (count == 0) return AssetLookupResult.NotFound;
            return count == 1 ? AssetLookupResult.Found : AssetLookupResult.Ambiguous;
        }

        private static void Append<TValue>(Dictionary<string, List<TValue>> map, string key, TValue value)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (!map.TryGetValue(key, out List<TValue> list))
            {
                list = new List<TValue>();
                map[key] = list;
            }

            list.Add(value);
        }
    }
}
