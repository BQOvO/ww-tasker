using System;
using System.Collections.Generic;

namespace WuwaChecklist
{
    /// <summary>任务类型。名字用鸣潮官方那套叫法，界面直接显示。</summary>
    internal static class QuestType
    {
        public const string Main = "main";   // 潮汐任务
        public const string Comp = "comp";   // 伴星任务
        public const string Tale = "tale";   // 道引任务
        public const string Side = "side";   // 纪闻任务
        public const string Expl = "expl";   // 危行任务
        public const string Hidden = "hidden"; // 隐藏任务（没追踪、要自己触发的那些）

        public static string Label(string t)
        {
            switch (t)
            {
                case Main: return "潮汐";
                case Comp: return "伴星";
                case Tale: return "道引";
                case Side: return "纪闻";
                case Expl: return "危行";
                case Hidden: return "隐藏";
                default: return t ?? "";
            }
        }

        public static string Normalize(string t)
        {
            if (string.IsNullOrEmpty(t)) return Side;
            t = t.Trim().ToLowerInvariant();
            switch (t)
            {
                case "main": case "主线": case "潮汐": case "潮汐任务": return Main;
                case "tale": case "幕间": case "道引": case "道引任务": return Tale;
                case "comp": case "companion": case "同行": case "伴星": case "伴星任务": return Comp;
                case "side": case "支线": case "纪闻": case "纪闻任务": return Side;
                case "expl": case "exploration": case "探索": case "危行": case "危行任务": return Expl;
                case "hidden": case "隐藏": case "隐藏任务": return Hidden;
                default: return t;
            }
        }
    }

    internal sealed class Quest
    {
        public string Id;
        public string Name;
        public string Type = QuestType.Side;
        public string SubArea = "";
        public List<string> Requires = new List<string>();
        public bool Hidden;
        public string Url = "";
        public string Note = "";

        // 运行期由 QuestStore 填：区域回指
        public string RegionId;

        /// <summary>合并时被并进来的 id。老存档挂在这些 id 上的进度会迁到 Id 上。</summary>
        public List<string> MergedIds = new List<string>();

        /// <summary>同地区判重用的键：去掉空白、统一半全角、不区分大小写。</summary>
        public static string KeyOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsWhiteSpace(c)) continue;
                if (c == '　') continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>把 other 的信息并进自己。规则：任一份说隐藏就算隐藏；空字段才补。</summary>
        public void Absorb(Quest other)
        {
            if (!string.IsNullOrEmpty(other.Id) && other.Id != Id && !MergedIds.Contains(other.Id))
                MergedIds.Add(other.Id);

            Hidden = Hidden || other.Hidden;

            if (string.IsNullOrEmpty(SubArea) && !string.IsNullOrEmpty(other.SubArea)) SubArea = other.SubArea;
            if (string.IsNullOrEmpty(Url) && !string.IsNullOrEmpty(other.Url)) Url = other.Url;

            // 备注都留着，不同才拼
            if (!string.IsNullOrEmpty(other.Note) && other.Note != Note)
                Note = string.IsNullOrEmpty(Note) ? other.Note : Note + "；" + other.Note;

            // 类型：有明确的主线/同行/幕间就压过默认的支线
            if (Type == QuestType.Side && other.Type != QuestType.Side) Type = other.Type;
            else if (Type != other.Type && Rank(other.Type) > Rank(Type)) Type = other.Type;

            foreach (var r in other.Requires)
                if (!string.IsNullOrEmpty(r) && !Requires.Contains(r) && r != Id) Requires.Add(r);
        }

        private static int Rank(string t)
        {
            switch (t)
            {
                case QuestType.Main: return 5;
                case QuestType.Comp: return 4;
                case QuestType.Tale: return 3;
                case QuestType.Expl: return 2;
                default: return 1;
            }
        }

