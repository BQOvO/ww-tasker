using System;
using System.Windows;
using System.Windows.Media;

namespace WuwaChecklist
{
    /// <summary>
    /// 深色定稿配色（和静态稿完全一致）。
    /// 所有颜色都带 alpha——面板底是可调的，字永远接近实色。
    /// </summary>
    internal static class Theme
    {
        // 背景层：alpha 会被滑杆整体缩放一次
        public static readonly Color Bg = Color.FromArgb(196, 18, 20, 22);
        public static readonly Color Head = Color.FromArgb(222, 26, 28, 31);
        public static readonly Color SelBg = Color.FromArgb(220, 44, 50, 53);
        public static readonly Color TagBg = Color.FromArgb(200, 48, 52, 56);

        // 线
        public static readonly Color Line = Color.FromArgb(170, 112, 118, 122);
        public static readonly Color RowLine = Color.FromArgb(150, 60, 64, 68);
        public static readonly Color Border = Color.FromArgb(180, 124, 130, 134);

        // 文字
        public static readonly Color Txt = Color.FromArgb(255, 242, 244, 246);
        public static readonly Color Done = Color.FromArgb(235, 132, 138, 142);
        public static readonly Color Strike = Color.FromArgb(200, 112, 118, 122);
        public static readonly Color Dim = Color.FromArgb(240, 164, 170, 174);
        public static readonly Color Sub = Color.FromArgb(238, 142, 148, 152);
        public static readonly Color RTxt = Color.FromArgb(255, 206, 210, 214);
        public static readonly Color Foot = Color.FromArgb(235, 158, 164, 168);
        public static readonly Color TagTx = Color.FromArgb(255, 190, 196, 200);
        public static readonly Color Note = Color.FromArgb(255, 236, 178, 104);
        public static readonly Color Hide = Color.FromArgb(240, 156, 162, 166);

        // 强调色
        public static readonly Color Acc = Color.FromArgb(255, 63, 224, 200);      // #3FE0C8
        public static readonly Color AccD = Color.FromArgb(255, 88, 222, 200);
        public static readonly Color AccDim = Color.FromArgb(150, 63, 224, 200);

        // 状态件
        public static readonly Color Ring = Color.FromArgb(235, 150, 156, 160);    // 未完成圆圈
        public static readonly Color BarBg = Color.FromArgb(205, 72, 76, 80);      // 进度条轨道
        public static readonly Color CheckInk = Color.FromArgb(255, 12, 50, 46);   // 勾的墨色（压在青绿上）

        // ---- 类型标签：一种类型一个色，别让六种任务看起来一模一样 ----
        public static Color TypeColor(string type)
        {
            switch (type)
            {
                case QuestType.Main: return Color.FromArgb(255, 63, 224, 200);   // 潮汐 青绿
                case QuestType.Comp: return Color.FromArgb(255, 232, 195, 106);  // 伴星 金
                case QuestType.Tale: return Color.FromArgb(255, 143, 184, 240);  // 道引 蓝
                case QuestType.Side: return Color.FromArgb(255, 199, 168, 242);  // 纪闻 紫
                case QuestType.Expl: return Color.FromArgb(255, 240, 148, 106);  // 危行 橙
                case QuestType.Event: return Color.FromArgb(255, 246, 132, 164); // 活动 玫红
                default: return Color.FromArgb(255, 176, 182, 186);              // 隐藏 灰
            }
        }

        /// <summary>标签底：类型本色压到接近实心，让它在深底上一眼就能扫到。</summary>
        public static Brush TypeBack(string type, double opacity)
        {
            var c = TypeColor(type);
            return Scaled(Color.FromArgb(214, c.R, c.G, c.B), opacity);
        }

        /// <summary>标签字：压在亮色底上，用深墨才看得清。</summary>
        public static Brush TypeTagInk
        {
            get { return Freeze(Color.FromArgb(255, 16, 18, 20)); }
        }

        public static Brush TypeInk(string type)
        {
            return Freeze(TypeColor(type));
        }

        // ---- 静态画刷缓存（UI 线程独占，没问题）----
        public static readonly Brush BrBg = Freeze(Bg);
        public static readonly Brush BrHead = Freeze(Head);
        public static readonly Brush BrSelBg = Freeze(SelBg);
        public static readonly Brush BrTagBg = Freeze(TagBg);
        public static readonly Brush BrLine = Freeze(Line);
        public static readonly Brush BrRowLine = Freeze(RowLine);
        public static readonly Brush BrBorder = Freeze(Border);
        public static readonly Brush BrTxt = Freeze(Txt);
        public static readonly Brush BrDone = Freeze(Done);
        public static readonly Brush BrStrike = Freeze(Strike);
        public static readonly Brush BrDim = Freeze(Dim);
        public static readonly Brush BrSub = Freeze(Sub);
        public static readonly Brush BrRTxt = Freeze(RTxt);
        public static readonly Brush BrFoot = Freeze(Foot);
        public static readonly Brush BrTagTx = Freeze(TagTx);
        public static readonly Brush BrNote = Freeze(Note);
        public static readonly Brush BrHide = Freeze(Hide);
        public static readonly Brush BrAcc = Freeze(Acc);
        public static readonly Brush BrAccD = Freeze(AccD);
        public static readonly Brush BrAccDim = Freeze(AccDim);
        public static readonly Brush BrRing = Freeze(Ring);
        public static readonly Brush BrBarBg = Freeze(BarBg);
        public static readonly Brush BrCheckInk = Freeze(CheckInk);

        // 字体
        public static readonly FontFamily Family =
            new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI, SimHei");

        public static readonly Typeface Regular = new Typeface(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        public static readonly Typeface Bold = new Typeface(Family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        public static Brush Freeze(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>把背景层的 alpha 按滑杆缩放，字不动。</summary>
        public static Brush Scaled(Color c, double opacity)
        {
            byte a = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(c.A * opacity)));
            return Freeze(Color.FromArgb(a, c.R, c.G, c.B));
        }

        // 尺寸
        public const double HeaderH = 40;
        public const double TitleH = 38;      // 右列「区域名 + 只看未完成」那一条
        public const double FooterH = 26;
        public const double LeftW = 128;
        public const double RegionRowH = 46;
        public const double QuestRowH = 46;
        public const double NoteRowH = 17;
    }
}
