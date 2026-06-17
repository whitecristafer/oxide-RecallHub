
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Rust;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries;

namespace Oxide.Plugins
{
    [Info("RecallHub", "whitecristafer", "1.0.3")]
    [Description("Teleport to Outpost and Bandit Camp with custom spawn points")]
    public class RecallHub : RustPlugin
    {
        private const string PluginVersion = "1.0.3";
        private const string DefaultUpdateSourceUrl = "https://raw.githubusercontent.com/whitecristafer/oxide-RecallHub/main/RecallHub.cs";

        [PluginReference]
        private Plugin NoEscape;

        #region Configuration

        private ConfigData configData;

        private class ConfigData
        {
            public bool BlockTeleportWhenMounted { get; set; } = false;
            public bool BlockTeleportFromCargo { get; set; } = false;
            public bool CancelTpAnyDamage { get; set; } = true;
            public bool CancelTpPlayerDamage { get; set; } = true;
            public bool CancelTpFallDamage { get; set; } = true;
            public bool ForceResetHostileTimer { get; set; } = true;

            public bool UseAutoDetectOutpost { get; set; } = true;
            public bool UseAutoDetectBandit { get; set; } = true;

            public List<SerializableVector3> OutpostSpawnPoints { get; set; } = new List<SerializableVector3>();
            public List<SerializableVector3> BanditSpawnPoints { get; set; } = new List<SerializableVector3>();

            public float TeleportOffsetY { get; set; } = 1.0f;

            public int OutpostCooldown { get; set; } = 30;
            public int OutpostCountdown { get; set; } = 30;
            public int BanditCooldown { get; set; } = 30;
            public int BanditCountdown { get; set; } = 30;

            public string OutpostCommand { get; set; } = "otp";
            public string BanditCommand { get; set; } = "btp";
            public string CancelCommand { get; set; } = "ttc";

            public UpdateSettings Update { get; set; } = new UpdateSettings();
        }

        private class UpdateSettings
        {
            public bool Enabled { get; set; } = true;
            public bool CheckOnStartup { get; set; } = true;

            // The default URL points to the raw source file in the repository.
            // If you switch to release assets, keep the file name the same.
            public string SourceUrl { get; set; } = DefaultUpdateSourceUrl;
            public int TimeoutSeconds { get; set; } = 15;
        }

        private class SerializableVector3
        {
            public float x;
            public float y;
            public float z;

            public SerializableVector3() { }

            public SerializableVector3(Vector3 vec)
            {
                x = vec.x;
                y = vec.y;
                z = vec.z;
            }

            public Vector3 ToVector3() => new Vector3(x, y, z);

            public static implicit operator Vector3(SerializableVector3 v) => v.ToVector3();
            public static implicit operator SerializableVector3(Vector3 v) => new SerializableVector3(v);
        }

        protected override void LoadDefaultConfig()
        {
            configData = GetDefaultConfig();
            SavePluginConfig();
        }

        private ConfigData GetDefaultConfig() => new ConfigData();

        private void LoadPluginConfig()
        {
            base.LoadConfig();

            try
            {
                configData = Config.ReadObject<ConfigData>();
            }
            catch
            {
                PrintWarning("Config is corrupted. A new default config will be generated.");
                configData = null;
            }

            if (configData == null)
            {
                configData = GetDefaultConfig();
            }

            NormalizeConfig();
            SavePluginConfig();
        }

        private void NormalizeConfig()
        {
            configData.Update ??= new UpdateSettings();
            configData.OutpostSpawnPoints ??= new List<SerializableVector3>();
            configData.BanditSpawnPoints ??= new List<SerializableVector3>();
            configData.OutpostCommand = string.IsNullOrWhiteSpace(configData.OutpostCommand) ? "otp" : configData.OutpostCommand.Trim();
            configData.BanditCommand = string.IsNullOrWhiteSpace(configData.BanditCommand) ? "btp" : configData.BanditCommand.Trim();
            configData.CancelCommand = string.IsNullOrWhiteSpace(configData.CancelCommand) ? "ttc" : configData.CancelCommand.Trim();
            configData.Update.SourceUrl = string.IsNullOrWhiteSpace(configData.Update.SourceUrl) ? DefaultUpdateSourceUrl : configData.Update.SourceUrl.Trim();
            if (configData.Update.TimeoutSeconds <= 0)
                configData.Update.TimeoutSeconds = 15;
        }

