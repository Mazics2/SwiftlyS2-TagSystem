namespace TagSystem.Config;

/// <summary>Ilk calistirmada olusturulan, aciklamali varsayilan tagconfig.toml.</summary>
public static class DefaultConfigTemplate
{
    public const string Content = """
# ==============================================================================
#  TagSystem - Scoreboard (clan tag) & Chat etiket sistemi  (SwiftlyS2 / CS2)
# ==============================================================================
#  Oyun içi görsel admin menüsü:  !tagmenu
#  Hızlı admin komutları:
#    !tagver <oyuncu/steamid> <grup_id>
#    !ozeltag <oyuncu/steamid> <etiket> [renk] [isim_rengi] [öncelik]
#    !tagsil <oyuncu/steamid>
#    !grupekle <id> <yetki> <etiket> [renk] [öncelik]
#    !tagreload  |  !tagcheck <oyuncu>
# ==============================================================================

[settings]
enable_scoreboard_tag = true
enable_chat_tag = true
use_permissions = true
cache_on_connect = true
apply_to_bots = false
scoreboard_refresh_seconds = 5.0
scoreboard_max_length = 24
freeze_while_menu_open = true
chat_format = "{DEAD}{TEAMCHAT}{CHAT_TAG} {NAME_COLOR}{NAME}[default]: {MESSAGE_COLOR}{MESSAGE}"
scoreboard_format = "{SCOREBOARD_TAG}"
prefix = "[gold][TagSystem][default]"
debug = false

[chat]
dead_prefix = "[grey]*ÖLÜ* [default]"
team_prefix_t = "[gold](T) [default]"
team_prefix_ct = "[lightblue](CT) [default]"
team_prefix_spec = "[grey](İzleyici) [default]"
ignore_prefixes = ["!", "/", "."]
log_to_console = true
dead_talk_only_to_dead = false
block_empty_messages = true

[commands]
menu_command = "tagmenu"
give_group_command = "tagver"
set_custom_tag_command = "ozeltag"
remove_tag_command = "tagsil"
add_group_command = "grupekle"
reload_command = "tagreload"
check_command = "tagcheck"
admin_permission = "tagsystem.admin"

[default]
display_name = "Varsayılan"
chat_tag = "[OYUNCU]"
scoreboard_tag = ""
chat_tag_color = "grey"
name_color = "teamcolor"
message_color = "default"
use_tag_color = true
priority = -1000
enabled = true

# ----------------------------- YETKİ GRUPLARI ---------------------------------

[[groups]]
id = "vip"
display_name = "VIP"
permissions = ["vip", "tagsystem.vip"]
chat_tag = "[VIP]"
scoreboard_tag = "[VIP]"
chat_tag_color = "gold"
name_color = "gold"
priority = 10

[[groups]]
id = "admin"
display_name = "Admin"
permissions = ["admin", "tagsystem.admin"]
chat_tag = "[ADMIN]"
scoreboard_tag = "[ADMIN]"
chat_tag_color = "lightred"
name_color = "red"
message_color = "lightyellow"
priority = 100

# ----------------------------- ÖZEL OYUNCULAR ---------------------------------

[[players]]
steamid = "STEAM_0:1:12345678"
display_name = "Özel"
chat_tag = "[ÖZEL]"
scoreboard_tag = "[ÖZEL]"
chat_tag_color = "lightblue"
name_color = "lightblue"
priority = 1000
""";
}
