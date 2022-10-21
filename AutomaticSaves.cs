using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AutomaticSaves
{
    /// <summary>
    /// AutomaticSaves is a mod for Green Hell that will automatically save game every 10 minutes (with configurable frequency and on/off toggle).
    /// Usage: Simply press the shortcut in game to open settings window (by default it is NumPad7).
    /// Author: OSubMarin
    /// </summary>
    public class AutomaticSaves : MonoBehaviour
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

        public AutomaticSaves()
        {
            Instance = this;
        }

        private static AutomaticSaves Instance;

        public static AutomaticSaves Get() => AutomaticSaves.Instance;

        #endregion

        #region Attributes

        /// <summary>The name of this mod.</summary>
        private static readonly string ModName = nameof(AutomaticSaves);

        /// <summary>Game saving frequency (in seconds).</summary>
        private static long AutoSaveEvery = 600L;
        private static string AutoSaveFrequency = "600";
        private static string AutoSaveFrequencyOrig = "600";

        /// <summary>Path to ModAPI runtime configuration file (contains game shortcuts).</summary>
        private static readonly string RuntimeConfigurationFile = Path.Combine(Application.dataPath.Replace("GH_Data", "Mods"), "RuntimeConfiguration.xml");

        /// <summary>Path to AutomaticSaves mod configuration file (if it does not already exist it will be automatically created on first run).</summary>
        private static readonly string AutoSaveConfigurationFile = Path.Combine(Application.dataPath.Replace("GH_Data", "Mods"), "AutoSave.txt");

        /// <summary>Default shortcut to disable/enable AutomaticSaves.</summary>
        private static readonly KeyCode DefaultModKeybindingId = KeyCode.Keypad7;

        private static KeyCode ModKeybindingId { get; set; } = DefaultModKeybindingId;

        private static long LastAutoSaveTime = -1L;

        private static HUDManager LocalHUDManager = null;
        private static Player LocalPlayer = null;

        private static bool IsEnabled { get; set; } = true;
        private static bool IsEnabledOrig { get; set; } = true;

        private static readonly float ModScreenTotalWidth = 400f;
        private static readonly float ModScreenTotalHeight = 100f;
        private static readonly float ModScreenMinWidth = 400f;
        private static readonly float ModScreenMaxWidth = 450f;
        private static readonly float ModScreenMinHeight = 50f;
        private static readonly float ModScreenMaxHeight = 150f;

        public static Rect AutomaticSavesScreen = new Rect(ModScreenStartPositionX, ModScreenStartPositionY, ModScreenTotalWidth, ModScreenTotalHeight);

        private static float ModScreenStartPositionX { get; set; } = Screen.width / 7f;
        private static float ModScreenStartPositionY { get; set; } = Screen.height / 7f;
        private static bool IsMinimized { get; set; } = false;

        private Color DefaultGuiColor = GUI.color;
        private bool ShowUI = false;

        #endregion

        #region Static functions

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

        private static void SaveReplicatedLogicalPlayerIfNeeded()
        {
            if (!ReplTools.IsPlayingAlone() && Player.Get() && Player.Get().GetPlayerComponent<ReplicatedLogicalPlayer>())
            {
                ReplicatedLogicalPlayer playerComponent = Player.Get().GetPlayerComponent<ReplicatedLogicalPlayer>();
                playerComponent.RequestSave();
                HUDTextChatHistory.AddMessage("SessionInfo_PlayerSaved", playerComponent.ReplGetOwner().GetDisplayName(), new Color?(playerComponent.GetPlayerColor()));
            }
        }

        private static int DoSave()
        {
            string prefix = $"[{ModName}:DoSave] ";
            ModAPI.Log.Write(prefix + "Saving game...");
            try
            {
                if (!(P2PSession.Instance.GetGameVisibility() == P2PGameVisibility.Singleplayer || ReplTools.AmIMaster()))
                {
                    ModAPI.Log.Write(prefix + "Cannot save game because you are not the host or not in singleplayer mode.");
                    return -2; // Not the host or not in singleplayer mode.
                }
                if (SaveGame.m_State != SaveGame.State.None)
                {
                    ModAPI.Log.Write(prefix + "Cannot save game because it is busy (State: " + SaveGame.m_State.ToString() + ").");
                    return -3; // Busy state.
                }
                if (DialogsManager.Get().IsAnyStoryDialogPlaying())
                {
                    ModAPI.Log.Write(prefix + "Cannot save game because a story dialog is playing.");
                    return -4; // A story dialog is playing.
                }
                if (MainLevel.Instance.m_SaveGameBlocked)
                {
                    ModAPI.Log.Write(prefix + "Cannot save game because the feature is blocked.");
                    return -5; // Save feature is blocked.
                }
                if (GreenHellGame.Instance.IsGamescom())
                {
                    ModAPI.Log.Write(prefix + "Cannot save game because Gamescom is enabled.");
                    return -6; // Gamescom is enabled.
                }
                if (ChallengesManager.Get().IsChallengeActive())
                {
                    ModAPI.Log.Write(prefix + "Cannot save game because a challenge is active.");
                    return -7; // A challenge is active.
                }
                SaveGame.Save();
                if (ReplTools.AmIMaster())
                    SaveReplicatedLogicalPlayerIfNeeded();
                ModAPI.Log.Write(prefix + "Game has been saved.");
                return 0; // Success.
            }
            catch (Exception ex)
            {
                ModAPI.Log.Write(prefix + "Exception caught: [" + ex.ToString() + "].");
                return -1; // Exception caught.
            }
        }

        private static void CheckTimerAndSave()
        {
            long currTime = DateTime.Now.Ticks / 10000000L;
            if (AutomaticSaves.LastAutoSaveTime <= 0L)
                AutomaticSaves.LastAutoSaveTime = currTime;
            else if ((currTime - AutomaticSaves.LastAutoSaveTime) > AutoSaveEvery)
            {
                AutomaticSaves.LastAutoSaveTime = currTime;
                int retval = AutomaticSaves.DoSave();
                if (retval == 0)
                    ShowHUDMessage("Game has been saved");
                else if (retval == -1)
                    ShowHUDMessage("Unable to save game, check logs");
                else if (retval == -2)
                    ShowHUDMessage("Unable to save game (not the host or not in singleplayer mode)");
                else if (retval == -3)
                    ShowHUDMessage("Unable to save game (busy state)");
                else if (retval == -4)
                    ShowHUDMessage("Unable to save game (a story dialog is playing)");
                else if (retval == -5)
                    ShowHUDMessage("Unable to save game (feature is temporarily blocked)");
                else if (retval == -6)
                    ShowHUDMessage("Unable to save game (Gamescom is enabled)");
                else if (retval == -7)
                    ShowHUDMessage("Unable to save game (a challenge is active)");
                else
                    ShowHUDMessage("Unable to save game, check logs");
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
                                                ModAPI.Log.Write($"[{ModName}:GetConfigurableKey] \"Show settings\" shortcut has been parsed ({parsed}).");
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
            ModAPI.Log.Write($"[{ModName}:GetConfigurableKey] Could not parse \"Show settings\" shortcut. Using default value ({DefaultModKeybindingId.ToString()}).");
            return DefaultModKeybindingId;
        }

        #endregion

        #region Methods

        private void ParseSavesFrequency(string freq)
        {
            if (freq.Trim().All(x => char.IsDigit(x)))
                if (int.TryParse(freq.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int savesFrequency) && savesFrequency > 0 && savesFrequency <= 2000000000)
                {
                    AutoSaveEvery = savesFrequency;
                    AutoSaveFrequency = Convert.ToString(savesFrequency, CultureInfo.InvariantCulture);
                    AutoSaveFrequencyOrig = AutoSaveFrequency;
                }
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
                                else if (line.StartsWith("SavesFrequency=") && line.Length > "SavesFrequency=".Length)
                                    ParseSavesFrequency(line.Substring("SavesFrequency=".Length));
                                else if (line.Trim().All(x => char.IsDigit(x))) // Backward compatibility
                                    ParseSavesFrequency(line);
                            }
                    }
                }
                else
                    File.WriteAllText(AutoSaveConfigurationFile, (IsEnabled ? "IsEnabled=true" : "IsEnabled=false") + "\r\nSavesFrequency=" + Convert.ToString((int)AutoSaveEvery, CultureInfo.InvariantCulture) + "\r\n", System.Text.Encoding.UTF8);
                ModAPI.Log.Write($"[{ModName}:LoadSettings] Settings were loaded (Feature enabled: {(IsEnabled ? "true" : "false")}. Saves frequency: {Convert.ToString((int)AutoSaveEvery, CultureInfo.InvariantCulture)} seconds).");
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
                File.WriteAllText(AutoSaveConfigurationFile, (IsEnabled ? "IsEnabled=true" : "IsEnabled=false") + "\r\nSavesFrequency=" + savesFrequency + "\r\n", System.Text.Encoding.UTF8);
                ModAPI.Log.Write($"[{ModName}:SaveSettings] Settings were updated (Feature enabled: {(IsEnabled ? "true" : "false")}. Saves frequency: {savesFrequency} seconds).");
            }
            catch (Exception ex)
            {
                ModAPI.Log.Write($"[{ModName}:SaveSettings] Exception caught while updating settings: [{ex.ToString()}].");
            }
        }

        #endregion

        #region UI methods

        private void InitWindow()
        {
            int wid = GetHashCode();
            AutomaticSavesScreen = GUILayout.Window(wid,
                AutomaticSavesScreen,
                InitAutomaticSavesScreen,
                "Automatic Saves mod v1.0.0.4, by OSubMarin",
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

        private void InitAutomaticSavesScreen(int windowID)
        {
            ModScreenStartPositionX = AutomaticSavesScreen.x;
            ModScreenStartPositionY = AutomaticSavesScreen.y;

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
            if (GUI.Button(new Rect(AutomaticSavesScreen.width - 40f, 0f, 20f, 20f), "-", GUI.skin.button))
                CollapseWindow();

            if (GUI.Button(new Rect(AutomaticSavesScreen.width - 20f, 0f, 20f, 20f), "X", GUI.skin.button))
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
                            ModAPI.Log.Write($"[{ModName}:ModOptionsBox] {ModName} has been turned on (frequency: every {Convert.ToString((int)AutoSaveEvery, CultureInfo.InvariantCulture)} seconds).");
                        }
                        else
                        {
                            ShowHUDBigInfo(HUDBigInfoMessage($"{ModName} has been turned off", MessageType.Info, Color.red));
                            ModAPI.Log.Write($"[{ModName}:ModOptionsBox] {ModName} has been turned off.");
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
                    GUI.color = Color.yellow;
                    GUILayout.Label($"{ModName} mod only works if you are the host or in singleplayer mode.", GUI.skin.label);
                    GUI.color = DefaultGuiColor;
                }
            }
        }

        private void CollapseWindow()
        {
            if (!IsMinimized)
            {
                AutomaticSavesScreen = new Rect(ModScreenStartPositionX, ModScreenStartPositionY, ModScreenTotalWidth, ModScreenMinHeight);
                IsMinimized = true;
            }
            else
            {
                AutomaticSavesScreen = new Rect(ModScreenStartPositionX, ModScreenStartPositionY, ModScreenTotalWidth, ModScreenTotalHeight);
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

        #endregion

        #region Unity methods

        private void Start()
        {
            ModAPI.Log.Write($"[{ModName}:Start] Initializing {ModName}...");
            LoadSettings();
            InitData();
            ModKeybindingId = GetConfigurableKey();
            ModAPI.Log.Write($"[{ModName}:Start] {ModName} initialized.");
            ModAPI.Log.Write($"[{ModName}:Start] {ModName} has been turned {(AutomaticSaves.IsEnabled ? "on" : "off")}.");
        }

        private void OnGUI()
        {
            if (ShowUI)
            {
                InitData();
                GUI.skin = ModAPI.Interface.Skin;
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
                ShowUI = !ShowUI;
                if (!ShowUI)
                    EnableCursor(false);
            }
            if (AutomaticSaves.IsEnabled)
                AutomaticSaves.CheckTimerAndSave();
        }

        #endregion
    }
}
