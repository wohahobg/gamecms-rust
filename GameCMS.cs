using Newtonsoft.Json;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins;

[Info("GameCMS", "GameCMS.ORG", "0.1.1")]
[Description("Link your game server with the GameCMS.ORG API.")]
class GameCMS : CovalencePlugin
{

    private string messagePrefix = "[GameCMS] ";
    private DateTime lastSent = DateTime.Now;

    private void Init()
    {
        timer.Every(60, TryToFetchCommands);
    }

    protected override void LoadDefaultConfig()
    {
        Log("Creating a new configuration file.");
        Config["ServerApiToken"] = "<Your Server API Token Here>";
    }


    [Command("gamecms.force"), Permission("gamecms.server.force")]
    private void ForceCommand(IPlayer player, string command, string[] args)
    {
        try
        {
            player.Reply(messagePrefix + "Forcing command execution...");
            FetchCommands(res => {
                DispatchCommands(res.Data);
            }, code => Log("Unable to fetch data from GameCMS.ORG API (code {0})", code));
        }
        catch (Exception ex)
        {
            player.Reply(messagePrefix + $"Error executing command: {ex.Message}");
            Log(messagePrefix + $"Error in ForceCommand: {ex}");
        }
    }

    [Command("gamecms.server.apikey"), Permission("gamecms.server.apikey")]
    private void SetApiKeyCommand(IPlayer player, string command, string[] args)
    {
        if (args.Length != 1)
        {
            player.Reply(messagePrefix + "Usage: gamecms.server.apikey <API_KEY>");
            return;
        }

        Config["ServerApiToken"] = args[0];
        SaveConfig();

        player.Reply(messagePrefix + "Server API Key updated successfully.");
    }

    private void TryToFetchCommands()
    {
        var now = DateTime.Now;

        if ((now - lastSent).TotalSeconds < 60)
        {
            return;
        }

        lastSent = now;

        FetchCommands(res => {
            DispatchCommands(res.Data);
        }, code => LogError("Unable to fetch data from GameCMS.ORG API (code {0})", code));
    }

    private void FetchCommands(Action<GameCMSApiResponse> callback, Action<int> errorHandler)
    {
        var url = "https://api.gamecms.org/v2/commands/queue/rust";
#if !CARBON
        webrequest.Enqueue(url, null, (code, response) =>
        {
            if (code != 200)
            {
                errorHandler(code);
                return;
            }
            callback(JsonConvert.DeserializeObject<GameCMSApiResponse>(response));
        }, this, RequestMethod.GET, GetRequestHeaders());
#else
            webrequest.Enqueue(url, null, (code, response) => {
              callback(JsonConvert.DeserializeObject < GameCMSApiResponse > (response));
            }, this, RequestMethod.GET, GetRequestHeaders(), 0f, onException: (responseCode, responseObject, responseException) => {
              errorHandler(responseCode);
            });
#endif
    }

    private void DispatchCommands(List<CommandData> commands)
    {
        List<int> executedCommandIds = new List<int>();
        foreach (var commandData in commands)
        {
#if CARBON
            var player = BasePlayer.Find(commandData.SteamId.);
#else
            var player = players.FindPlayerById(commandData.SteamId.ToString());
#endif

            if (player == null) continue;
            if (commandData.MustBeOnline && !player.IsConnected) continue;

            foreach (var command in commandData.Commands)
            {
                server.Command(command);
            }
            executedCommandIds.Add(commandData.Id);
        }
        if (executedCommandIds.Count > 0)
        {
            MarkCommandsAsCompleted(executedCommandIds);
        }
        Log("Fetched {0} commands.", executedCommandIds.Count);
    }

    private void MarkCommandsAsCompleted(List<int> commandIds)
    {
        var url = "https://api.gamecms.org/v2/commands/complete";
        var requestBody = $"ids={Uri.EscapeDataString(JsonConvert.SerializeObject(commandIds))}";
        webrequest.Enqueue(url, requestBody, (code, response) =>
        {

        }, this, RequestMethod.POST, GetFormRequestHeaders());
    }

    private Dictionary<string, string> GetFormRequestHeaders()
    {
        var ServerApiToken = (string)Config["ServerApiToken"];

        return new Dictionary<string, string>
        {
            { "Authorization", $"Bearer {ServerApiToken}" },
            { "Content-Type", "application/x-www-form-urlencoded" }
        };
    }

    private Dictionary<string, string> GetRequestHeaders()
    {
        var ServerApiToken = (string)Config["ServerApiToken"];

        return new Dictionary<string, string>
        {
            { "Authorization", $"Bearer {ServerApiToken}" },
            { "Accept", "application/json" },
            { "Content-type", "application/json" }
        };
    }
}

class GameCMSApiResponse
{
    [JsonProperty("status")] public int Status { get; set; }
    [JsonProperty("data")] public List<CommandData> Data { get; set; }
}

class CommandData
{
    [JsonProperty("id")] public int Id { get; set; }
    [JsonProperty("username")] public string Username { get; set; }
    [JsonProperty("must_be_online")] public bool MustBeOnline { get; set; }
    [JsonProperty("steam_id")] public string SteamId { get; set; }
    [JsonProperty("commands")] public List<string> Commands { get; set; }
}