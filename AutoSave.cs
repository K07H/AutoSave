using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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

        #region Attributes

        /// <summary>The name of this mod.</summary>
        private static readonly string ModName = nameof(AutoSave);

        /// <summary>Game saving frequency (in seconds).</summary>
        private static long AutoSaveEvery = 600L;

        private static string AutoSaveFrequency = "600";

        private static string AutoSaveFrequencyOrig = "600";

        /// <summary>Path to ModAPI runtime configuration file (contains game shortcuts).</summary>
        private static readonly string RuntimeConfigurationFile = Path.Combine(Application.dataPath.Replace("GH_Data", "Mods"), "RuntimeConfiguration.xml");

        /// <summary>Path to AutoSave mod configuration file (if it does not already exist it will be automatically created on first run).</summary>
        private static readonly string AutoSaveConfigurationFile = Path.Combine(Application.dataPath.Replace("GH_Data", "Mods"), "AutoSave.txt");

        /// <summary>Default shortcut to disable/enable AutoSave.</summary>
        private static readonly KeyCode DefaultModKeybindingId = KeyCode.Keypad7;

        private static KeyCode ModKeybindingId { get; set; } = DefaultModKeybindingId;

        private static long LastAutoSaveTime = -1L;

        private static HUDManager LocalHUDManager = null;
        private static Player LocalPlayer;

        private static bool IsEnabled { get; set; } = true;
        private static bool IsEnabledOrig { get; set; } = true;

        private static readonly float ModScreenTotalWidth = 400f;
        private static readonly float ModScreenTotalHeight = 100f;
        private static readonly float ModScreenMinWidth = 350f;
        private static readonly float ModScreenMaxWidth = 400f;
        private static readonly float ModScreenMinHeight = 50f;
        private static readonly float ModScreenMaxHeight = 150f;

        public static Rect ModCraftingScreen = new Rect(ModScreenStartPositionX, ModScreenStartPositionY, ModScreenTotalWidth, ModScreenTotalHeight);

        private static float ModScreenStartPositionX { get; set; } = Screen.width / 7f;
        private static float ModScreenStartPositionY { get; set; } = Screen.height / 7f;
        private static bool IsMinimized { get; set; } = false;

        private Color DefaultGuiColor = GUI.color;
        private bool ShowUI = false;

        #endregion

        #region Statics

        public static void ShowHUDMessage(string message) => ((HUDMessages)LocalHUDManager.GetHUD(typeof(HUDMessages))).AddMessage(message);

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
                    if (P2PSession.Instance.GetGameVisibility() == P2PGameVisibility.Singleplayer || ReplTools.AmIMaster())
                    {
                        if (SaveGame.m_State == SaveGame.State.None)
                        {
                            if (AutoSave.DoSave())
                                ShowHUDMessage("Game has been saved");
                            else
                                ShowHUDMessage("Unable to save game, check logs");
                        }
                        else
                            ModAPI.Log.Write($"[{ModName}:CheckTimerAndSave] Game has not been saved (State = {SaveGame.m_State.ToString()}).");
                    }
                    else
                    {
                        ModAPI.Log.Write($"[{ModName}:CheckTimerAndSave] Unable to save game (feature only available in singleplayer mode or if you are the host.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    ModAPI.Log.Write($"[{ModName}:CheckTimerAndSave] Exception caught: [{ex.ToString()}].");
                }
            }
        }

        private static KeyCode GetConfigurableKey()
        {
            if (File.Exists(RuntimeConfigurationFile))
            {
                string[] lines = null;
                try
                {
                    lines = File.ReadAllLines(RuntimeConfigurationFile);
                }
                catch (Exception ex)
                {
                    ModAPI.Log.Write($"[{ModName}:GetConfigurableKey] Exception caught while reading configured shortcut: [{ex.ToString()}].");
                }
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
                                            if (!string.IsNullOrEmpty(parsed) && Enum.TryParse<KeyCode>(parsed, true, out KeyCode parsedKey))
                                            {
                                                ModAPI.Log.Write($"[{ModName}:GetConfigurableKey] On/Off shortcut has been parsed ({parsed}).");
                                                return parsedKey;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            ModAPI.Log.Write($"[{ModName}:GetConfigurableKey] Could not parse On/Off shortcut. Using default value ({DefaultModKeybindingId.ToString()}).");
            return DefaultModKeybindingId;
        }

        #endregion

        private void InitWindow()
        {
            int wid = GetHashCode();
            ModCraftingScreen = GUILayout.Window(wid,
                ModCraftingScreen,
                InitModCraftingScreen,
                ModName,
                GUI.skin.window,
                GUILayout.ExpandWidth(true),
                GUILayout.MinWidth(ModScreenMinWidth),
                GUILayout.MaxWidth(ModScreenMaxWidth),
                GUILayout.ExpandHeight(true),
                GUILayout.MinHeight(ModScreenMinHeight),
                GUILayout.MaxHeight(ModScreenMaxHeight));
        }

        private void InitData()
        {
            LocalHUDManager = HUDManager.Get();
            LocalPlayer = Player.Get();
        }

        private void InitSkinUI()
        {
            GUI.skin = ModAPI.Interface.Skin;
        }

        private void InitModCraftingScreen(int windowID)
        {
            ModScreenStartPositionX = ModCraftingScreen.x;
            ModScreenStartPositionY = ModCraftingScreen.y;

            using (var modContentScope = new GUILayout.VerticalScope(GUI.skin.box))
            {
                ScreenMenuBox();
                if (!IsMinimized)
                    ModOptionsBox();
            }
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 10000f));
        }

        private void ScreenMenuBox()
        {
            if (GUI.Button(new Rect(ModCraftingScreen.width - 40f, 0f, 20f, 20f), "-", GUI.skin.button))
                CollapseWindow();

            if (GUI.Button(new Rect(ModCraftingScreen.width - 20f, 0f, 20f, 20f), "X", GUI.skin.button))
                CloseWindow();
        }

        private void ModOptionsBox()
        {
            if (P2PSession.Instance.GetGameVisibility() == P2PGameVisibility.Singleplayer || ReplTools.AmIMaster())
            {
                using (var optionsScope = new GUILayout.VerticalScope(GUI.skin.box))
                {
                    IsEnabled = GUILayout.Toggle(IsEnabled, "Enable automatic saves?", GUI.skin.toggle);
                    if (IsEnabled != IsEnabledOrig)
                    {
                        IsEnabledOrig = IsEnabled;
                        if (IsEnabled)
                        {
                            ShowHUDBigInfo(HUDBigInfoMessage($"{ModName} has been turned on (frequency: every {Convert.ToString((int)AutoSaveEvery, CultureInfo.InvariantCulture)} seconds)", MessageType.Info, Color.green));
                            ModAPI.Log.Write($"[{ModName}:Update] {ModName} has been turned on (frequency: every {Convert.ToString((int)AutoSaveEvery, CultureInfo.InvariantCulture)} seconds).");
                        }
                        else
                        {
                            ShowHUDBigInfo(HUDBigInfoMessage($"{ModName} has been turned off", MessageType.Info, Color.red));
                            ModAPI.Log.Write($"[{ModName}:Update] {ModName} has been turned off.");
                        }
                        SaveSettings();
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Automatic saves frequency in seconds: ", GUI.skin.box);
                    AutoSaveFrequency = GUILayout.TextField(AutoSaveFrequency, 10, GUI.skin.textField, GUILayout.MinWidth(150.0f));
                    if (!string.IsNullOrEmpty(AutoSaveFrequency) && AutoSaveFrequency != AutoSaveFrequencyOrig)
                    {
                        AutoSaveFrequencyOrig = AutoSaveFrequency;
                        if (AutoSaveFrequency.Length > 0 && AutoSaveFrequency.Length < 11 && int.TryParse(AutoSaveFrequency, NumberStyles.Integer, CultureInfo.InvariantCulture, out int savesFrequency) && savesFrequency > 0 && savesFrequency <= 2000000000)
                        {
                            AutoSaveEvery = savesFrequency;
                            ShowHUDBigInfo(HUDBigInfoMessage($"Automatic saves frequency updated to {Convert.ToString(savesFrequency, CultureInfo.InvariantCulture)} seconds.", MessageType.Info, Color.green));
                            ModAPI.Log.Write($"[{ModName}:ModOptionsBox] Saves frequency updated to {Convert.ToString(savesFrequency, CultureInfo.InvariantCulture)} seconds.");
                            SaveSettings();
                        }
                        else
                        {
                            ShowHUDBigInfo(HUDBigInfoMessage("Incorrect saves frequency value (it must be between 1 and 2000000000).", MessageType.Error, Color.red));
                            ModAPI.Log.Write($"[{ModName}:ModOptionsBox] Incorrect saves frequency value (it must be between 1 and 2000000000).");
                        }
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.FlexibleSpace();
                }
            }
            else
            {
                using (var optionsScope = new GUILayout.VerticalScope(GUI.skin.box))
                {
                    GUILayout.Label("AutoSave mod only works if you are the host or in singleplayer mode.", GUI.skin.label);
                }
            }
        }

        private void CollapseWindow()
        {
            if (!IsMinimized)
            {
                ModCraftingScreen = new Rect(ModScreenStartPositionX, ModScreenStartPositionY, ModScreenTotalWidth, ModScreenMinHeight);
                IsMinimized = true;
            }
            else
            {
                ModCraftingScreen = new Rect(ModScreenStartPositionX, ModScreenStartPositionY, ModScreenTotalWidth, ModScreenTotalHeight);
                IsMinimized = false;
            }
            InitWindow();
        }

        private void CloseWindow()
        {
            ShowUI = false;
            EnableCursor(false);
        }

        private void EnableCursor(bool blockPlayer = false)
        {
            CursorManager.Get().ShowCursor(blockPlayer, false);

            if (blockPlayer)
            {
                LocalPlayer.BlockMoves();
                LocalPlayer.BlockRotation();
                LocalPlayer.BlockInspection();
            }
            else
            {
                LocalPlayer.UnblockMoves();
                LocalPlayer.UnblockRotation();
                LocalPlayer.UnblockInspection();
            }
        }

        private void ToggleShowUI()
        {
            ShowUI = !ShowUI;
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(AutoSaveConfigurationFile))
                {
                    string[] lines = File.ReadAllLines(AutoSaveConfigurationFile);
                    if (lines != null && lines.Length > 0)
                    {
                        foreach (string line in lines)
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                if (line.Contains("true", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    IsEnabled = true;
                                    IsEnabledOrig = true;
                                }
                                else if (line.Contains("false", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    IsEnabled = false;
                                    IsEnabledOrig = false;
                                }
                                else if (line.Trim().All(x => char.IsDigit(x)))
                                    if (int.TryParse(line.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int savesFrequency) && savesFrequency > 0 && savesFrequency <= 2000000000)
                                    {
                                        AutoSaveEvery = savesFrequency;
                                        AutoSaveFrequency = Convert.ToString(savesFrequency, CultureInfo.InvariantCulture);
                                        AutoSaveFrequencyOrig = AutoSaveFrequency;
                                    }
                            }
                    }
                }
                else
                    File.WriteAllText(AutoSaveConfigurationFile, (IsEnabled ? "true" : "false") + "\r\n" + Convert.ToString((int)AutoSaveEvery, CultureInfo.InvariantCulture) + "\r\n", System.Text.Encoding.UTF8);
                ModAPI.Log.Write($"[{ModName}:LoadSettings] Settings were loaded. Feature enabled: {(IsEnabled ? "true" : "false")}. Saves frequency: {Convert.ToString((int)AutoSaveEvery, CultureInfo.InvariantCulture)} seconds).");
            }
            catch (Exception ex)
            {
                ModAPI.Log.Write($"[{ModName}:LoadSettings] Exception caught while loading settings: [{ex.ToString()}].");
            }
        }

        private void SaveSettings()
        {
            try
            {
                string savesFrequency = Convert.ToString((int)AutoSaveEvery, CultureInfo.InvariantCulture);
                File.WriteAllText(AutoSaveConfigurationFile, (IsEnabled ? "true" : "false") + "\r\n" + savesFrequency + "\r\n", System.Text.Encoding.UTF8);
                ModAPI.Log.Write($"[{ModName}:LoadSettings] Settings were updated. Feature enabled: {(IsEnabled ? "true" : "false")}. Saves frequency: {savesFrequency} seconds).");
            }
            catch (Exception ex)
            {
                ModAPI.Log.Write($"[{ModName}:LoadSettings] Exception caught while updating settings: [{ex.ToString()}].");
            }
        }

        #region Unity methods

        private void Start()
        {
            ModAPI.Log.Write($"[{ModName}:Start] Initializing {ModName}...");
            LoadSettings();
            InitData();
            ModKeybindingId = GetConfigurableKey();
            ModAPI.Log.Write($"[{ModName}:Start] {ModName} initialized.");
            ModAPI.Log.Write($"[{ModName}:Start] {ModName} has been turned {(AutoSave.IsEnabled ? "on" : "off")}.");
        }

        private void OnGUI()
        {
            if (ShowUI)
            {
                InitData();
                InitSkinUI();
                InitWindow();
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(ModKeybindingId))
            {
                if (!ShowUI)
                {
                    InitData();
                    EnableCursor(true);
                }
                ToggleShowUI();
                if (!ShowUI)
                    EnableCursor(false);
            }
            if (AutoSave.IsEnabled)
                AutoSave.CheckTimerAndSave();
        }

        #endregion
    }
}
