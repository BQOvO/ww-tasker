using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;

namespace WuwaChecklist
{
    /// <summary>
    /// 任务库的来源与更新。
    /// 优先级：用户目录里的 quests.json（检查更新拉下来的） &gt; 内嵌在 exe 里的那份。
    /// </summary>
    internal static class QuestStore
    {
        public const string RepoUrl = "https://github.com/BQOvO/ww-tasker";

        public const string RemoteUrl =
            "https://raw.githubusercontent.com/BQOvO/ww-tasker/main/data/quests.json";

        public static string LocalPath
        {
            get { return Path.Combine(Store.DefaultDir(), "quests.json"); }
        }

        /// <summary>本地有没有从网上下过一份。没有的话启动时后台悄悄拉一次。</summary>
        public static bool HasLocalCopy
        {
            get { return File.Exists(LocalPath); }
        }

        /// <summary>当前这份任务库是从哪来的，显示在设置里。</summary>
        public static string SourceNote = "内置";

        public static QuestDatabase Load()
        {
            // 1) 用户目录里有就用它（检查更新下下来的）
            try
            {
                if (File.Exists(LocalPath))
                {
                    object parsed;
                    string err;
                    if (Json.TryParse(File.ReadAllText(LocalPath, Encoding.UTF8), out parsed, out err))
                    {
                        var db = QuestDatabase.FromJson(Json.Obj(parsed));
                        if (db.Regions.Count > 0)
                        {
                            SourceNote = "外部（" + (string.IsNullOrEmpty(db.UpdatedAt) ? LocalPath : db.UpdatedAt) + "）";
                            return db;
                        }
                    }
                }
            }
            catch
            {
                // 外部那份坏了就退回内置，不让程序起不来
            }

            // 2) 内嵌资源
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("quests.json"))
                {
                    if (stream != null)
                    {
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            object parsed;
                            string err;
                            string text = reader.ReadToEnd();
                            if (Json.TryParse(text, out parsed, out err))
                            {
                                var db = QuestDatabase.FromJson(Json.Obj(parsed));
                                if (db.Regions.Count > 0)
                                {
                                    SourceNote = "内置（v" + db.GameVersion + "）";
                                    return db;
                                }
                                throw new FormatException("内置任务库没有区域：" + err);
                            }
                            throw new FormatException("内置任务库 JSON 解析失败：" + err);
                        }
                    }
                }
            }
            catch
            {
                // 落到最后的空库
            }

            SourceNote = "空库（内置资源读取失败）";
            var empty = new QuestDatabase();
            empty.Regions.Add(new Region { Id = "empty", Name = "（任务库为空）", Order = 0 });
            return empty;
        }

        /// <summary>
        /// 检查更新：拉一次远端 JSON，成功了才落盘。
        /// 全部同步执行，调用方自己丢到线程里，别阻塞 UI。
        /// </summary>
        public static bool TryFetchRemote(out string message, out QuestDatabase fetched)
        {
            fetched = null;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                string text;
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    wc.Headers.Add("User-Agent", "ww-tasker");
                    wc.Headers.Add("Cache-Control", "no-cache");
                    text = wc.DownloadString(RemoteUrl);
                }

                if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
                {
                    message = "拉取失败：返回内容为空。";
                    return false;
                }

                object parsed;
                string err;
                if (!Json.TryParse(text, out parsed, out err))
                {
                    message = "拉取失败：远端 JSON 解析不过（" + err + "）。";
                    return false;
                }

                var db = QuestDatabase.FromJson(Json.Obj(parsed));
                if (db.Regions.Count == 0)
                {
                    message = "拉取失败：远端任务库没有任何区域。";
                    return false;
                }

                string dir = Store.DefaultDir();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string tmp = LocalPath + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                if (File.Exists(LocalPath)) File.Copy(LocalPath, LocalPath + ".bak", true);
                File.Copy(tmp, LocalPath, true);
                File.Delete(tmp);

                fetched = db;
                int n = 0;
                foreach (var r in db.Regions) n += r.Quests.Count;
                message = "已更新：" + db.Regions.Count + " 个区域 / " + n + " 条任务"
                          + (string.IsNullOrEmpty(db.GameVersion) ? "" : "，游戏版本 " + db.GameVersion) + "。";
                return true;
            }
            catch (Exception ex)
            {
                message = "拉取失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>把当前库导出回本地文件（设置里那个「导出」用）。</summary>
        public static bool ExportToFile(string path, QuestDatabase db)
        {
            try
            {
                var root = db.ToJson();
                root["updatedAt"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                File.WriteAllText(path, Json.Stringify(root, true), new UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
