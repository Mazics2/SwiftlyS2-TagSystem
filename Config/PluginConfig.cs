using TagSystem.Models;

namespace TagSystem.Config;

public class PluginConfig
{
    public SettingsSection Settings { get; set; } = new();
    public ChatSection Chat { get; set; } = new();
    public CommandSection Commands { get; set; } = new();
    public TagEntry Default { get; set; } = CreateDefaultTag();
    public List<GroupData> Groups { get; set; } = new();
    public List<PlayerData> Players { get; set; } = new();

    public class SettingsSection
    {
        public bool EnableScoreboardTag { get; set; } = true;
        public bool EnableChatTag { get; set; } = true;
        public bool UsePermissions { get; set; } = true;
        public bool CacheOnConnect { get; set; } = true;

        /// <summary>Botlara da etiket uygulansin mi?</summary>
        public bool ApplyToBots { get; set; } = false;

        /// <summary>Oyun bazen clan tag'i sifirlar; bu aralikla (sn) tekrar uygulanir. 0 = kapali.</summary>
        public float ScoreboardRefreshSeconds { get; set; } = 5f;

        /// <summary>Clan tag icin maksimum uzunluk (m_szClan 32 byte; UTF-8 icin guvenli limit).</summary>
        public int ScoreboardMaxLength { get; set; } = 24;

        /// <summary>Admin menusu acikken oyuncu hareket etmesin (W/S/A/D ile rahat gezinsin).</summary>
        public bool FreezeWhileMenuOpen { get; set; } = true;

        public string ChatFormat { get; set; } = "{DEAD}{TEAMCHAT}{CHAT_TAG} {NAME_COLOR}{NAME}[default]: {MESSAGE_COLOR}{MESSAGE}";
        public string ScoreboardFormat { get; set; } = "{SCOREBOARD_TAG}";

        /// <summary>Eklenti mesajlarinin basina eklenecek on ek.</summary>
        public string Prefix { get; set; } = "[gold][TagSystem][default]";

        public bool Debug { get; set; } = false;
    }

    public class ChatSection
    {
        public string DeadPrefix { get; set; } = "[grey]*ÖLÜ* [default]";
        public string TeamPrefixT { get; set; } = "[gold](T) [default]";
        public string TeamPrefixCt { get; set; } = "[lightblue](CT) [default]";
        public string TeamPrefixSpec { get; set; } = "[grey](İzleyici) [default]";

        /// <summary>Bu karakterlerle baslayan mesajlar (komutlar) formatlanmaz, SwiftlyS2'ye dokunulmadan iletilir.</summary>
        public List<string> IgnorePrefixes { get; set; } = new() { "!", "/", "." };

        /// <summary>Mesajlari sunucu konsoluna da yazdir (orijinal mesaj iptal edildigi icin).</summary>
        public bool LogToConsole { get; set; } = true;

        /// <summary>Olu oyuncularin mesajini yalnizca olu oyuncular gorsun (CS2 varsayilan davranisi gibi). false = herkes gorur.</summary>
        public bool DeadTalkOnlyToDead { get; set; } = false;

        /// <summary>Bos / sadece bosluk iceren mesajlari engelle.</summary>
        public bool BlockEmptyMessages { get; set; } = true;
    }

    public class CommandSection
    {
        public string MenuCommand { get; set; } = "tagmenu";
        public string GiveGroupCommand { get; set; } = "tagver";
        public string SetCustomTagCommand { get; set; } = "ozeltag";
        public string RemoveTagCommand { get; set; } = "tagsil";
        public string AddGroupCommand { get; set; } = "grupekle";
        public string ReloadCommand { get; set; } = "tagreload";
        public string CheckCommand { get; set; } = "tagcheck";
        public string AdminPermission { get; set; } = "tagsystem.admin";
    }

    public static TagEntry CreateDefaultTag() => new()
    {
        Id = "default",
        DisplayName = "Varsayılan",
        ChatTag = "[OYUNCU]",
        ScoreboardTag = "",
        ChatTagColor = "grey",
        NameColor = "teamcolor",
        MessageColor = "default",
        UseTagColor = true,
        Priority = -1000,
        Enabled = true,
    };
}
