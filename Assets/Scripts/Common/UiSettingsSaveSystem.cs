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
    /// 음량(AudioManager)과 HUD 토글(HudToggleButton)이 같은 파일의 서로 다른 필드를 각자
    /// 저장한다 - 한쪽이 저장할 때 다른 쪽 필드를 덮어쓰지 않도록 SaveMasterVolume/SaveHudVisible은
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

                return Deserialize(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UiSettingsSaveSystem] 불러오기 실패, 기본값으로 시작합니다: {e.Message}");
                return null;
            }
        }

        /// <summary>파일 IO 없이 이전 설정을 새 마스터 볼륨 설정으로 해석한다.</summary>
        public static UiSettingsData Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            UiSettingsData data = JsonUtility.FromJson<UiSettingsData>(json);
            if (data == null) return null;

            // 옛 파일에는 masterVolume 키가 없다. 그 경우에만 기존 음소거 설정을 이관한다.
            // 키 존재를 직접 확인해 새 파일의 명시적인 0과 누락된 값을 구분한다.
            if (json.IndexOf("\"masterVolume\"", StringComparison.Ordinal) < 0)
            {
                data.masterVolume = data.sfxEnabled ? 1f : 0f;
                data.lastNonZeroMasterVolume = 1f;
            }
            data.masterVolume = Mathf.Clamp01(data.masterVolume);
            data.lastNonZeroMasterVolume = Mathf.Clamp01(data.lastNonZeroMasterVolume);
            if (data.lastNonZeroMasterVolume <= 0f) data.lastNonZeroMasterVolume = 1f;
            return data;
        }

        public static void SaveMasterVolume(float volume, float lastNonZeroVolume)
        {
            Save(UpdateMasterVolume(Load(), volume, lastNonZeroVolume));
        }

        /// <summary>파일 IO 없이 볼륨 필드만 변경한다. 다른 옵션 값은 보존한다.</summary>
        public static UiSettingsData UpdateMasterVolume(UiSettingsData data, float volume, float lastNonZeroVolume)
        {
            data = data ?? new UiSettingsData();
            data.masterVolume = Mathf.Clamp01(volume);
            data.lastNonZeroMasterVolume = Mathf.Clamp01(lastNonZeroVolume);
            if (data.lastNonZeroMasterVolume <= 0f) data.lastNonZeroMasterVolume = 1f;
            data.sfxEnabled = data.masterVolume > 0f; // 구버전 설정 필드와도 일관되게 기록
            return data;
        }

        public static void SaveHudVisible(bool visible)
        {
            UiSettingsData data = Load() ?? new UiSettingsData();
            data.hudVisible = visible;
            Save(data);
        }

        public static void SaveSizeScale(float scale)
        {
            Save(UpdateSizeScale(Load(), scale));
        }

        /// <summary>파일 IO 없이 배율 필드만 변경한다. 다른 옵션 값은 보존한다.</summary>
        public static UiSettingsData UpdateSizeScale(UiSettingsData data, float scale)
        {
            data = data ?? new UiSettingsData();
            data.sizeScale = scale;
            return data;
        }
    }
}
