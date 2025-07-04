/*
## Configuration
The following configuration options are available:
{
  "store-secret-key": "your-api-key-here",
  "claim-command": "tgclaim",
  "secret-command": "tgsecret"
}

## Localization
English (en):
  "ApiOffline": "API Services are currently offline. Please check back shortly.",
  "CommandReserved": "Command Reserved for the permission group teamgames.admin",
  "SecretUsage": "Usage: /teamgames.secret <secret>",
  "SecretUpdated": "Store secret key has been updated.",
  "ErrorProcessing": "An error occurred while processing your request. Please try again later.",
  "NullTransaction": "Encountered a null transaction object.",
  "InvalidAmount": "Invalid product amount: {0}",
  "ItemNotFound": "Item {0} not found.",
  "CreatingItem": "Creating {0} of {1}.",
  "ItemGiven": "Gave {0} {1} to {2}.",
  "ItemDropped": "Dropped {0} {1} to {2}.",
  "FailedToCreate": "Failed to create item {0}.",
  "SetCommandUsage": "Usage: /tgsetcmd <claim|secret> <newname>",
  "InvalidCommandType": "Invalid command type. Use 'claim' or 'secret'.",
  "CommandUpdated": "{0} command has been updated to /{1}.",
  "TeamGamesMessage": "{0}",
  "ClaimCooldown": "You must wait {0} seconds before using this command again.",
  "CommandNameInUse": "The command name '{0}' is already in use by another plugin."
}
*/

