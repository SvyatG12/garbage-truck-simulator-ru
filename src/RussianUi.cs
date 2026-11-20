using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GarbageTruckRu
{
    public static class RussianUi
    {
        private const string Cyrillic = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Prompts = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<Font, bool> CyrillicSupport = new Dictionary<Font, bool>();
        private static bool loaded;

        private static bool failed;
        private static float nextRefresh;
        private static float nextDiscovery;
        private static Font windowsFont;

        private static TMP_Text[] tmpLabels;
        private static Text[] legacyLabels;
        private static TMP_Dropdown[] tmpLists;
        private static Dropdown[] legacyLists;
        private static Text menuHint;
        private static int playedMode;
        private static int playedTruck;
        private static int shownMode = -1;
        private static int shownTruck = -1;

        public static void BeginShift(int mode, int truck)
        {
            playedMode = mode;
            playedTruck = truck;
        }

        public static string Translate(string source, Component context)
        {
            LoadCatalog();
            string translated;
            if (context)
            {
                string key = context.name;
                if (key == "Tutorial_Street_1" && playedMode >= 6)
                    key = "Tutorial_Street_1_Infinite";
                if (key == "Tutorial_Street_6" || key == "Tutorial_Mission_6")
                    key = playedTruck == 2 ? "PressToggle" : playedTruck == 3 ? "PressSide" : "PressHold";
                if (Prompts.TryGetValue(key, out translated))
                    return translated;
            }
            if (!string.IsNullOrEmpty(source) && Labels.TryGetValue(source.Trim(), out translated))
                return translated;
            return source;
        }

        private static void LoadCatalog()
        {
            if (loaded)
                return;
            loaded = true;
            try
            {
                var path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "GarbageTruckRu", "ru.tsv");
                foreach (string line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                        continue;
                    string[] fields = line.Split('\t');
                    if (fields.Length != 2 || string.IsNullOrWhiteSpace(fields[0]) || string.IsNullOrWhiteSpace(fields[1]))
                        throw new FormatException("Invalid localization row.");
                    if (fields[0][0] == '@')
                        Prompts.Add(fields[0].Substring(1), fields[1].Replace("\\n", "\n"));
                    else
                        Labels.Add(fields[0], fields[1]);
                }
                Debug.Log("[GarbageTruckRu] Loaded " + (Labels.Count + Prompts.Count) + " translations.");
            }
            catch (Exception error)
            {
                Labels.Clear();
                Prompts.Clear();
                failed = true;
                Debug.LogError("[GarbageTruckRu] " + error);
            }
        }

        public static void Refresh()
        {
            if (failed || Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + 0.18f;
            try
            {
                LoadCatalog();
                if (failed)
                    return;
                if (tmpLabels == null || Time.unscaledTime >= nextDiscovery)
                {
                    nextDiscovery = Time.unscaledTime + 1f;
                    tmpLabels = Object.FindObjectsOfType<TMP_Text>(true);
                    legacyLabels = Object.FindObjectsOfType<Text>(true);
                    tmpLists = Object.FindObjectsOfType<TMP_Dropdown>(true);
                    legacyLists = Object.FindObjectsOfType<Dropdown>(true);
                    PrepareMenu();
                }
                foreach (TMP_Dropdown list in tmpLists)
                    if (list)
                        TranslateList(list);
                foreach (Dropdown list in legacyLists)
                    if (list)
                        TranslateList(list);
                foreach (TMP_Text label in tmpLabels)
                    if (label)
                        TranslateLabel(label);
                foreach (Text label in legacyLabels)
                    if (label)
                        TranslateLabel(label);
                UpdateMenuHint();
            }
            catch (Exception error)
            {
                failed = true;
                Debug.LogError("[GarbageTruckRu] " + error);
            }
        }

        private static void TranslateLabel(TMP_Text label)
        {
            if (label.name == "Highscore Entry Text_Name")
                return;

            string text = Translate(label.text, label);
            if (!HasCyrillic(text))
                return;

            if (!label.enableAutoSizing && text.IndexOf("<style=", StringComparison.Ordinal) < 0)
            {
                label.fontSizeMax = label.fontSize;
                label.fontSizeMin = label.fontSizeMax * 0.76f;
                label.enableAutoSizing = true;
            }
            if (label.text != text)
                label.text = text;
        }

        private static void TranslateLabel(Text label)
        {
            if (label.name == "Highscore Entry Text_Name")
                return;

            string text = Translate(label.text, label);
            if (!HasCyrillic(text))
                return;
            if (!label.font || !SupportsCyrillic(label.font))
            {
                if (!windowsFont)
                    windowsFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial" }, 24);
                label.font = windowsFont;
            }

            if (label.text != text)
                label.text = text;
        }

        private static bool SupportsCyrillic(Font font)
        {
            bool supported;
            if (!CyrillicSupport.TryGetValue(font, out supported))
            {
                supported = true;
                foreach (char character in Cyrillic)
                    if (!font.HasCharacter(character))
                    {
                        supported = false;
                        break;
                    }
                CyrillicSupport.Add(font, supported);
            }
            return supported;
        }

        private static bool HasCyrillic(string value)
        {
            if (value != null)
                foreach (char character in value)
                    if (character >= '\u0400' && character <= '\u04FF')
                        return true;
            return false;
        }

        private static void TranslateList(TMP_Dropdown list)
        {
            bool changed = false;
            foreach (TMP_Dropdown.OptionData item in list.options)
            {
                string text = Translate(item.text, null);
                if (text == item.text)
                    continue;
                item.text = text;
                changed = true;
            }
            if (changed)
                list.RefreshShownValue();
        }

        private static void TranslateList(Dropdown list)
        {
            bool changed = false;
            foreach (Dropdown.OptionData item in list.options)
            {
                string text = Translate(item.text, null);
                if (text == item.text)
                    continue;
                item.text = text;
                changed = true;
            }
            if (changed)
                list.RefreshShownValue();
        }

        private static void PrepareMenu()
        {
            if (menuHint)
                return;
            GameObject start = GameObject.Find("Button Start Level");
            if (!start)
                return;
            Button button = start.GetComponent<Button>();
            if (!button || !button.targetGraphic)
                return;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color32(25, 114, 99, 255);
            colors.highlightedColor = new Color32(30, 131, 113, 255);
            colors.pressedColor = new Color32(19, 88, 76, 255);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color32(65, 75, 73, 255);
            colors.colorMultiplier = 1f;
            button.targetGraphic.color = Color.white;
            button.colors = colors;

            var hint = new GameObject("GarbageTruckRu Mode Hint", typeof(RectTransform), typeof(Text));
            hint.layer = start.layer;
            hint.transform.SetParent(start.transform, false);
            menuHint = hint.GetComponent<Text>();
            RectTransform rect = menuHint.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.right;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(0f, -13f);
            rect.sizeDelta = new Vector2(0f, 162f);
            menuHint.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            menuHint.fontSize = 18;
            menuHint.resizeTextForBestFit = true;
            menuHint.resizeTextMinSize = 15;
            menuHint.resizeTextMaxSize = 18;
            menuHint.supportRichText = true;
            menuHint.color = new Color32(227, 232, 229, 255);
            menuHint.alignment = TextAnchor.UpperLeft;
            menuHint.raycastTarget = false;
            shownMode = -1;
            shownTruck = -1;
        }

        private static void UpdateMenuHint()
        {
            if (!menuHint)
                return;

            int mode = 0;
            int truck = 0;
            foreach (TMP_Dropdown list in tmpLists)
            {
                if (!list || !list.isActiveAndEnabled)
                    continue;
                if (list.name.StartsWith("Dropdown Mode ", StringComparison.Ordinal))
                    mode = list.value;
                else if (list.name.StartsWith("Dropdown Truck ", StringComparison.Ordinal))
                    truck = list.value;
            }
            if (mode == shownMode && truck == shownTruck)
                return;

            string modeDescription;
            string truckTip;
            string controls;
            Prompts.TryGetValue("mode." + mode, out modeDescription);
            Prompts.TryGetValue("truck." + truck, out truckTip);
            Prompts.TryGetValue("hint.controls", out controls);
            menuHint.text = modeDescription + "\n\n" + truckTip + "\n" + controls;
            shownMode = mode;
            shownTruck = truck;
        }
    }
}
