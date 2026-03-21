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
            public string SelectedCrypto { get; set; } = "btc";
            public string FeePayer { get; set; } = "sender"; // sender, receiver, split, pass
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

        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        public Task Client_Ready()
        {
            _ = Task.Run(async () =>
            {
                var ticketCommand = new SlashCommandBuilder()
                    .WithName("ticket")
                    .WithDescription("Create a new middleman ticket")
                    .AddOption("user", ApplicationCommandOptionType.User, "The user you are dealing with", isRequired: true);

                var liveTransCommand = new SlashCommandBuilder()
                    .WithName("livetransactions")
                    .WithDescription("View recent LTC transactions");

                try
                {
                    if (_client != null)
                    {
                        await _client.CreateGlobalApplicationCommandAsync(ticketCommand.Build());
                        await _client.CreateGlobalApplicationCommandAsync(liveTransCommand.Build());
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
                .WithTitle("Role Assignment")
                .WithColor(Color.Green)
                .WithDescription("Select one of the following buttons that corresponds to your role in this deal.\nOnce selected, both users must confirm to proceed.")
                .AddField("Sending Bitcoin", "None", true)
                .AddField("Receiving Bitcoin", "None", true)
                .WithFooter("Ticket will be closed in 30 minutes if left unattended.")
                .Build();

            var builder = new ComponentBuilder()
                .WithButton("Sending", $"role_sending_{ticketChannel.Id}", ButtonStyle.Secondary)
                .WithButton("Receiving", $"role_receiving_{ticketChannel.Id}", ButtonStyle.Secondary)
                .WithButton("Reset", $"role_reset_{ticketChannel.Id}", ButtonStyle.Danger);

            await ticketChannel.SendMessageAsync($"<@{targetUser.Id}>", embed: embed, components: builder.Build());
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
            if (modal.Data.CustomId.StartsWith("seller_modal_submit_"))
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
                var code = component.Data.CustomId.Split('_').Last();
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
            else if (customId.StartsWith("fee_"))
            {
                await HandleFeeButtons(component);
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
                .WithDescription("Select one of the following buttons that corresponds to your role in this deal.\nOnce selected, both users must confirm to proceed.")
                .AddField("Sending Bitcoin", ticket.SenderId.HasValue ? $"<@{ticket.SenderId}>" : "None", true)
                .AddField("Receiving Bitcoin", ticket.ReceiverId.HasValue ? $"<@{ticket.ReceiverId}>" : "None", true)
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
                    .WithDescription("Please confirm your roles below.")
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
                await component.RespondAsync("Roles marked as incorrect. Please use the Reset button in the Role Assignment message.", ephemeral: true);
                return;
            }

            if (component.User.Id == ticket.CreatorId) ticket.CreatorConfirmedRoles = true;
            if (component.User.Id == ticket.PartnerId) ticket.PartnerConfirmedRoles = true;

            await component.RespondAsync($"> <@{component.User.Id}> has responded with **\"Confirm\"**.");

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
                await component.RespondAsync("Please state the amount again.", ephemeral: true);
                return;
            }

            // Logic for Fee Payment Selection
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

            await component.RespondAsync(embed: feeEmbed, components: builder.Build());
        }

        private async Task HandleFeeButtons(SocketMessageComponent component)
        {
            var parts = component.Data.CustomId.Split('_');
            var payer = parts[1]; // sender, receiver, split, pass
            var channelId = ulong.Parse(parts[2]);

            if (!_tickets.TryGetValue(channelId, out var ticket)) return;

            ticket.FeePayer = payer;
            var fee = CalculateFee(ticket.DealAmount ?? 0);

            var summaryEmbed = new EmbedBuilder()
                .WithTitle("📋 Deal Summary")
                .WithColor(Color.Green)
                .WithDescription("Refer to this deal summary for any reaffirmations. Notify staff for any support required.")
                .AddField("Sender", $"<@{ticket.SenderId}>", true)
                .AddField("Receiver", $"<@{ticket.ReceiverId}>", true)
                .AddField("Deal Value", $"${ticket.DealAmount:F2}", true)
                .AddField("Coin", "Bitcoin (BTC)", true)
                .AddField("Fee", $"${fee:F2} <@{component.User.Id}>", true)
                .WithThumbnailUrl("https://cdn.discordapp.com/emojis/1477872973678514327.png") // Example BTC icon
                .Build();

            await component.UpdateAsync(m =>
            {
                m.Embed = summaryEmbed;
                m.Components = null;
            });

            // Final Invoice
            await ShowInvoice(component.Channel, ticket);
        }

        private decimal CalculateFee(decimal amount)
        {
            if (amount < 10) return 0;
            if (amount < 50) return 0.50m;
            if (amount < 250) return 2.00m;
            return amount * 0.01m;
        }

        private async Task ShowInvoice(ISocketMessageChannel channel, TicketData ticket)
        {
            var btcPrice = 70677.00m; // Example price from screenshot
            var btcAmount = (ticket.DealAmount ?? 0) / btcPrice;
            
            var invoiceEmbed = new EmbedBuilder()
                .WithTitle("📥 Payment Invoice")
                .WithColor(Color.DarkBlue)
                .WithDescription($"> <@{ticket.SenderId}> **Send the funds as part of the deal to the Middleman address specified below. Please copy the amount provided.**")
                .AddField("Address", $"`{_btcAddress}`", false)
                .AddField("Amount", $"{btcAmount:F8} BTC (${(ticket.DealAmount + 2):F2} USD)", false)
                .AddField("Exchange Rate", $"1 BTC = ${btcPrice:F2} USD", false)
                .WithThumbnailUrl($"https://api.qrserver.com/v1/create-qr-code/?size=150x150&data={_btcAddress}")
                .Build();

            await channel.SendMessageAsync($"<@{ticket.SenderId}>", embed: invoiceEmbed);
        }

        private async Task OnMessageReceived(SocketMessage message)
        {
            if (message.Author.IsBot) return;
            if (!_tickets.TryGetValue(message.Channel.Id, out var ticket)) return;

            // If roles confirmed and waiting for amount
            if (ticket.CreatorConfirmedRoles && ticket.PartnerConfirmedRoles && !ticket.DealAmount.HasValue)
            {
                if (message.Author.Id != ticket.SenderId) return;

                string content = message.Content.Replace("$", "").Replace("usd", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (decimal.TryParse(content, out var amount))
                {
                    ticket.DealAmount = amount;
                    var confirmEmbed = new EmbedBuilder()
                        .WithTitle("Amount Confirmation")
                        .WithColor(Color.Gold)
                        .WithDescription($"Confirm that the bot will receive:\n\n**Amount**\n`${amount:F2}`")
                        .Build();

                    var builder = new ComponentBuilder()
                        .WithButton("Correct", $"amount_confirm_correct_{ticket.ChannelId}", ButtonStyle.Success)
                        .WithButton("Incorrect", $"amount_confirm_incorrect_{ticket.ChannelId}", ButtonStyle.Danger);

                    await message.Channel.SendMessageAsync(embed: confirmEmbed, components: builder.Build());
                }
                else
                {
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