using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries;
using Newtonsoft.Json;
using System.Collections.Generic;
using System;
using UnityEngine;
using Epic.OnlineServices.Ecom;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("TeamGames Store", "TeamGames", "1.2.6")]
    [Description("Official support for the TeamGames monetization platform.")]
    public class TeamGames : RustPlugin
    {
        private const string ApiUrl = "https://api.teamgames.io/api/v3/store/transaction/update";
        private string apiKey;
        private string claimCommand;
        private string secretCommand;
        private Dictionary<string, string> headers;


        private readonly Dictionary<ulong, float> lastClaimTimes = new Dictionary<ulong, float>();
        private const float ClaimCooldownSeconds = 10f;

        private PluginConfig config;

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<PluginConfig>();
            }
            catch
            {
                PrintWarning("Config file is corrupt or invalid, creating a new one.");
                LoadDefaultConfig();
            }

            SaveConfig();

            apiKey = config.StoreSecretKey;
            claimCommand = config.ClaimCommand;
            secretCommand = config.SecretCommand;
        }

        protected override void LoadDefaultConfig()
        {
            config = new PluginConfig();
            SaveConfig();
        }


        private void Init()
        {
            headers = new Dictionary<string, string>
            {
                ["X-API-Key"] = apiKey,
                ["Content-Type"] = "application/json"
            };

            AddCovalenceCommand(claimCommand, nameof(ClaimCommand));
            AddCovalenceCommand(secretCommand, nameof(SetSecretCommand));
            AddCovalenceCommand("tgsetcmd", nameof(SetCommandName));
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ApiOffline"] = "API Services are currently offline. Please check back shortly.",
                ["CommandReserved"] = "Command Reserved for the permission group teamgames.admin",
                ["SecretUsage"] = "Usage: /teamgames.secret <secret>",
                ["SecretUpdated"] = "Store secret key has been updated.",
                ["ErrorProcessing"] = "An error occurred while processing your request. Please try again later.",
                ["NullTransaction"] = "Encountered a null transaction object.",
                ["InvalidAmount"] = "Invalid product amount: {0}",
                ["ItemNotFound"] = "Item {0} not found.",
                ["CreatingItem"] = "Creating {0} of {1}.",
                ["ItemGiven"] = "Gave {0} {1} to {2}.",
                ["ItemDropped"] = "Dropped {0} {1} to {2}.",
                ["FailedToCreate"] = "Failed to create item {0}.",
                ["SetCommandUsage"] = "Usage: /tgsetcmd <claim|secret> <newname>",
                ["InvalidCommandType"] = "Invalid command type. Use 'claim' or 'secret'.",
                ["CommandUpdated"] = "{0} command has been updated to /{1}.",
                ["TeamGamesMessage"] = "{0}",
                ["ClaimCooldown"] = "You must wait {0} seconds before using this command again.",
                ["CommandNameInUse"] = "The command name '{0}' is already in use by another plugin."
            }, this);
        }

        [ChatCommand("tg.claim")]
        private void ClaimCommand(IPlayer player, string command, string[] args)
        {
            var basePlayer = player.Object as BasePlayer;
            if (basePlayer == null) return;

            ulong userId = basePlayer.userID;
            float currentTime = UnityEngine.Time.realtimeSinceStartup;

            if (lastClaimTimes.TryGetValue(userId, out float lastTime))
            {
                float timeSinceLastUse = currentTime - lastTime;
                if (timeSinceLastUse < ClaimCooldownSeconds)
                {
                    float remaining = ClaimCooldownSeconds - timeSinceLastUse;
                    player.Reply(Lang("ClaimCooldown", player.Id, Mathf.CeilToInt(remaining)));
                    return;
                }
            }

            lastClaimTimes[userId] = currentTime;

            var postData = new Dictionary<string, string> { ["playerName"] = basePlayer.UserIDString };
            string jsonData = JsonConvert.SerializeObject(postData);

            webrequest.Enqueue(ApiUrl, jsonData, (code, response) => HandleWebResponse(basePlayer, code, response), this, RequestMethod.POST, headers);
        }


        [ChatCommand("tg.secret")]
        private void SetSecretCommand(IPlayer player, string command, string[] args)
        {
            var basePlayer = player.Object as BasePlayer;
            bool isRcon = basePlayer?.net?.connection?.authLevel == 2;
            bool hasPermission = permission.UserHasPermission(player.Id, "teamgames.admin");

            if (!isRcon && !hasPermission)
            {
                player.Reply(Lang("CommandReserved", player.Id));
                return;
            }

            if (args.Length != 1)
            {
                player.Reply(Lang("SecretUsage", player.Id));
                return;
            }

            apiKey = args[0];
            config.StoreSecretKey = apiKey;
            Config.WriteObject(config);

            headers["X-API-Key"] = apiKey;

            player.Reply(Lang("SecretUpdated", player.Id));
            PrintWarning($"Store secret key has been updated by {player.Name ?? "RCON"}.");
        }


        [ChatCommand("tg.setcmd")]
        private void SetCommandName(IPlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.Id, "teamgames.admin"))
            {
                player.Reply(Lang("CommandReserved", player.Id));
                return;
            }

            if (args.Length != 2)
            {
                player.Reply(Lang("SetCommandUsage", player.Id));
                return;
            }

            string cmdType = args[0].ToLower();
            string newName = args[1].ToLower();

            // PATCH: Check if the command name is already registered by another plugin or by the server.
            if (ServerMgr.Instance.chatCommands.ContainsKey(newName) || ServerMgr.Instance.consoleCommands.ContainsKey(newName))
            {
                player.Reply(Lang("CommandNameInUse", player.Id, newName));
                return;
            }

            switch (cmdType)
            {
                case "claim":
                    config.ClaimCommand = newName;
                    claimCommand = newName;
                    break;
                case "secret":
                    config.SecretCommand = newName;
                    secretCommand = newName;
                    break;
                default:
                    player.Reply(Lang("InvalidCommandType", player.Id));
                    return;
            }
            Config.WriteObject(config);

            SaveConfig();
            player.Reply(Lang("CommandUpdated", player.Id, cmdType, newName));
            PrintWarning($"{cmdType} command has been updated to /{newName} by {player.Name}.");
        }

        private void HandleWebResponse(BasePlayer player, int code, string response)
        {
            if (string.IsNullOrEmpty(response) || code != 200)
            {
                PrintWarning($"Failed to fetch transactions for {player.displayName}: {response ?? "No response"} (Code: {code})");
                player.ChatMessage(Lang("ApiOffline", player.UserIDString));
                return;
            }

            try
            {
                var transactions = JsonConvert.DeserializeObject<Transaction[]>(response);
                if (transactions != null)
                {
                    ProcessTransactions(player, transactions);
                }
                else
                {
                    PrintWarning("No transactions found in the response.");
                    player.ChatMessage(Lang("ErrorProcessing", player.UserIDString));
                }
            }
            catch (JsonException ex)
            {
                PrintWarning($"Error parsing JSON response: {ex.Message}");
                player.ChatMessage(Lang("ErrorProcessing", player.UserIDString));
            }
        }

        private void ProcessTransactions(BasePlayer player, Transaction[] transactions)
        {
            if (transactions.Length == 1 && transactions[0].message != null)
            {
                player.ChatMessage(Lang("TeamGamesMessage", player.UserIDString, transactions[0].message));
                return;
            }

            foreach (var transaction in transactions)
            {
                if (transaction == null)
                {
                    player.ChatMessage(Lang("NullTransaction", player.UserIDString));
                    continue;
                }

                if (transaction.product_amount < 1)
                {
                    player.ChatMessage(Lang("InvalidAmount", player.UserIDString, transaction.product_amount));
                    continue;
                }

                string itemName = ParseItemName(transaction.product_id_string);
                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(itemName);
                if (itemDefinition == null)
                {
                    player.ChatMessage(Lang("ItemNotFound", player.UserIDString, itemName));
                    continue;
                }

                player.ChatMessage(Lang("CreatingItem", player.UserIDString, transaction.product_amount, itemName));
                Item item = ItemManager.Create(itemDefinition, transaction.product_amount);
                if (item != null)
                {
                    bool given = player.inventory.GiveItem(item);
                    if (!given)
                    {
                        item.Drop(player.GetDropPosition(), player.GetDropVelocity());
                        player.ChatMessage(Lang("ItemDropped", player.UserIDString, transaction.product_amount, itemName, player.displayName));
                    }
                    else
                    {
                        player.ChatMessage(Lang("ItemGiven", player.UserIDString, transaction.product_amount, itemName, player.displayName));
                    }
                }
                else
                {
                    player.ChatMessage(Lang("FailedToCreate", player.UserIDString, itemName));
                }
            }
        }

        private string ParseItemName(string productIdentifier)
        {
            if (string.IsNullOrEmpty(productIdentifier))
            {
                return null;
            }

            var parts = productIdentifier.Split(':');
            return parts.Length > 1 ? parts[1] : parts[0];
        }

        private string Lang(string key, string id = null, params object[] args)
        {
            string format = lang.GetMessage(key, this, id);
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException ex)
            {
                PrintWarning($"Formatting error for key '{key}': {ex.Message}");
                return format;
            }
        }

        private class Transaction
        {
            public string player_name { get; set; }
            public string product_id_string { get; set; }
            public int product_amount { get; set; }
            public string message { get; set; }
        }

        private class PluginConfig
        {
            [JsonProperty("store-secret-key")]
            public string StoreSecretKey { get; set; } = "your-api-key-here";

            [JsonProperty("claim-command")]
            public string ClaimCommand { get; set; } = "tgclaim";

            [JsonProperty("secret-command")]
            public string SecretCommand { get; set; } = "tgsecret";
        }

    }
}