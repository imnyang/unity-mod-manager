using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using HarmonyLib;

namespace UnityModManagerNet
{
    public partial class UnityModManager
    {
        public partial class UI : MonoBehaviour
        {
            internal static bool Load()
            {
                if (Instance) return true;

                try
                {
                    new GameObject(typeof(UI).FullName, typeof(UI));

                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                return false;
            }

            private static UI mInstance = null;

            public static UI Instance
            {
                get { return mInstance; }
            }

            public static GUIStyle window = null;
            public static GUIStyle h1 = null;
            public static GUIStyle h2 = null;
            public static GUIStyle bold = null;
            /// <summary>
            /// [0.13.1]
            /// </summary>
            public static GUIStyle button = null;
            private static GUIStyle settings = null;
            private static GUIStyle status = null;
            private static GUIStyle www = null;
            private static GUIStyle updates = null;
            private static GUIStyle question = null;

            private static GUIStyle tooltipBox = null;

            private bool mFirstLaunched = false;
            private bool mInit = false;

            private bool mOpened = false;
            public bool Opened { get { return mOpened; } }

            private Rect mWindowRect = new Rect(0, 0, 0, 0);
            private Vector2 mWindowSize = Vector2.zero;
            private Vector2 mExpectedWindowSize = Vector2.zero;

            private GUIContent mTooltip = null;

            private float mUIScale = 1f;
            private float mExpectedUIScale = 1f;
            private bool mUIScaleChanged;

            private string[] mOSfonts = null;
            private string mDefaultFont = "Arial";
            private int mSelectedFont;
            private string mModFilter = "";

            public int globalFontSize = 13;

            private void Awake()
            {
                Logger.Log("Spawning.");

                mInstance = this;
                DontDestroyOnLoad(this);
                mWindowSize = ClampWindowSize(new Vector2(Params.WindowWidth, Params.WindowHeight));
                mExpectedWindowSize = mWindowSize;
                mUIScale = Mathf.Clamp(Params.UIScale, 0.5f, 5f);
                mExpectedUIScale = mUIScale;
                Textures.Init();
                
                mOSfonts = Font.GetOSInstalledFontNames();
                if (mOSfonts.Length == 0)
                {
                    Logger.Error("No compatible font found in OS. If you play through Wine, install winetricks allfonts.");
                    OpenUnityFileLog();
                }
                else
                {
                    if (string.IsNullOrEmpty(Params.UIFont))
                        Params.UIFont = mDefaultFont;
                    if (!mOSfonts.Contains(Params.UIFont))
                        Params.UIFont = mOSfonts.First();

                    mSelectedFont = Array.IndexOf(mOSfonts, Params.UIFont);
                }

                var harmony = new HarmonyLib.Harmony("UnityModManager.UI");
                var original = typeof(Screen).GetMethod("set_lockCursor");
                var prefix = typeof(Screen_lockCursor_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(original, new HarmonyMethod(prefix));
            }

            private void Start()
            {
                CalculateWindowPos();
                CreateUGUI();
                if (string.IsNullOrEmpty(Config.UIStartingPoint))
                {
                    FirstLaunch();
                }
                if (Params.CheckUpdates == 2 || Params.CheckUpdates == 1 && Params.LastUpdateCheck.DayOfYear != DateTime.Now.DayOfYear)
                {
                    CheckModUpdates();
                }
            }

            private void OnDestroy()
            {
                Logger.Log("Destroying when exit.");
                DisposeUGUI();
                if (mInstance == this)
                    mInstance = null;
                SaveSettingsAndParams();
                Logger.WriteBuffers();
            }

            private void Update()
            {
                if (Opened)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }

                try
                {
                    KeyBinding.BindKeyboard();
                }
                catch (Exception e)
                {
                    Logger.LogException("BindKeyboard", e);
                }

                var deltaTime = Time.deltaTime;
                foreach (var mod in modEntries)
                {
                    if (mod.Active && mod.OnUpdate != null)
                    {
                        try
                        {
                            mod.OnUpdate.Invoke(mod, deltaTime);
                        }
                        catch (Exception e)
                        {
                            mod.Logger.LogException("OnUpdate", e);
                        }
                    }
                }

                var capturingHotkey = mCapturedHotkey != null;
                UpdateUGUI();

                if (!capturingHotkey && (Params.Hotkey.Up() || Param.DefaultHotkey.Up()))
                {
                    ToggleWindow();
                }

                if (!capturingHotkey && mOpened && Param.EscapeHotkey.Up())
                {
                    ToggleWindow();
                }

                if (capturingHotkey)
                    return;

                foreach (var mod in modEntries)
                {
                    if (mod.Active && mod.Hotkey.Up())
                    {
                        if (tabId == 0 && mOpened == true && mModFilter == mod.Info.DisplayName)
                        {
                            ToggleWindow(false);
                        }
                        else
                        {
                            tabId = 0;
                            mModFilter = mod.Info.DisplayName;
                            ToggleWindow(true);
                            var index = modEntries.FindIndex(x => x.Info.Id == mod.Info.Id);
                            if (index >= 0 && ShowModSettings != index)
                            {
                                ShowModSettings = index;
                            }
                        }
                        
                        break;
                    }
                }
            }

            private void FixedUpdate()
            {
                var deltaTime = Time.fixedDeltaTime;
                foreach (var mod in modEntries)
                {
                    if (mod.Active && mod.OnFixedUpdate != null)
                    {
                        try
                        {
                            mod.OnFixedUpdate.Invoke(mod, deltaTime);
                        }
                        catch (Exception e)
                        {
                            mod.Logger.LogException("OnFixedUpdate", e);
                        }
                    }
                }
            }

            private void LateUpdate()
            {
                var deltaTime = Time.deltaTime;
                foreach (var mod in modEntries)
                {
                    if (mod.Active && mod.OnLateUpdate != null)
                    {
                        try
                        {
                            mod.OnLateUpdate.Invoke(mod, deltaTime);
                        }
                        catch (Exception e)
                        {
                            mod.Logger.LogException("OnLateUpdate", e);
                        }
                    }
                }

                Logger.Watcher(deltaTime);
            }

            private void PrepareGUI()
            {
                window = new GUIStyle();
                window.name = "umm window";
                window.normal.background = Textures.Window;
                window.normal.background.wrapMode = TextureWrapMode.Repeat;

                h1 = new GUIStyle();
                h1.name = "umm h1";
                h1.normal.textColor = Color.white;
                h1.fontStyle = FontStyle.Bold;
                h1.alignment = TextAnchor.MiddleCenter;
                h1.clipping = TextClipping.Overflow;

                h2 = new GUIStyle();
                h2.name = "umm h2";
                h2.normal.textColor = new Color(0.6f, 0.91f, 1f);
                h2.fontStyle = FontStyle.Bold;
                h2.clipping = TextClipping.Overflow;

                bold = new GUIStyle(GUI.skin.label);
                bold.name = "umm bold";
                bold.normal.textColor = Color.white;
                bold.fontStyle = FontStyle.Bold;
                bold.clipping = TextClipping.Overflow;

                button = new GUIStyle(GUI.skin.button);
                button.name = "umm button";
                button.clipping = TextClipping.Overflow;

                settings = new GUIStyle();
                settings.alignment = TextAnchor.MiddleCenter;
                settings.stretchHeight = true;

                status = new GUIStyle();
                status.alignment = TextAnchor.MiddleCenter;
                status.stretchHeight = true;

                www = new GUIStyle();
                www.alignment = TextAnchor.MiddleCenter;
                www.stretchHeight = true;

                updates = new GUIStyle();
                updates.alignment = TextAnchor.MiddleCenter;
                updates.stretchHeight = true;

                question = new GUIStyle();

                tooltipBox = new GUIStyle();
                tooltipBox.alignment = TextAnchor.MiddleCenter;
                tooltipBox.normal.textColor = Color.white;
                tooltipBox.normal.background = Textures.Tooltip;
                tooltipBox.normal.background.SetPixels32(new Color32[4].Select(x => x = new Color32(30, 30, 30, 255)).ToArray());
                tooltipBox.normal.background.Apply();
                tooltipBox.hover = tooltipBox.normal;
                tooltipBox.border.left = 2;
                tooltipBox.border.right = 2;
                tooltipBox.border.top = 2;
                tooltipBox.border.bottom = 2;
                tooltipBox.richText = true;
            }

            private void ScaleGUI()
            {
                GUI.skin.font = Font.CreateDynamicFontFromOSFont(Params.UIFont, Scale(globalFontSize));
                GUI.skin.label.clipping = TextClipping.Overflow;
                GUI.skin.button.padding = new RectOffset(Scale(10), Scale(10), Scale(7), Scale(7));
                GUI.skin.textField.padding = GUI.skin.textArea.padding = new RectOffset(Scale(9), Scale(9), Scale(3), Scale(3));
                foreach (var style in new[] { GUI.skin.label, GUI.skin.button, GUI.skin.toggle, GUI.skin.textField, GUI.skin.textArea })
                    foreach (var state in new[] { style.normal, style.hover, style.active, style.focused,
                        style.onNormal, style.onHover, style.onActive, style.onFocused })
                        state.textColor = UGUITheme.Text;
                //GUI.skin.button.margin = RectOffset(Scale(4), Scale(2));

                GUI.skin.horizontalSlider.fixedHeight = Scale(28);
                GUI.skin.horizontalSlider.border = RectOffset(3, 0);
                GUI.skin.horizontalSlider.padding = RectOffset(Scale(-1), 0);
                GUI.skin.horizontalSlider.margin = RectOffset(Scale(4), Scale(8));

                GUI.skin.horizontalSliderThumb.fixedHeight = Scale(20);
                GUI.skin.horizontalSliderThumb.fixedWidth = Scale(12);
                GUI.skin.horizontalSliderThumb.border = RectOffset(4, 0);
                GUI.skin.horizontalSliderThumb.padding = RectOffset(Scale(7), 0);
                GUI.skin.horizontalSliderThumb.margin = RectOffset(0);

                GUI.skin.toggle.margin.left = Scale(10);
                GUI.skin.toggle.padding.left = Scale(28);
                GUI.skin.toggle.padding.top = GUI.skin.toggle.padding.bottom = Scale(7);

                window.padding = RectOffset(Scale(5));
                h1.fontSize = Scale(16);
                h1.margin = RectOffset(0, Scale(5));
                h2.fontSize = Scale(13);
                h2.margin = RectOffset(0, Scale(3));
                button.fontSize = Scale(13);
                button.padding = RectOffset(Scale(30), Scale(7));

                int iconHeight = 28;
                settings.fixedWidth = Scale(24);
                settings.fixedHeight = Scale(iconHeight);
                status.fixedWidth = Scale(12);
                status.fixedHeight = Scale(iconHeight);
                www.fixedWidth = Scale(24);
                www.fixedHeight = Scale(iconHeight);
                updates.fixedWidth = Scale(26);
                updates.fixedHeight = Scale(iconHeight);
                question.fixedWidth = Scale(10);
                question.fixedHeight = Scale(11);
                question.margin = RectOffset(0, 9);

            }

            private void OnGUI()
            {
                if (mGUIBridge == null)
                    return;
                mGUIBridge.BeginFrame();
                try
                {
                    DrawGUICompatibility();
                }
                finally
                {
                    mGUIBridge.EndFrame();
                }
            }

            private void DrawGUICompatibility()
            {
                if (Event.current.type == EventType.Repaint)
                    mTooltip = null;
                if (!mInit)
                {
                    mInit = true;
                    PrepareGUI();
                    ScaleGUI();
                }
                if (mUIScaleChanged)
                {
                    mUIScaleChanged = false;
                    ScaleGUI();
                }

                bool anyRendered = false;
                if (mPopupList.Count > 0)
                {
                    var toRemove = new List<PopupWindow>(0);
                    foreach (var item in mPopupList)
                    {
                        item.DestroyCounter.Add(Time.frameCount);
                        if (item.DestroyCounter.Count > 1)
                        {
                            toRemove.Add(item);
                            continue;
                        }
                        if (item.Opened && !anyRendered)
                        {
                            item.Render();
                            anyRendered = true;
                        }
                    }
                    foreach (var item in toRemove)
                    {
                        mPopupList.Remove(item);
                    }
                }

                if (Window_GUI.mList.Count > 0)
                {
                    foreach (var item in Window_GUI.mList)
                    {
                        if (item.Opened)
                        {
                            item.Render();
                        }
                    }
                }

                RenderModOptions();

                foreach (var mod in modEntries)
                {
                    if (mod.Active && mod.OnFixedGUI != null)
                    {
                        try
                        {
                            mod.OnFixedGUI.Invoke(mod);
                        }
                        catch (Exception e)
                        {
                            mod.Logger.LogException("OnFixedGUI", e);
                        }
                    }
                }
                if (mTooltip != null && Event.current.type == EventType.Repaint)
                {
                    var size = tooltipBox.CalcSize(mTooltip) + Vector2.one * 10;
                    var position = Event.current.mousePosition;
                    GUI.Box(new Rect(Mathf.Min(position.x + 20, Screen.width - size.x),
                        Mathf.Max(0, position.y - size.y), size.x, size.y), mTooltip, tooltipBox);
                }
            }

            public int tabId = 0;
            public string[] tabs = { "Mods", "Logs", "Settings" };

            private int mPreviousShowModSettings = -1;
            private int mShowModSettings = -1;
            private int ShowModSettings {
                get { return mShowModSettings; }
                set
                {
                    Action<ModEntry> Hide = (mod) =>
                    {
                        if (mod.Active && mod.OnHideGUI != null && mod.OnGUI != null)
                        {
                            try
                            {
                                mod.OnHideGUI(mod);
                            }
                            catch (ExitGUIException)
                            {
                            }
                            catch (Exception ex)
                            {
                                mod.Logger.LogException("OnHideGUI", ex);
                            }
                        }
                    };

                    Action<ModEntry> Show = (mod) =>
                    {
                        if (mod.Active && mod.OnShowGUI != null && mod.OnGUI != null)
                        {
                            try
                            {
                                mod.OnShowGUI(mod);
                            }
                            catch (ExitGUIException)
                            {
                            }
                            catch (Exception ex)
                            {
                                mod.Logger.LogException("OnShowGUI", ex);
                            }
                        }
                    };

                    mShowModSettings = value;
                    if (mShowModSettings != mPreviousShowModSettings)
                    {
                        if (mShowModSettings == -1)
                        {
                            if (mPreviousShowModSettings >= 0 && mPreviousShowModSettings < modEntries.Count)
                                Hide(modEntries[mPreviousShowModSettings]);
                        }
                        else if (mPreviousShowModSettings == -1)
                        {
                            if (mShowModSettings < modEntries.Count)
                                Show(modEntries[mShowModSettings]);
                        }
                        else 
                        {
                            if (mPreviousShowModSettings < modEntries.Count)
                                Hide(modEntries[mPreviousShowModSettings]);
                            if (mShowModSettings < modEntries.Count)
                                Show(modEntries[mShowModSettings]);
                        }
                        mPreviousShowModSettings = mShowModSettings;
                    }
                }
            }

            public static int Scale(int value)
            {
                if (!Instance)
                    return value;
                return (int)(value * Instance.mUIScale);
            }

            public static float Scale(float value)
            {
                if (!Instance)
                    return value;
                return value * Instance.mUIScale;
            }

            private void CalculateWindowPos()
            {
                mWindowSize = ClampWindowSize(mWindowSize);
                mWindowRect = new Rect((Screen.width - (int)mWindowSize.x) / 2, (Screen.height - (int)mWindowSize.y) / 2f, 0, 0);
            }

            private Vector2 ClampWindowSize(Vector2 orig)
            {
                return new Vector2(Mathf.Clamp((int)orig.x, Mathf.Min(960, Screen.width), Screen.width), Mathf.Clamp((int)orig.y, Mathf.Min(720, Screen.height), Screen.height));
            }

            private static string[] mCheckUpdateStrings = { "Disabled", "Once a day", "Everytime" };
            
            private static string[] mShowOnStartStrings = { "No", "Yes" };

            [Obsolete]
            private static string[] mHotkeyNames = { "CTRL+F10", "ScrollLock", "Num *", "~" };

            internal bool GameCursorVisible { get; set; }
            internal CursorLockMode GameCursorLockMode { get; set; }

            public void FirstLaunch()
            {
                if (mFirstLaunched || UnityModManager.Params.ShowOnStart == 0 && modEntries.All(x => !x.ErrorOnLoading))
                    return;

                ToggleWindow(true);
            }

            public void ToggleWindow()
            {
                ToggleWindow(!mOpened);
            }

            public void ToggleWindow(bool open)
            {
                if (open == mOpened)
                    return;

                if (open)
                    mFirstLaunched = true;

                if (!open)
                {
                    var i = ShowModSettings;
                    ShowModSettings = -1;
                    mShowModSettings = i;
                }
                else
                {
                    ShowModSettings = mShowModSettings;
                }

                try
                {
                    mOpened = open;
                    SetUGUIVisible(open);
                    //if (!open)
                    //    SaveSettingsAndParams();
                    if (open)
                    {
                        GameCursorLockMode = Cursor.lockState;
                        GameCursorVisible = Cursor.visible;
                        
                        Cursor.visible = true;
                        Cursor.lockState = CursorLockMode.None;
                    }
                    else
                    {
                        Cursor.visible = GameCursorVisible;
                        Cursor.lockState = GameCursorLockMode;
                    }
                    GameScripts.OnToggleWindow(open);
                }
                catch (Exception e)
                {
                    Logger.LogException("ToggleWindow", e);
                }
            }

            private static RectOffset RectOffset(int value)
            {
                return new RectOffset(value, value, value, value);
            }

            private static RectOffset RectOffset(int x, int y)
            {
                return new RectOffset(x, x, y, y);
            }

            private static int mLastWindowId = 0;

            public static int GetNextWindowId()
            {
                return ++mLastWindowId;
            }

            /// <summary>
            /// Renders question mark with a tooltip [0.25.0]
            /// </summary>
            public static void RenderTooltip(string str, GUIStyle style = null, params GUILayoutOption[] options)
            {
                BeginTooltip(str);
                EndTooltip(str, style, options);
            }

            /// <summary>
            /// Call after any GUILayout [0.28.2]
            /// </summary>
            public static void Tooltip(string str)
            {
                if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                {
                    ShowTooltip(str);
                }
            }
        }

        //        [HarmonyPatch(typeof(Screen), "lockCursor", MethodType.Setter)]
        static class Screen_lockCursor_Patch
        {
            static bool Prefix(bool value)
            {
                if (UI.Instance != null && UI.Instance.Opened)
                {
                    if (value)
                    {
                        UI.Instance.GameCursorVisible = false;
                        UI.Instance.GameCursorLockMode = CursorLockMode.Locked;
                    }
                    else
                    {
                        UI.Instance.GameCursorLockMode = CursorLockMode.None;
                        UI.Instance.GameCursorVisible = true;
                    }
                    Cursor.visible = true;
                    Cursor.lockState = CursorLockMode.None;
                    return false;
                }

                return true;
            }
        }
    }
}