        private void SavePluginConfig() => Config.WriteObject(configData, true);

        #endregion

        #region Data (Cooldowns)

        private StoredData storedData;

        private class StoredData
        {
            public Dictionary<ulong, int> Cooldowns = new Dictionary<ulong, int>();
        }

        private void LoadDataFile()
        {
            storedData = Interface.GetMod().DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData();
            storedData.Cooldowns ??= new Dictionary<ulong, int>();
        }

        private void SaveDataFile()
        {
            Interface.GetMod().DataFileSystem.WriteObject(Name, storedData);
        }

        private int GetUnix() => (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Prefix"] = "<size=12><color=#9966cc><b>[RecallHub]</b></color></size>",
                ["OutpostTeleport"] = "{0} Teleporting to Outpost in {1} seconds.\nType /{2} to cancel.",
                ["BanditTeleport"] = "{0} Teleporting to Bandit Camp in {1} seconds.\nType /{2} to cancel.",
                ["TeleportSuccessOutpost"] = "{0} You have successfully teleported to Outpost.",
                ["TeleportSuccessBandit"] = "{0} You have successfully teleported to Bandit Camp.",
                ["NoActiveTeleport"] = "{0} You are not preparing to teleport.\nType /{1} for Outpost\nType /{2} for Bandit Camp.",
                ["AlreadyTeleporting"] = "{0} You are already preparing to teleport.",

                ["Error: Seated"] = "You cannot teleport while seated.",
                ["Error: NoBuildingPrivilege"] = "You cannot teleport without building privilege.",
                ["Error: Wounded"] = "You cannot teleport while wounded.",
                ["Error: Mounted"] = "You cannot teleport while mounted.",
                ["Error: CargoShip"] = "You cannot teleport from the Cargo Ship.",
                ["Error: Hostile"] = "You cannot teleport while hostile.\nHostility will reset in {0}.",
                ["Error: Cooldown"] = "You cannot teleport yet.\nAvailable in {0}.",
                ["Error: RaidBlocked"] = "You cannot teleport while raid blocked.",
                ["Error: CombatBlocked"] = "You cannot teleport while combat blocked.",

                ["TeleportCancelled"] = "{0} Teleport cancelled.",
                ["TeleportCancelledPlayerDamage"] = "{0} Teleport cancelled due to player damage.",
                ["TeleportCancelledFallDamage"] = "{0} Teleport cancelled due to fall damage.",
                ["TeleportCancelledDamage"] = "{0} Teleport cancelled due to damage.",

                ["OutpostNotFound"] = "{0} Outpost spawn points were not found.",
                ["BanditNotFound"] = "{0} Bandit Camp spawn points were not found.",
                ["NoPermission"] = "{0} You do not have permission to use this command.",

                ["UpdateCheckStart"] = "{0} Checking for updates...",
                ["UpdateCheckFailed"] = "{0} Update check failed.",
                ["UpdateRemoteInvalid"] = "{0} Remote build is not a stable release and was ignored.",
                ["UpdateLocalInvalid"] = "{0} Local version is invalid. Update check skipped.",
                ["UpdateCurrent"] = "{0} You are already running the latest stable version: {1}.",
                ["UpdateAvailable"] = "{0} New version found: {1} -> {2}. Downloading now...",
                ["UpdateDownloaded"] = "{0} Update downloaded successfully. Saved to: {1}",
                ["UpdateWriteFailed"] = "{0} Failed to write the update file.",
                ["UpdateBanner"] = "{0} Project page: infunv.ru"
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Prefix"] = "<size=12><color=#9966cc><b>[RecallHub]</b></color></size>",
                ["OutpostTeleport"] = "{0} Телепортация на Аванпост через {1} секунд.\nВведите /{2}, чтобы отменить телепортацию.",
                ["BanditTeleport"] = "{0} Телепортация в Лагерь бандитов через {1} секунд.\nВведите /{2}, чтобы отменить телепортацию.",
                ["TeleportSuccessOutpost"] = "{0} Вы успешно телепортировались на Аванпост.",
                ["TeleportSuccessBandit"] = "{0} Вы успешно телепортировались в Лагерь бандитов.",
                ["NoActiveTeleport"] = "{0} Вы не собираетесь телепортироваться.\nВведите /{1} для Аванпоста\nВведите /{2} для Лагеря бандитов.",
                ["AlreadyTeleporting"] = "{0} Вы уже собираетесь телепортироваться.",

                ["Error: Seated"] = "Вы не можете телепортироваться, сидя в транспорте.",
                ["Error: NoBuildingPrivilege"] = "Вы не можете телепортироваться без привилегии на строительство.",
                ["Error: Wounded"] = "Вы не можете телепортироваться, будучи раненым.",
                ["Error: Mounted"] = "Вы не можете телепортироваться, находясь верхом.",
                ["Error: CargoShip"] = "Вы не можете телепортироваться с грузового корабля.",
                ["Error: Hostile"] = "Вы не можете телепортироваться, пока отмечены как враждебный.\nСтатус враждебности снимут через {0}.",
                ["Error: Cooldown"] = "Вы не можете телепортироваться ещё.\nТелепортация станет доступна через {0}.",
                ["Error: RaidBlocked"] = "Вы не можете телепортироваться во время рейд-блока.",
                ["Error: CombatBlocked"] = "Вы не можете телепортироваться во время боевого блока.",

                ["TeleportCancelled"] = "{0} Вы отменили телепортацию.",
                ["TeleportCancelledPlayerDamage"] = "{0} Телепортация отменена из-за получения урона от игрока.",
                ["TeleportCancelledFallDamage"] = "{0} Телепортация отменена из-за урона от падения.",
                ["TeleportCancelledDamage"] = "{0} Телепортация отменена, так как вы получили урон.",

                ["OutpostNotFound"] = "{0} Плагин не смог найти Аванпост.",
                ["BanditNotFound"] = "{0} Плагин не смог найти Лагерь бандитов.",
                ["NoPermission"] = "{0} У вас нет прав на использование этой команды.",

                ["UpdateCheckStart"] = "{0} Проверка обновлений...",
                ["UpdateCheckFailed"] = "{0} Проверка обновлений не удалась.",
                ["UpdateRemoteInvalid"] = "{0} Удалённая сборка не является стабильной и была проигнорирована.",
                ["UpdateLocalInvalid"] = "{0} Локальная версия некорректна. Проверка обновлений пропущена.",
                ["UpdateCurrent"] = "{0} У вас уже установлена последняя стабильная версия: {1}.",
                ["UpdateAvailable"] = "{0} Найдена новая версия: {1} -> {2}. Начинаю загрузку...",
                ["UpdateDownloaded"] = "{0} Обновление успешно загружено. Файл сохранён: {1}",
                ["UpdateWriteFailed"] = "{0} Не удалось записать файл обновления.",
                ["UpdateBanner"] = "{0} Страница проекта: infunv.ru"
            }, this, "ru");
        }

        private string Lang(string key, string playerId, params object[] args)
        {
            return string.Format(lang.GetMessage(key, this, playerId), args);
        }

        #endregion

        #region Permissions

        private const string PermOutpost = "recallhub.outpost";
        private const string PermBandit = "recallhub.bandit";
        private const string PermNoCooldown = "recallhub.nocooldown";

        #endregion

        #region Fields

        private static DateTime _lastUpdateCheckTime = DateTime.MinValue;
        private static bool _updateJustApplied = false;
        private List<Vector3> outpostSpawns = new List<Vector3>();
        private List<Vector3> banditSpawns = new List<Vector3>();
        private Dictionary<ulong, Timer> teleportTimers = new Dictionary<ulong, Timer>();
        private bool updateReloadScheduled;

        #endregion

        #region Hooks

        private void Loaded()
        {
            LoadPluginConfig();
            LoadDataFile();

            PrintStartupBanner();

            permission.RegisterPermission(PermOutpost, this);
            permission.RegisterPermission(PermBandit, this);
            permission.RegisterPermission(PermNoCooldown, this);

            cmd.AddChatCommand(configData.OutpostCommand, this, nameof(CmdOutpost));
            cmd.AddChatCommand(configData.BanditCommand, this, nameof(CmdBandit));
            cmd.AddChatCommand(configData.CancelCommand, this, nameof(CmdCancelTp));
        }

        private void OnServerInitialized()
        {
            FindTownsAndSaveSpawns();

            if (configData.Update.Enabled && configData.Update.CheckOnStartup)
            {
                CheckForUpdates();
            }
        }

        private void Unload()
        {
            foreach (var timer in teleportTimers.Values)
            {
                timer?.Destroy();
            }

            teleportTimers.Clear();
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
                return;

            if (teleportTimers.TryGetValue(player.userID, out var timer))
            {
                timer?.Destroy();
                teleportTimers.Remove(player.userID);
            }
        }

        #endregion

        #region Startup / Update

        private void PrintStartupBanner()
        {
            var prefix = Lang("Prefix", "0");
            Puts("==================================================");
            Puts($"RecallHub v{PluginVersion} loaded.");
            Puts($"Teleport system initialized.");
            Puts($"Sponsor: infunv.ru");
            Puts("==================================================");
        }

        private void CheckForUpdates()
        {
            if (_updateJustApplied && (DateTime.Now - _lastUpdateCheckTime).TotalSeconds < 60)
            {
                Puts("[RecallHub] Update just applied, skipping check for 60 seconds.");
                return;
            }
            string sourceUrl = configData?.Update?.SourceUrl?.Trim();
            if (string.IsNullOrWhiteSpace(sourceUrl))
            {
                PrintWarning("[RecallHub] Update source URL is empty. Skipping update check.");
                return;
            }

            Puts(Lang("UpdateCheckStart", "0", Lang("Prefix", "0")));

            var headers = new Dictionary<string, string>
            {
                ["User-Agent"] = $"RecallHub/{PluginVersion}",
                ["Accept"] = "text/plain, */*"
            };

            webrequest.Enqueue(sourceUrl, null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrWhiteSpace(response))
                {
                    Puts(Lang("UpdateCheckFailed", "0", Lang("Prefix", "0")));
                    PrintWarning($"[RecallHub] HTTP {(code == 0 ? "no-response" : code.ToString())} while checking updates.");
                    return;
                }

                string remoteVersionRaw = ExtractVersionFromSource(response);
                if (string.IsNullOrWhiteSpace(remoteVersionRaw))
                {
                    Puts(Lang("UpdateCheckFailed", "0", Lang("Prefix", "0")));
                    PrintWarning("[RecallHub] Could not detect a version string in the remote source.");
                    return;
                }

                if (IsDevVersion(remoteVersionRaw))
                {
                    PrintWarning(Lang("UpdateRemoteInvalid", "0", Lang("Prefix", "0")));
                    return;
                }

                if (!TryParseStableVersion(remoteVersionRaw, out var remoteVersion))
                {
                    PrintWarning(Lang("UpdateRemoteInvalid", "0", Lang("Prefix", "0")));
                    return;
                }

                if (!TryParseStableVersion(PluginVersion, out var localVersion))
                {
                    PrintWarning(Lang("UpdateLocalInvalid", "0", Lang("Prefix", "0")));
                    return;
                }

                if (remoteVersion <= localVersion)
                {
                    Puts(Lang("UpdateCurrent", "0", Lang("Prefix", "0"), localVersion));
                    return;
                }

                Puts(Lang("UpdateAvailable", "0", Lang("Prefix", "0"), localVersion, remoteVersionRaw));

                TrySaveUpdate(response, remoteVersionRaw);
            }, this, Oxide.Core.Libraries.RequestMethod.GET, headers, configData.Update.TimeoutSeconds);
        }

        private void TrySaveUpdate(string sourceContent, string remoteVersionRaw)
        {
            string downloadedVersionRaw = ExtractVersionFromSource(sourceContent);
            if (string.IsNullOrWhiteSpace(downloadedVersionRaw))
                downloadedVersionRaw = remoteVersionRaw;

            if (IsDevVersion(downloadedVersionRaw))
            {
                PrintWarning(Lang("UpdateRemoteInvalid", "0", Lang("Prefix", "0")));
                return;
            }

            if (!TryParseStableVersion(downloadedVersionRaw, out var downloadedVersion))
            {
                PrintWarning("[RecallHub] Downloaded version is invalid.");
                return;
            }

            if (!TryParseStableVersion(PluginVersion, out var localVersion))
            {
                PrintWarning("[RecallHub] Local version is invalid.");
                return;
            }

            if (downloadedVersion <= localVersion)
            {
                Puts(Lang("UpdateCurrent", "0", Lang("Prefix", "0"), localVersion));
                return;
            }

            string pluginPath = Path.Combine(Interface.Oxide.PluginDirectory, $"{Name}.cs");
            string directory = Path.GetDirectoryName(pluginPath);

            if (!Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    PrintWarning($"[RecallHub] Failed to create directory: {ex.Message}");
                    return;
                }
            }

            try
            {
                // Direct file overwrite (without deleting or moving)
                File.WriteAllText(pluginPath, sourceContent, new UTF8Encoding(false));

                _updateJustApplied = true;
                _lastUpdateCheckTime = DateTime.Now;

                Puts(Lang("UpdateDownloaded", "0", Lang("Prefix", "0"), pluginPath));
                Puts("[RecallHub] Update saved. Waiting for Oxide compilation...");
            }
            catch (Exception ex)
            {
                PrintWarning($"{Lang("UpdateWriteFailed", "0", Lang("Prefix", "0"))} {ex.Message}");
            }
        }

        private void ScheduleReload()
        {
            if (updateReloadScheduled)
                return;

            updateReloadScheduled = true;
            Puts("[RecallHub] Applying update...");

            timer.Once(3f, () =>
            {
                try
                {
                    Server.Command($"oxide.reload {Name}");
                }
                catch (Exception ex)
                {
                    PrintError($"[RecallHub] Failed to reload plugin: {ex.Message}");
                }
                finally
                {
                    updateReloadScheduled = false;
                }
            });
        }

        private string ExtractVersionFromSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return null;

            // Main lookup: [Info("RecallHub", "whitecristafer", "1.0.3")]
            var match = Regex.Match(
                source,
                @"\[Info\(\s*""RecallHub""\s*,\s*""[^""]+""\s*,\s*""(?<version>[^""]+)""\s*\)\]",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);

            if (match.Success)
                return match.Groups["version"].Value.Trim();

            // Fallback: PluginVersion constant.
            match = Regex.Match(
                source,
                @"PluginVersion\s*=\s*""(?<version>[^""]+)""",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);

            if (match.Success)
                return match.Groups["version"].Value.Trim();

            return null;
        }

        private bool IsDevVersion(string versionText)
        {
            if (string.IsNullOrWhiteSpace(versionText))
                return false;

            versionText = versionText.Trim();

            return Regex.IsMatch(
                versionText,
                @"^(d\d+(\.\d+){1,3}|.*-(dev|alpha|beta|preview|rc)\d*)$",
                RegexOptions.IgnoreCase);
        }

        private bool TryParseStableVersion(string versionText, out System.Version version)
        {
            version = null;

            if (string.IsNullOrWhiteSpace(versionText))
                return false;

            versionText = versionText.Trim();

            if (IsDevVersion(versionText))
                return false;

            if (versionText.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                versionText = versionText.Substring(1);

            if (!Regex.IsMatch(versionText, @"^\d+(\.\d+){1,3}$"))
                return false;

            try
            {
                version = new System.Version(versionText);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Spawn Detection & Configuration

        private void FindTownsAndSaveSpawns()
        {
            bool changed = false;

            if (configData.UseAutoDetectOutpost)
            {
                var found = FindOutpostSpawns();
                if (found.Count > 0)
                {
                    configData.OutpostSpawnPoints = found.ConvertAll(v => new SerializableVector3(v));
                    changed = true;
                    PrintWarning($"[RecallHub] Auto-detected {found.Count} Outpost spawn points.");
                }
                else
                {
                    PrintWarning("[RecallHub] Auto-detection for Outpost found no spawns. Manual points will be used if present.");
                }
            }

            if (configData.UseAutoDetectBandit)
            {
                var found = FindBanditSpawns();
                if (found.Count > 0)
                {
                    configData.BanditSpawnPoints = found.ConvertAll(v => new SerializableVector3(v));
                    changed = true;
                    PrintWarning($"[RecallHub] Auto-detected {found.Count} Bandit Camp spawn points.");
                }
                else
                {
                    PrintWarning("[RecallHub] Auto-detection for Bandit Camp found no spawns. Manual points will be used if present.");
                }
            }

            if (changed)
                SavePluginConfig();

            outpostSpawns = configData.OutpostSpawnPoints.Select(v => v.ToVector3()).ToList();
            banditSpawns = configData.BanditSpawnPoints.Select(v => v.ToVector3()).ToList();

            if (outpostSpawns.Count == 0)
                PrintError("[RecallHub] No Outpost spawn points available. Please check the configuration.");

            if (banditSpawns.Count == 0)
                PrintError("[RecallHub] No Bandit Camp spawn points available. Please check the configuration.");
        }

        private List<Vector3> FindOutpostSpawns()
        {
            List<Vector3> spawns = new List<Vector3>();

            foreach (var monument in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            {
                string name = monument.name.ToLowerInvariant();
                if (!name.Contains("compound") && !name.Contains("outpost"))
                    continue;

                List<BaseEntity> entities = new List<BaseEntity>();
                Vis.Entities(monument.transform.position, 150f, entities); // Increased radius to 150!

                foreach (var entity in entities)
                {
                    if (entity is VendingMachine vm)
                    {
                        Vector3? safePos = FindSafeSpawnNear(vm.transform.position, vm.transform.forward);
                        if (safePos.HasValue && !ContainsApprox(spawns, safePos.Value))
                        {
                            spawns.Add(safePos.Value);
                        }
                    }
                }

                if (spawns.Count == 0)
                {
                    Vector3 center = monument.transform.position;
                    center.y += 2f;
                    center = GetGroundPosition(center);
                    spawns.Add(center);
                    PrintWarning($"[RecallHub] No VendingMachine found in Outpost, using center point: {center}");
                }
            }

            return spawns;
        }

        private List<Vector3> FindBanditSpawns()
        {
            List<Vector3> spawns = new List<Vector3>();

            foreach (var monument in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            {
                string name = monument.name.ToLowerInvariant();
                if (!name.Contains("bandit"))
                    continue;

                List<BaseEntity> entities = new List<BaseEntity>();
                Vis.Entities(monument.transform.position, 150f, entities); // Increased the radius

                foreach (var entity in entities)
                {
                    if (entity is VendingMachine vm)
                    {
                        Vector3? safePos = FindSafeSpawnNear(vm.transform.position, vm.transform.forward);
                        if (safePos.HasValue && !ContainsApprox(spawns, safePos.Value))
                        {
                            spawns.Add(safePos.Value);
                        }
                    }
                }

                if (spawns.Count == 0)
                {
                    Vector3 basePos = monument.transform.position;
                    Vector3 pos = basePos + (-monument.transform.right * -50.75f);
                    pos += monument.transform.forward * -21.75f;
                    pos.y += 2f;
                    pos = GetGroundPosition(pos);
                    spawns.Add(pos);
                    PrintWarning($"[RecallHub] No VendingMachine found in Bandit Camp, using calculated point: {pos}");
                }
            }

            return spawns;
        }

        // Helper method for finding a point on the ground surface
       private Vector3 GetGroundPosition(Vector3 pos)
        {
            RaycastHit hit;
            Vector3 origin = pos + Vector3.up * 5f; 
            int mask = LayerMask.GetMask("Terrain", "World", "Construction", "Deployed");

            if (Physics.Raycast(origin, Vector3.down, out hit, 10f, mask))
            {
                return hit.point;
            }
            return pos; 
        }

        private Vector3? FindSafeSpawnNear(Vector3 startPos, Vector3 forward)
        {
            float[] distances = { 1.5f, 2.0f, 2.5f, 3.0f }; 
            int mask = LayerMask.GetMask("Construction", "World", "Terrain", "Deployed");

            foreach (var dist in distances)
            {
                Vector3 pos = startPos + forward * dist;
                pos.y += 0.5f;
                pos = GetGroundPosition(pos);

                Vector3 bottom = pos + new Vector3(0, 0.5f, 0);
                Vector3 top = pos + new Vector3(0, 1.5f, 0);

                if (!Physics.CheckCapsule(bottom, top, 0.45f, mask))
                {
                    return pos; 
                }
            }
            return null;
        }

        private bool ContainsApprox(List<Vector3> list, Vector3 value, float tolerance = 0.05f)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (Vector3.Distance(list[i], value) <= tolerance)
                    return true;
            }

            return false;
        }

        #endregion

        #region Commands

        private void CmdOutpost(BasePlayer player)
        {
            if (!HasPermission(player, PermOutpost))
            {
                player.ChatMessage(Lang("NoPermission", player.UserIDString, Lang("Prefix", player.UserIDString)));
                return;
            }

            var result = CanTeleport(player, "outpost", true);
            if (result != null)
            {
                player.ChatMessage(result);
                return;
            }

            StartTeleport(player, "outpost");
            player.ChatMessage(Lang("OutpostTeleport", player.UserIDString, Lang("Prefix", player.UserIDString), configData.OutpostCountdown, configData.CancelCommand));
        }

        private void CmdBandit(BasePlayer player)
        {
            if (!HasPermission(player, PermBandit))
            {
                player.ChatMessage(Lang("NoPermission", player.UserIDString, Lang("Prefix", player.UserIDString)));
                return;
            }

            var result = CanTeleport(player, "bandit", true);
            if (result != null)
            {
                player.ChatMessage(result);
                return;
            }

            StartTeleport(player, "bandit");
            player.ChatMessage(Lang("BanditTeleport", player.UserIDString, Lang("Prefix", player.UserIDString), configData.BanditCountdown, configData.CancelCommand));
        }

        private void CmdCancelTp(BasePlayer player)
        {
            if (!HasPermission(player, PermOutpost) && !HasPermission(player, PermBandit))
            {
                player.ChatMessage(Lang("NoPermission", player.UserIDString, Lang("Prefix", player.UserIDString)));
                return;
            }

            if (!teleportTimers.ContainsKey(player.userID))
            {
                player.ChatMessage(Lang("NoActiveTeleport", player.UserIDString, Lang("Prefix", player.UserIDString), configData.OutpostCommand, configData.BanditCommand));
                return;
            }

            CancelTeleport(player, Lang("TeleportCancelled", player.UserIDString, Lang("Prefix", player.UserIDString)));
        }

        #endregion

        #region Teleport Logic

        private string CanTeleport(BasePlayer player, string town, bool checkTimers)
        {
            if (player == null || !player.IsValid())
                return null;

            if (town == "outpost" && outpostSpawns.Count == 0)
                return Lang("OutpostNotFound", player.UserIDString, Lang("Prefix", player.UserIDString));

            if (town == "bandit" && banditSpawns.Count == 0)
                return Lang("BanditNotFound", player.UserIDString, Lang("Prefix", player.UserIDString));

            if (!HasPermission(player, PermNoCooldown))
            {
                int cooldown = town == "outpost" ? configData.OutpostCooldown : configData.BanditCooldown;
                if (cooldown > 0 && storedData.Cooldowns.TryGetValue(player.userID, out int lastTime))
                {
                    int remaining = lastTime + cooldown - GetUnix();
                    if (remaining > 0)
                    {
                        return Lang("Error: Cooldown", player.UserIDString, FormatDuration(remaining));
                    }
                }
            }

            if (!player.CanBuild())
                return Lang("Error: NoBuildingPrivilege", player.UserIDString);

            if (player.IsWounded())
                return Lang("Error: Wounded", player.UserIDString);

            if (configData.BlockTeleportWhenMounted && player.isMounted)
                return Lang("Error: Mounted", player.UserIDString);

            if (configData.BlockTeleportFromCargo && player.GetComponentInParent<CargoShip>() != null)
                return Lang("Error: CargoShip", player.UserIDString);

            if (player.IsHostile())
            {
                double remaining = player.State.unHostileTimestamp - UnityEngine.Time.realtimeSinceStartup;
                if (remaining > 0)
                    return Lang("Error: Hostile", player.UserIDString, FormatDuration((int)remaining));
            }

            if (NoEscape != null)
            {
                try
                {
                    if ((bool)NoEscape.Call("IsRaidBlocked", player))
                        return Lang("Error: RaidBlocked", player.UserIDString);

                    if ((bool)NoEscape.Call("IsCombatBlocked", player))
                        return Lang("Error: CombatBlocked", player.UserIDString);
                }
                catch
                {
                    // Ignore plugin integration errors to avoid breaking teleport logic.
                }
            }

            if (checkTimers && teleportTimers.ContainsKey(player.userID))
                return Lang("AlreadyTeleporting", player.UserIDString, Lang("Prefix", player.UserIDString));

            return null;
        }

        private void StartTeleport(BasePlayer player, string town)
        {
            int countdown = town == "outpost" ? configData.OutpostCountdown : configData.BanditCountdown;
            ulong userId = player.userID;

            Timer timerObj = timer.Once(countdown, () =>
            {
                if (player == null || !player.IsValid() || player.IsDestroyed)
                    return;

                var result = CanTeleport(player, town, false);
                if (result != null)
                {
                    player.ChatMessage(result);
                    teleportTimers.Remove(userId);
                    return;
                }

                player.EnsureDismounted();

                if (configData.ForceResetHostileTimer)
                {
                    player.State.unHostileTimestamp = 0;
                    player.ClientRPCPlayer(null, player, "SetHostileLength", 0f);
                }

                List<Vector3> spawns = town == "outpost" ? outpostSpawns : banditSpawns;
                if (spawns.Count == 0)
                {
                    player.ChatMessage(Lang(town == "outpost" ? "OutpostNotFound" : "BanditNotFound", player.UserIDString, Lang("Prefix", player.UserIDString)));
                    teleportTimers.Remove(userId);
                    return;
                }

                Vector3 target = spawns[UnityEngine.Random.Range(0, spawns.Count)];
                target.y += configData.TeleportOffsetY;

                SafeTeleport(player, target);

                if (!HasPermission(player, PermNoCooldown))
                {
                    storedData.Cooldowns[player.userID] = GetUnix();
                    SaveDataFile();
                }

                teleportTimers.Remove(userId);

                string msgKey = town == "outpost" ? "TeleportSuccessOutpost" : "TeleportSuccessBandit";
                player.ChatMessage(Lang(msgKey, player.UserIDString, Lang("Prefix", player.UserIDString)));
            });

            teleportTimers[userId] = timerObj;
        }

        private void SafeTeleport(BasePlayer player, Vector3 position)
        {
            if (player == null || !player.IsValid())
                return;

            // Resetting the current states to avoid bugs
            if (player.IsSleeping()) player.EndSleeping();
            player.EnsureDismounted();
            player.SetParent(null, true, true);

            // Performing a physical transfer
            player.Teleport(position);

            // Forcibly update triggers (so that the Safe Zone applies instantly and the turrets don't kill you)
            player.ForceUpdateTriggers();

            // We put the player to sleep for a second so that the client can load the floor textures
            if (player.IsConnected)
            {
                player.StartSleeping();
                timer.Once(1.5f, () =>
                {
                    if (player != null && player.IsSleeping())
                    {
                        player.EndSleeping();
                    }
                });
            }

            // Synchronize the position over the network
            player.UpdateNetworkGroup();
            player.SendNetworkUpdateImmediate();
            player.ClientRPCPlayer(null, player, "ForcePositionTo", position);
            player.ClearEntityQueue();
        }

        private void CancelTeleport(BasePlayer player, string reason)
        {
            if (player == null)
                return;

            if (teleportTimers.TryGetValue(player.userID, out var timer))
            {
                timer?.Destroy();
                teleportTimers.Remove(player.userID);
            }

            player.ChatMessage(reason);
        }

        #endregion

        #region Damage Handling

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var player = entity?.ToPlayer();
            if (player == null || info == null)
                return;

            if (!teleportTimers.ContainsKey(player.userID))
                return;

            NextTick(() =>
            {
                if (player == null || !player.IsValid() || info.damageTypes.Total() <= 0f)
                    return;

                if (configData.CancelTpAnyDamage)
                {
                    CancelTeleport(player, Lang("TeleportCancelledDamage", player.UserIDString, Lang("Prefix", player.UserIDString)));
                    return;
                }

                if (configData.CancelTpPlayerDamage && info.Initiator is BasePlayer)
                {
                    CancelTeleport(player, Lang("TeleportCancelledPlayerDamage", player.UserIDString, Lang("Prefix", player.UserIDString)));
                    return;
                }

                if (configData.CancelTpFallDamage && info.damageTypes.Has(DamageType.Fall))
                {
                    CancelTeleport(player, Lang("TeleportCancelledFallDamage", player.UserIDString, Lang("Prefix", player.UserIDString)));
                }
            });
        }

        #endregion

        #region Helpers

        private bool HasPermission(BasePlayer player, string perm) => player != null && permission.UserHasPermission(player.UserIDString, perm);

        private string FormatDuration(int seconds)
        {
            if (seconds <= 0)
                return "0s";

            if (seconds < 60)
                return $"{seconds}s";

            TimeSpan ts = TimeSpan.FromSeconds(seconds);

            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";

            return $"{ts.Minutes}m {ts.Seconds}s";
        }

        #endregion
    }
}
