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
    /// ItemDefinition / MonsterMotionProfile / Sprite는 사람이 만든 자산이고 임포터의 입력일 뿐이다.
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

        private readonly Dictionary<string, List<ItemDefinition>> itemsById =
            new Dictionary<string, List<ItemDefinition>>(StringComparer.Ordinal);

        /// <summary>Sprite 이름 -> 그 이름을 가진 Sprite가 들어 있는 에셋 경로들.</summary>
        private readonly Dictionary<string, List<string>> spritePathsByName =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        private readonly Dictionary<string, List<Sprite>> resolvedSprites =
            new Dictionary<string, List<Sprite>>(StringComparer.Ordinal);

        private SpriteDataProviderFactories spriteDataProviderFactories;

        private bool motionProfilesBuilt;
        private bool itemsBuilt;
        private bool spriteNamesBuilt;

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

        /// <summary>ItemDefinition을 <b>에셋 이름이 아니라 ItemId</b>로 찾는다 - CSV가 가리키는 것은
        /// 저장 키이고, 에셋 파일 이름은 사람이 자유롭게 바꿀 수 있는 값이기 때문이다.</summary>
        public AssetLookupResult FindItemByItemId(string itemId, out ItemDefinition item, out int count)
        {
            EnsureItems();
            return Resolve(itemsById, itemId, out item, out count);
        }

        private void EnsureItems()
        {
            if (itemsBuilt) return;
            itemsBuilt = true;

            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (asset == null) continue;

                // ItemId는 비어 있으면 에셋 이름으로 대체되는 값이라, 여기서도 그 규칙을 그대로 읽는다.
                string id = asset.ItemId;
                if (string.IsNullOrEmpty(id)) continue;

                Append(itemsById, id, asset);
            }
        }

        // ---- Sprite ----

        public AssetLookupResult FindSprite(string spriteName, out Sprite sprite, out int count)
        {
            sprite = null;
            count = 0;

            if (string.IsNullOrEmpty(spriteName)) return AssetLookupResult.NotFound;

            EnsureSpriteNames();

            if (resolvedSprites.TryGetValue(spriteName, out List<Sprite> cached))
            {
                return Take(cached, out sprite, out count);
            }

            var matches = new List<Sprite>();
            if (spritePathsByName.TryGetValue(spriteName, out List<string> paths))
            {
                foreach (string path in paths)
                {
                    // 이름이 맞은 파일만 실제로 읽는다.
                    foreach (UnityEngine.Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        if (candidate is Sprite found && string.Equals(found.name, spriteName, StringComparison.Ordinal))
                        {
                            matches.Add(found);
                        }
                    }
                }
            }

            resolvedSprites[spriteName] = matches;
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

                    CollectSpriteNames(path);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void CollectSpriteNames(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
            {
                // 텍스처가 아닌 Sprite 에셋(드묾)은 메인 에셋 이름이 곧 Sprite 이름이다.
                Append(spritePathsByName, Path.GetFileNameWithoutExtension(path), path);
                return;
            }

            switch (importer.spriteImportMode)
            {
                case SpriteImportMode.None:
                    // Sprite로 import되지 않은 텍스처. 참조 대상이 아니다.
                    return;

                case SpriteImportMode.Multiple:
                    CollectSheetSpriteNames(importer, path);
                    return;

                default:
                    // Single / Polygon: Sprite 이름은 파일 이름과 같다.
                    Append(spritePathsByName, Path.GetFileNameWithoutExtension(path), path);
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
        private void CollectSheetSpriteNames(TextureImporter importer, string path)
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
                        if (!string.IsNullOrEmpty(rect.name)) Append(spritePathsByName, rect.name, path);
                    }

                    return;
                }
            }

            // 자른 정보를 읽지 못하면 최소한 파일 이름으로라도 찾을 수 있게 남겨 둔다.
            Append(spritePathsByName, Path.GetFileNameWithoutExtension(path), path);
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