        public string TypeLabel { get { return QuestType.Label(Type); } }

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            d["id"] = Id;
            d["name"] = Name;
            d["type"] = Type;
            if (!string.IsNullOrEmpty(SubArea)) d["subArea"] = SubArea;
            if (Requires != null && Requires.Count > 0)
            {
                var arr = new List<object>();
                foreach (var r in Requires) arr.Add(r);
                d["requires"] = arr;
            }
            if (Hidden) d["hidden"] = true;
            if (!string.IsNullOrEmpty(Url)) d["url"] = Url;
            if (!string.IsNullOrEmpty(Note)) d["note"] = Note;
            if (MergedIds != null && MergedIds.Count > 0)
            {
                var m = new List<object>();
                foreach (var x in MergedIds) m.Add(x);
                d["mergedIds"] = m;
            }
            return d;
        }
    }

    internal sealed class Region
    {
        public string Id;
        public string Name;
        public string Chapter = "";
        public int Order = 100;
        public List<Quest> Quests = new List<Quest>();

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            d["id"] = Id;
            d["name"] = Name;
            if (!string.IsNullOrEmpty(Chapter)) d["chapter"] = Chapter;
            d["order"] = Order;
            var arr = new List<object>();
            foreach (var q in Quests) arr.Add(q.ToJson());
            d["quests"] = arr;
            return d;
        }
    }

    internal sealed class QuestDatabase
    {
        public int SchemaVersion = 1;
        public string GameVersion = "";
        public string UpdatedAt = "";
        public List<Region> Regions = new List<Region>();

        private Dictionary<string, Quest> _byId = new Dictionary<string, Quest>(StringComparer.Ordinal);

        public void Reindex()
        {
            _byId.Clear();
            Regions.Sort(delegate (Region a, Region b)
            {
                int c = a.Order.CompareTo(b.Order);
                if (c != 0) return c;
                return string.CompareOrdinal(a.Name, b.Name);
            });
            foreach (var r in Regions)
            {
                foreach (var q in r.Quests)
                {
                    q.RegionId = r.Id;
                    if (!string.IsNullOrEmpty(q.Id) && !_byId.ContainsKey(q.Id)) _byId[q.Id] = q;
                }
            }
        }

        /// <summary>
        /// 把一份清单并进现有库：同名（同一地区内）的合成一条，空字段补齐，隐藏任一份标了就算隐藏。
        /// 返回合并掉多少条、新增多少条。
        /// </summary>
        public void MergeFrom(QuestDatabase other, out int merged, out int added)
        {
            merged = 0;
            added = 0;
            foreach (var src in other.Regions)
            {
                var dst = FindRegion(src.Id);
                if (dst == null)
                {
                    dst = new Region { Id = src.Id, Name = src.Name, Chapter = src.Chapter, Order = src.Order };
                    Regions.Add(dst);
                }
                if (string.IsNullOrEmpty(dst.Name) && !string.IsNullOrEmpty(src.Name)) dst.Name = src.Name;
                if (string.IsNullOrEmpty(dst.Chapter) && !string.IsNullOrEmpty(src.Chapter)) dst.Chapter = src.Chapter;

                var byName = new Dictionary<string, Quest>(StringComparer.Ordinal);
                foreach (var q in dst.Quests)
                {
                    string k = Quest.KeyOf(q.Name);
                    if (k.Length > 0 && !byName.ContainsKey(k)) byName[k] = q;
                }

                foreach (var q in src.Quests)
                {
                    string k = Quest.KeyOf(q.Name);
                    Quest hit;
                    if (k.Length > 0 && byName.TryGetValue(k, out hit))
                    {
                        hit.Absorb(q);
                        merged++;
                        continue;
                    }
                    var copy = new Quest
                    {
                        Id = q.Id,
                        Name = q.Name,
                        Type = q.Type,
                        SubArea = q.SubArea,
                        Requires = new List<string>(q.Requires),
                        Hidden = q.Hidden,
                        Url = q.Url,
                        Note = q.Note,
                        MergedIds = new List<string>(q.MergedIds),
                        RegionId = dst.Id
                    };
                    dst.Quests.Add(copy);
                    if (k.Length > 0) byName[k] = copy;
                    added++;
                }
            }
            Reindex();
        }

        /// <summary>地区内部去重（导入时用）。返回合并掉的条数。</summary>
        private static int DedupeRegion(Region region)
        {
            int n = 0;
            var byName = new Dictionary<string, Quest>(StringComparer.Ordinal);
            var keep = new List<Quest>(region.Quests.Count);
            foreach (var q in region.Quests)
            {
                string k = Quest.KeyOf(q.Name);
                Quest hit;
                if (k.Length > 0 && byName.TryGetValue(k, out hit))
                {
                    hit.Absorb(q);
                    n++;
                    continue;
                }
                if (k.Length > 0) byName[k] = q;
                keep.Add(q);
            }
            if (n > 0)
            {
                region.Quests.Clear();
                region.Quests.AddRange(keep);
            }
            return n;
        }

        /// <summary>整库跑一遍地区内去重。</summary>
        public int Dedupe()
        {
            int n = 0;
            foreach (var r in Regions) n += DedupeRegion(r);
            Reindex();
            return n;
        }

        /// <summary>库里有几条任务身上挂着被合并掉的 id（只用来显示）。</summary>
        public int CountMerged()
        {
            int n = 0;
            foreach (var r in Regions)
                foreach (var q in r.Quests)
                    n += q.MergedIds == null ? 0 : q.MergedIds.Count;
            return n;
        }

        public Quest Find(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            Quest q;
            return _byId.TryGetValue(questId, out q) ? q : null;
        }

        public Region FindRegion(string regionId)
        {
            foreach (var r in Regions) if (r.Id == regionId) return r;
            return null;
        }

        public static QuestDatabase FromJson(Dictionary<string, object> root)
        {
            var db = new QuestDatabase();
            db.SchemaVersion = Json.Int(root, "schemaVersion", 1);
            db.GameVersion = Json.Str(root, "gameVersion", "");
            db.UpdatedAt = Json.Str(root, "updatedAt", "");

            object regionsValue;
            if (!root.TryGetValue("regions", out regionsValue)) regionsValue = null;

            int regionOrder = 0;
            foreach (var ro in Json.Arr(regionsValue))
            {
                var rd = Json.Obj(ro);
                var region = new Region();
                region.Id = Json.Str(rd, "id", "");
                region.Name = Json.Str(rd, "name", "");
                if (string.IsNullOrEmpty(region.Id))
                {
                    region.Id = "region." + (regionOrder + 1);
                }
                region.Chapter = Json.Str(rd, "chapter", "");
                region.Order = Json.Int(rd, "order", 100 + regionOrder);
                regionOrder++;

                object qv;
                if (rd.TryGetValue("quests", out qv))
                {
                    foreach (var qo in Json.Arr(qv))
                    {
                        var qd = Json.Obj(qo);
                        var q = new Quest();
                        q.Id = Json.Str(qd, "id", "");
                        q.Name = Json.Str(qd, "name", "");
                        q.Type = QuestType.Normalize(Json.Str(qd, "type", QuestType.Side));
                        q.SubArea = Json.Str(qd, "subArea", "");
                        q.Requires = new List<string>(Json.StrArray(qd, "requires"));
                        q.Hidden = Json.Bool(qd, "hidden", false);
                        q.Url = Json.Str(qd, "url", "");
                        q.Note = Json.Str(qd, "note", "");
                        q.MergedIds = new List<string>(Json.StrArray(qd, "mergedIds"));
                        q.RegionId = region.Id;
                        if (string.IsNullOrEmpty(q.Id)) continue;   // 没 id 的条目没法存状态，直接丢
                        region.Quests.Add(q);
                    }
                }
                db.Regions.Add(region);
            }
            db.Reindex();
            db.Dedupe();
            return db;
        }

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            d["schemaVersion"] = SchemaVersion;
            d["gameVersion"] = GameVersion;
            if (!string.IsNullOrEmpty(UpdatedAt)) d["updatedAt"] = UpdatedAt;
            var arr = new List<object>();
            foreach (var r in Regions) arr.Add(r.ToJson());
            d["regions"] = arr;
            return d;
        }
    }
}
