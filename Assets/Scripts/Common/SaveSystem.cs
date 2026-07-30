using System;
using System.IO;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// SaveData를 Application.persistentDataPath에 JSON으로 저장/불러오기 하는 최소 로컬 저장소.
    /// 파일이 없거나, 손상됐거나, 쓰기에 실패해도 예외를 밖으로 던지지 않는다 - 저장 기능 하나 때문에
    /// 공격/콤보/경험치 같은 기존 동작이 막히면 안 된다.
    ///
    /// <b>저장 문서는 <see cref="Data"/> 하나뿐이다.</b> 예전에는 저장할 때마다 호출부가 새 SaveData를
    /// 만들어 넘겼는데, 그러면 저장 대상이 둘 이상이 되는 순간(경험치 + 캐릭터 상태) 나중에 저장한
    /// 쪽이 상대의 필드를 기본값으로 덮어써서 지운다. 지금은 모든 시스템이 이 인스턴스를 직접 고친 뒤
    /// <see cref="Save"/>를 호출한다.
    ///
    /// 파일이 없거나 읽기에 실패하면 기본값 SaveData를 새로 만들고 <see cref="LoadedFromFile"/>을
    /// false로 남긴다 - 호출부는 그 값을 보고 "새 게임 시작값"을 채울지 결정한다.
    /// </summary>
    public static class SaveSystem
    {
        private const string FileName = "playerprogress.json";

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        private static SaveData data;

        /// <summary>저장 파일에서 실제로 값을 읽어왔는지 여부. false면 <see cref="Data"/>는 기본값이다.</summary>
        public static bool LoadedFromFile { get; private set; }

        /// <summary>이 프로세스가 공유하는 유일한 저장 문서. 최초 접근 시 파일을 한 번 읽는다.</summary>
        public static SaveData Data
        {
            get
            {
                EnsureLoaded();
                return data;
            }
        }

        /// <summary>현재 <see cref="Data"/>를 파일에 기록하고 성공 여부를 돌려준다. 실패해도 예외를
        /// 밖으로 던지지 않으며, 쓰기가 실패한 경우 기존 저장 파일은 그대로 남는다(부분 기록으로
        /// 망가뜨리지 않는다). 반환값은 무시해도 되지만, 사용자에게 결과를 알려야 하는 호출부는
        /// 이 값을 보고 안내를 남긴다.</summary>
        public static bool Save()
        {
            EnsureLoaded();

            try
            {
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(FilePath, json);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] 저장 실패: {e.Message}");
                return false;
            }
        }

        private static void EnsureLoaded()
        {
            if (data != null) return;

            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        data = JsonUtility.FromJson<SaveData>(json);
                        LoadedFromFile = data != null;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] 불러오기 실패, 기본값으로 시작합니다: {e.Message}");
            }

            if (data == null)
            {
                data = new SaveData();
                LoadedFromFile = false;
            }

            // 예전 저장 파일에는 characters/items 항목 자체가 없다 - JsonUtility는 그 경우 null을
            // 남기므로 여기서 한 번만 보정해서 호출부가 null 검사를 반복하지 않게 한다. 재화(currency)는
            // 값 타입이라 항목이 없으면 자동으로 기본값 0이 된다.
            if (data.characters == null)
            {
                data.characters = new System.Collections.Generic.List<CharacterSaveState>();
            }
            if (data.items == null)
            {
                data.items = new System.Collections.Generic.List<InventoryItemState>();
            }
        }
    }
}
