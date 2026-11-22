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

        /// <summary>
        /// 任务库做过同名合并后，把挂在旧 id 上的进度挪到合并后的 id 上。
        /// 两边都有进度时取更靠后的状态，备注拼起来。返回挪动的条数。
        /// </summary>
        public int MigrateMerged(QuestDatabase db)
        {
            if (db == null) return 0;
            int moved = 0;
            foreach (var r in db.Regions)
            {
                foreach (var q in r.Quests)
                {
                    if (q.MergedIds == null || q.MergedIds.Count == 0) continue;
                    foreach (var oldId in q.MergedIds)
                    {
                        if (string.IsNullOrEmpty(oldId) || oldId == q.Id) continue;
                        QuestState old;
                        if (!States.TryGetValue(oldId, out old)) continue;
                        States.Remove(oldId);

                        QuestState now;
                        if (!States.TryGetValue(q.Id, out now))
                        {
                            States[q.Id] = old;
                            moved++;
                            continue;
                        }
                        if (old.S > now.S)
                        {
                            if (string.IsNullOrEmpty(now.Note) && !string.IsNullOrEmpty(old.Note)) now.Note = old.Note;
                            now.S = old.S;
                            if (!string.IsNullOrEmpty(old.At)) now.At = old.At;
                        }
                        else if (!string.IsNullOrEmpty(old.Note) && !string.IsNullOrEmpty(now.Note) && old.Note != now.Note)
                        {
                            now.Note = now.Note + "；" + old.Note;
                        }
                        else if (string.IsNullOrEmpty(now.Note) && !string.IsNullOrEmpty(old.Note))
                        {
                            now.Note = old.Note;
                        }
                        moved++;
                    }
                }
            }
            return moved;
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
        /// <summary>当前默认窗口尺寸的版本。改默认尺寸就 +1，老存档才会跟着换。</summary>
        public const int CurrentLayoutVersion = 1;
        public const int DefaultWinW = 392;
        public const int DefaultWinH = 440;

        public double Opacity = 0.75;
        public bool Folded = false;
        public bool FilterIncomplete = false;
        /// <summary>左列把做完的版本/区域藏起来。</summary>
        public bool HideDoneRegions = false;
        public string LastRegion = "";

        /// <summary>当前展开的章节（左列按版本折叠，一次只开一章）。</summary>
        public string OpenChapter = "";
        /// <summary>右列的类型分页签："" = 全部。</summary>
        public string TypeFilter = "";

        public int WinX = int.MinValue;
        public int WinY = int.MinValue;
        public int WinW = 392;
        public int WinH = 440;
        /// <summary>默认尺寸的版本。老存档是 0，读到比当前小的值就把窗口尺寸刷成新默认。</summary>
        public int LayoutVersion = 0;
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
            d["hideDoneRegions"] = HideDoneRegions;
            if (!string.IsNullOrEmpty(LastRegion)) d["lastRegion"] = LastRegion;
            if (!string.IsNullOrEmpty(OpenChapter)) d["openChapter"] = OpenChapter;
            if (!string.IsNullOrEmpty(TypeFilter)) d["typeFilter"] = TypeFilter;
            if (WinX != int.MinValue) d["winX"] = WinX;
            if (WinY != int.MinValue) d["winY"] = WinY;
            d["winW"] = WinW;
            d["winH"] = WinH;
            d["layoutVersion"] = LayoutVersion;
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
            s.HideDoneRegions = Json.Bool(d, "hideDoneRegions", false);
            s.LastRegion = Json.Str(d, "lastRegion", "");
            s.OpenChapter = Json.Str(d, "openChapter", "");
            s.TypeFilter = Json.Str(d, "typeFilter", "");
            s.WinX = Json.Int(d, "winX", int.MinValue);
            s.WinY = Json.Int(d, "winY", int.MinValue);
            s.WinW = Json.Int(d, "winW", DefaultWinW);
            s.WinH = Json.Int(d, "winH", DefaultWinH);
            s.LayoutVersion = Json.Int(d, "layoutVersion", 0);
            s.StripeX = Json.Int(d, "stripeX", int.MinValue);
            s.StripeY = Json.Int(d, "stripeY", int.MinValue);
            s.HkToggle = Json.Str(d, "hkToggle", "Ctrl+Alt+W");
            s.HkLock = Json.Str(d, "hkLock", "Ctrl+Alt+K");
            s.HkFold = Json.Str(d, "hkFold", "Ctrl+Alt+F");
            // 默认尺寸换过一轮，老存档里那份旧尺寸就不作数了，
            // 否则改了默认值也只能影响新装的用户
            if (s.LayoutVersion < CurrentLayoutVersion)
            {
                s.LayoutVersion = CurrentLayoutVersion;
                s.WinW = DefaultWinW;
                s.WinH = DefaultWinH;
                s.WinX = int.MinValue;
                s.WinY = int.MinValue;
            }
            if (s.WinW < 320) s.WinW = DefaultWinW;
            if (s.WinH < 300) s.WinH = DefaultWinH;
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

        /// <summary>所有存档都跟着任务库的合并走一遍，挪完有变化才存盘。</summary>
        public int MigrateAll(QuestDatabase db)
        {
            int n = 0;
            foreach (var kv in Profiles) n += kv.Value.MigrateMerged(db);
            if (n > 0) Save();
            return n;
        }

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
