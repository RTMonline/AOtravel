using System;
using System.Collections.Generic;
using System.IO;
using AOSharp.Core;
using Newtonsoft.Json;

namespace TravelPlugin
{
    /// <summary>
    /// Travel settings file, following the pattern other AO# plugins use (ManagerGMI's Config):
    /// %LOCALAPPDATA%\AOSharp\Travel\&lt;character id&gt;\Config.json, holding a CharSettings map keyed by
    /// the character id (Game.ClientInst). Each logged-in character reads and writes only its own file.
    /// </summary>
    public class Config
    {
        public const string PluginFolder = "Travel";

        public Dictionary<int, TravelSettings> CharSettings { get; set; } = new Dictionary<int, TravelSettings>();

        [JsonIgnore]
        public string FilePath { get; private set; }

        public static string FolderFor(int characterId) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AOSharp", PluginFolder, characterId.ToString());

        public static string PathFor(int characterId) => Path.Combine(FolderFor(characterId), "Config.json");

        public static Config Load(string path, int characterId, int rows, out bool created)
        {
            Config config = null;
            created = false;

            try
            {
                if (File.Exists(path))
                    config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
            }
            catch
            {
                // Unreadable file: keep a copy so nothing is lost, then start fresh.
                try { File.Copy(path, path + ".broken", true); } catch { }
                config = null;
            }

            if (config == null)
            {
                config = new Config();
                created = true;
            }

            if (config.CharSettings == null)
                config.CharSettings = new Dictionary<int, TravelSettings>();

            if (!config.CharSettings.TryGetValue(characterId, out TravelSettings settings) || settings == null)
                config.CharSettings[characterId] = settings = new TravelSettings();

            settings.Normalize(rows);
            config.FilePath = path;

            if (created)
                config.Save(out _);

            return config;
        }

        public bool Save(out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(this, Formatting.Indented));
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
                File.Move(temp, FilePath);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }
    }

    public class TravelSettings
    {
        public bool JoinIn { get; set; } = true;
        public List<TravelSlot> Slots { get; set; } = new List<TravelSlot>();

        /// <summary>Travel window size this character last used (0 = default). Added in 2.1.</summary>
        public float WindowWidth { get; set; }
        public float WindowHeight { get; set; }

        public void Normalize(int rows)
        {
            if (Slots == null)
                Slots = new List<TravelSlot>();
            for (int i = 0; i < Slots.Count; i++)
                if (Slots[i] == null)
                    Slots[i] = new TravelSlot();
            while (Slots.Count < rows)
                Slots.Add(new TravelSlot());
            if (Slots.Count > rows)
                Slots.RemoveRange(rows, Slots.Count - rows);
        }
    }
}
