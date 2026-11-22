using System;
using System.Collections.Generic;

namespace WuwaChecklist
{
    /// <summary>任务类型。</summary>
    internal static class QuestType
    {
        public const string Main = "main";   // 主线
        public const string Tale = "tale";   // 幕间
        public const string Comp = "comp";   // 同行
        public const string Side = "side";   // 支线
        public const string Expl = "expl";   // 探索

        public static string Label(string t)
        {
            switch (t)
            {
                case Main: return "主线";
                case Tale: return "幕间";
                case Comp: return "同行";
                case Side: return "支线";
                case Expl: return "探索";
                default: return t ?? "";
            }
        }

        public static string Normalize(string t)
        {
            if (string.IsNullOrEmpty(t)) return Side;
            t = t.Trim().ToLowerInvariant();
            switch (t)
            {
                case "main": case "主线": case "潮汐": return Main;
                case "tale": case "幕间": return Tale;
                case "comp": case "companion": case "同行": return Comp;
                case "side": case "支线": return Side;
                case "expl": case "exploration": case "探索": return Expl;
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
                        q.RegionId = region.Id;
                        if (string.IsNullOrEmpty(q.Id)) continue;   // 没 id 的条目没法存状态，直接丢
                        region.Quests.Add(q);
                    }
                }
                db.Regions.Add(region);
            }
            db.Reindex();
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
