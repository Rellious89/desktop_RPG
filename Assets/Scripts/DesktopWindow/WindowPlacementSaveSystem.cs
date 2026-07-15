using System;
using System.IO;
using UnityEngine;

namespace DesktopWindow
{
    /// <summary>
    /// WindowPlacementData를 Application.persistentDataPath에 JSON으로 저장/불러오기 하는 최소 로컬
    /// 저장소. Common.SaveSystem과 같은 방어적 패턴(예외를 밖으로 던지지 않음)이지만, 파일명이 달라서
    /// 플레이어 진행도 저장 파일과 절대 섞이지 않는다.
    /// </summary>
    public static class WindowPlacementSaveSystem
    {
        private const string FileName = "windowplacement.json";

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static void Save(WindowPlacementData data)
        {
            try
            {
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WindowPlacementSaveSystem] 저장 실패: {e.Message}");
            }
        }

        /// <summary>저장 파일이 없거나 읽기/파싱에 실패하면 null을 반환한다.</summary>
        public static WindowPlacementData Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;

                string json = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(json)) return null;

                return JsonUtility.FromJson<WindowPlacementData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WindowPlacementSaveSystem] 불러오기 실패, 기본 배치로 시작합니다: {e.Message}");
                return null;
            }
        }
    }
}
