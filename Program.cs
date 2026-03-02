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
        private DiscordSocketClient _client;
        private readonly Dictionary<ulong, string> _pendingCodes = new();
        private readonly Dictionary<ulong, bool> _authenticatedUsers = new();

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
                .WithDescription("Register and get your unique 6-digit code");

            var loginCommand = new SlashCommandBuilder()
                .WithName("login")
                .WithDescription("Login using your 6-digit code")
                .AddOption("code", ApplicationCommandOptionType.String, "Your 6-digit registration code", isRequired: true);

            var cryptoCommand = new SlashCommandBuilder()
                .WithName("crypto")
                .WithDescription("Show cryptocurrency deal options");

            try
            {
                await _client.CreateGlobalApplicationCommandAsync(registerCommand.Build());
                await _client.CreateGlobalApplicationCommandAsync(loginCommand.Build());
                await _client.CreateGlobalApplicationCommandAsync(cryptoCommand.Build());
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
                case "crypto":
                    await HandleCryptoCommand(command);
                    break;
            }
        }

        private async Task HandleRegisterCommand(SocketSlashCommand command)
        {
            var random = new Random();
            string code;
            do
            {
                code = random.Next(100000, 999999).ToString();
            } while (_pendingCodes.ContainsValue(code));

            _pendingCodes[command.User.Id] = code;

            var embed = new EmbedBuilder()
                .WithTitle("Registration Successful")
                .WithColor(Color.Green)
                .WithDescription($"Your unique 6-digit registration code is: **{code}**\nUse `/login code:{code}` to authenticate.")
                .AddField("Fees:", 
                    "Deals $250+: 1%\n" +
                    "Deals under $250: $2\n" +
                    "Deals under $50: $0.50\n" +
                    "**Deals under $10 are FREE**\n" +
                    "USDT & USDC has $1 surcharge")
                .WithFooter(footer => footer.Text = "Press the dropdown below to select & initiate a deal")
                .Build();

            var menuBuilder = new SelectMenuBuilder()
                .WithPlaceholder("Make a selection")
                .WithCustomId("crypto_selection")
                .AddOption("Bitcoin", "btc", "Initiate a Bitcoin deal", new Emoji("₿"))
                .AddOption("Ethereum", "eth", "Initiate an Ethereum deal", new Emoji("🛡️"))
                .AddOption("Litecoin", "ltc", "Initiate a Litecoin deal", new Emoji("Ł"))
                .AddOption("Solana", "sol", "Initiate a Solana deal", new Emoji("⚛️"))
                .AddOption("USDT [ERC-20]", "usdt_erc", "Initiate a USDT [ERC-20] deal", new Emoji("₮"))
                .AddOption("USDC [ERC-20]", "usdc_erc", "Initiate a USDC [ERC-20] deal", new Emoji("🪙"))
                .AddOption("USDT [SOL]", "usdt_sol", "Initiate a USDT [SOL] deal", new Emoji("₮"))
                .AddOption("USDC [SOL]", "usdc_sol", "Initiate a USDC [SOL] deal", new Emoji("🪙"));

            var builder = new ComponentBuilder()
                .WithSelectMenu(menuBuilder);

            await command.RespondAsync(embed: embed, components: builder.Build(), ephemeral: true);
        }

        private async Task HandleLoginCommand(SocketSlashCommand command)
        {
            var code = (string)command.Data.Options.First().Value;

            if (_pendingCodes.TryGetValue(command.User.Id, out var savedCode) && savedCode == code)
            {
                _authenticatedUsers[command.User.Id] = true;
                await command.RespondAsync("Login successful! You can now use the crypto features.", ephemeral: true);
            }
            else
            {
                await command.RespondAsync("Invalid code. Please use `/register` to get a new code.", ephemeral: true);
            }
        }

        private async Task HandleCryptoCommand(SocketSlashCommand command)
        {
            if (!_authenticatedUsers.ContainsKey(command.User.Id))
            {
                await command.RespondAsync("You must be logged in to use this. Use `/login` first.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("Cryptocurrency")
                .WithColor(Color.Green)
                .AddField("Fees:", 
                    "Deals $250+: 1%\n" +
                    "Deals under $250: $2\n" +
                    "Deals under $50: $0.50\n" +
                    "**Deals under $10 are FREE**\n" +
                    "USDT & USDC has $1 surcharge")
                .WithDescription("Press the dropdown below to select & initiate a deal involving: **Bitcoin, Ethereum, Litecoin, Solana, USDT [ERC-20], USDC [ERC-20], USDT [SOL], USDC [SOL]**.")
                .Build();

            var menuBuilder = new SelectMenuBuilder()
                .WithPlaceholder("Make a selection")
                .WithCustomId("crypto_selection")
                .AddOption("Bitcoin", "btc", "Initiate a Bitcoin deal", new Emoji("₿"))
                .AddOption("Ethereum", "eth", "Initiate an Ethereum deal", new Emoji("🛡️"))
                .AddOption("Litecoin", "ltc", "Initiate a Litecoin deal", new Emoji("Ł"))
                .AddOption("Solana", "sol", "Initiate a Solana deal", new Emoji("⚛️"))
                .AddOption("USDT [ERC-20]", "usdt_erc", "Initiate a USDT [ERC-20] deal", new Emoji("₮"))
                .AddOption("USDC [ERC-20]", "usdc_erc", "Initiate a USDC [ERC-20] deal", new Emoji("🪙"))
                .AddOption("USDT [SOL]", "usdt_sol", "Initiate a USDT [SOL] deal", new Emoji("₮"))
                .AddOption("USDC [SOL]", "usdc_sol", "Initiate a USDC [SOL] deal", new Emoji("🪙"));

            var builder = new ComponentBuilder()
                .WithSelectMenu(menuBuilder);

            await command.RespondAsync(embed: embed, components: builder.Build());
        }

        private async Task SelectMenuHandler(SocketMessageComponent component)
        {
            if (component.Data.CustomId == "crypto_selection")
            {
                var selection = component.Data.Values.First();
                await component.RespondAsync($"You selected **{selection.ToUpper()}**. Initiating deal process...", ephemeral: true);
            }
        }
    }
}
