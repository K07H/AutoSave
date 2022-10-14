using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace AutoSave
{
    /// <summary>AutoSave is a mod for Green Hell that will automatically save game every 10 minutes.</summary>
    public class AutoSave : MonoBehaviour
    {
        #region Enums

        public enum MessageType
        {
            Info,
            Warning,
            Error
        }

        #endregion

        #region Constructors/Destructor

        public AutoSave()
        {
            Instance = this;
        }

        private static AutoSave Instance;

        public static AutoSave Get() => AutoSave.Instance;

        #endregion

        #region Statics

        /// <summary>The name of this mod.</summary>
        private static readonly string ModName = nameof(AutoSave);

        /// <summary>Game saving frequency (in seconds).</summary>
        private static long AutoSaveEvery = 600L;

        /// <summary>Default shortcut to disable/enable AutoSave.</summary>
        private static readonly KeyCode DefaultModKeybindingId = KeyCode.Keypad7;

        private static KeyCode ModKeybindingId { get; set; } = DefaultModKeybindingId;

        /// <summary>Path to ModAPI runtime configuration file (contains game shortcuts).</summary>
        private static readonly string RuntimeConfigurationFile = Path.Combine(Application.dataPath.Replace("GH_Data", "Mods"), "RuntimeConfiguration.xml");

        /// <summary>Path to AutoSave mod configuration file (if it does not already exist it will be automatically created on first run).</summary>
        private static readonly string AutoSaveConfigurationFile = Path.Combine(Application.dataPath.Replace("GH_Data", "Mods"), "AutoSave.txt");

        private static long LastAutoSaveTime = -1L;

        private static HUDManager LocalHUDManager = null;

        private static bool IsEnabled { get; set; } = true;

        public static void ShowHUDMessage(string message) => ((HUDMessages)LocalHUDManager.GetHUD(typeof(HUDMessages))).AddMessage(message);

        public static void ShowHUDInfoLog(string message) => ((HUDInfoLog)HUDManager.Get().GetHUD(typeof(HUDInfoLog))).AddInfo(message, string.Empty, HUDInfoLogTextureType.Notepad);

        public static string HUDBigInfoMessage(string message, MessageType messageType, Color? headcolor = null) => $"<color=#{ (headcolor != null ? ColorUtility.ToHtmlStringRGBA(headcolor.Value) : ColorUtility.ToHtmlStringRGBA(Color.red))  }>{messageType}</color>\n{message}";

        private static void ShowHUDBigInfo(string text)
        {
            string header = ModName + " Info";
            string textureName = HUDInfoLogTextureType.Reputation.ToString();
            HUDBigInfo obj = (HUDBigInfo)LocalHUDManager.GetHUD(typeof(HUDBigInfo));
            HUDBigInfoData.s_Duration = 2f;
            HUDBigInfoData data = new HUDBigInfoData
            {
                m_Header = header,
                m_Text = text,
                m_TextureName = textureName,
                m_ShowTime = Time.time
            };
            obj.AddInfo(data);
            obj.Show(show: true);
        }

        private static bool DoSave()
        {
            string prefix = $"[{ModName}:DoSave] ";
            ModAPI.Log.Write(prefix + "Saving game...");
            if (DialogsManager.Get().IsAnyStoryDialogPlaying())
            {
                ModAPI.Log.Write(prefix + "Cannot save game because a story dialog is playing.");
                return false;
            }
            if (MainLevel.Instance.m_SaveGameBlocked)
            {
                ModAPI.Log.Write(prefix + "Cannot save game because the feature is blocked.");
                return false;
            }
            if (GreenHellGame.Instance.IsGamescom())
            {
                ModAPI.Log.Write(prefix + "Cannot save game because Gamescom is enabled.");
                return false;
            }
            if (ChallengesManager.Get().IsChallengeActive())
            {
                ModAPI.Log.Write(prefix + "Cannot save game because a challenge is active.");
                return false;
            }
            if (P2PSession.Instance.GetGameVisibility() == P2PGameVisibility.Singleplayer)
                MenuInGameManager.Get().ShowScreen(typeof(SaveGameMenu));
            else if (ReplTools.AmIMaster())
            {
                if (SaveGame.s_MainSaveName.StartsWith(SaveGame.MP_SLOT_NAME))
                {
                    SaveGame.Save();
                    if (!ReplTools.IsPlayingAlone() && Player.Get() && Player.Get().GetPlayerComponent<ReplicatedLogicalPlayer>())
                    {
                        ReplicatedLogicalPlayer playerComponent = Player.Get().GetPlayerComponent<ReplicatedLogicalPlayer>();
                        playerComponent.RequestSave();
                        HUDTextChatHistory.AddMessage("SessionInfo_PlayerSaved", playerComponent.ReplGetOwner().GetDisplayName(), new Color?(playerComponent.GetPlayerColor()));
                    }
                }
                else
                    MenuInGameManager.Get().ShowScreen(typeof(SaveGameMenu));
            }
            else
                Player.Get().GetPlayerComponent<ReplicatedLogicalPlayer>().RequestSave();
            ModAPI.Log.Write(prefix + "Game has been saved.");
            return true;
        }

        private static void CheckTimerAndSave()
        {
            long currTime = DateTime.Now.Ticks / 10000000L;
            if (AutoSave.LastAutoSaveTime <= 0L)
                AutoSave.LastAutoSaveTime = currTime;
            else if ((currTime - AutoSave.LastAutoSaveTime) > AutoSaveEvery)
            {
                AutoSave.LastAutoSaveTime = currTime;
                try
                {
                    if (!ReplTools.IsPlayingAlone() && !ReplTools.AmIMaster())
                    {
                        ModAPI.Log.Write($"[{ModName}:CheckTimerAndSave] Unable to save game (feature only available in singleplayer mode or if you are the host.");
                        return;
                    }
                    if (SaveGame.m_State == SaveGame.State.None)
                    {
                        ShowHUDMessage("Saving game...");
                        if (AutoSave.DoSave())
                            ShowHUDMessage("Game has been saved");
                        else
                            ShowHUDMessage("Unable to save game, check logs");
                    }
                    else
                        ModAPI.Log.Write($"[{ModName}:CheckTimerAndSave] Game has not been saved (State = {SaveGame.m_State.ToString()}).");
                }
                catch (Exception ex)
                {
                    ModAPI.Log.Write($"[{ModName}:CheckTimerAndSave] Exception caught: [{ex.ToString()}].");
                }
            }
        }

        private static void LoadAutoSaveFrequency()
        {
            if (!File.Exists(AutoSaveConfigurationFile))
            {
                ModAPI.Log.Write($"[{ModName}:LoadAutoSaveFrequency] Configuration file was not found, creating it.");
                try
                {
                    using (var configFile = File.CreateText(AutoSaveConfigurationFile))
                    {
                        configFile.WriteLine(Convert.ToString((int)AutoSaveEvery, CultureInfo.InvariantCulture));
                    }
                    ModAPI.Log.Write($"[{ModName}:LoadAutoSaveFrequency] Configuration file was successfully created.");
                }
                catch (Exception ex)
                {
                    ModAPI.Log.Write($"[{ModName}:LoadAutoSaveFrequency] Exception caught while creating configuration file: [{ex.ToString()}].");
                }
            }
            else
            {
                ModAPI.Log.Write($"[{ModName}:LoadAutoSaveFrequency] Parsing configuration file...");
                try
                {
                    string configFileContent = File.ReadAllText(AutoSaveConfigurationFile);
                    if (configFileContent != null)
                    {
                        configFileContent = configFileContent.Replace("\r\n", "");
                        configFileContent = configFileContent.Replace("\n", "");
                        configFileContent = configFileContent.Replace("\t", "");
                        configFileContent = configFileContent.Replace(" ", "");
                        if (configFileContent.Length > 0)
                        {
                            if (int.TryParse(configFileContent, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0 && parsed <= 2000000000)
                            {
                                AutoSaveEvery = (long)parsed;
                                ModAPI.Log.Write($"[{ModName}:LoadAutoSaveFrequency] Successfully parsed configuration file (Save frequency: Every {Convert.ToString((int)AutoSaveEvery, CultureInfo.InvariantCulture)} seconds).");
                            }
                            else
                                ModAPI.Log.Write($"[{ModName}:LoadAutoSaveFrequency] Warning: Save frequency value was not correct (it must be between 1 and 2000000000).");
                        }
                        else
                            ModAPI.Log.Write($"[{ModName}:LoadAutoSaveFrequency] Warning: Configuration file was empty.");
                    }
                    else
                        ModAPI.Log.Write($"[{ModName}:LoadAutoSaveFrequency] Warning: Could not read configuration file.");
                }
                catch (Exception ex)
                {
                    ModAPI.Log.Write($"[{ModName}:LoadAutoSaveFrequency] Exception caught while parsing configuration file: [{ex.ToString()}].");
                }
            }
        }

        private static KeyCode GetConfigurableKey()
        {
            try
            {
                if (File.Exists(RuntimeConfigurationFile))
                {
                    string[] lines = File.ReadAllLines(RuntimeConfigurationFile);
                    if (lines != null && lines.Length > 0)
                    {
                        string sttDelim = "<Button ID=\"" + ModName + "\">";
                        string endDelim = "</Button>";
                        foreach (string line in lines)
                        {
                            if (line.Contains(sttDelim) && line.Contains(endDelim))
                            {
                                int stt = line.IndexOf(sttDelim);
                                if ((stt >= 0) && (line.Length > (stt + sttDelim.Length)))
                                {
                                    string split = line.Substring(stt + sttDelim.Length);
                                    if (split != null && split.Contains(endDelim))
                                    {
                                        int end = split.IndexOf(endDelim);
                                        if ((end > 0) && (split.Length > end))
                                        {
                                            string parsed = split.Substring(0, end);
                                            if (!string.IsNullOrEmpty(parsed))
                                            {
                                                parsed = parsed.Replace("NumPad", "Keypad").Replace("Oem", "");
                                                if (!string.IsNullOrEmpty(parsed))
                                                {
                                                    KeyCode configuredKeyCode = Enum.Parse<KeyCode>(parsed, true);
                                                    ModAPI.Log.Write($"[{ModName}:GetConfigurableKey] On/Off shortcut has been parsed ({parsed}).");
                                                    return configuredKeyCode;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModAPI.Log.Write($"[{ModName}:GetConfigurableKey] Exception caught while reading configured shortcut: [{ex.ToString()}].");
            }
            ModAPI.Log.Write($"[{ModName}:GetConfigurableKey] Could not parse On/Off shortcut. Using default value ({DefaultModKeybindingId.ToString()}).");
            return DefaultModKeybindingId;
        }

        #endregion

        #region Unity methods

        private void Start()
        {
            ModAPI.Log.Write($"[{ModName}:Start] Initializing {ModName}...");
            // Grab HUD manager.
            if (AutoSave.LocalHUDManager == null)
                AutoSave.LocalHUDManager = HUDManager.Get();
            // Load OFF/ON shortcut.
            ModKeybindingId = GetConfigurableKey();
            // Load auto save frequency.
            AutoSave.LoadAutoSaveFrequency();
            ModAPI.Log.Write($"[{ModName}:Start] {ModName} initialized.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(ModKeybindingId))
            {
                AutoSave.IsEnabled = !AutoSave.IsEnabled;
                if (AutoSave.IsEnabled)
                    ShowHUDBigInfo(HUDBigInfoMessage($"{ModName} has been turned on", MessageType.Info, Color.green));
                else
                    ShowHUDBigInfo(HUDBigInfoMessage($"{ModName} has been turned off", MessageType.Info, Color.red));
            }
            if (AutoSave.IsEnabled)
                AutoSave.CheckTimerAndSave();
        }

        #endregion
    }
}
