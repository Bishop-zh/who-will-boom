using System.Text;
using BombTavern.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 联机大厅的 UGUI 视图。房间状态来自 LanNetworkManager，本类不保存联机业务状态。
    /// CanvasScaler + SafeArea + 布局组共同保证窗口、16:9、超宽屏与移动横屏适配。
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyUI : MonoBehaviour
    {
        [Header("绑定")] public LanNetworkManager lan;
        public GameDirector director;

        Canvas _canvas;
        RectTransform _panel;
        TMP_Text _title, _address, _help, _status, _connectLabel, _startHint;
        readonly TMP_Text[] _seatRows = new TMP_Text[4];
        readonly TMP_Text[] _notices = new TMP_Text[3];
        TMP_InputField _addressInput;
        Button _connectButton, _startButton, _copyButton;
        readonly TMP_Text[] _seatStatus = new TMP_Text[4];
        RectTransform _clientControls;
        Vector2Int _lastScreen;
        string _lastFingerprint;
        int _lastDotStep = -1;

        void Awake()
        {
            if (lan == null) lan = FindObjectOfType<LanNetworkManager>();
            if (director == null) director = FindObjectOfType<GameDirector>();
        }

        void Start()
        {
            Build();
            Refresh(true);
        }

        bool ShouldShow()
        {
            if (lan == null || director == null || !lan.enabled) return false;
            if (lan.mode == GameMode.SinglePlayer) return false;
            return director.Session == null;
        }

        void Build()
        {
            _canvas = UiKit.CreateCanvas("LobbyCanvas", 230);
            _canvas.transform.SetParent(transform, false);
            var safe = UiKit.CreateSafeAreaRoot(_canvas);

            var image = UiKit.Panel(safe, "LobbyPanel");
            _panel = image.rectTransform;
            UiKit.VBox(_panel, 8, new RectOffset(30, 30, 22, 22));

            var eyebrow = UiKit.Label(_panel, "联机牌桌", UiTheme.FontCaption, UiTheme.Accent,
                TextAnchor.MiddleCenter);
            eyebrow.fontStyle = FontStyles.Bold;
            UiKit.Size(eyebrow.rectTransform, h: 22f);

            _title = UiKit.Label(_panel, "等待玩家入座", UiTheme.FontTitle, UiTheme.TextHi,
                TextAnchor.MiddleCenter);
            _title.fontStyle = FontStyles.Bold;
            UiKit.Size(_title.rectTransform, h: 48f);

            _address = UiKit.Paragraph(_panel, string.Empty, UiTheme.FontHead, UiTheme.Accent,
                TextAnchor.MiddleCenter);
            _address.fontStyle = FontStyles.Bold;
            UiKit.Size(_address.rectTransform, h: 42f);
            _address.fontSize = 19;

            _help = UiKit.Paragraph(_panel, string.Empty, UiTheme.FontCaption, UiTheme.TextLo,
                TextAnchor.MiddleCenter);
            UiKit.Size(_help.rectTransform, h: 48f);

            var actions = UiKit.Rect(_panel, "LobbyActions", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UiKit.HBox(actions, UiTheme.S2, new RectOffset());
            UiKit.Size(actions, h: 64f);
            _copyButton = UiKit.Btn(actions, "复制房主地址", CopyHostAddress);
            UiKit.Size(_copyButton.GetComponent<RectTransform>(), w: 280f, h: 56f);
            var exit = UiKit.Btn(actions, "退出到主界面", SessionContext.ReturnToMainMenu);
            UiKit.Size(exit.GetComponent<RectTransform>(), w: 280f, h: 56f);

            BuildClientControls();

            var divider = UiKit.Rect(_panel, "Divider", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            divider.gameObject.AddComponent<Image>().color = UiTheme.Line;
            UiKit.Size(divider, h: 1f);

            var rosterTitle = UiKit.Label(_panel, "座位", UiTheme.FontCaption, UiTheme.TextMid);
            UiKit.Size(rosterTitle.rectTransform, h: 22f);

            for (int i = 0; i < _seatRows.Length; i++)
            {
                var card = UiKit.Panel(_panel, "Seat_" + i, card: true);
                UiKit.Size(card.rectTransform, h: 62f);
                var row = UiKit.Label(card.transform, string.Empty, UiTheme.FontBody, UiTheme.TextHi);
                row.rectTransform.offsetMin = new Vector2(16f, 4f);
                row.rectTransform.anchorMax = new Vector2(.65f, 1);
                row.rectTransform.offsetMax = new Vector2(-16f, -4f);
                var state = UiKit.Label(card.transform, "", 22, UiTheme.Success, TextAnchor.MiddleRight);
                state.rectTransform.anchorMin = new Vector2(.65f, 0);
                state.rectTransform.offsetMin = new Vector2(0, 4);
                state.rectTransform.offsetMax = new Vector2(-22, -4);
                _seatStatus[i] = state;
                row.overflowMode = TextOverflowModes.Ellipsis;
                _seatRows[i] = row;
            }

            _status = UiKit.Paragraph(_panel, string.Empty, UiTheme.FontBody, UiTheme.TextMid,
                TextAnchor.MiddleCenter);
            UiKit.Size(_status.rectTransform, h: 30f);

            for (int i = 0; i < _notices.Length; i++)
            {
                _notices[i] = UiKit.Paragraph(_panel, string.Empty, UiTheme.FontCaption, UiTheme.TextLo,
                    TextAnchor.MiddleCenter);
                UiKit.Size(_notices[i].rectTransform, h: 20f);
            }

            _startButton = UiKit.Btn(_panel, "开 始 游 戏", () => lan.StartGame(), accent: true);
            UiKit.Size(_startButton.GetComponent<RectTransform>(), h: 58f);
            TavernSettingsLayout.StylePrimary(_startButton);
            _startHint = UiKit.Paragraph(_panel, "人数不足的座位会由 AI 自动补齐。",
                UiTheme.FontCaption, UiTheme.TextLo, TextAnchor.MiddleCenter);
            UiKit.Size(_startHint.rectTransform, h: 24f);
            actions.SetSiblingIndex(_clientControls.GetSiblingIndex() + 1);
            _startButton.transform.SetAsLastSibling();
            for (int i = 0; i < _notices.Length; i++) _notices[i].gameObject.SetActive(false);
            ApplyLayout();
        }

        void BuildClientControls()
        {
            var row = UiKit.Rect(_panel, "ClientControls", Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _clientControls = row;
            UiKit.HBox(row, UiTheme.S2, new RectOffset());
            UiKit.Size(row, h: 60f);

            _addressInput = UiKit.Input(row, lan != null ? lan.connectAddress : string.Empty,
                "房主地址", 64);
            UiKit.Size(_addressInput.GetComponent<RectTransform>(), w: 460f, h: 56f);
            _connectButton = UiKit.Btn(row, "连接 / 重连", RetryConnect);
            UiKit.Size(_connectButton.GetComponent<RectTransform>(), w: 210f, h: 56f);
            _connectLabel = _connectButton.GetComponentInChildren<TMP_Text>();
        }

        void RetryConnect()
        {
            string value = _addressInput != null ? _addressInput.text : string.Empty;
            if (NetAddressUtility.TryParseEndpointInput(value, lan != null ? lan.port : 7788,
                out string host, out int endpointPort, out string reason))
            {
                lan.port = endpointPort;
                lan.RetryConnect(host);
            }
            else lan.AddNotice(reason);
        }

        void CopyHostAddress()
        {
            if (lan == null || lan.ShareableAddresses.Count == 0) return;
            var selected = lan.ShareableAddresses[0];
            GUIUtility.systemCopyBuffer = selected.address + ":" + lan.port;
            lan.AddNotice("已复制 " + GUIUtility.systemCopyBuffer);
            Refresh(true);
        }

        void Update()
        {
            if (_canvas == null) return;
            bool show = ShouldShow();
            if (_canvas.gameObject.activeSelf != show) _canvas.gameObject.SetActive(show);
            if (!show) return;
            if (_lastScreen != new Vector2Int(Screen.width, Screen.height)) ApplyLayout();
            Refresh(false);
            int dots = Mathf.FloorToInt(Time.unscaledTime * 2f) % 4;
            if (_lastDotStep != dots && _title != null)
            {
                _lastDotStep = dots;
                _title.text = lan.IsHost ? "等待玩家入座" + new string('·', dots) : "正在加入牌桌" + new string('·', dots);
            }
        }

        void ApplyLayout()
        {
            _lastScreen = new Vector2Int(Screen.width, Screen.height);
            bool compact = UiKit.CompactLayout;
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.anchoredPosition = Vector2.zero;
            _panel.sizeDelta = new Vector2(820f, 790f);
        }

        void Refresh(bool force)
        {
            if (lan == null || _canvas == null) return;
            string fingerprint = BuildFingerprint();
            if (!force && fingerprint == _lastFingerprint) return;
            _lastFingerprint = fingerprint;

            bool host = lan.IsHost;
            _title.text = host ? "等待玩家入座" : "正在加入牌桌";
            _clientControls.gameObject.SetActive(!host);
            _addressInput.gameObject.SetActive(!host);
            _connectButton.gameObject.SetActive(!host);
            _copyButton.gameObject.SetActive(host);

            if (host)
            {
                _address.text = BuildHostAddresses();
                _help.text = lan.ShareableAddresses.Count == 0
                    ? "没有检测到可分享地址，请先确认网络连接。"
                    : "分享上方地址邀请好友；未入座的位置将在开局时由 AI 补齐。";
            }
            else
            {
                _address.text = $"{lan.connectAddress}:{lan.port}  ·  {StateText()}";
                _help.text = lan.LinkState == TcpLanTransport.LinkState.Failed && !string.IsNullOrEmpty(lan.LinkError)
                    ? lan.LinkError : "输入房主地址后连接，成功后等待房主开始。";
            }

            for (int i = 0; i < _seatRows.Length; i++)
            {
                if (i < lan.RosterList.Count)
                {
                    var seat = lan.RosterList[i];
                    bool local = seat.seat == lan.localSeat;
                    _seatRows[i].text = $"{(seat.seat == 0 ? "♛" : "●")}    {seat.name}{(local ? "  [你]" : "")}";
                    _seatStatus[i].text = "已就位  ✓";
                    _seatStatus[i].color = UiTheme.Success;
                    _seatRows[i].color = local ? UiTheme.Accent : UiTheme.TextHi;
                }
                else
                {
                    _seatRows[i].text = $"○    空座位 {i + 1}";
                    _seatStatus[i].text = "等待加入…";
                    _seatStatus[i].color = UiTheme.TextLo;
                    _seatRows[i].color = UiTheme.TextLo;
                }
            }

            _status.text = $"{lan.StatusText}    ·    已连接 {lan.connectedClients} 人";
            for (int i = 0; i < _notices.Length; i++)
            {
                int source = lan.Notices.Count - _notices.Length + i;
                _notices[i].text = source >= 0 ? "· " + lan.Notices[source] : string.Empty;
            }

            _startButton.gameObject.SetActive(host);
            _startHint.gameObject.SetActive(host);
            _startButton.interactable = lan.CanStart;
            bool connecting = lan.LinkState == TcpLanTransport.LinkState.Connecting;
            _connectButton.interactable = !connecting;
            if (_connectLabel != null) _connectLabel.text = connecting ? "连 接 中…" : "连接 / 重连";
        }

        string BuildHostAddresses()
        {
            if (lan.ShareableAddresses.Count == 0) return $"端口 {lan.port}";
            var sb = new StringBuilder();
            int count = Mathf.Min(3, lan.ShareableAddresses.Count);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append("    ");
                var a = lan.ShareableAddresses[i];
                sb.Append(a.Label).Append(' ').Append(a.address).Append(':').Append(lan.port);
            }
            return sb.ToString();
        }

        string BuildFingerprint()
        {
            var sb = new StringBuilder();
            sb.Append(lan.IsHost).Append('|').Append(lan.LinkState).Append('|')
              .Append(lan.StatusText).Append('|').Append(lan.connectedClients).Append('|')
              .Append(lan.CanStart).Append('|').Append(lan.connectAddress).Append('|').Append(lan.LinkError);
            for (int i = 0; i < lan.RosterList.Count; i++)
            {
                var r = lan.RosterList[i];
                sb.Append('|').Append(r.seat).Append(':').Append(r.name).Append(':').Append(r.skinId);
            }
            for (int i = Mathf.Max(0, lan.Notices.Count - 3); i < lan.Notices.Count; i++)
                sb.Append('|').Append(lan.Notices[i]);
            return sb.ToString();
        }

        string StateText()
        {
            switch (lan.LinkState)
            {
                case TcpLanTransport.LinkState.Connecting: return "正在连接";
                case TcpLanTransport.LinkState.Connected: return "已连接";
                case TcpLanTransport.LinkState.Failed: return "连接失败";
                default: return "未连接";
            }
        }
    }
}
