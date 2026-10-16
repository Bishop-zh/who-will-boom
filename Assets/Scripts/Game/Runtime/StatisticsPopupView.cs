using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BombTavern.Runtime
{
    public sealed class StatisticsPopupView : MonoBehaviour
    {
        RectTransform panel;
        UiModalAnimator animator;
        [Tooltip("整个弹窗的设计尺寸（含顶部标题），用于窗口适配。")] public Vector2 designSize = new Vector2(1800,1320);
        [Range(.75f,1.2f)] public float preferredScale = 1.08f;
        public bool IsOpen { get; private set; }
        public MatchStatisticsStore Store { get; set; }
        Transform Node(string name) => GetComponentsInChildren<Transform>(true).First(t => t.name == name);
        TMP_Text Label(string name) => Node(name).GetComponent<TMP_Text>();
        void Awake()
        {
            panel = (RectTransform)Node("StatisticsPanel"); animator = panel.GetComponent<UiModalAnimator>();
            Store = MatchStatisticsStore.Local;
            Label("DetailText").richText = false;
            Node("Close").GetComponent<Button>().onClick.AddListener(Close);
            Node("Shade").GetComponent<Button>().onClick.AddListener(Close);
            Node("Refresh").GetComponent<Button>().onClick.AddListener(Refresh);
            for (int i = 0; i < 10; i++) { int row = i; Node("Recent_" + i).GetComponent<Button>().onClick.AddListener(() => ShowRecord(row)); }
            Node("DetailsClose").GetComponent<Button>().onClick.AddListener(() => Node("Details").gameObject.SetActive(false));
        }
        public void Open()
        {
            gameObject.SetActive(true); transform.SetAsLastSibling(); IsOpen = true;
            Node("Details").gameObject.SetActive(false); Refresh(); Fit(); animator.Show();
        }
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false; animator.Hide(() => { if (!IsOpen) gameObject.SetActive(false); });
        }
        void Update() { if (IsOpen) Fit(); }
        void Fit() => animator.SetTargetScale(preferredScale * UiKit.FitScale(designSize * preferredScale,16));
        public void Refresh()
        {
            Store.Reload(); var data = Store.Data;
            Label("TotalValue").text = data.total.ToString(); Label("WinsValue").text = data.wins.ToString();
            string rate = data.total == 0 ? "—" : (data.WinRate * 100).ToString("F0") + "%";
            Label("RateValue").text = rate; Label("RingValue").text = rate; Label("StreakValue").text = data.bestStreak.ToString();
            Node("WinRing").GetComponent<StatisticsRingGraphic>().Fraction = data.WinRate;
            Label("WinsLegend").text = "● 胜场  " + data.wins;
            Label("LossLegend").text = "● 负场  " + (data.total - data.wins);
            SetMode("Single", data.single, data.total); SetMode("Multi", data.multiplayer, data.total);
            Label("Empty").gameObject.SetActive(data.recent.Count == 0);
            Label("Status").text = string.IsNullOrEmpty(Store.Error) ? "本机记录 · 仅统计完成的对局" : Store.Error;
            for (int i = 0; i < 10; i++)
            {
                var row = Node("Recent_" + i); row.gameObject.SetActive(i < data.recent.Count);
                if (i >= data.recent.Count) continue;
                var record = data.recent[i];
                row.Find("Result").GetComponent<TMP_Text>().text = record.won ? "胜利" : "失败";
                row.Find("Result").GetComponent<TMP_Text>().color = record.won ? new Color(1, .8f, .36f) : new Color(1, .44f, .44f);
                var resultTag = row.Find("ResultTag");
                if (resultTag != null) resultTag.GetComponent<Image>().color = record.won ? new Color(.68f,.43f,.12f) : new Color(.58f,.13f,.1f);
                row.Find("Icon").GetComponent<Image>().sprite = Resources.LoadAll<Sprite>("BombTavern/UI/StatisticsSheet").First(s => s.name == (record.won ? "StatsCrown" : "StatsSkull"));
                row.Find("Mode").GetComponent<TMP_Text>().text = ModeLabel(record.mode);
                row.Find("Time").GetComponent<TMP_Text>().text = RelativeTime(record.endedUtcTicks);
            }
        }
        void SetMode(string prefix, int count, int total)
        {
            float ratio = total > 0 ? (float)count / total : 0;
            Node(prefix + "Fill").GetComponent<Image>().fillAmount = ratio;
            Label(prefix + "Value").text = count + " 场\n" + (ratio * 100).ToString("F0") + "%";
        }
        void ShowRecord(int index)
        {
            if (index >= Store.Data.recent.Count) return;
            var record = Store.Data.recent[index];
            Label("DetailText").text = (record.won ? "胜利" : "失败") + " · " + ModeLabel(record.mode) + "\n" +
                new DateTime(record.endedUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm") + "\n\n玩家：" + record.playerName +
                "\n对局回合：" + record.rounds + "\n翻开格子：" + record.reveals + "\n踩雷次数：" + record.hits + "\n使用道具：" + record.itemsUsed;
            Node("Details").gameObject.SetActive(true); Node("Details").SetAsLastSibling();
        }
        public static string ModeLabel(GameMode mode) => mode == GameMode.SinglePlayer ? "单人模式" : mode == GameMode.LanHost ? "开房模式" : "加入模式";
        static string RelativeTime(long ticks)
        {
            var elapsed = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            if (elapsed.TotalMinutes < 1) return "刚刚";
            if (elapsed.TotalHours < 1) return (int)elapsed.TotalMinutes + "分钟前";
            if (elapsed.TotalDays < 1) return (int)elapsed.TotalHours + "小时前";
            return (int)elapsed.TotalDays + "天前";
        }
    }
}
