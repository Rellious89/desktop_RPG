using System;
using System.IO;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// UiSettingsData를 Application.persistentDataPath에 JSON으로 저장/불러오기 하는 최소 로컬 저장소.
    /// Common.SaveSystem/DesktopWindow.WindowPlacementSaveSystem과 같은 방어적 패턴(예외를 밖으로
    /// 던지지 않음)이지만 파일명이 달라서 다른 저장 데이터와 섞이지 않는다.
    ///
    /// SFX 토글(AudioManager)과 HUD 토글(HudToggleButton)이 같은 파일의 서로 다른 필드를 각자
    /// 저장한다 - 한쪽이 저장할 때 다른 쪽 필드를 덮어쓰지 않도록 SaveSfxEnabled/SaveHudVisible은
    /// 항상 먼저 파일을 읽어(없으면 기본값) 그 필드만 바꾼 뒤 다시 쓰는 read-modify-write로 동작한다.
    /// </summary>
    public static class UiSettingsSaveSystem
    {
        private const string FileName = "uisettings.json";

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static void Save(UiSettingsData data)
        {
            try
            {
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UiSettingsSaveSystem] 저장 실패: {e.Message}");
            }
        }

        /// <summary>저장 파일이 없거나 읽기/파싱에 실패하면 null을 반환한다.</summary>
        public static UiSettingsData Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;

                string json = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(json)) return null;

                return JsonUtility.FromJson<UiSettingsData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UiSettingsSaveSystem] 불러오기 실패, 기본값으로 시작합니다: {e.Message}");
                return null;
            }
        }

        public static void SaveSfxEnabled(bool enabled)
        {
            UiSettingsData data = Load() ?? new UiSettingsData();
            data.sfxEnabled = enabled;
            Save(data);
        }

        public static void SaveHudVisible(bool visible)
        {
            UiSettingsData data = Load() ?? new UiSettingsData();
            data.hudVisible = visible;
            Save(data);
        }

        public static void SaveSizeScale(float scale)
        {
            UiSettingsData data = Load() ?? new UiSettingsData();
            data.sizeScale = scale;
            Save(data);
        }
    }
}
