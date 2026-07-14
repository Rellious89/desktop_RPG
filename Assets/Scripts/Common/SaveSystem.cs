using System;
using System.IO;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// SaveData를 Application.persistentDataPath에 JSON으로 저장/불러오기 하는 최소 로컬 저장소.
    /// 파일이 없거나, 손상됐거나, 쓰기에 실패해도 예외를 밖으로 던지지 않는다 - 저장 기능 하나 때문에
    /// 공격/콤보/경험치 같은 기존 동작이 막히면 안 된다. 실패 시 Load()는 null을 반환하고,
    /// 호출부(PlayerProgress)가 기본값으로 새 게임을 시작한다.
    /// </summary>
    public static class SaveSystem
    {
        private const string FileName = "playerprogress.json";

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static void Save(SaveData data)
        {
            try
            {
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] 저장 실패: {e.Message}");
            }
        }

        /// <summary>저장 파일이 없거나 읽기/파싱에 실패하면 null을 반환한다.</summary>
        public static SaveData Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;

                string json = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(json)) return null;

                return JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] 불러오기 실패, 기본값으로 시작합니다: {e.Message}");
                return null;
            }
        }
    }
}
