﻿// JotunnLib
// a Valheim mod
// 
// File:    InGameConfig.cs
// Project: JotunnLib

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Jotunn.GUI
{
    /// <summary>
    ///     An ingame GUI for BepInEx config files
    /// </summary>
    internal class InGameConfig
    {
        /// <summary>
        ///     Name of the menu entry
        /// </summary>
        private const string MenuName = "ModConfig";
        
        /// <summary>
        ///     Cached transform of the vanilla menu list
        /// </summary>
        private static Transform MenuList;

        /// <summary>
        ///     Cached prefab of the vanilla Settings window
        /// </summary>
        private static GameObject SettingsPrefab;

        /// <summary>
        ///     Cached prefab of the vanilla tab button prefab
        /// </summary>
        private static GameObject TabButtonPrefab;

        /// <summary>
        ///     Our own Settings window
        /// </summary>
        private static GameObject SettingsRoot;

        /// <summary>
        ///     Our own mod config tab buttons
        /// </summary>
        private static readonly List<GameObject> ConfigTabButtons = new List<GameObject>();

        /// <summary>
        ///     Our own mod config tabs
        /// </summary>
        private static readonly List<GameObject> ConfigTabs = new List<GameObject>();

        /// <summary>
        ///     Key currently in the binding process
        /// </summary>
        private static ConfigBoundKeyCode KeyInBinding;

        /// <summary>
        ///     Cache keybinds
        /// </summary>
        internal static Dictionary<string, List<Tuple<string, ConfigDefinition, ConfigEntryBase>>> ConfigurationKeybindings =
            new Dictionary<string, List<Tuple<string, ConfigDefinition, ConfigEntryBase>>>();

        /// <summary>
        ///     Hook into settings setup
        /// </summary>
        [PatchInit(0)]
        public static void HookOnSettings()
        {
            On.FejdStartup.SetupGui += FejdStartup_SetupGui;
            On.Menu.Start += Menu_Start;

        }

        /// <summary>
        ///     After SetupGui
        /// </summary>
        private static void FejdStartup_SetupGui(On.FejdStartup.orig_SetupGui orig, FejdStartup self)
        {
            orig(self);

            try
            {
                Instantiate(self.m_mainMenu.transform.Find("MenuList"), self.m_settingsPrefab);
            }
            catch (Exception ex)
            {
                SettingsRoot = null;
                Logger.LogWarning($"Exception caught while creating the Settings tab: {ex}");
            }
        }

        /// <summary>
        ///     After first menu start
        /// </summary>
        private static void Menu_Start(On.Menu.orig_Start orig, Menu self)
        {
            orig(self);

            try
            {
                SynchronizationManager.Instance.CacheConfigurationValues();
                Instantiate(self.m_menuDialog, self.m_settingsPrefab);
            }
            catch (Exception ex)
            {
                SettingsRoot = null;
                Logger.LogWarning($"Exception caught while creating the Settings tab: {ex}");
            }
        }

        /// <summary>
        ///     Create our own menu list entry when mod config is available
        /// </summary>
        /// <param name="menuList"></param>
        /// <param name="settingsPrefab"></param>
        private static void Instantiate(Transform menuList, GameObject settingsPrefab)
        {
            var anyConfig = BepInExUtils.GetDependentPlugins(true).Any(x => GetConfigurationEntries(x.Value).Any());

            if (!anyConfig)
            {
                return;
            }

            MenuList = menuList;
            SettingsPrefab = settingsPrefab;
            TabButtonPrefab = settingsPrefab.GetComponentInChildren<TabHandler>(true).transform.GetChild(1).gameObject;

            bool settingsFound = false;
            for (int i = 0; i < menuList.childCount; i++)
            {
                if (menuList.GetChild(i).name == "Settings")
                {
                    Transform modSettings = Object.Instantiate(menuList.GetChild(i), menuList);
                    modSettings.GetComponentInChildren<Text>().text = MenuName;
                    Button modSettingsButton = modSettings.GetComponent<Button>();
                    for (int j = 0; j < modSettingsButton.onClick.GetPersistentEventCount(); ++j)
                    {
                        modSettingsButton.onClick.SetPersistentListenerState(j, UnityEventCallState.Off);
                    }
                    modSettingsButton.onClick.RemoveAllListeners();
                    modSettingsButton.onClick.AddListener(CreateWindow);
                    settingsFound = true;
                }
                else if (settingsFound)
                {
                    RectTransform rectTransform = menuList.GetChild(i).GetComponent<RectTransform>();
                    rectTransform.anchoredPosition = new Vector2(rectTransform.anchoredPosition.x,
                        rectTransform.anchoredPosition.y - 40);
                }
            }
        }
        
        /// <summary>
        ///     Create custom configuration window
        /// </summary>
        private static void CreateWindow()
        {
            // Create settings window
            SettingsRoot = Object.Instantiate(SettingsPrefab, MenuList.parent);
            SettingsRoot.transform.GetComponentInChildren<Text>().text = MenuName;
            ConfigTabs.Clear();
            ConfigTabButtons.Clear();

            // Gather TabButtons
            Transform tabButtons = SettingsRoot.transform.Find("panel/TabButtons");
            TabHandler tabHandler = tabButtons.GetComponentInChildren<TabHandler>();
            RectTransform tabButtonsParent = tabHandler.transform as RectTransform;

            // Destroy old tab buttons
            foreach (Transform t in tabButtonsParent)
            {
                Object.Destroy(t.gameObject);
            }
            tabHandler.m_tabs.Clear();

            // Gather Tabs
            RectTransform tabsParent = SettingsRoot.transform.Find("panel/Tabs") as RectTransform;

            // Deactivate old tab contents
            foreach (Transform t in tabsParent)
            {
                t.gameObject.SetActive(false);
            }

            // Reset keybinding cache
            ConfigurationKeybindings.Clear();
            foreach (var mod in BepInExUtils.GetDependentPlugins(true).OrderBy(x => x.Value.Info.Metadata.Name))
            {
                foreach (var kv in GetConfigurationEntries(mod.Value).Where(x => x.Value.IsVisible() && x.Value.IsButtonBound()))
                {
                    var buttonName = kv.Value.GetBoundButtonName();
                    if (!string.IsNullOrEmpty(buttonName))
                    {
                        if (!ConfigurationKeybindings.ContainsKey(buttonName))
                        {
                            ConfigurationKeybindings.Add(buttonName, new List<Tuple<string, ConfigDefinition, ConfigEntryBase>>());
                        }
                        ConfigurationKeybindings[buttonName].Add(new Tuple<string, ConfigDefinition, ConfigEntryBase>(mod.Key, kv.Key, kv.Value));
                    }
                }
            }

            // Iterate over all dependent plugins (including Jotunn itself)
            foreach (var mod in BepInExUtils.GetDependentPlugins(true))
            {
                CreateTab(mod, tabButtonsParent, tabsParent, tabHandler);
            }

            // Add RectMask
            tabButtonsParent.gameObject.GetOrAddMonoBehaviour<RectMask2D>();
            //tabButtonsParent.gameObject.SetWidth(tabButtonsParent.rect.width / 2f);

            // Reorder tabs
            float offset = 0f;
            foreach (GameObject go in ConfigTabButtons)
            {
                go.SetBottomLeft().SetHeight(25f);
                RectTransform tf = go.transform as RectTransform;
                tf.anchoredPosition = new Vector2(offset, tf.anchoredPosition.y);
                offset += tf.rect.width;
            }

            // Hook SaveSettings to be notified when OK was pressed
            On.Settings.SaveSettings += Settings_SaveSettings;
            On.Settings.OnBack += Settings_OnBack;
            On.Settings.OnOk += Settings_OnOk;

            // Go to first tab
            tabHandler.SetActiveTab(0);
        }

        private static void CreateTab(KeyValuePair<string, BaseUnityPlugin> mod, RectTransform tabButtonsParent, RectTransform tabsParent,
            TabHandler tabHandler)
        {
            // Create the tab button
            GameObject tabButton = Object.Instantiate(TabButtonPrefab, tabButtonsParent);

            // And set it's new property values
            string modName = mod.Value.Info.Metadata.Name;
            tabButton.name = modName;
            if (tabButton.TryGetComponent<Text>(out var txt))
            {
                txt.text = modName;
            }
            foreach (Text text in tabButton.GetComponentsInChildren<Text>(true))
            {
                text.text = modName;
            }
            ConfigTabButtons.Add(tabButton);

            // Create the tab contents
            GameObject tabContent = GUIManager.Instance.CreateScrollView(
                    tabsParent, false, true, 8f, 10f, GUIManager.Instance.ValheimScrollbarHandleColorBlock,
                    new Color(0, 0, 0, 1), tabsParent.rect.width - 50f, tabsParent.rect.height - 50f)
                .SetMiddleCenter();
            tabContent.name = mod.Key;

            // configure the ui group handler
            var groupHandler = tabContent.AddComponent<UIGroupHandler>();
            groupHandler.m_groupPriority = 10;
            groupHandler.m_canvasGroup = tabContent.GetComponent<CanvasGroup>();
            groupHandler.m_canvasGroup.ignoreParentGroups = true;
            groupHandler.m_canvasGroup.blocksRaycasts = true;
            groupHandler.Update();

            // create ok and back button (just copy them from Controls tab)
            var ok = Object.Instantiate(tabsParent.Find("Controls/Ok").gameObject, tabContent.transform);
            ok.GetComponent<RectTransform>().anchoredPosition =
                ok.GetComponent<RectTransform>().anchoredPosition - new Vector2(0, 25f);
            ok.GetComponent<Button>().onClick.AddListener(() =>
            {
                Settings.instance.OnOk();

                // After applying ingame values, lets synchronize any changed (and unlocked) values
                SynchronizationManager.Instance.SynchronizeChangedConfig();

                // remove reference to gameobject
                SettingsRoot = null;
                tabContent = null;
            });

            var back = Object.Instantiate(tabsParent.Find("Controls/Back").gameObject, tabContent.transform);
            back.GetComponent<RectTransform>().anchoredPosition =
                back.GetComponent<RectTransform>().anchoredPosition - new Vector2(0, 25f);
            back.GetComponent<Button>().onClick.AddListener(() =>
            {
                Settings.instance.OnBack();

                // remove reference to gameobject
                SettingsRoot = null;
                tabContent = null;
            });

            // initially hide the configTab
            tabContent.SetActive(false);

            // Add a new Tab to the TabHandler
            TabHandler.Tab newTab = new TabHandler.Tab();
            newTab.m_default = false;
            newTab.m_button = tabButton.GetComponent<Button>();
            newTab.m_page = tabContent.GetComponent<RectTransform>();
            newTab.m_onClick = new UnityEvent();
            newTab.m_onClick.AddListener(() =>
            {
                tabContent.GetComponent<UIGroupHandler>().SetActive(true);
                tabContent.SetActive(true);
                tabContent.transform.Find("Scroll View").GetComponent<ScrollRect>().normalizedPosition = new Vector2(0, 1);
            });

            // Add the onClick of the tabhandler to the tab button
            tabButton.GetComponent<Button>().onClick.AddListener(() => tabHandler.OnClick(newTab.m_button));

            // and add the new Tab to the tabs list
            tabHandler.m_tabs.Add(newTab);

            float innerWidth = tabContent.GetComponent<RectTransform>().rect.width - 25f;
            Transform viewport = tabContent.transform.Find("Scroll View/Viewport/Content");

            // Create a header if there are any relevant configuration entries
            if (GetConfigurationEntries(mod.Value).Where(x => x.Value.IsVisible()).GroupBy(x => x.Key.Section).Any())
            {
                // Create module header Text element
                var text = GUIManager.Instance.CreateText(mod.Key, viewport,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0, 0), GUIManager.Instance.AveriaSerifBold, 20, Color.white, true,
                    Color.black,
                    tabContent.GetComponent<RectTransform>().rect.width, 50, false);
                text.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);
                text.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
                text.AddComponent<LayoutElement>().preferredHeight = 40f;
            }

            // Iterate over all configuration entries (grouped by their sections)
            foreach (var kv in GetConfigurationEntries(mod.Value).Where(x => x.Value.IsVisible()).GroupBy(x => x.Key.Section))
            {
                // Create section header Text element
                var sectiontext = GUIManager.Instance.CreateText(
                    "Section " + kv.Key, viewport, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0, 0), GUIManager.Instance.AveriaSerifBold, 16, GUIManager.Instance.ValheimOrange,
                    true, Color.black, tabContent.GetComponent<RectTransform>().rect.width, 30, false);
                sectiontext.SetMiddleCenter();
                sectiontext.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 0);
                sectiontext.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
                sectiontext.AddComponent<LayoutElement>().preferredHeight = 30f;

                // Iterate over all entries of this section
                foreach (var entry in kv.OrderByDescending(x =>
                {
                    if (x.Value.Description.Tags.FirstOrDefault(y => y is ConfigurationManagerAttributes) is
                        ConfigurationManagerAttributes cma)
                    {
                        return cma.Order ?? 0;
                    }

                    return 0;
                }).ThenBy(x => x.Key.Key))
                {
                    // Create config entry
                    // switch by type
                    var entryAttributes =
                        entry.Value.Description.Tags.FirstOrDefault(x => x is ConfigurationManagerAttributes) as
                            ConfigurationManagerAttributes;
                    if (entryAttributes == null)
                    {
                        entryAttributes = new ConfigurationManagerAttributes();
                    }

                    if (entry.Value.SettingType == typeof(bool))
                    {
                        // Create toggle element
                        var go = CreateToggleElement(viewport,
                            entry.Key.Key + ":",
                            entryAttributes.EntryColor,
                            entry.Value.Description.Description + (entryAttributes.IsAdminOnly
                                ? $"{Environment.NewLine}(Server side setting)"
                                : ""),
                            entryAttributes.DescriptionColor, mod.Value.Info.Metadata.GUID, entry.Key.Section, entry.Key.Key,
                            innerWidth);
                        SetProperties(go.GetComponent<ConfigBoundBoolean>(), entry);
                    }
                    else if (entry.Value.SettingType == typeof(int))
                    {
                        var description = entry.Value.Description.Description;
                        if (entry.Value.Description.AcceptableValues != null)
                        {
                            description += Environment.NewLine + "(" +
                                           entry.Value.Description.AcceptableValues.ToDescriptionString().TrimStart('#')
                                               .Trim() + ")";
                        }

                        // Create input field int
                        var go = CreateTextInputField(viewport,
                            entry.Key.Key + ":",
                            entryAttributes.EntryColor,
                            description + (entryAttributes.IsAdminOnly ? $"{Environment.NewLine}(Server side setting)" : ""),
                            entryAttributes.DescriptionColor, innerWidth);
                        go.AddComponent<ConfigBoundInt>()
                            .SetData(mod.Value.Info.Metadata.GUID, entry.Key.Section, entry.Key.Key);
                        go.transform.Find("Input").GetComponent<InputField>().characterValidation =
                            InputField.CharacterValidation.Integer;
                        SetProperties(go.GetComponent<ConfigBoundInt>(), entry);
                        go.transform.Find("Input").GetComponent<InputField>().onValueChanged.AddListener(x =>
                        {
                            go.transform.Find("Input").GetComponent<InputField>().textComponent.color =
                                go.GetComponent<ConfigBoundInt>().IsValid() ? Color.white : Color.red;
                        });
                    }
                    else if (entry.Value.SettingType == typeof(float))
                    {
                        var description = entry.Value.Description.Description;
                        if (entry.Value.Description.AcceptableValues != null)
                        {
                            description += Environment.NewLine + "(" +
                                           entry.Value.Description.AcceptableValues.ToDescriptionString().TrimStart('#')
                                               .Trim() + ")";
                        }

                        // Create input field float
                        var go = CreateTextInputField(viewport,
                            entry.Key.Key + ":",
                            entryAttributes.EntryColor,
                            description + (entryAttributes.IsAdminOnly ? $"{Environment.NewLine}(Server side setting)" : ""),
                            entryAttributes.DescriptionColor, innerWidth);
                        go.AddComponent<ConfigBoundFloat>()
                            .SetData(mod.Value.Info.Metadata.GUID, entry.Key.Section, entry.Key.Key);
                        go.transform.Find("Input").GetComponent<InputField>().characterValidation =
                            InputField.CharacterValidation.Decimal;
                        SetProperties(go.GetComponent<ConfigBoundFloat>(), entry);
                        go.transform.Find("Input").GetComponent<InputField>().onValueChanged.AddListener(x =>
                        {
                            go.transform.Find("Input").GetComponent<InputField>().textComponent.color =
                                go.GetComponent<ConfigBoundFloat>().IsValid() ? Color.white : Color.red;
                        });
                    }
                    else if (entry.Value.SettingType == typeof(KeyCode) &&
                             ZInput.instance.m_buttons.ContainsKey(entry.Value.GetBoundButtonName()))
                    {
                        // Create key binder
                        var buttonName = entry.Value.GetBoundButtonName();
                        var buttonText =
                            $"{entry.Value.Description.Description}{Environment.NewLine}This key is bound to button '{buttonName.Split('!')[0]}'.";
                        if (!string.IsNullOrEmpty(buttonName) && ConfigurationKeybindings.ContainsKey(buttonName))
                        {
                            var duplicateKeybindingText = "";
                            if (ConfigurationKeybindings[buttonName].Count > 1)
                            {
                                duplicateKeybindingText +=
                                    $"{Environment.NewLine}Other mods using this button:{Environment.NewLine}";
                                foreach (var buttons in ConfigurationKeybindings[buttonName])
                                {
                                    // If it is the same config entry, just skip it
                                    if (buttons.Item2 == entry.Key && buttons.Item1 == mod.Key)
                                    {
                                        continue;
                                    }

                                    // Add modguid as text
                                    duplicateKeybindingText += $"{buttons.Item1}, ";
                                }

                                // add to buttonText, but without last ', '
                                buttonText += duplicateKeybindingText.Trim(' ').TrimEnd(',');
                            }
                        }

                        var go = CreateKeybindElement(viewport,
                            entry.Key.Key + ":", buttonText,
                            mod.Value.Info.Metadata.GUID, entry.Key.Section, entry.Key.Key, buttonName, innerWidth);
                        go.GetComponent<ConfigBoundKeyCode>()
                            .SetData(mod.Value.Info.Metadata.GUID, entry.Key.Section, entry.Key.Key);
                        SetProperties(go.GetComponent<ConfigBoundKeyCode>(), entry);
                    }
                    else if (entry.Value.SettingType == typeof(string))
                    {
                        // Create input field string
                        var go = CreateTextInputField(viewport,
                            entry.Key.Key + ":",
                            entryAttributes.EntryColor,
                            entry.Value.Description.Description + (entryAttributes.IsAdminOnly
                                ? $"{Environment.NewLine}(Server side setting)"
                                : ""),
                            entryAttributes.DescriptionColor, innerWidth);
                        go.AddComponent<ConfigBoundString>()
                            .SetData(mod.Value.Info.Metadata.GUID, entry.Key.Section, entry.Key.Key);
                        go.transform.Find("Input").GetComponent<InputField>().characterValidation =
                            InputField.CharacterValidation.None;
                        SetProperties(go.GetComponent<ConfigBoundString>(), entry);
                    }
                    else if (entry.Value.SettingType == typeof(Color))
                    {
                        // Create input field string with color picker
                        var go = CreateColorInputField(viewport,
                            entry.Key.Key + ":",
                            entryAttributes.EntryColor,
                            entry.Value.Description.Description + (entryAttributes.IsAdminOnly
                                ? $"{Environment.NewLine}(Server side setting)"
                                : ""),
                            entryAttributes.DescriptionColor, innerWidth);
                        var conf = go.AddComponent<ConfigBoundColor>();
                        conf.Register();
                        conf.SetData(mod.Value.Info.Metadata.GUID, entry.Key.Section, entry.Key.Key);
                        conf.Input.characterValidation = InputField.CharacterValidation.None;
                        conf.Input.contentType = InputField.ContentType.Alphanumeric;
                        SetProperties(conf, entry);
                    }
                }
            }
            ConfigTabs.Add(tabContent);
        }

        /// <summary>
        ///     Get all config entries of a module
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        private static IEnumerable<KeyValuePair<ConfigDefinition, ConfigEntryBase>> GetConfigurationEntries(BaseUnityPlugin module)
        {
            using var enumerator = module.Config.GetEnumerator();
            while (enumerator.MoveNext())
            {
                yield return enumerator.Current;
            }
        }

        private static void Settings_OnOk(On.Settings.orig_OnOk orig, Settings self)
        {
            try { ColorPicker.Done(); } catch (Exception) { }
            orig(self);
            On.Settings.OnOk -= Settings_OnOk;
        }

        private static void Settings_OnBack(On.Settings.orig_OnBack orig, Settings self)
        {
            try { ColorPicker.Cancel(); } catch (Exception) { }
            orig(self);
            On.Settings.OnBack -= Settings_OnBack;
        }

        /// <summary>
        ///     SaveSettings Hook
        /// </summary>
        /// <param name="orig"></param>
        /// <param name="self"></param>
        private static void Settings_SaveSettings(On.Settings.orig_SaveSettings orig, Settings self)
        {
            orig(self);

            // Iterate over all tabs
            foreach (GameObject tab in ConfigTabs)
            {
                // Just iterate over the children in the scroll view and act if we find a ConfigBound<T> component
                foreach (Transform tabTransform in tab.transform.Find("Scroll View/Viewport/Content"))
                {
                    var childBoolean = tabTransform.gameObject.GetComponent<ConfigBoundBoolean>();
                    if (childBoolean != null)
                    {
                        childBoolean.WriteBack();
                        continue;
                    }

                    var childInt = tabTransform.gameObject.GetComponent<ConfigBoundInt>();
                    if (childInt != null)
                    {
                        childInt.WriteBack();
                        continue;
                    }

                    var childFloat = tabTransform.gameObject.GetComponent<ConfigBoundFloat>();
                    if (childFloat != null)
                    {
                        childFloat.WriteBack();
                        continue;
                    }

                    var childKeyCode = tabTransform.gameObject.GetComponent<ConfigBoundKeyCode>();
                    if (childKeyCode != null)
                    {
                        childKeyCode.WriteBack();
                        continue;
                    }

                    var childString = tabTransform.gameObject.GetComponent<ConfigBoundString>();
                    if (childString != null)
                    {
                        childString.WriteBack();
                        continue;
                    }

                    var childColor = tabTransform.gameObject.GetComponent<ConfigBoundColor>();
                    if (childColor != null)
                    {
                        childColor.WriteBack();
                        continue;
                    }
                }
            }

            // Remove hook again until the next time
            On.Settings.SaveSettings -= Settings_SaveSettings;
        }

        /// <summary>
        ///     Set the properties of the <see cref="ConfigBound{T}" />
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="binding"></param>
        /// <param name="entry"></param>
        private static void SetProperties<T>(ConfigBound<T> binding, KeyValuePair<ConfigDefinition, ConfigEntryBase> entry)
        {
            var configurationManagerAttribute =
                (ConfigurationManagerAttributes)entry.Value.Description.Tags.FirstOrDefault(x => x is ConfigurationManagerAttributes);

            // Only act, if we have a valid ConfigurationManagerAttributes tag
            if (configurationManagerAttribute != null)
            {
                binding.SetReadOnly(configurationManagerAttribute.ReadOnly == true);

                // Disable the input field if it is a synchronizable and not unlocked
                if (configurationManagerAttribute.IsAdminOnly && !configurationManagerAttribute.IsUnlocked)
                {
                    binding.SetEnabled(false);
                }
                else
                {
                    binding.SetEnabled(true);
                }

                // and set it's default value
                binding.Default = (T)entry.Value.DefaultValue;
            }

            // Set clamp
            binding.Clamp = entry.Value.Description.AcceptableValues;

            // set the value from the configuration
            binding.Value = binding.GetValueFromConfig();
        }

        /// <summary>
        ///     Create a text input field (used for string, int, float)
        /// </summary>
        /// <param name="parent">parent transform</param>
        /// <param name="labelname">Label text</param>
        /// <param name="labelColor">Color of the label</param>
        /// <param name="description">Description text</param>
        /// <param name="descriptionColor">Color of the description text</param>
        /// <param name="width">Width</param>
        /// <returns></returns>
        private static GameObject CreateTextInputField(Transform parent, string labelname, Color labelColor, string description, Color descriptionColor, float width)
        {
            // Create the outer gameobject first
            var result = new GameObject("TextField", typeof(RectTransform), typeof(LayoutElement));
            result.SetWidth(width);
            result.transform.SetParent(parent, false);

            // create the label text
            var label = GUIManager.Instance.CreateText(labelname, result.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0),
                GUIManager.Instance.AveriaSerifBold, 16, labelColor, true, Color.black, width - 150f, 0, false);
            label.SetUpperLeft().SetToTextHeight();

            // create the description text
            var desc = GUIManager.Instance.CreateText(description, result.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0),
                GUIManager.Instance.AveriaSerifBold, 12, descriptionColor, true, Color.black, width - 150f, 0, false).SetUpperLeft();
            desc.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -(label.GetHeight() + 3f));
            desc.SetToTextHeight();

            // calculate combined height
            result.SetHeight(label.GetHeight() + 3f + desc.GetHeight() + 15f);

            // Add the input field element
            var field = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField)).SetUpperRight().SetSize(140f, label.GetHeight() + 6f);
            field.GetComponent<Image>().sprite = GUIManager.Instance.GetSprite("text_field");
            field.GetComponent<Image>().type = Image.Type.Sliced;
            field.transform.SetParent(result.transform, false);

            var inputField = field.GetComponent<InputField>();

            var text = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline)).SetMiddleLeft().SetHeight(label.GetHeight() + 6f)
                .SetWidth(130f);
            inputField.textComponent = text.GetComponent<Text>();
            text.transform.SetParent(field.transform, false);
            text.GetComponent<RectTransform>().anchoredPosition = new Vector2(5, 0);
            text.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;
            text.GetComponent<Text>().font = GUIManager.Instance.AveriaSerifBold;

            // create the placeholder element
            var placeholder = new GameObject("Placeholder", typeof(RectTransform), typeof(Text)).SetMiddleLeft().SetHeight(label.GetHeight() + 6f)
                .SetWidth(130f);
            inputField.placeholder = placeholder.GetComponent<Text>();
            placeholder.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;
            placeholder.GetComponent<Text>().text = "";
            placeholder.GetComponent<Text>().font = GUIManager.Instance.AveriaSerifBold;
            placeholder.GetComponent<Text>().fontStyle = FontStyle.Italic;
            placeholder.GetComponent<Text>().color = Color.gray;
            placeholder.transform.SetParent(field.transform, false);
            placeholder.GetComponent<RectTransform>().anchoredPosition = new Vector2(5, 0);

            // set the preferred height on the layout element
            result.GetComponent<LayoutElement>().preferredHeight = result.GetComponent<RectTransform>().rect.height;
            return result;
        }

        /// <summary>
        ///     Create a text input field and a ColorPicker button (used for Color)
        /// </summary>
        /// <param name="parent">parent transform</param>
        /// <param name="labelname">Label text</param>
        /// <param name="labelColor">Color of the label</param>
        /// <param name="description">Description text</param>
        /// <param name="descriptionColor">Color of the description text</param>
        /// <param name="width">Width</param>
        /// <returns></returns>
        private static GameObject CreateColorInputField(Transform parent, string labelname, Color labelColor, string description, Color descriptionColor, float width)
        {
            // Create the outer gameobject first
            var result = new GameObject("TextField", typeof(RectTransform), typeof(LayoutElement));
            result.SetWidth(width);
            result.transform.SetParent(parent, false);

            // create the label text
            var label = GUIManager.Instance.CreateText(labelname, result.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0),
                GUIManager.Instance.AveriaSerifBold, 16, labelColor, true, Color.black, width - 150f, 0, false);
            label.SetUpperLeft().SetToTextHeight();

            // create the description text
            var desc = GUIManager.Instance.CreateText(description, result.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0),
                GUIManager.Instance.AveriaSerifBold, 12, descriptionColor, true, Color.black, width - 150f, 0, false).SetUpperLeft();
            desc.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -(label.GetHeight() + 3f));
            desc.SetToTextHeight();

            // calculate combined height
            result.SetHeight(label.GetHeight() + 3f + desc.GetHeight() + 15f);

            // Add a layout component
            var layout = new GameObject("Layout", typeof(RectTransform), typeof(LayoutElement)).SetUpperRight().SetSize(140f, label.GetHeight() + 6f);
            layout.transform.SetParent(result.transform, false);

            // Add the input field element
            var field = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField)).SetUpperLeft().SetSize(100f, label.GetHeight() + 6f);
            field.GetComponent<Image>().sprite = GUIManager.Instance.GetSprite("text_field");
            field.GetComponent<Image>().type = Image.Type.Sliced;
            field.transform.SetParent(layout.transform, false);

            var inputField = field.GetComponent<InputField>();

            var text = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline)).SetMiddleLeft().SetHeight(label.GetHeight() + 6f)
                .SetWidth(130f);
            inputField.textComponent = text.GetComponent<Text>();
            text.transform.SetParent(field.transform, false);
            text.GetComponent<RectTransform>().anchoredPosition = new Vector2(5, 0);
            text.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;
            text.GetComponent<Text>().font = GUIManager.Instance.AveriaSerifBold;

            // create the placeholder element
            var placeholder = new GameObject("Placeholder", typeof(RectTransform), typeof(Text)).SetMiddleLeft().SetHeight(label.GetHeight() + 6f)
                .SetWidth(130f);
            inputField.placeholder = placeholder.GetComponent<Text>();
            placeholder.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;
            placeholder.GetComponent<Text>().text = "";
            placeholder.GetComponent<Text>().font = GUIManager.Instance.AveriaSerifBold;
            placeholder.GetComponent<Text>().fontStyle = FontStyle.Italic;
            placeholder.GetComponent<Text>().color = Color.gray;
            placeholder.transform.SetParent(field.transform, false);
            placeholder.GetComponent<RectTransform>().anchoredPosition = new Vector2(5, 0);

            // Add the ColorPicker button
            var button = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(ButtonSfx)).SetUpperRight().SetSize(30f, label.GetHeight() + 6f);
            button.transform.SetParent(layout.transform, false);

            // Image
            var image = button.GetComponent<Image>();
            var sprite = GUIManager.Instance.GetSprite("button");
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 2f;
            button.GetComponent<Button>().image = image;

            // SFX
            var sfx = button.GetComponent<ButtonSfx>();
            sfx.m_sfxPrefab = PrefabManager.Cache.GetPrefab<GameObject>("sfx_gui_button");
            sfx.m_selectSfxPrefab = PrefabManager.Cache.GetPrefab<GameObject>("sfx_gui_select");

            // Colors
            var tinter = new ColorBlock()
            {
                disabledColor = new Color(0.566f, 0.566f, 0.566f, 0.502f),
                fadeDuration = 0.1f,
                normalColor = new Color(0.824f, 0.824f, 0.824f, 1f),
                highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f),
                pressedColor = new Color(0.537f, 0.556f, 0.556f, 1f),
                selectedColor = new Color(0.824f, 0.824f, 0.824f, 1f),
                colorMultiplier = 1f
            };
            button.GetComponent<Button>().colors = tinter;

            // set the preferred height on the layout element
            result.GetComponent<LayoutElement>().preferredHeight = result.GetComponent<RectTransform>().rect.height;
            return result;
        }

        /// <summary>
        ///     Create a toggle element
        /// </summary>
        /// <param name="parent">parent transform</param>
        /// <param name="labelname">label text</param>
        /// <param name="labelColor">Color of the label</param>
        /// <param name="description">Description text</param>
        /// <param name="descriptionColor">Color of the description text</param>
        /// <param name="modguid">module GUID</param>
        /// <param name="section">section</param>
        /// <param name="key">key</param>
        /// <param name="width">width</param>
        /// <returns></returns>
        private static GameObject CreateToggleElement(Transform parent, string labelname, Color labelColor, string description, Color descriptionColor,
            string modguid, string section, string key, float width)
        {
            // Create the outer gameobject first
            var result = new GameObject("Toggler", typeof(RectTransform));
            result.transform.SetParent(parent, false);
            result.SetWidth(width);

            // and now the toggle itself
            GUIManager.Instance.CreateToggle(result.transform, 28f, 28f).SetUpperRight();

            // create the label text element
            var label = GUIManager.Instance.CreateText(labelname, result.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0),
                GUIManager.Instance.AveriaSerifBold, 16, labelColor, true, Color.black, width - 45f, 0, true).SetUpperLeft().SetToTextHeight();
            label.SetWidth(width - 45f);
            label.SetToTextHeight();
            label.transform.SetParent(result.transform, false);

            // create the description text element (easy mode, just copy the label element and change some properties)
            var desc = Object.Instantiate(result.transform.Find("Text").gameObject, result.transform);
            desc.name = "Description";
            desc.GetComponent<Text>().color = descriptionColor;
            desc.GetComponent<Text>().fontSize = 12;
            desc.GetComponent<Text>().text = description;
            desc.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -(result.transform.Find("Text").gameObject.GetHeight() + 3f));
            desc.SetToTextHeight();


            result.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                -desc.GetComponent<RectTransform>().anchoredPosition.y + desc.GetComponent<Text>().preferredHeight + 15f);

            // and add a layout element
            var layoutElement = result.AddComponent<LayoutElement>();
            layoutElement.preferredHeight =
                Math.Max(38f, -desc.GetComponent<RectTransform>().anchoredPosition.y + desc.GetComponent<Text>().preferredHeight) + 15f;
            result.SetHeight(layoutElement.preferredHeight);

            // Bind to config entry
            result.AddComponent<ConfigBoundBoolean>().SetData(modguid, section, key);

            return result;
        }

        /// <summary>
        ///     Create a keybinding element
        /// </summary>
        /// <param name="parent">parent transform</param>
        /// <param name="labelname">label text</param>
        /// <param name="description">description text</param>
        /// <param name="modguid">module GUID</param>
        /// <param name="section">section</param>
        /// <param name="key">key</param>
        /// ´<param name="buttonName">buttonName</param>
        /// <param name="width">width</param>
        /// <returns></returns>
        private static GameObject CreateKeybindElement(Transform parent, string labelname, string description, string modguid, string section, string key,
            string buttonName, float width)
        {
            // Create label and keybind button
            var result = GUIManager.Instance.CreateKeyBindField(labelname, parent, width, 0);

            // Add this keybinding to the list in Settings to utilize valheim's keybind dialog
            Settings.instance.m_keys.Add(new Settings.KeySetting
            {
                //m_keyName = $"{buttonName}!{modguid}", m_keyTransform = result.GetComponent<RectTransform>()
                m_keyName = buttonName,
                m_keyTransform = result.GetComponent<RectTransform>()
            });

            // Create description text
            var idx = 0;
            var lastPosition = new Vector2(0, -result.GetComponent<RectTransform>().rect.height - 3f);
            GameObject desc = null;
            foreach (var part in description.Split(Environment.NewLine[0]))
            {
                var p2 = part.Trim();
                desc = GUIManager.Instance.CreateText(p2, result.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0),
                    GUIManager.Instance.AveriaSerifBold, 12, Color.white, true, Color.black, width - 150f, 0, false);
                desc.name = $"Description{idx}";
                desc.SetUpperLeft().SetToTextHeight();

                desc.GetComponent<RectTransform>().anchoredPosition = lastPosition;
                lastPosition = new Vector2(0, lastPosition.y - desc.GetHeight() - 3);

                idx++;
            }

            // set height and add the layout element
            result.SetHeight(-desc.GetComponent<RectTransform>().anchoredPosition.y + desc.GetComponent<Text>().preferredHeight + 15f);
            result.AddComponent<LayoutElement>().preferredHeight = result.GetComponent<RectTransform>().rect.height;

            // and add the config binding
            result.AddComponent<ConfigBoundKeyCode>().SetData(modguid, section, key);

            return result;
        }


        // Helper classes 

        /// <summary>
        ///     Generic abstract version of the config binding class
        /// </summary>
        /// <typeparam name="T"></typeparam>
        internal abstract class ConfigBound<T> : MonoBehaviour
        {
            public string ModGUID { get; set; }
            public string Section { get; set; }
            public string Key { get; set; }

            public AcceptableValueBase Clamp { get; set; }

            public T Default { get; set; }

            public T Value
            {
                get => GetValue();
                set => SetValue(value);
            }

            internal abstract T GetValueFromConfig();

            public abstract void SetValueInConfig(T value);

            public abstract T GetValue();
            internal abstract void SetValue(T value);


            public void WriteBack()
            {
                SetValueInConfig(GetValue());
            }

            public void SetData(string modGuid, string section, string key)
            {
                ModGUID = modGuid;
                Section = section;
                Key = key;
                var value = GetValueFromConfig();

                SetValue(value);
            }

            public abstract void SetEnabled(bool enabled);

            public abstract void SetReadOnly(bool readOnly);

            public void Reset()
            {
                SetValue(Default);
            }

            // Wrap AcceptableValueBase's IsValid
            public bool IsValid()
            {
                if (Clamp != null)
                {
                    var value = GetValue();
                    return Clamp.IsValid(value);
                }

                return true;
            }
        }

        /// <summary>
        ///     Boolean Binding
        /// </summary>
        internal class ConfigBoundBoolean : ConfigBound<bool>
        {
            internal override bool GetValueFromConfig()
            {
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key];
                return (bool)entry.BoxedValue;
            }

            public override void SetValueInConfig(bool value)
            {
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key] as ConfigEntry<bool>;
                entry.Value = value;
            }

            public override bool GetValue()
            {
                return gameObject.transform.Find("Toggle").GetComponent<Toggle>().isOn;
            }

            internal override void SetValue(bool value)
            {
                gameObject.transform.Find("Toggle").GetComponent<Toggle>().isOn = value;
            }

            public override void SetEnabled(bool enabled)
            {
                gameObject.transform.Find("Toggle").GetComponent<Toggle>().enabled = enabled;
            }

            public override void SetReadOnly(bool readOnly)
            {
                gameObject.transform.Find("Toggle").GetComponent<Toggle>().enabled = !readOnly;
            }
        }

        /// <summary>
        ///     Integer binding
        /// </summary>
        internal class ConfigBoundInt : ConfigBound<int>
        {
            internal override int GetValueFromConfig()
            {
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key];
                return (int)entry.BoxedValue;
            }

            public override void SetValueInConfig(int value)
            {
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key] as ConfigEntry<int>;
                entry.Value = value;
            }

            public override int GetValue()
            {
                int temp;
                var text = gameObject.transform.Find("Input").GetComponent<InputField>();
                if (!int.TryParse(text.text, out temp))
                {
                    temp = Default;
                }

                return temp;
            }

            internal override void SetValue(int value)
            {
                gameObject.transform.Find("Input").GetComponent<InputField>().text = value.ToString();
            }

            public override void SetEnabled(bool enabled)
            {
                gameObject.transform.Find("Input").GetComponent<InputField>().enabled = enabled;
            }

            public override void SetReadOnly(bool readOnly)
            {
                gameObject.transform.Find("Input").GetComponent<InputField>().readOnly = readOnly;
                gameObject.transform.Find("Input").GetComponent<InputField>().textComponent.color = readOnly ? Color.grey : Color.white;
            }
        }

        /// <summary>
        ///     Float binding
        /// </summary>
        internal class ConfigBoundFloat : ConfigBound<float>
        {
            internal override float GetValueFromConfig()
            {
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key];
                return (float)entry.BoxedValue;
            }

            public override void SetValueInConfig(float value)
            {
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key] as ConfigEntry<float>;
                entry.Value = value;
            }

            public override float GetValue()
            {
                float temp;
                var text = gameObject.transform.Find("Input").GetComponent<InputField>();
                if (!float.TryParse(text.text, NumberStyles.Number, CultureInfo.CurrentCulture.NumberFormat, out temp))
                {
                    temp = Default;
                }

                return temp;
            }

            internal override void SetValue(float value)
            {
                gameObject.transform.Find("Input").GetComponent<InputField>().text = value.ToString("F3");
            }

            public override void SetEnabled(bool enabled)
            {
                gameObject.transform.Find("Input").GetComponent<InputField>().enabled = enabled;
            }

            public override void SetReadOnly(bool readOnly)
            {
                gameObject.transform.Find("Input").GetComponent<InputField>().readOnly = readOnly;
                gameObject.transform.Find("Input").GetComponent<InputField>().textComponent.color = readOnly ? Color.grey : Color.white;
            }
        }

        /// <summary>
        ///     KeyCode binding
        /// </summary>
        internal class ConfigBoundKeyCode : ConfigBound<KeyCode>
        {
            internal override KeyCode GetValueFromConfig()
            {
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key];
                return (KeyCode)entry.BoxedValue;
            }

            public override void SetValueInConfig(KeyCode value)
            {
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key] as ConfigEntry<KeyCode>;
                entry.Value = value;
            }

            public override KeyCode GetValue()
            {
                // TODO: Get and parse value from input field
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key];
                var temp = KeyCode.None;
                if (Enum.TryParse(gameObject.transform.Find("Button/Text").GetComponent<Text>().text, out temp))
                {
                    return temp;
                }

                Logger.LogError($"Error parsing Keycode {gameObject.transform.Find("Button/Text").GetComponent<Text>().text}");
                return temp;
            }

            internal override void SetValue(KeyCode value)
            {
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key];
                var buttonName = entry.GetBoundButtonName();
                gameObject.transform.Find("Button/Text").GetComponent<Text>().text = value.ToString();
            }

            public void Awake()
            {
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key];
                var buttonName = entry.GetBoundButtonName();
                gameObject.transform.Find("Button").GetComponent<Button>().onClick.AddListener(() =>
                {
                    KeyInBinding = this;
                    Settings.instance.OpenBindDialog(buttonName);
                });
            }

            public override void SetEnabled(bool enabled)
            {
                gameObject.transform.Find("Button").GetComponent<Button>().enabled = enabled;
            }

            public override void SetReadOnly(bool readOnly)
            {
                gameObject.transform.Find("Button").GetComponent<Button>().enabled &= readOnly;
                gameObject.transform.Find("Button/Text").GetComponent<Text>().color = readOnly ? Color.grey : Color.white;
            }
        }

        /// <summary>
        ///     String binding
        /// </summary>
        internal class ConfigBoundString : ConfigBound<string>
        {
            internal override string GetValueFromConfig()
            {
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key];
                return (string)entry.BoxedValue;
            }

            public override void SetValueInConfig(string value)
            {
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key] as ConfigEntry<string>;
                entry.Value = value;
            }

            public override string GetValue()
            {
                return gameObject.transform.Find("Input").GetComponent<InputField>().text;
            }

            internal override void SetValue(string value)
            {
                gameObject.transform.Find("Input").GetComponent<InputField>().text = value;
            }

            public override void SetEnabled(bool enabled)
            {
                gameObject.transform.Find("Input").GetComponent<InputField>().enabled = enabled;
            }

            public override void SetReadOnly(bool readOnly)
            {
                gameObject.transform.Find("Input").GetComponent<InputField>().readOnly = readOnly;
                gameObject.transform.Find("Input").GetComponent<InputField>().textComponent.color = readOnly ? Color.grey : Color.white;
            }
        }

        internal class ConfigBoundColor : ConfigBound<Color>
        {
            internal InputField Input;
            internal Button Button;

            internal void Register()
            {
                Input = gameObject.transform.Find("Layout/Input").GetComponent<InputField>();
                Button = gameObject.transform.Find("Layout/Button").GetComponent<Button>();
                Button.onClick.AddListener(ShowColorPicker);
            }

            internal override Color GetValueFromConfig()
            {
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key] as ConfigEntry<Color>;
                return (Color)entry.BoxedValue;
            }

            public override void SetValueInConfig(Color value)
            {
                var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                var entry = pluginConfig[Section, Key] as ConfigEntry<Color>;
                entry.Value = value;
            }

            public override Color GetValue()
            {
                var col = Input.text;
                try
                {
                    return ColorFromString(col);
                }
                catch (Exception e)
                {
                    Logger.LogWarning(e.Message);
                    var pluginConfig = BepInExUtils.GetDependentPlugins(true).First(x => x.Key == ModGUID).Value.Config;
                    var entry = pluginConfig[Section, Key] as ConfigEntry<Color>;
                    Logger.LogWarning($"Using default value ({(Color)entry.DefaultValue}) instead.");
                    return (Color)entry.DefaultValue;
                }
            }

            internal override void SetValue(Color value)
            {
                Input.text = StringFromColor(value);
            }

            public override void SetEnabled(bool enabled)
            {
                Input.enabled = enabled;
                Button.enabled = enabled;
                if (enabled)
                {
                    Button.onClick.AddListener(ShowColorPicker);
                }
                else
                {
                    Button.onClick.RemoveAllListeners();
                }
            }

            public override void SetReadOnly(bool readOnly)
            {
                Input.readOnly = readOnly;
                Input.textComponent.color = readOnly ? Color.grey : Color.white;
                Button.enabled = !readOnly;
            }

            private void ShowColorPicker()
            {
                if (!ColorPicker.done)
                {
                    ColorPicker.Cancel();
                }
                GUIManager.Instance.CreateColorPicker(
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    GetValue(), Key, SetValue, null, true);
            }

            private string StringFromColor(Color col)
            {
                var r = (int)(col.r * 255f);
                var g = (int)(col.g * 255f);
                var b = (int)(col.b * 255f);
                var a = (int)(col.a * 255f);

                return $"{r:x2}{g:x2}{b:x2}{a:x2}".ToUpper();
            }

            private Color ColorFromString(string str)
            {
                if (long.TryParse(str.Trim().ToLower(), NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo, out var fromHex))
                {
                    var r = (int)(fromHex >> 24);
                    var g = (int)(fromHex >> 16 & 0xff);
                    var b = (int)(fromHex >> 8 & 0xff);
                    var a = (int)(fromHex & 0xff);
                    var result = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
                    return result;
                }

                throw new ArgumentException($"'{str}' is no valid color value");
            }
        }
    }
}
