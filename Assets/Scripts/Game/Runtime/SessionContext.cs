using BombTavern.Config;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 跨场景会话上下文：把「开始界面」里选的东西带到对局场景。
    /// 挂在 DontDestroyOnLoad 上，全局唯一。
    /// </summary>
    [DisallowMultipleComponent]
    public class SessionContext : MonoBehaviour
    {
        public static SessionContext Instance { get; private set; }

        [Header("玩家")]
        [Tooltip("玩家昵称。")]
        public string playerName = "红雷";

        [Tooltip("选中的皮肤 ID。")]
        public string skinId = "round_black";

        [Header("联机")]
        [Tooltip("运行模式。")]
        public GameMode mode = GameMode.SinglePlayer;

        [Tooltip("客户端要连接的主机地址。")]
        public string connectAddress = "";

        [Tooltip("端口。0 表示沿用 GameConfig.defaultPort。")]
        public int port = 0;

        [Header("自定义规则")]
        [Range(6, 24)] public int boardColumns = 12;
        [Range(6, 18)] public int boardRows = 10;
        [Range(1, 200)] public int bombCount = 19;
        [Range(0f, 30f)] public float driftPercent = 5f;
        [Range(0f, 20f)] public float driftGrowthPercentPerRound = 3f;
        [Range(10f, 120f)] public float turnTimeLimitSeconds = 30f;

        [Header("资源")]
        [Tooltip("皮肤目录。用于开始界面展示与对局中给角色穿衣。")]
        public SkinCatalog skinCatalog;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>取当前选中的皮肤；找不到时回退到目录默认皮肤。</summary>
        public SkinDefinition GetSkin()
        {
            if (skinCatalog == null) return null;
            var s = skinCatalog.Find(skinId);
            return s != null ? s : skinCatalog.GetDefault();
        }

        /// <summary>退出大厅或对局，回到同一场景内的新版主界面。</summary>
        public static void ReturnToMainMenu()
        {
            if (Instance != null)
            {
                Instance.mode = GameMode.SinglePlayer;
                Instance.connectAddress = string.Empty;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
