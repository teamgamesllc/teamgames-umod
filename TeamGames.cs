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
  "CommandNameEmpty": "New command name cannot be empty.",
  "InvalidCommandNameFormat": "New command name '{0}' contains invalid characters. Only alphanumeric characters and underscores are allowed."
}
*/

using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries;
using Newtonsoft.Json;
using System.Collections.Generic;
using System;
using UnityEngine;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("TeamGames Store", "TeamGames", "1.2.0")]
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
                if (config == null) throw new Exception("Config is null");
            }
            catch
            {
                PrintWarning("Config file is corrupt or invalid, creating a new one.");
                LoadDefaultConfig();
            }

            SaveConfig(); // Save to ensure any new fields from default config are written

            apiKey = config.StoreSecretKey;
            claimCommand = config.ClaimCommand;
            secretCommand = config.SecretCommand;
        }

        protected override void LoadDefaultConfig()
        {
            config = new PluginConfig();
        }


        private void Init()
        {
            if (config == null) LoadConfig(); 

            apiKey = config.StoreSecretKey; 
            claimCommand = config.ClaimCommand;
            secretCommand = config.SecretCommand;

            headers = new Dictionary<string, string>
            {
                ["X-API-Key"] = apiKey,
                ["Content-Type"] = "application/json"
            };

            if (apiKey == "your-api-key-here" || string.IsNullOrWhiteSpace(apiKey))
            {
                PrintWarning("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                PrintWarning("IMPORTANT: TeamGames API Key is not configured or is set to the default placeholder!");
                PrintWarning($"Please set a valid API key using the RCON command: {secretCommand} <your-api-key-here>");
                PrintWarning($"Or in-game if you have 'teamgames.admin' permission: /{secretCommand} <your-api-key-here>");
                PrintWarning("The plugin will not function correctly until a valid API key is set.");
                PrintWarning("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            }


            AddCovalenceCommand(claimCommand, nameof(ClaimCommand));
            AddCovalenceCommand(secretCommand, nameof(SetSecretCommand));
            // tgsetcmd is handled by [ChatCommand] attribute, but AddCovalenceCommand can also be used if you want to ensure it's registered via Covalence explicitly.
            // If using [ChatCommand], AddCovalenceCommand for "tgsetcmd" might be redundant or could cause issues if not handled carefully.
            // For simplicity, I'll assume [ChatCommand] is sufficient for "tgsetcmd".
            // If you want "tgsetcmd" to also be a Covalence command for console use through Covalence, you can add:
            // AddCovalenceCommand("tgsetcmd", nameof(SetCommandName));
            // However, the method `SetCommandName` is already decorated with `[ChatCommand("tgsetcmd")]`.
            // Oxide's `AddCovalenceCommand` helper internally uses `Covalence.RegisterCommand`.
            // The `[ChatCommand]` attribute registers the command with the game's chat system.
            // If a command is registered via `AddCovalenceCommand`, it's available via Covalence (console, other plugins).
            // If it's `[ChatCommand]`, it's available in chat. Often they overlap.
            // Let's keep tgsetcmd as a [ChatCommand] for now, as its dynamic registration is handled by the attribute.
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ApiOffline"] = "API Services are currently offline. Please check back shortly.",
                ["CommandReserved"] = "Command Reserved for the permission group teamgames.admin",
                ["SecretUsage"] = "Usage: /{0} <secret>",
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
                ["CommandNameEmpty"] = "New command name cannot be empty.",
                ["InvalidCommandNameFormat"] = "New command name '{0}' contains invalid characters. Only alphanumeric characters and underscores are allowed."
            }, this);
        }

        private void ClaimCommand(IPlayer player, string command, string[] args)
        {
            var basePlayer = player.Object as BasePlayer;
            if (basePlayer == null && player.IsServer) 
            {
                PrintWarning("Claim command called by RCON/Server. This command is intended for players.");
                player.Reply("This command is intended for in-game players.");
                return;
            }
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

            if (apiKey == "your-api-key-here" || string.IsNullOrWhiteSpace(apiKey))
            {
                player.Reply(Lang("ApiOffline", player.Id)); 
                PrintWarning($"Claim attempt by {player.Name} while API key is not configured.");
                return;
            }

            var postData = new Dictionary<string, string> { ["playerName"] = basePlayer.UserIDString }; 
            string jsonData = JsonConvert.SerializeObject(postData);

            webrequest.Enqueue(ApiUrl, jsonData, (code, response) => HandleWebResponse(basePlayer, code, response), this, RequestMethod.POST, headers);
        }

        private void SetSecretCommand(IPlayer player, string command, string[] args)
        {
            var basePlayer = player.Object as BasePlayer; 
            bool isRcon = player.IsServer; 

            bool hasPermission = false;
            if (!isRcon && basePlayer != null) 
            {
                hasPermission = permission.UserHasPermission(player.Id, "teamgames.admin");
            }
             else if (isRcon) 
            {
                hasPermission = true;
            }


            if (!hasPermission) 
            {
                player.Reply(Lang("CommandReserved", player.Id));
                return;
            }

            if (args.Length != 1)
            {
                player.Reply(Lang("SecretUsage", player.Id, secretCommand)); 
                return;
            }

            string newApiKey = args[0];
            if (string.IsNullOrWhiteSpace(newApiKey))
            {
                player.Reply("API Key cannot be empty or whitespace.");
                return;
            }
            if (newApiKey == "your-api-key-here")
            {
                player.Reply("Please provide a valid API key, not the default placeholder.");
                return;
            }


            apiKey = newApiKey;
            config.StoreSecretKey = apiKey;
            SaveConfig();

            if (headers != null)
            {
                headers["X-API-Key"] = apiKey;
            }
            else
            {
                 headers = new Dictionary<string, string>
                {
                    ["X-API-Key"] = apiKey,
                    ["Content-Type"] = "application/json"
                };
            }


            player.Reply(Lang("SecretUpdated", player.Id));
            PrintWarning($"Store secret key has been updated by {player.Name ?? "RCON"}.");
        }

        [ChatCommand("tgsetcmd")]
        private void SetCommandName(IPlayer player, string command, string[] args)
        {
             bool hasPermission = false;
            if (player.IsServer) 
            {
                hasPermission = true;
            }
            else 
            {
                hasPermission = permission.UserHasPermission(player.Id, "teamgames.admin");
            }

            if (!hasPermission)
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
            string newNameInput = args[1];

            if (string.IsNullOrWhiteSpace(newNameInput))
            {
                player.Reply(Lang("CommandNameEmpty", player.Id));
                return;
            }

            foreach (char c in newNameInput)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    player.Reply(Lang("InvalidCommandNameFormat", player.Id, newNameInput));
                    return;
                }
            }
            
            string newName = newNameInput.ToLower(); 
            string oldCommandName = "";

            switch (cmdType)
            {
                case "claim":
                    oldCommandName = config.ClaimCommand;
                    if (!string.IsNullOrEmpty(oldCommandName) && oldCommandName != newName)
                        Covalence.CommandSystem.UnregisterCommand(oldCommandName, this);
                    
                    config.ClaimCommand = newName;
                    claimCommand = newName; 
                    AddCovalenceCommand(newName, nameof(ClaimCommand));
                    break;
                case "secret":
                    oldCommandName = config.SecretCommand;
                     if (!string.IsNullOrEmpty(oldCommandName) && oldCommandName != newName)
                        Covalence.CommandSystem.UnregisterCommand(oldCommandName, this);
                    
                    config.SecretCommand = newName;
                    secretCommand = newName; 
                    AddCovalenceCommand(newName, nameof(SetSecretCommand));
                    break;
                default:
                    player.Reply(Lang("InvalidCommandType", player.Id));
                    return;
            }
            SaveConfig();
            player.Reply(Lang("CommandUpdated", player.Id, cmdType, newName));
            PrintWarning($"{cmdType} command has been updated to /{newName} by {player.Name ?? "RCON"}. Old: /{oldCommandName}");
        }

        private void HandleWebResponse(BasePlayer player, int code, string response)
        {
            if (player == null || !player.IsConnected) return; 

            if (string.IsNullOrEmpty(response) || code != 200)
            {
                PrintWarning($"Failed to fetch transactions for {player.displayName} ({player.UserIDString}): {response ?? "No response"} (Code: {code})");
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
                    PrintWarning($"No transactions found or null deserialization for {player.displayName}. Response: {response}");
                    player.ChatMessage(Lang("ErrorProcessing", player.UserIDString));
                }
            }
            catch (JsonException ex)
            {
                PrintWarning($"Error parsing JSON response for {player.displayName}: {ex.Message}. Response: {response}");
                player.ChatMessage(Lang("ErrorProcessing", player.UserIDString));
            }
        }

        private void ProcessTransactions(BasePlayer player, Transaction[] transactions)
        {
            if (transactions.Length == 0) 
            {
                return;
            }

            if (transactions.Length == 1 && !string.IsNullOrEmpty(transactions[0].message) && string.IsNullOrEmpty(transactions[0].product_id_string))
            {
                player.ChatMessage(Lang("TeamGamesMessage", player.UserIDString, transactions[0].message));
                return;
            }

            foreach (var transaction in transactions)
            {
                if (transaction == null)
                {
                    player.ChatMessage(Lang("NullTransaction", player.UserIDString));
                    PrintWarning($"Encountered a null transaction object for player {player.displayName}.");
                    continue;
                }

                if (!string.IsNullOrEmpty(transaction.message) && string.IsNullOrEmpty(transaction.product_id_string))
                {
                     player.ChatMessage(Lang("TeamGamesMessage", player.UserIDString, transaction.message));
                     continue;
                }
                
                if (string.IsNullOrEmpty(transaction.product_id_string))
                {
                    PrintWarning($"Transaction for {player.displayName} missing product_id_string. Data: {JsonConvert.SerializeObject(transaction)}");
                    if (!string.IsNullOrEmpty(transaction.message)) 
                         player.ChatMessage(Lang("TeamGamesMessage", player.UserIDString, transaction.message));
                    else 
                         player.ChatMessage(Lang("ErrorProcessing", player.UserIDString));
                    continue;
                }


                if (transaction.product_amount < 1)
                {
                    player.ChatMessage(Lang("InvalidAmount", player.UserIDString, transaction.product_amount));
                    PrintWarning($"Invalid product amount {transaction.product_amount} for item {transaction.product_id_string} for player {player.displayName}.");
                    continue;
                }

                string itemName = ParseItemName(transaction.product_id_string);
                if (string.IsNullOrEmpty(itemName))
                {
                    player.ChatMessage(Lang("ItemNotFound", player.UserIDString, transaction.product_id_string)); 
                    PrintWarning($"Could not parse item name from product_id_string: '{transaction.product_id_string}' for player {player.displayName}.");
                    continue;
                }

                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(itemName);
                if (itemDefinition == null)
                {
                    player.ChatMessage(Lang("ItemNotFound", player.UserIDString, itemName));
                    PrintWarning($"ItemDefinition not found for '{itemName}' (from '{transaction.product_id_string}') for player {player.displayName}.");
                    continue;
                }

                Puts(Lang("CreatingItem", player.UserIDString, transaction.product_amount, itemDefinition.shortname)); 
                Item item = ItemManager.Create(itemDefinition, transaction.product_amount, 0); 
                if (item != null)
                {
                    bool given = player.inventory.GiveItem(item);
                    if (!given)
                    {
                        item.Drop(player.GetDropPosition(), player.GetDropVelocity());
                        player.ChatMessage(Lang("ItemDropped", player.UserIDString, transaction.product_amount, itemDefinition.displayName.english, player.displayName));
                        Puts($"Dropped {transaction.product_amount} of {itemDefinition.shortname} for {player.displayName} as inventory was full.");
                    }
                    else
                    {
                        player.ChatMessage(Lang("ItemGiven", player.UserIDString, transaction.product_amount, itemDefinition.displayName.english, player.displayName));
                        Puts($"Gave {transaction.product_amount} of {itemDefinition.shortname} to {player.displayName}.");
                    }
                }
                else
                {
                    player.ChatMessage(Lang("FailedToCreate", player.UserIDString, itemName));
                    PrintWarning($"Failed to create item '{itemName}' (amount: {transaction.product_amount}) for player {player.displayName}.");
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
            string potentialItemName = parts.Length > 1 ? parts[1] : parts[0];

            if (string.IsNullOrWhiteSpace(potentialItemName)) return null; 

            return potentialItemName.Trim(); 
        }

        private string Lang(string key, string id = null, params object[] args)
        {
            string message = lang.GetMessage(key, this, id);
            if (args == null || args.Length == 0) return message;
            try
            {
                return string.Format(message, args);
            }
            catch (FormatException ex)
            {
                PrintWarning($"Lang formatting error for key '{key}' with {args.Length} args. Message: '{message}'. Error: {ex.Message}");
                return message; 
            }
        }
        
        protected override void SaveConfig() 
        {
            Config.WriteObject(config, true);
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