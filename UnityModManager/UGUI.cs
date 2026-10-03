using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityModManagerNet
{
    public partial class UnityModManager
    {
        public partial class UI
        {
            private GameObject mUGUIRoot;
            private RectTransform mUGUIWindow;
            private CanvasGroup mUGUIWindowGroup;
            private CanvasScaler mUGUIScaler;
            private Font mUGUIFont;
            private string mUGUIFontName;
            private ScrollRect mUGUIScroll;
            private RectTransform mUGUIContent;
            private RectTransform mUGUIActions;
            private InputField mUGUIFilter;
            private GameObject mUGUIFilterRow;
            private EventSystem mOwnedEventSystem;
            private EventSystem mBorrowedEventSystem;
            private GameObject mPreviousSelection;
            private int mRenderedTab = -1;
            private int mScreenWidth, mScreenHeight;
            private float mNextModRefresh;
            private string mRenderedFilter;
            private int mRenderedSettings = -2;
            private readonly Dictionary<int, Vector2> mUGUIScrollPositions = new Dictionary<int, Vector2>();
            private readonly List<ModRow> mUGUIModRows = new List<ModRow>();
            private readonly List<Text> mUGUITabLabels = new List<Text>();
            private readonly List<Button> mUGUITabButtons = new List<Button>();
            private Text mUGUISummary;
            private Scrollbar mUGUIVerticalScrollbar;
            private ModEntry mOptionsMod;
            private RectTransform mModOptions;
            private IMGUIToUGUI mGUIBridge;
            private readonly List<Text> mLogRows = new List<Text>();
            private string mLogSnapshot;
            private KeyBinding mCapturedHotkey;
            private Text mCapturedHotkeyLabel;
            private KeyBinding[] mCaptureKeys;

            private sealed class ModRow
            {
                internal ModEntry Mod;
                internal int Index;
                internal RectTransform Root, Options;
                internal Button Name;
                internal Text Chevron;
                internal Text Version, Requirements, Status;
                internal Toggle Enabled;
                internal GameObject Reload, Update;
                internal bool Updating;
            }

            private void CreateUGUI()
            {
                if (mUGUIRoot)
                    return;

                mUGUIRoot = new GameObject("Unity Mod Manager UI", typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));
                mUGUIRoot.transform.SetParent(transform, false);
                var canvas = mUGUIRoot.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = short.MaxValue - 1;
                mGUIBridge = new IMGUIToUGUI(transform);
                mUGUIScaler = mUGUIRoot.GetComponent<CanvasScaler>();
                mUGUIScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                UpdateUGUIFont();

                var backdrop = Panel(mUGUIRoot.transform, "Backdrop", new Color(0, 0, 0, .35f), 0);
                Stretch(backdrop, 0, 0, 0, 0);
                mUGUIWindow = RectObject(mUGUIRoot.transform, "Manager");
                mUGUIWindow.anchorMin = mUGUIWindow.anchorMax = new Vector2(.5f, .5f);
                mUGUIWindow.pivot = new Vector2(.5f, .5f);
                mUGUIWindowGroup = mUGUIWindow.gameObject.AddComponent<CanvasGroup>();
                var shadow = Panel(mUGUIWindow, "Shadow", new Color(0, 0, 0, .25f), 4);
                Stretch(shadow, -3, -3, 0, -6);
                shadow.GetComponent<Image>().raycastTarget = false;
                var surface = Panel(mUGUIWindow, "Surface", UGUITheme.Window, 3, true);
                Stretch(surface, 0, 0, 0, 0);

                var header = Panel(mUGUIWindow, "Header", Color.clear, 0);
                Top(header, 12, 30);
                Horizontal(header);
                var title = Label(header, "Unity Mod Manager", 0, 17);
                title.fontStyle = FontStyle.Bold;
                var versionLabel = Label(header, "v" + version, 94, 12);
                versionLabel.color = UGUITheme.Muted;
                versionLabel.alignment = TextAnchor.MiddleRight;
                var drag = header.gameObject.AddComponent<UGUIWindowDrag>();
                drag.Window = mUGUIWindow;
                drag.Scale = () => mUIScale;

                var toolbar = RectObject(mUGUIWindow, "Toolbar");
                Top(toolbar, 54, 32);
                Horizontal(toolbar);
                for (var i = 0; i < tabs.Length; i++)
                {
                    var index = i;
                    var tabButton = MakeButton(toolbar, tabs[i], () => { tabId = index; }, 96, false, true);
                    mUGUITabButtons.Add(tabButton);
                    mUGUITabLabels.Add(tabButton.GetComponentInChildren<Text>());
                }
                FlexibleSpace(toolbar);
                mUGUIFilterRow = RectObject(toolbar, "Filter").gameObject;
                Horizontal(mUGUIFilterRow.transform);
                mUGUIFilter = MakeInput(mUGUIFilterRow.transform, mModFilter, value =>
                {
                    mModFilter = value;
                    RefreshModRows();
                }, 200);
                var placeholder = Label(mUGUIFilter.transform, "Search mods...", 0);
                placeholder.color = UGUITheme.Muted;
                Stretch(placeholder.rectTransform, 10, 10, 0, 0);
                mUGUIFilter.placeholder = placeholder;
                MakeButton(mUGUIFilterRow.transform, "Clear", () => { mUGUIFilter.text = ""; }, 52, false, true);

                var divider = Panel(mUGUIWindow, "Toolbar divider", UGUITheme.Border, 0);
                Top(divider, 96, 1);
                var viewport = Panel(mUGUIWindow, "Scroll view", Color.white, 0);
                Stretch(viewport, 16, 30, 108, 62);
                viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                mUGUIScroll = viewport.gameObject.AddComponent<ScrollRect>();
                mUGUIScroll.horizontal = true;
                mUGUIScroll.vertical = true;
                mUGUIScroll.movementType = ScrollRect.MovementType.Clamped;
                mUGUIScroll.scrollSensitivity = 24;
                mUGUIContent = RectObject(viewport, "Content");
                mUGUIContent.anchorMin = new Vector2(0, 1);
                mUGUIContent.anchorMax = new Vector2(1, 1);
                mUGUIContent.pivot = new Vector2(0, 1);
                Vertical(mUGUIContent);
                mUGUIContent.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(1, 1, 1, 10);
                mUGUIContent.GetComponent<VerticalLayoutGroup>().spacing = 6;
                mUGUIContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                mUGUIScroll.content = mUGUIContent;
                var scrollbar = Panel(mUGUIWindow, "Scrollbar", Color.clear, 0);
                scrollbar.anchorMin = new Vector2(1, 0);
                scrollbar.anchorMax = Vector2.one;
                scrollbar.offsetMin = new Vector2(-22, 64);
                scrollbar.offsetMax = new Vector2(-16, -108);
                var thumb = Panel(scrollbar, "Thumb", Color.white, 2);
                Stretch(thumb, 0, 0, 0, 0);
                mUGUIVerticalScrollbar = scrollbar.gameObject.AddComponent<Scrollbar>();
                mUGUIVerticalScrollbar.direction = Scrollbar.Direction.BottomToTop;
                mUGUIVerticalScrollbar.handleRect = thumb;
                mUGUIVerticalScrollbar.targetGraphic = thumb.GetComponent<Image>();
                var scrollColors = mUGUIVerticalScrollbar.colors;
                scrollColors.normalColor = UGUITheme.Track;
                scrollColors.highlightedColor = UGUITheme.Muted;
                scrollColors.pressedColor = UGUITheme.Muted;
                mUGUIVerticalScrollbar.colors = UGUITheme.WithSelectedColor(scrollColors, UGUITheme.Track);
                mUGUIScroll.verticalScrollbar = mUGUIVerticalScrollbar;

                var footerDivider = Panel(mUGUIWindow, "Footer divider", UGUITheme.Border, 0);
                footerDivider.anchorMin = Vector2.zero;
                footerDivider.anchorMax = new Vector2(1, 0);
                footerDivider.offsetMin = new Vector2(16, 50);
                footerDivider.offsetMax = new Vector2(-16, 51);
                mUGUIActions = RectObject(mUGUIWindow, "Actions");
                mUGUIActions.anchorMin = Vector2.zero;
                mUGUIActions.anchorMax = new Vector2(1, 0);
                mUGUIActions.offsetMin = new Vector2(16, 10);
                mUGUIActions.offsetMax = new Vector2(-16, 40);
                Horizontal(mUGUIActions);
                ResizeUGUI();
                BuildUGUIPage();
                mUGUIRoot.SetActive(mOpened);
            }

            private void UpdateUGUIFont()
            {
                var previous = mUGUIFont;
                var previousOwned = !string.IsNullOrEmpty(mUGUIFontName);
                mUGUIFontName = Params.UIFont;
                mUGUIFont = string.IsNullOrEmpty(Params.UIFont)
                    ? Resources.GetBuiltinResource<Font>("Arial.ttf")
                    : Font.CreateDynamicFontFromOSFont(Params.UIFont, globalFontSize);
                if (mUGUIRoot)
                    foreach (var text in mUGUIRoot.GetComponentsInChildren<Text>(true))
                        text.font = mUGUIFont;
                if (previous && previousOwned)
                    Destroy(previous);
            }

            private void ResizeUGUI()
            {
                mScreenWidth = Screen.width;
                mScreenHeight = Screen.height;
                mUGUIScaler.scaleFactor = mUIScale;
                mUGUIRoot.GetComponent<Canvas>().scaleFactor = mUIScale;
                mUGUIWindow.sizeDelta = mWindowSize / mUIScale;
                mUGUIWindow.anchoredPosition = Vector2.zero;
                // Keep columns and settings usable at high scale through horizontal scrolling.
                var viewportWidth = mWindowSize.x / mUIScale - 46;
                mUGUIContent.sizeDelta = new Vector2(Mathf.Max(0, 760 - viewportWidth), mUGUIContent.sizeDelta.y);
            }

            private void SetUGUIVisible(bool visible)
            {
                CreateUGUI();
                mUGUIRoot.SetActive(visible);
                if (visible)
                {
                    EnsureUGUIEventSystem();
                    mNextModRefresh = 0;
                }
                else
                {
                    CancelHotkeyCapture();
                    ClearModOptions();
                    RestoreUGUISelection();
                    if (mOwnedEventSystem)
                        mOwnedEventSystem.gameObject.SetActive(false);
                    foreach (var popup in mPopupList)
                        popup.Opened = false;
                    foreach (var legacyWindow in Window_GUI.mList.ToArray())
                        legacyWindow.Close();
                }
            }

            private void EnsureUGUIEventSystem()
            {
                // Reuse the game's active system, including its configured input module.
                var gameSystem = FindObjectsOfType<EventSystem>().FirstOrDefault(x =>
                    x != mOwnedEventSystem && x.isActiveAndEnabled);
                if (gameSystem)
                {
                    if (mOwnedEventSystem)
                        mOwnedEventSystem.gameObject.SetActive(false);
                    if (mBorrowedEventSystem != gameSystem)
                    {
                        RestoreUGUISelection();
                        mBorrowedEventSystem = gameSystem;
                        mPreviousSelection = gameSystem.currentSelectedGameObject;
                        gameSystem.SetSelectedGameObject(null);
                    }
                    return;
                }

                RestoreUGUISelection();
                if (!mOwnedEventSystem)
                {
                    var owner = new GameObject("UMM EventSystem");
                    owner.SetActive(false);
                    owner.transform.SetParent(transform, false);
                    mOwnedEventSystem = owner.AddComponent<EventSystem>();
                    if (KeyBinding.LegacyInputDisabled)
                    {
                        var moduleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                        if (moduleType == null)
                            throw new InvalidOperationException("Unity Input System UI module was not found.");
                        var module = owner.AddComponent(moduleType);
                        var assign = moduleType.GetMethod("AssignDefaultActions", Type.EmptyTypes);
                        if (assign != null)
                            assign.Invoke(module, null);
                    }
                    else
                    {
                        var module = owner.AddComponent<UGUIMouseInputModule>();
                        // UMM needs no project-specific Horizontal/Vertical/Submit/Cancel axes.
                        module.horizontalAxis = module.verticalAxis = "";
                        module.submitButton = module.cancelButton = "";
                        mOwnedEventSystem.sendNavigationEvents = false;
                    }
                }
                mOwnedEventSystem.gameObject.SetActive(true);
            }

            private void RestoreUGUISelection()
            {
                if (mBorrowedEventSystem)
                    mBorrowedEventSystem.SetSelectedGameObject(mPreviousSelection);
                mBorrowedEventSystem = null;
                mPreviousSelection = null;
            }

            private void UpdateUGUI()
            {
                if (!mOpened || !mUGUIRoot)
                    return;
                EnsureUGUIEventSystem();
                if (Screen.width != mScreenWidth || Screen.height != mScreenHeight)
                {
                    CalculateWindowPos();
                    mExpectedWindowSize = mWindowSize;
                    ResizeUGUI();
                    mRenderedTab = -1;
                }
                if (mUGUIScaler.scaleFactor != mUIScale || mUGUIFontName != Params.UIFont)
                {
                    UpdateUGUIFont();
                    ResizeUGUI();
                }
                tabId = Mathf.Clamp(tabId, 0, Mathf.Max(0, tabs.Length - 1));
                if (mRenderedTab != tabId || (tabId == 0 && mUGUIModRows.Count != modEntries.Count))
                    BuildUGUIPage();
                if (mUGUIFilter.text != mModFilter)
                    mUGUIFilter.text = mModFilter;
                if (tabId == 0)
                {
                    if (Time.unscaledTime >= mNextModRefresh || mRenderedFilter != mModFilter || mRenderedSettings != ShowModSettings)
                    {
                        RefreshModRows();
                        mNextModRefresh = Time.unscaledTime + .25f;
                    }
                    UpdateModOptions();
                }
                else if (tabId == 1)
                    RefreshLogs();
                CaptureHotkey();
                mUGUIVerticalScrollbar.gameObject.SetActive(mUGUIContent.rect.height > mUGUIScroll.GetComponent<RectTransform>().rect.height + 1);
                mUGUIWindowGroup.interactable = !mPopupList.Any(x => x.Opened) && !Window_GUI.mList.Any(x => x.Opened);
            }

            private void BuildUGUIPage()
            {
                if (mRenderedTab >= 0)
                    mUGUIScrollPositions[mRenderedTab] = mUGUIScroll.normalizedPosition;
                CancelHotkeyCapture();
                ClearModOptions();
                ClearChildren(mUGUIContent);
                ClearChildren(mUGUIActions);
                mUGUIModRows.Clear();
                mLogRows.Clear();
                mLogSnapshot = null;
                mRenderedTab = tabId;
                mUGUIFilterRow.SetActive(tabId == 0);
                for (var i = 0; i < mUGUITabLabels.Count; i++)
                {
                    var colors = mUGUITabButtons[i].colors;
                    colors.normalColor = i == tabId ? UGUITheme.Pressed : Color.clear;
                    mUGUITabButtons[i].colors = colors;
                    mUGUITabLabels[i].color = i == tabId ? UGUITheme.Text : UGUITheme.Muted;
                    mUGUITabLabels[i].fontStyle = i == tabId ? FontStyle.Bold : FontStyle.Normal;
                }

                mUGUISummary = Label(mUGUIActions, "", 0, 12);
                mUGUISummary.color = UGUITheme.Muted;
                if (tabId == 0)
                    BuildMods();
                else if (tabId == 1)
                {
                    RefreshLogs();
                    MakeButton(mUGUIActions, "Clear", () => { Logger.Clear(); RefreshLogs(); }, 90);
                    MakeButton(mUGUIActions, "Open detailed log", OpenUnityFileLog, 160);
                }
                else if (tabId == 2)
                    BuildSettings();
                MakeButton(mUGUIActions, "Close", () => ToggleWindow(false), 84, false, true);
                MakeButton(mUGUIActions, "Save", SaveSettingsAndParams, 100, true);
                Canvas.ForceUpdateCanvases();
                Vector2 position;
                mUGUIScroll.normalizedPosition = mUGUIScrollPositions.TryGetValue(tabId, out position)
                    ? position : new Vector2(0, tabId == 1 ? 0 : 1);
            }

            private void BuildMods()
            {
                var headings = Row(mUGUIContent, "Columns");
                headings.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(12, 12, 0, 0);
                Label(headings, "Name", 0, 12).color = UGUITheme.Muted;
                Label(headings, "Version", 75, 12).color = UGUITheme.Muted;
                Label(headings, "Requirements", 180, 12).color = UGUITheme.Muted;
                Label(headings, "Status", 104, 12).color = UGUITheme.Muted;
                Label(headings, "On", 36, 12).color = UGUITheme.Muted;
                for (var i = 0; i < modEntries.Count; i++)
                {
                    var mod = modEntries[i];
                    var row = new ModRow { Mod = mod, Index = i };
                    row.Root = Panel(mUGUIContent, mod.Info.Id, Color.clear, 0);
                    Vertical(row.Root);
                    row.Root.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(12, 12, 3, 4);
                    row.Root.GetComponent<VerticalLayoutGroup>().spacing = 6;
                    var line = Row(row.Root, "Summary");
                    Size(line, 0, 34);
                    var name = Row(line, "Name");
                    row.Name = MakeButton(name, mod.Info.DisplayName, () =>
                    {
                        ShowModSettings = ShowModSettings == row.Index ? -1 : row.Index;
                        RefreshModRows();
                    }, 0, false, true);
                    var nameText = row.Name.GetComponentInChildren<Text>();
                    nameText.alignment = TextAnchor.MiddleLeft;
                    nameText.fontSize = 14;
                    nameText.fontStyle = FontStyle.Bold;
                    Stretch(nameText.rectTransform, 24, 8, 0, 0);
                    row.Chevron = Label(row.Name.transform, ">", 0, 12);
                    row.Chevron.color = UGUITheme.Muted;
                    row.Chevron.rectTransform.anchorMin = new Vector2(0, 0);
                    row.Chevron.rectTransform.anchorMax = new Vector2(0, 1);
                    row.Chevron.rectTransform.offsetMin = new Vector2(4, 0);
                    row.Chevron.rectTransform.offsetMax = new Vector2(20, 0);
                    if (!string.IsNullOrEmpty(mod.Info.HomePage))
                        MakeButton(name, "Web", () => Application.OpenURL(mod.Info.HomePage), 42, false, true);
                    var update = Label(name, "Update", 48, 11);
                    update.color = UGUITheme.Warning;
                    row.Update = update.gameObject;
                    row.Version = Label(line, mod.Info.Version, 75);
                    row.Version.color = UGUITheme.Muted;
                    row.Requirements = Label(line, "", 180, 12);
                    row.Requirements.color = UGUITheme.Muted;
                    row.Status = Label(line, "", 104, 12);
                    row.Enabled = MakeToggle(line, "", mod.Enabled, enabled =>
                    {
                        if (row.Updating || forbidDisableMods)
                            return;
                        mod.Enabled = enabled;
                        if (mod.Toggleable || enabled && !mod.Loaded)
                            mod.Active = enabled;
                        RefreshModRows();
                    }, 36);
                    row.Options = RectObject(row.Root, "Options");
                    Vertical(row.Options);
                    row.Options.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(4, 4, 0, 2);
                    row.Options.GetComponent<VerticalLayoutGroup>().spacing = 8;
                    var separator = Panel(row.Options, "Divider", UGUITheme.Border, 0);
                    Size(separator, 0, 1);
                    var actions = Row(row.Options, "Option actions");
                    Label(actions, "Options", 0, 12).color = UGUITheme.Muted;
                    row.Reload = MakeButton(actions, "Reload", () =>
                    {
                        ClearModOptions();
                        ShowModSettings = -1;
                        mod.Reload();
                        ShowModSettings = row.Index;
                        RefreshModRows();
                    }, 90).gameObject;
                    var rowDivider = Panel(row.Root, "Row divider", UGUITheme.Border, 0);
                    Size(rowDivider, 0, 1);
                    mUGUIModRows.Add(row);
                }
                RefreshModRows();
            }

            private void RefreshModRows()
            {
                mRenderedFilter = mModFilter;
                mRenderedSettings = ShowModSettings;
                foreach (var row in mUGUIModRows)
                {
                    var mod = row.Mod;
                    row.Root.gameObject.SetActive(string.IsNullOrEmpty(mModFilter) ||
                        mod.Info.DisplayName.IndexOf(mModFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                    row.Name.interactable = mod.OnGUI != null || mod.CanReload;
                    row.Chevron.gameObject.SetActive(row.Name.interactable);
                    row.Chevron.text = ShowModSettings == row.Index ? "v" : ">";
                    var nameColors = row.Name.colors;
                    nameColors.normalColor = Color.clear;
                    nameColors.disabledColor = Color.clear;
                    row.Name.colors = nameColors;
                    row.Version.text = mod.Info.Version;
                    row.Requirements.text = ModRequirements(mod);
                    row.Updating = true;
                    row.Enabled.isOn = mod.Enabled;
                    row.Updating = false;
                    row.Enabled.interactable = !forbidDisableMods;
                    row.Update.SetActive(mod.NewestVersion != null);
                    var restart = mod.Active != mod.Enabled;
                    row.Status.text = mod.ErrorOnLoading ? "Error" : restart ? "Restart needed" : mod.Active ? "Active" : "Inactive";
                    var statusColor = mod.ErrorOnLoading ? UGUITheme.Error : restart ? UGUITheme.Warning : mod.Active ? UGUITheme.Success : UGUITheme.Muted;
                    row.Status.color = statusColor;
                    row.Options.gameObject.SetActive(ShowModSettings == row.Index);
                    row.Reload.SetActive(mod.CanReload);
                }
                if (mUGUISummary)
                {
                    var count = mUGUIModRows.Count(x => x.Root.gameObject.activeSelf);
                    mUGUISummary.text = count + (count == 1 ? " mod" : " mods") + "  /  " + modEntries.Count(x => x.Active) + " active";
                }
            }

            private static string ModRequirements(ModEntry mod)
            {
                if (mod.ManagerVersion > GetVersion())
                    return "<color=#CD5C5C>Manager-" + mod.Info.ManagerVersion + "</color>";
                if (gameVersion != VER_0 && mod.GameVersion > gameVersion)
                    return "<color=#CD5C5C>Game-" + mod.Info.GameVersion + "</color>";
                if (mod.Requirements.Count == 0)
                    return string.IsNullOrEmpty(mod.CustomRequirements) ? "-" : mod.CustomRequirements;
                return string.Join(", ", mod.Requirements.Select(requirement =>
                {
                    var found = FindMod(requirement.Key);
                    var issue = found == null ? "Missing" : !found.Active ? "Inactive" :
                        requirement.Value != null && requirement.Value > found.Version ? "Outdated" : null;
                    return issue == null ? requirement.Key : "<color=#CD5C5C>" + requirement.Key + " (" + issue + ")</color>";
                }).ToArray());
            }

            private void UpdateModOptions()
            {
                var row = mUGUIModRows.FirstOrDefault(x => x.Index == ShowModSettings && x.Root.gameObject.activeSelf);
                var mod = row == null ? null : row.Mod;
                if (mod == null || !mod.Active || mod.OnGUI == null)
                {
                    ClearModOptions();
                    return;
                }
                if (mOptionsMod == mod && mModOptions)
                    return;
                ClearModOptions();
                mOptionsMod = mod;
                mModOptions = RectObject(row.Options, "OnGUI options");
                Size(mModOptions, 0, 100 / mUIScale);
                var image = mModOptions.gameObject.AddComponent<Image>();
                image.color = Color.clear;
                // IMGUI handles input here; prevent duplicate ScrollRect wheel processing.
                mModOptions.gameObject.AddComponent<UGUIIMGUIInputBarrier>();
            }

            private void ClearModOptions()
            {
                if (mModOptions)
                {
                    mModOptions.gameObject.SetActive(false);
                    Destroy(mModOptions.gameObject);
                }
                mModOptions = null;
                mOptionsMod = null;
            }

            private static Rect ScreenRect(RectTransform rect)
            {
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                var topLeft = RectTransformUtility.WorldToScreenPoint(null, corners[1]);
                var bottomRight = RectTransformUtility.WorldToScreenPoint(null, corners[3]);
                return new Rect(topLeft.x, Screen.height - topLeft.y, bottomRight.x - topLeft.x, topLeft.y - bottomRight.y);
            }

            private void RenderModOptions()
            {
                if (!mOpened || tabId != 0 || !mModOptions || !mModOptions.gameObject.activeInHierarchy ||
                    mOptionsMod == null || !mOptionsMod.Active || mOptionsMod.OnGUI == null)
                    return;
                var area = ScreenRect(mModOptions);
                var viewport = ScreenRect(mUGUIScroll.GetComponent<RectTransform>());
                var visible = IMGUIToUGUI.Intersect(area, viewport);
                if (Event.current.type == EventType.MouseDown && !visible.Contains(Event.current.mousePosition))
                    GUIUtility.keyboardControl = 0;
                var wheel = Event.current.type == EventType.ScrollWheel && visible.Contains(Event.current.mousePosition);
                var wheelDelta = Event.current.delta.y;
                mGUIBridge.SetClipBoundary(viewport);
                try
                {
                    GUI.BeginGroup(viewport);
                    GUILayout.BeginArea(new Rect(area.x - viewport.x, area.y - viewport.y, area.width, area.height));
                    GUILayout.BeginVertical(GUILayout.ExpandHeight(false));
                    try
                    {
                        mOptionsMod.OnGUI(mOptionsMod);
                    }
                    catch (ExitGUIException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        mOptionsMod.Logger.LogException("OnGUI", exception);
                        ShowModSettings = -1;
                        GUIUtility.ExitGUI();
                    }
                    GUILayout.EndVertical();
                    if (Event.current.type == EventType.Repaint)
                    {
                        var height = Math.Max(1, GUILayoutUtility.GetLastRect().height) / mUIScale;
                        var layout = mModOptions.GetComponent<LayoutElement>();
                        layout.minHeight = layout.preferredHeight = height;
                    }
                    GUILayout.EndArea();
                    GUI.EndGroup();
                    if (wheel && Event.current.type == EventType.ScrollWheel)
                    {
                        var overflow = mUGUIContent.rect.height - mUGUIScroll.GetComponent<RectTransform>().rect.height;
                        if (overflow > 0)
                            mUGUIScroll.verticalNormalizedPosition -= wheelDelta * mUGUIScroll.scrollSensitivity / overflow;
                        Event.current.Use();
                    }
                }
                finally
                {
                    mGUIBridge.SetClipBoundary(null);
                }
            }

            // A logical GUILayout area provides the old API's layout/input state. Its
            // managed GUIStyle paint calls are captured by IMGUIToUGUI, including popups.
            internal static Rect CompatibilityWindow(Rect rect, Action<int> draw, int id, float width, float height)
            {
                width = Mathf.Clamp(width, 1, Math.Max(1, Screen.width));
                height = Mathf.Clamp(height, 1, Math.Max(1, Screen.height));
                if (rect.x < 0 || rect.width <= 0)
                    rect.x = (Screen.width - width) / 2;
                if (rect.y < 0 || rect.height <= 0)
                    rect.y = (Screen.height - height) / 2;
                rect.width = width;
                rect.height = height;
                GUI.Box(rect, GUIContent.none, window);
                GUILayout.BeginArea(rect);
                GUILayout.BeginVertical(window, GUILayout.ExpandHeight(false), GUILayout.ExpandWidth(false));
                draw(id);
                GUILayout.EndVertical();
                var measured = GUILayoutUtility.GetLastRect();
                GUILayout.EndArea();
                return new Rect(rect.x, rect.y, measured.width, measured.height);
            }

            private void RefreshLogs()
            {
                if (tabId != 1)
                    return;
                var snapshot = string.Join("\n", Logger.history.Skip(Mathf.Max(0,
                    Logger.history.Count - Logger.historyCapacity)).ToArray());
                if (snapshot == mLogSnapshot)
                    return;
                var follow = mLogSnapshot == null || mUGUIScroll.verticalNormalizedPosition <= .01f;
                mLogSnapshot = snapshot;
                var lines = Logger.history.Skip(Mathf.Max(0, Logger.history.Count - Logger.historyCapacity));
                var rowIndex = 0;
                foreach (var line in lines)
                {
                    // Older uGUI meshes cannot hold more than 65K vertices per Text.
                    for (var start = 0; start < Math.Max(1, line.Length); start += 12000)
                    {
                        if (rowIndex == mLogRows.Count)
                        {
                            var label = Label(mUGUIContent, "", 0);
                            label.supportRichText = false;
                            mLogRows.Add(label);
                        }
                        var text = mLogRows[rowIndex++];
                        text.gameObject.SetActive(true);
                        text.text = line.Substring(start, Math.Min(12000, line.Length - start));
                    }
                }
                for (var i = rowIndex; i < mLogRows.Count; i++)
                    mLogRows[i].gameObject.SetActive(false);
                Canvas.ForceUpdateCanvases();
                if (follow)
                    mUGUIScroll.verticalNormalizedPosition = 0;
            }

            private void BuildSettings()
            {
                HotkeyRow(mUGUIContent, "Hotkey (default Ctrl+F10)", Params.Hotkey);
                ChoiceRow("Check updates", mCheckUpdateStrings, Params.CheckUpdates, value => Params.CheckUpdates = value);
                ChoiceRow("Show this window on startup", mShowOnStartStrings, Params.ShowOnStart, value => Params.ShowOnStart = value);
                Label(mUGUIContent, "Window size", 0, 15).fontStyle = FontStyle.Bold;
                SliderRow("Width", mExpectedWindowSize.x, Mathf.Min(Screen.width, 960), Screen.width,
                    value => mExpectedWindowSize.x = value, "f0");
                SliderRow("Height", mExpectedWindowSize.y, Mathf.Min(Screen.height, 720), Screen.height,
                    value => mExpectedWindowSize.y = value, "f0");
                MakeButton(mUGUIContent, "Apply window size", () =>
                {
                    mWindowSize = ClampWindowSize(mExpectedWindowSize);
                    mExpectedWindowSize = mWindowSize;
                    Params.WindowWidth = mWindowSize.x;
                    Params.WindowHeight = mWindowSize.y;
                    ResizeUGUI();
                }, 180);
                Label(mUGUIContent, "UI", 0, 15).fontStyle = FontStyle.Bold;
                var fonts = Row(mUGUIContent, "Font");
                Label(fonts, "Font", 230);
                MakeFontDropdown(fonts);
                SliderRow("Scale", mExpectedUIScale, .5f, 5, value => mExpectedUIScale = value, "f2");
                MakeButton(mUGUIContent, "Apply UI", () =>
                {
                    mUIScale = mExpectedUIScale;
                    Params.UIScale = mUIScale;
                    if (mSelectedFont >= 0 && mSelectedFont < mOSfonts.Length)
                        Params.UIFont = mOSfonts[mSelectedFont];
                    mUIScaleChanged = true;
                    UpdateUGUIFont();
                    ResizeUGUI();
                }, 180);
                Label(mUGUIContent, "Mods Hotkeys", 0, 15).fontStyle = FontStyle.Bold;
                foreach (var mod in modEntries)
                    HotkeyRow(mUGUIContent, mod.Info.DisplayName, mod.Hotkey);
            }

            private void ChoiceRow(string title, string[] choices, int selected, Action<int> changed)
            {
                var row = Row(mUGUIContent, title);
                Label(row, title, 230);
                var toggles = new List<Toggle>();
                for (var i = 0; i < choices.Length; i++)
                {
                    var index = i;
                    toggles.Add(MakeToggle(row, choices[i], selected == i, enabled =>
                    {
                        if (!enabled || selected == index)
                            return;
                        selected = index;
                        changed(index);
                        for (var j = 0; j < toggles.Count; j++)
                            toggles[j].isOn = j == index;
                    }, 130));
                }
                var group = row.gameObject.AddComponent<ToggleGroup>();
                foreach (var toggle in toggles)
                    toggle.group = group;
            }

            private void MakeFontDropdown(Transform parent)
            {
                var root = Panel(parent, "Font selection", Color.white, 2, true);
                Size(root, 260, UGUITheme.ControlHeight);
                var caption = Label(root, "", 0);
                Stretch(caption.rectTransform, 10, 26, 0, 0);
                var arrow = Label(root, "v", 0, 12);
                arrow.color = UGUITheme.Muted;
                arrow.rectTransform.anchorMin = new Vector2(1, 0);
                arrow.rectTransform.anchorMax = Vector2.one;
                arrow.rectTransform.offsetMin = new Vector2(-22, 0);
                arrow.rectTransform.offsetMax = new Vector2(-8, 0);
                var template = Panel(root, "Template", UGUITheme.Panel, 2, true);
                template.anchorMin = new Vector2(0, 0);
                template.anchorMax = new Vector2(1, 0);
                template.pivot = new Vector2(.5f, 1);
                template.sizeDelta = new Vector2(0, 220);
                var viewport = Panel(template, "Viewport", Color.white);
                Stretch(viewport, 0, 0, 0, 0);
                viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                var content = RectObject(viewport, "Content");
                content.anchorMin = new Vector2(0, 1);
                content.anchorMax = Vector2.one;
                content.pivot = new Vector2(.5f, 1);
                content.sizeDelta = new Vector2(0, 30);
                var item = MakeToggle(content, "Font", false, value => { }, 0);
                var itemRect = item.GetComponent<RectTransform>();
                Stretch(itemRect, 0, 0, 0, 0);
                var scroll = template.gameObject.AddComponent<ScrollRect>();
                scroll.content = content;
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 24;
                var dropdown = root.gameObject.AddComponent<Dropdown>();
                UGUITheme.Apply(dropdown, true);
                dropdown.targetGraphic = root.GetComponent<Image>();
                dropdown.template = template;
                dropdown.captionText = caption;
                dropdown.itemText = item.GetComponentInChildren<Text>();
                dropdown.AddOptions(mOSfonts.ToList());
                dropdown.value = Math.Max(0, mSelectedFont);
                dropdown.interactable = mOSfonts.Length > 0;
                dropdown.onValueChanged.AddListener(value => mSelectedFont = value);
                template.gameObject.SetActive(false);
            }

            private void SliderRow(string title, float value, float min, float max, Action<float> changed, string format)
            {
                var row = Row(mUGUIContent, title);
                Label(row, title, 230);
                var root = Panel(row, title + " slider", Color.clear, 0);
                Size(root, 260, UGUITheme.ControlHeight);
                var track = Panel(root, "Track", UGUITheme.Track, 2);
                Stretch(track, 8, 8, 14, 14);
                var fillArea = RectObject(root, "Fill area");
                Stretch(fillArea, 8, 8, 14, 14);
                var fill = Panel(fillArea, "Fill", UGUITheme.Accent, 2);
                Stretch(fill, 0, 0, 0, 0);
                var handles = RectObject(root, "Handle area");
                Stretch(handles, 8, 8, 0, 0);
                var handle = Panel(handles, "Handle", Color.white, 2);
                handle.sizeDelta = new Vector2(12, 16);
                var slider = root.gameObject.AddComponent<Slider>();
                slider.fillRect = fill;
                slider.handleRect = handle;
                slider.targetGraphic = handle.GetComponent<Image>();
                var colors = slider.colors;
                colors.normalColor = UGUITheme.Accent;
                colors.highlightedColor = UGUITheme.Accent;
                colors.pressedColor = UGUITheme.Muted;
                slider.colors = UGUITheme.WithSelectedColor(colors, UGUITheme.Accent);
                slider.minValue = min;
                slider.maxValue = Mathf.Max(min, max);
                slider.value = value;
                var display = Label(row, value.ToString(format), 80);
                slider.onValueChanged.AddListener(number =>
                {
                    changed(number);
                    display.text = number.ToString(format);
                });
            }

            private void HotkeyRow(Transform parent, string title, KeyBinding key)
            {
                var row = Row(parent, title);
                Label(row, title, 230);
                Button button = null;
                button = MakeButton(row, key.ToString(), () =>
                {
                    CancelHotkeyCapture();
                    mCapturedHotkey = key;
                    mCapturedHotkeyLabel = button.GetComponentInChildren<Text>();
                    mCapturedHotkeyLabel.text = "Press key… (Esc cancels)";
                }, 260);
                MakeButton(row, "Clear", () =>
                {
                    if (mCapturedHotkey == key)
                        CancelHotkeyCapture();
                    key.Change(KeyCode.None);
                    button.GetComponentInChildren<Text>().text = key.ToString();
                }, 70);
            }

            private void CaptureHotkey()
            {
                if (mCapturedHotkey == null)
                    return;
                if (mCaptureKeys == null)
                    mCaptureKeys = KeyBinding.KeysCode.Select(code => new KeyBinding
                    {
                        keyCode = (KeyCode)Enum.Parse(typeof(KeyCode), code)
                    }).ToArray();
                foreach (var key in mCaptureKeys)
                {
                    if (!key.ReleasedWithoutModifiers())
                        continue;
                    if (key.keyCode == KeyCode.Escape)
                    {
                        CancelHotkeyCapture();
                        return;
                    }
                    var ctrlKey = key.keyCode == KeyCode.LeftControl || key.keyCode == KeyCode.RightControl;
                    var shiftKey = key.keyCode == KeyCode.LeftShift || key.keyCode == KeyCode.RightShift;
                    var altKey = key.keyCode == KeyCode.LeftAlt || key.keyCode == KeyCode.RightAlt;
                    mCapturedHotkey.Change(key.keyCode, !ctrlKey && KeyBinding.Ctrl(),
                        !shiftKey && KeyBinding.Shift(), !altKey && KeyBinding.Alt());
                    CancelHotkeyCapture();
                    return;
                }
            }

            private void CancelHotkeyCapture()
            {
                if (mCapturedHotkeyLabel && mCapturedHotkey != null)
                    mCapturedHotkeyLabel.text = mCapturedHotkey.ToString();
                mCapturedHotkey = null;
                mCapturedHotkeyLabel = null;
            }

            private void DisposeUGUI()
            {
                if (mOpened)
                    ToggleWindow(false);
                RestoreUGUISelection();
                ClearModOptions();
                if (mOwnedEventSystem)
                    Destroy(mOwnedEventSystem.gameObject);
                if (mUGUIRoot)
                    Destroy(mUGUIRoot);
                if (mGUIBridge != null)
                    mGUIBridge.Dispose();
                if (mUGUIFont && !string.IsNullOrEmpty(mUGUIFontName))
                    Destroy(mUGUIFont);
                UGUITheme.Dispose();
            }

            private RectTransform RectObject(Transform parent, string name)
            {
                var obj = new GameObject(name, typeof(RectTransform));
                obj.transform.SetParent(parent, false);
                return obj.GetComponent<RectTransform>();
            }

            private RectTransform Panel(Transform parent, string name, Color color, float radius = 2, bool border = false)
            {
                var rect = RectObject(parent, name);
                var image = rect.gameObject.AddComponent<Image>();
                image.color = color;
                UGUITheme.Shape(image, radius, border);
                return rect;
            }

            private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(left, bottom);
                rect.offsetMax = new Vector2(-right, -top);
            }

            private static void Top(RectTransform rect, float top, float height)
            {
                rect.anchorMin = new Vector2(0, 1);
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(16, -top - height);
                rect.offsetMax = new Vector2(-16, -top);
            }

            private static void Size(RectTransform rect, float width, float height = -1)
            {
                var layout = rect.gameObject.AddComponent<LayoutElement>();
                if (width > 0)
                    layout.minWidth = layout.preferredWidth = width;
                else
                    layout.flexibleWidth = 1;
                if (height > 0)
                    layout.minHeight = layout.preferredHeight = height;
            }

            private static void Horizontal(Transform parent)
            {
                var layout = parent.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 8;
                layout.childAlignment = TextAnchor.MiddleLeft;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
            }

            private static void Vertical(Transform parent)
            {
                var layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.padding = new RectOffset(6, 6, 6, 6);
                layout.spacing = 6;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
            }

            private RectTransform Row(Transform parent, string name)
            {
                var row = RectObject(parent, name);
                Horizontal(row);
                return row;
            }

            private Text Label(Transform parent, string value, float width, int fontSize = 13)
            {
                var root = RectObject(parent, "Text");
                var text = root.gameObject.AddComponent<Text>();
                text.font = mUGUIFont;
                text.fontSize = fontSize;
                text.color = UGUITheme.Text;
                text.supportRichText = true;
                text.raycastTarget = false;
                text.alignment = TextAnchor.MiddleLeft;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.text = value;
                Size(root, width);
                return text;
            }

            private Button MakeButton(Transform parent, string title, UnityAction clicked, float width, bool primary = false, bool quiet = false)
            {
                var root = Panel(parent, title, Color.white);
                Size(root, width, UGUITheme.ControlHeight);
                var button = root.gameObject.AddComponent<Button>();
                UGUITheme.Apply(button, false, primary, quiet);
                button.targetGraphic = root.GetComponent<Image>();
                button.onClick.AddListener(clicked);
                var text = Label(root, title, 0);
                text.color = UGUITheme.Text;
                text.fontStyle = primary ? FontStyle.Bold : FontStyle.Normal;
                text.alignment = TextAnchor.MiddleCenter;
                Stretch(text.rectTransform, 6, 6, 0, 0);
                return button;
            }

            private Toggle MakeToggle(Transform parent, string title, bool value, UnityAction<bool> changed, float width)
            {
                var root = RectObject(parent, "Toggle " + title);
                Size(root, width, UGUITheme.ControlHeight);
                var box = Panel(root, "Box", Color.white, 2, true);
                box.anchorMin = box.anchorMax = new Vector2(0, .5f);
                box.pivot = new Vector2(0, .5f);
                box.anchoredPosition = new Vector2(3, 0);
                box.sizeDelta = new Vector2(20, 20);
                var check = Panel(box, "Check", UGUITheme.Accent, 1);
                Stretch(check, 5, 5, 5, 5);
                var toggle = root.gameObject.AddComponent<Toggle>();
                UGUITheme.Apply(toggle);
                toggle.targetGraphic = box.GetComponent<Image>();
                toggle.graphic = check.GetComponent<Image>();
                toggle.isOn = value;
                var text = Label(root, title, 0);
                Stretch(text.rectTransform, 28, 0, 0, 0);
                toggle.onValueChanged.AddListener(changed);
                return toggle;
            }

            private InputField MakeInput(Transform parent, string value, UnityAction<string> changed, float width)
            {
                var root = Panel(parent, "Input", Color.white, 2, true);
                Size(root, width, UGUITheme.ControlHeight);
                var text = Label(root, "", 0);
                text.supportRichText = false;
                Stretch(text.rectTransform, 10, 10, 0, 0);
                var input = root.gameObject.AddComponent<InputField>();
                UGUITheme.Apply(input, true);
                input.targetGraphic = root.GetComponent<Image>();
                input.textComponent = text;
                input.text = value;
                input.onValueChanged.AddListener(changed);
                return input;
            }

            private void FlexibleSpace(Transform parent)
            {
                Size(RectObject(parent, "Space"), 0);
            }

            private static void ClearChildren(Transform parent)
            {
                for (var i = parent.childCount - 1; i >= 0; i--)
                {
                    var child = parent.GetChild(i);
                    child.gameObject.SetActive(false);
                    Destroy(child.gameObject);
                }
            }
        }
    }

    internal sealed class UGUIWindowDrag : MonoBehaviour, IDragHandler
    {
        internal RectTransform Window;
        internal Func<float> Scale;

        public void OnDrag(PointerEventData eventData)
        {
            if (!Window)
                return;
            var scale = Scale();
            var position = Window.anchoredPosition + eventData.delta / scale;
            var limit = new Vector2(Mathf.Max(0, Screen.width / scale - Window.sizeDelta.x) / 2,
                Mathf.Max(0, Screen.height / scale - Window.sizeDelta.y) / 2);
            Window.anchoredPosition = new Vector2(Mathf.Clamp(position.x, -limit.x, limit.x),
                Mathf.Clamp(position.y, -limit.y, limit.y));
        }
    }

    internal sealed class UGUIIMGUIInputBarrier : MonoBehaviour, IScrollHandler, IPointerDownHandler
    {
        public void OnScroll(PointerEventData eventData) { eventData.Use(); }
        public void OnPointerDown(PointerEventData eventData)
        {
            if (EventSystem.current)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }

    internal sealed class UGUIMouseInputModule : StandaloneInputModule
    {
        public override bool ShouldActivateModule()
        {
            return isActiveAndEnabled && UnityModManager.UI.Instance && UnityModManager.UI.Instance.Opened;
        }
    }
}
