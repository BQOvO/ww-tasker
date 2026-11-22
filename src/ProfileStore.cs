using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WuwaChecklist
{
    /// <summary>单条任务在某存档里的状态。</summary>
    internal sealed class QuestState
    {
        public const int None = 0;      // 未开始
        public const int Doing = 1;     // 进行中
        public const int Done = 2;      // 已完成

        public int S = None;
        public string Note = "";
        public string At = "";

        public QuestState Clone()
        {
            return new QuestState { S = S, Note = Note, At = At };
        }

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            d["s"] = S;
            if (!string.IsNullOrEmpty(Note)) d["note"] = Note;
            if (!string.IsNullOrEmpty(At)) d["at"] = At;
            return d;
        }

        public static QuestState FromJson(Dictionary<string, object> d)
        {
            var st = new QuestState();
            st.S = Json.Int(d, "s", None);
            if (st.S < 0 || st.S > 2) st.S = None;
            st.Note = Json.Str(d, "note", "");
            st.At = Json.Str(d, "at", "");
            return st;
        }
    }

    internal sealed class Profile
    {
        public string Name = "默认";
        public Dictionary<string, QuestState> States = new Dictionary<string, QuestState>(StringComparer.Ordinal);

        public int Get(string questId)
        {
            QuestState st;
            if (States.TryGetValue(questId, out st)) return st.S;
            return QuestState.None;
        }

        public QuestState GetOrCreate(string questId)
        {
            QuestState st;
            if (!States.TryGetValue(questId, out st))
            {
                st = new QuestState();
                States[questId] = st;
            }
            return st;
        }

        public void Set(string questId, int state)
        {
            if (state == QuestState.None)
            {
                QuestState existing;
                if (States.TryGetValue(questId, out existing) && string.IsNullOrEmpty(existing.Note))
                    States.Remove(questId);
                else if (existing != null) existing.S = QuestState.None;
                return;
            }
            var st = GetOrCreate(questId);
            st.S = state;
            st.At = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            var states = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in States) states[kv.Key] = kv.Value.ToJson();
            d["states"] = states;
            return d;
        }

        public static Profile FromJson(string name, Dictionary<string, object> d)
        {
            var p = new Profile();
            p.Name = name;
            object sv;
            if (d != null && d.TryGetValue("states", out sv))
            {
                foreach (var kv in Json.Obj(sv))
                    p.States[kv.Key] = QuestState.FromJson(Json.Obj(kv.Value));
            }
            return p;
        }
    }

    internal sealed class AppSettings
    {
        public double Opacity = 0.75;
        public bool Folded = false;
        public bool FilterIncomplete = false;
        public string LastRegion = "";
        public int WinX = int.MinValue;
        public int WinY = int.MinValue;
        public int WinW = 392;
        public int WinH = 632;
        public int StripeX = int.MinValue;
        public int StripeY = int.MinValue;

        public string HkToggle = "Ctrl+Alt+W";
        public string HkLock = "Ctrl+Alt+K";
        public string HkFold = "Ctrl+Alt+F";

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            d["opacity"] = Opacity;
            d["folded"] = Folded;
            d["filterIncomplete"] = FilterIncomplete;
            if (!string.IsNullOrEmpty(LastRegion)) d["lastRegion"] = LastRegion;
            if (WinX != int.MinValue) d["winX"] = WinX;
            if (WinY != int.MinValue) d["winY"] = WinY;
            d["winW"] = WinW;
            d["winH"] = WinH;
            if (StripeX != int.MinValue) d["stripeX"] = StripeX;
            if (StripeY != int.MinValue) d["stripeY"] = StripeY;
            d["hkToggle"] = HkToggle;
            d["hkLock"] = HkLock;
            d["hkFold"] = HkFold;
            return d;
        }

        public static AppSettings FromJson(Dictionary<string, object> d)
        {
            var s = new AppSettings();
            s.Opacity = Json.Dbl(d, "opacity", 0.75);
            if (s.Opacity < 0.2 || s.Opacity > 1.0) s.Opacity = 0.75;
            s.Folded = Json.Bool(d, "folded", false);
            s.FilterIncomplete = Json.Bool(d, "filterIncomplete", false);
            s.LastRegion = Json.Str(d, "lastRegion", "");
            s.WinX = Json.Int(d, "winX", int.MinValue);
            s.WinY = Json.Int(d, "winY", int.MinValue);
            s.WinW = Json.Int(d, "winW", 392);
            s.WinH = Json.Int(d, "winH", 632);
            s.StripeX = Json.Int(d, "stripeX", int.MinValue);
            s.StripeY = Json.Int(d, "stripeY", int.MinValue);
            s.HkToggle = Json.Str(d, "hkToggle", "Ctrl+Alt+W");
            s.HkLock = Json.Str(d, "hkLock", "Ctrl+Alt+K");
            s.HkFold = Json.Str(d, "hkFold", "Ctrl+Alt+F");
            if (s.WinW < 320) s.WinW = 392;
            if (s.WinH < 300) s.WinH = 632;
            return s;
        }
    }

    /// <summary>存档 + 设置的持久化。数据放 %APPDATA%\ww-tasker，换 exe 不丢进度。</summary>
    internal sealed class Store
    {
        public AppSettings Settings = new AppSettings();
        public Dictionary<string, Profile> Profiles = new Dictionary<string, Profile>(StringComparer.Ordinal);
        public string ActiveName = "默认";

        public string Dir { get; private set; }
        public string FilePath { get; private set; }
        public string LastError { get; private set; }

        public static string DefaultDir()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
                appData = Path.GetDirectoryName(typeof(Store).Assembly.Location);
            return Path.Combine(appData, "ww-tasker");
        }

        public Profile Active
        {
            get
            {
                Profile p;
                if (Profiles.TryGetValue(ActiveName, out p)) return p;
                p = new Profile { Name = ActiveName };
                Profiles[ActiveName] = p;
                return p;
            }
        }

        public static Store Load(string dir = null)
        {
            var s = new Store();
            s.Dir = dir ?? DefaultDir();
            s.FilePath = Path.Combine(s.Dir, "profile.json");

            string text = null;
            try
            {
                if (File.Exists(s.FilePath)) text = File.ReadAllText(s.FilePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                s.LastError = "读存档失败：" + ex.Message;
            }

            if (!string.IsNullOrEmpty(text))
            {
                object parsed;
                string err;
                if (Json.TryParse(text, out parsed, out err))
                {
                    var root = Json.Obj(parsed);
                    s.Settings = AppSettings.FromJson(Json.Obj(root.ContainsKey("settings") ? root["settings"] : null));

                    object pv;
                    if (root.TryGetValue("profiles", out pv))
                    {
                        foreach (var kv in Json.Obj(pv))
                            s.Profiles[kv.Key] = Profile.FromJson(kv.Key, Json.Obj(kv.Value));
                    }
                    string active = Json.Str(root, "activeProfile", "");
                    if (!string.IsNullOrEmpty(active) && s.Profiles.ContainsKey(active)) s.ActiveName = active;
                    else if (s.Profiles.Count > 0)
                    {
                        foreach (var k in s.Profiles.Keys) { s.ActiveName = k; break; }
                    }
                }
                else
                {
                    s.LastError = "存档 JSON 解析失败：" + err;
                }
            }

            if (s.Profiles.Count == 0) s.Profiles["默认"] = new Profile { Name = "默认" };
            if (!s.Profiles.ContainsKey(s.ActiveName)) s.ActiveName = FirstKey(s.Profiles);
            return s;
        }

        private static string FirstKey(Dictionary<string, Profile> d)
        {
            foreach (var k in d.Keys) return k;
            return "默认";
        }

        public bool Save()
        {
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);

                var root = new Dictionary<string, object>(StringComparer.Ordinal);
                root["schemaVersion"] = 1;
                root["activeProfile"] = ActiveName;
                root["settings"] = Settings.ToJson();
                var profiles = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var kv in Profiles) profiles[kv.Key] = kv.Value.ToJson();
                root["profiles"] = profiles;

                string json = Json.Stringify(root, true);

                // 先写临时文件再替换，避免写一半断电把存档写坏
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
                File.Copy(tmp, FilePath, true);
                File.Delete(tmp);
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = "写存档失败：" + ex.Message;
                return false;
            }
        }

        // ---------- 存档管理 ----------

        public bool CreateProfile(string name, bool copyFromActive = false)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return false;
            if (Profiles.ContainsKey(name)) return false;
            var p = new Profile { Name = name };
            if (copyFromActive)
            {
                foreach (var kv in Active.States) p.States[kv.Key] = kv.Value.Clone();
            }
            Profiles[name] = p;
            ActiveName = name;
            return Save();
        }

        public bool RenameProfile(string oldName, string newName)
        {
            oldName = (oldName ?? "").Trim();
            newName = (newName ?? "").Trim();
            if (newName.Length == 0 || oldName == newName) return false;
            Profile p;
            if (!Profiles.TryGetValue(oldName, out p)) return false;
            if (Profiles.ContainsKey(newName)) return false;
            Profiles.Remove(oldName);
            p.Name = newName;
            Profiles[newName] = p;
            if (ActiveName == oldName) ActiveName = newName;
            return Save();
        }

        public bool DeleteProfile(string name)
        {
            if (Profiles.Count <= 1) return false;
            if (!Profiles.ContainsKey(name)) return false;
            Profiles.Remove(name);
            if (ActiveName == name) ActiveName = FirstKey(Profiles);
            return Save();
        }
    }
}
