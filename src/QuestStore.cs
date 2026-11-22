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
            QuestDatabase local = TryLoadFile(LocalPath);

            // 2) 内嵌资源
            QuestDatabase builtin = TryLoadBuiltin();

            // 两份都有的时候比一下谁新：内嵌的更新（换了新版本 exe）就以它为准，
            // 再把本地那份并进去，这样用户手工加的任务不会丢。
            if (local != null && builtin != null
                && string.CompareOrdinal(builtin.UpdatedAt, local.UpdatedAt) > 0)
            {
                int merged, added;
                builtin.MergeFrom(DropLegacy(local), out merged, out added);
                SourceNote = "内置（v" + builtin.GameVersion + "）";
                WriteLocal(builtin);   // 落一份，下次启动就不用再合一次
                return builtin;
            }

            if (local != null)
            {
                SourceNote = "外部（" + (string.IsNullOrEmpty(local.UpdatedAt) ? LocalPath : local.UpdatedAt) + "）";
                return local;
            }

            if (builtin != null)
            {
                SourceNote = "内置（v" + builtin.GameVersion + "）";
                return builtin;
            }

            SourceNote = "空库（内置资源读取失败）";
            var empty = new QuestDatabase();
            empty.Regions.Add(new Region { Id = "empty", Name = "（任务库为空）", Order = 0 });
            return empty;
        }

        private static QuestDatabase TryLoadFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                object parsed;
                string err;
                if (!Json.TryParse(File.ReadAllText(path, Encoding.UTF8), out parsed, out err))
                    return null;
                var db = QuestDatabase.FromJson(Json.Obj(parsed));
                return db.Regions.Count > 0 ? db : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 0.1.0 那版的占位任务（id 形如 jz.main.01、区域 id 形如 huanglong.jinzhou）已经作废，
        /// 换任务库的时候别把它们的壳子并进来。只丢掉这些，用户自己加的不动。
        /// </summary>
        private static QuestDatabase DropLegacy(QuestDatabase db)
        {
            if (db == null) return null;
            var keep = new List<Region>();
            foreach (var r in db.Regions)
            {
                if (r.Id != null && r.Id.IndexOf('.') >= 0) continue;   // 旧区域 id 一律带点
                var qs = new List<Quest>();
                foreach (var q in r.Quests)
                {
                    if (q.Id != null && q.Id.IndexOf('.') >= 0) continue; // 旧任务 id 也带点
                    qs.Add(q);
                }
                r.Quests = qs;
                if (qs.Count > 0) keep.Add(r);
            }
            db.Regions = keep;

            // 同名区域（比如老库里「今州」叫 huanglong.jinzhou，新库叫 jz）并成一个，
            // 不然左列会出现两条「今州」。
            var byName = new Dictionary<string, Region>(StringComparer.Ordinal);
            var merged = new List<Region>(keep.Count);
            foreach (var r in keep)
            {
                string k = Quest.KeyOf(r.Name);
                Region hit;
                if (k.Length > 0 && byName.TryGetValue(k, out hit))
                {
                    foreach (var q in r.Quests)
                    {
                        q.RegionId = hit.Id;
                        hit.Quests.Add(q);
                    }
                    if (string.IsNullOrEmpty(hit.Chapter)) hit.Chapter = r.Chapter;
                    continue;
                }
                if (k.Length > 0) byName[k] = r;
                merged.Add(r);
            }
            db.Regions = merged;
            db.Reindex();
            return db;
        }

        private static QuestDatabase TryLoadBuiltin()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("quests.json"))
                {
                    if (stream == null) return null;
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        object parsed;
                        string err;
                        if (!Json.TryParse(reader.ReadToEnd(), out parsed, out err)) return null;
                        var db = QuestDatabase.FromJson(Json.Obj(parsed));
                        return db.Regions.Count > 0 ? db : null;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把一份任务库写到用户目录（.tmp 中转 + .bak 备份）。</summary>
        private static bool WriteLocal(QuestDatabase db)
        {
            try
            {
                string dir = Store.DefaultDir();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string body = Json.Stringify(db.ToJson(), true);
                string tmp = LocalPath + ".tmp";
                File.WriteAllText(tmp, body, new UTF8Encoding(false));
                if (File.Exists(LocalPath)) File.Copy(LocalPath, LocalPath + ".bak", true);
                File.Copy(tmp, LocalPath, true);
                File.Delete(tmp);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检查更新：拉一次远端 JSON，跟当前库合并（同地区同名合成一条），成功了才落盘。
        /// 全部同步执行，调用方自己丢到线程里，别阻塞 UI。
        /// </summary>
        public static bool TryFetchRemote(QuestDatabase current, out string message, out QuestDatabase fetched)
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
                    wc.Headers.Add("Pragma", "no-cache");
                    // 带一个时间戳绕开 CDN 缓存，否则刚推上去的数据要等几分钟才拉得到
                    text = wc.DownloadString(RemoteUrl + "?t=" + DateTime.Now.Ticks);
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

                var remote = DropLegacy(QuestDatabase.FromJson(Json.Obj(parsed)));
                if (remote.Regions.Count == 0)
                {
                    message = "拉取失败：远端任务库没有任何区域。";
                    return false;
                }

                // 并进当前库：同名任务合成一条，本地多出来的（手工加的）不会被冲掉
                int merged = 0, added = 0;
                var cur = DropLegacy(current);
                var db = (cur != null && cur.Regions.Count > 0) ? cur : remote;
                bool merging = !ReferenceEquals(db, remote);

                // 远端要是比你手上这份还旧，就别拿它往回污染了（比如刚换了新版 exe，
                // 而仓库上那份还没跟着更新）
                if (merging && !string.IsNullOrEmpty(db.UpdatedAt) && !string.IsNullOrEmpty(remote.UpdatedAt)
                    && string.CompareOrdinal(remote.UpdatedAt, db.UpdatedAt) < 0)
                {
                    message = "远端那份比你本地的旧（" + remote.UpdatedAt + "），已经跳过了，不会往回退。";
                    return false;
                }

                if (merging)
                {
                    db.MergeFrom(remote, out merged, out added);
                    if (string.IsNullOrEmpty(db.GameVersion) || string.CompareOrdinal(remote.GameVersion, db.GameVersion) > 0)
                        db.GameVersion = remote.GameVersion;
                    db.UpdatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                }

                WriteLocal(db);

                fetched = db;
                int n = 0;
                foreach (var r in db.Regions) n += r.Quests.Count;
                message = "已更新：" + db.Regions.Count + " 个区域 / " + n + " 条任务"
                          + (merging && (added > 0 || merged > 0)
                              ? "（新增 " + added + " 条，同名合并 " + merged + " 条）" : "")
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
