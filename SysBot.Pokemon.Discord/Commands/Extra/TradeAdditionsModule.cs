using Discord;
using Discord.Commands;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Discord.Helpers;
using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;

namespace SysBot.Pokemon.Discord
{
    [Summary("Generates and queues various silly trade additions")]
    public class TradeAdditionsModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        private static TradeQueueInfo<T> Info => SysCord<T>.Runner.Hub.Queues.Info;
        private readonly PokeTradeHub<T> Hub = SysCord<T>.Runner.Hub;
        private readonly ExtraCommandUtil<T> Util = new();

        [Command("raidinfo")]
        [Alias("ri", "rv")]
        [Summary("Displays basic Raid Info of the provided seed.")]
        public async Task RaidSeedInfoAsync(string seedValue, int level, string dlc = "p")
        {
            uint seed;
            try
            {
                seed = uint.Parse(seedValue, NumberStyles.AllowHexSpecifier);
            }
            catch (FormatException)
            {
               await ReplyAsync("Invalid seed format. Please enter a valid seed.");
                return;
            }

            var crystalType = level switch
            {
                >= 1 and <= 5 => (TeraCrystalType)0,
                6 => (TeraCrystalType)1,
                7 => (TeraCrystalType)3,
                8 => (TeraCrystalType)2,
                _ => throw new ArgumentException("Invalid difficulty level.")
            };

            try
            {
                var embed = RotatingRaidBotSV.RaidInfoCommand(seedValue, (int)crystalType, dlc != "p" ? TeraRaidMapParent.Kitakami : TeraRaidMapParent.Paldea);
                await ReplyAsync(embed: embed);
            }
            catch(Exception ex)
            {
                await ReplyAsync(ex.Message);
            }
        }

        [Command("giveawayqueue")]
        [Alias("gaq")]
        [Summary("Prints the users in the giveway queues.")]
        [RequireSudo]
        public async Task GetGiveawayListAsync()
        {
            string msg = Info.GetTradeList(PokeRoutineType.LinkTrade);
            var embed = new EmbedBuilder();
            embed.AddField(x =>
            {
                x.Name = "Pending Giveaways";
                x.Value = msg;
                x.IsInline = false;
            });
            await ReplyAsync("These are the users who are currently waiting:", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("giveawaypool")]
        [Alias("gap")]
        [Summary("Show a list of Pokémon available for giveaway.")]
        [RequireQueueRole(nameof(DiscordManager.RolesGiveaway))]
        public async Task DisplayGiveawayPoolCountAsync()
        {
            var pool = Info.Hub.Ledy.Pool;
            if (pool.Count > 0)
            {
                var test = pool.Files;
                var lines = pool.Files.Select((z, i) => $"{i + 1}: {z.Key} = {(Species)z.Value.RequestInfo.Species}");
                var msg = string.Join("\n", lines);
                await Util.ListUtil(Context, "Giveaway Pool Details", msg).ConfigureAwait(false);
            }
            else await ReplyAsync("Giveaway pool is empty.").ConfigureAwait(false);
        }

        [Command("giveaway")]
        [Alias("ga", "giveme", "gimme")]
        [Summary("Makes the bot trade you the specified giveaway Pokémon.")]
        [RequireQueueRole(nameof(DiscordManager.RolesGiveaway))]
        public async Task GiveawayAsync([Remainder] string content)
        {
            var code = Info.GetRandomTradeCode();
            await GiveawayAsync(code, content).ConfigureAwait(false);
        }

        [Command("giveaway")]
        [Alias("ga", "giveme", "gimme")]
        [Summary("Makes the bot trade you the specified giveaway Pokémon.")]
        [RequireQueueRole(nameof(DiscordManager.RolesGiveaway))]
        public async Task GiveawayAsync([Summary("Giveaway Code")] int code, [Remainder] string content)
        {
            T pk;
            content = ReusableActions.StripCodeBlock(content);
            var pool = Info.Hub.Ledy.Pool;
            if (pool.Count == 0)
            {
                await ReplyAsync("Giveaway pool is empty.").ConfigureAwait(false);
                return;
            }
            else if (content.ToLower() == "random") // Request a random giveaway prize.
                pk = Info.Hub.Ledy.Pool.GetRandomSurprise();
            else if (Info.Hub.Ledy.Distribution.TryGetValue(content, out LedyRequest<T>? val) && val is not null)
                pk = val.RequestInfo;
            else
            {
                await ReplyAsync($"Requested Pokémon not available, use \"{Info.Hub.Config.Discord.CommandPrefix}giveawaypool\" for a full list of available giveaways!").ConfigureAwait(false);
                return;
            }

            var sig = Context.User.GetFavor();
            await QueueHelper<T>.AddToQueueAsync(Context, code, Context.User.Username, sig, pk, PokeRoutineType.LinkTrade, PokeTradeType.Specific).ConfigureAwait(false);
        }

        [Command("fixOT")]
        [Alias("fix", "f")]
        [Summary("Fixes OT and Nickname of a Pokémon you show via Link Trade if an advert is detected.")]
        [RequireQueueRole(nameof(DiscordManager.RolesFixOT))]
        public async Task FixAdOT()
        {
            var code = Info.GetRandomTradeCode();
            var sig = Context.User.GetFavor();
            await QueueHelper<T>.AddToQueueAsync(Context, code, Context.User.Username, sig, new T(), PokeRoutineType.FixOT, PokeTradeType.FixOT).ConfigureAwait(false);
        }

        [Command("fixOT")]
        [Alias("fix", "f")]
        [Summary("Fixes OT and Nickname of a Pokémon you show via Link Trade if an advert is detected.")]
        [RequireQueueRole(nameof(DiscordManager.RolesFixOT))]
        public async Task FixAdOT([Summary("Trade Code")] int code)
        {
            var sig = Context.User.GetFavor();
            await QueueHelper<T>.AddToQueueAsync(Context, code, Context.User.Username, sig, new T(), PokeRoutineType.FixOT, PokeTradeType.FixOT).ConfigureAwait(false);
        }

        [Command("fixOTList")]
        [Alias("fl", "fq")]
        [Summary("Prints the users in the FixOT queue.")]
        [RequireSudo]
        public async Task GetFixListAsync()
        {
            string msg = Info.GetTradeList(PokeRoutineType.FixOT);
            var embed = new EmbedBuilder();
            embed.AddField(x =>
            {
                x.Name = "Pending Trades";
                x.Value = msg;
                x.IsInline = false;
            });
            await ReplyAsync("These are the users who are currently waiting:", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("itemTrade")]
        [Alias("it", "item")]
        [Summary("Makes the bot trade you a Pokémon holding the requested item, or Ditto if stat spread keyword is provided.")]
        [RequireQueueRole(nameof(DiscordManager.RolesSupportTrade))]
        public async Task ItemTrade([Remainder] string item)
        {
            var code = Info.GetRandomTradeCode();
            await ItemTrade(code, item).ConfigureAwait(false);
        }

        [Command("itemTrade")]
        [Alias("it", "item")]
        [Summary("Makes the bot trade you a Pokémon holding the requested item.")]
        [RequireQueueRole(nameof(DiscordManager.RolesSupportTrade))]
        public async Task ItemTrade([Summary("Trade Code")] int code, [Remainder] string item)
        {
            Species species = Info.Hub.Config.Trade.ItemTradeSpecies == Species.None ? Species.Diglett : Info.Hub.Config.Trade.ItemTradeSpecies;
            var set = new ShowdownSet($"{SpeciesName.GetSpeciesNameGeneration((ushort)species, 2, 8)} @ {item.Trim()}");
            var template = AutoLegalityWrapper.GetTemplate(set);
            var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
            var pkm = sav.GetLegal(template, out var result);
            pkm = EntityConverter.ConvertToType(pkm, typeof(T), out _) ?? pkm;
            if (pkm.HeldItem == 0 && !Info.Hub.Config.Trade.Memes)
            {
                await ReplyAsync($"{Context.User.Username}, the item you entered wasn't recognized.").ConfigureAwait(false);
                return;
            }

            var la = new LegalityAnalysis(pkm);
            if (Info.Hub.Config.Trade.Memes && await TrollAsync(Context, pkm is not T || !la.Valid, pkm, true).ConfigureAwait(false))
                return;

            if (pkm is not T pk || !la.Valid)
            {
                var reason = result == "Timeout" ? "That set took too long to generate." : "I wasn't able to create something from that.";
                var imsg = $"Oops! {reason} Here's my best attempt for that {species}!";
                await Context.Channel.SendPKMAsync(pkm, imsg).ConfigureAwait(false);
                return;
            }
            pk.ResetPartyStats();

            var sig = Context.User.GetFavor();
            await QueueHelper<T>.AddToQueueAsync(Context, code, Context.User.Username, sig, pk, PokeRoutineType.LinkTrade, PokeTradeType.SupportTrade).ConfigureAwait(false);
        }

        [Command("dittoTrade")]
        [Alias("dt", "ditto")]
        [Summary("Makes the bot trade you a Ditto with a requested stat spread and language.")]
        [RequireQueueRole(nameof(DiscordManager.RolesSupportTrade))]
        public async Task DittoTrade([Summary("A combination of \"ATK/SPA/SPE\" or \"6IV\"")] string keyword, [Summary("Language")] string language, [Summary("Nature")] string nature)
        {
            var code = Info.GetRandomTradeCode();
            await DittoTrade(code, keyword, language, nature).ConfigureAwait(false);
        }

        [Command("dittoTrade")]
        [Alias("dt", "ditto")]
        [Summary("Makes the bot trade you a Ditto with a requested stat spread and language.")]
        [RequireQueueRole(nameof(DiscordManager.RolesSupportTrade))]
        public async Task DittoTrade([Summary("Trade Code")] int code, [Summary("A combination of \"ATK/SPA/SPE\" or \"6IV\"")] string keyword, [Summary("Language")] string language, [Summary("Nature")] string nature)
        {
            keyword = keyword.ToLower().Trim();
            if (Enum.TryParse(language, true, out LanguageID lang))
                language = lang.ToString();
            else
            {
                await Context.Message.ReplyAsync($"Couldn't recognize language: {language}.").ConfigureAwait(false);
                return;
            }
            nature = nature.Trim()[..1].ToUpper() + nature.Trim()[1..].ToLower();
            var set = new ShowdownSet($"{keyword}(Ditto)\nLanguage: {language}\nNature: {nature}");
            var template = AutoLegalityWrapper.GetTemplate(set);
            var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
            var pkm = sav.GetLegal(template, out var result);
            TradeExtensions<T>.DittoTrade((T)pkm);

            var la = new LegalityAnalysis(pkm);
            if (Info.Hub.Config.Trade.Memes && await TrollAsync(Context, pkm is not T || !la.Valid, pkm).ConfigureAwait(false))
                return;

            if (pkm is not T pk || !la.Valid)
            {
                var reason = result == "Timeout" ? "That set took too long to generate." : "I wasn't able to create something from that.";
                var imsg = $"Oops! {reason} Here's my best attempt for that Ditto!";
                await Context.Channel.SendPKMAsync(pkm, imsg).ConfigureAwait(false);
                return;
            }

            pk.ResetPartyStats();
            var sig = Context.User.GetFavor();
            await QueueHelper<T>.AddToQueueAsync(Context, code, Context.User.Username, sig, pk, PokeRoutineType.LinkTrade, PokeTradeType.SupportTrade).ConfigureAwait(false);
        }

        [Command("peek")]
        [Summary("Take and send a screenshot from the specified Switch.")]
        [RequireOwner]
        public async Task Peek(string address)
        {
            var source = new CancellationTokenSource();
            var token = source.Token;

            var bot = SysCord<T>.Runner.GetBot(address);
            if (bot == null)
            {
                await ReplyAsync($"No bot found with the specified address ({address}).").ConfigureAwait(false);
                return;
            }

            var c = bot.Bot.Connection;
            var bytes = await c.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
            if (bytes.Length == 1)
            {
                await ReplyAsync($"Failed to take a screenshot for bot at {address}. Is the bot connected?").ConfigureAwait(false);
                return;
            }
            MemoryStream ms = new(bytes);

            var img = "cap.jpg";
            var embed = new EmbedBuilder { ImageUrl = $"attachment://{img}", Color = Color.Purple }.WithFooter(new EmbedFooterBuilder { Text = $"Captured image from bot at address {address}." });
            await Context.Channel.SendFileAsync(ms, img, "", false, embed: embed.Build());
        }

        public static async Task<bool> TrollAsync(SocketCommandContext context, bool invalid, PKM pkm, bool itemTrade = false)
        {
            var rng = new Random();
            bool noItem = pkm.HeldItem == 0 && itemTrade;
            var path = Info.Hub.Config.Trade.MemeFileNames.Split(',');
            if (Info.Hub.Config.Trade.MemeFileNames == "" || path.Length == 0)
                path = new string[] { "https://i.imgur.com/qaCwr09.png" }; //If memes enabled but none provided, use a default one.

            if (invalid || !ItemRestrictions.IsHeldItemAllowed(pkm) || noItem || (pkm.Nickname.ToLower() == "egg" && !Breeding.CanHatchAsEgg(pkm.Species)))
            {
                var msg = $"{(noItem ? $"{context.User.Username}, the item you entered wasn't recognized." : $"Oops! I wasn't able to create that {GameInfo.Strings.Species[pkm.Species]}.")} Here's a meme instead!\n";
                await context.Channel.SendMessageAsync($"{(invalid || noItem ? msg : "")}{path[rng.Next(path.Length)]}").ConfigureAwait(false);
                return true;
            }
            return false;
        }

        // NotTrade Additions       
        [Command("repeek")]
        [Summary("Take and send a screenshot from the specified Switch.")]
        [RequireOwner]
        public async Task RePeek(string address)
        {
            var source = new CancellationTokenSource();
            var token = source.Token;

            var bot = SysCord<T>.Runner.GetBot(address);
            if (bot == null)
            {
                await ReplyAsync($"No bot found with the specified address ({address}).").ConfigureAwait(false);
                return;
            }

            var c = bot.Bot.Connection;
            c.Reset();
            var bytes = Task.Run(async () => await c.PixelPeek(token).ConfigureAwait(false)).Result ?? Array.Empty<byte>();
            MemoryStream ms = new(bytes);
            var img = "cap.jpg";
            var embed = new EmbedBuilder { ImageUrl = $"attachment://{img}", Color = Color.Purple }.WithFooter(new EmbedFooterBuilder { Text = $"Captured image from bot at address {address}." });
            await Context.Channel.SendFileAsync(ms, img, "", false, embed: embed.Build());
        }

        [Command("setCatchLimit")]
        [Alias("scl")]
        [Summary("Set the Catch Limit for Raids in SV.")]
        [RequireSudo]
        public async Task SetOffsetIncrement([Summary("Set the Catch Limit for Raids in SV.")] int limit)
        {
            int parse = SysCord<T>.Runner.Hub.Config.RaidSV.CatchLimit = limit;

            var msg = $"{Context.User.Mention} Catch Limit for Raids has been set to {parse}.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        [Command("clearRaidSVBans")]
        [Alias("crb")]
        [Summary("Clears the RaidSV ban list.")]
        [RequireSudo]
        public async Task ClearRaidBansSV()
        {
            SysCord<T>.Runner.Hub.Config.RaidSV.RaiderBanList.Clear();
            var msg = "RaidSV ban list has been cleared.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        [Command("addRaidParams")]
        [Alias("arp")]
        [Summary("Adds new raid parameter.")]
        [RequireSudo]
        public async Task AddNewRaidParam([Summary("Seed")] string seed, [Summary("Difficulty Level (1-8)")] int level)
        {
            // Validate the seed for hexadecimal format
            if (seed.Length != 8 || !seed.All(c => "0123456789abcdefABCDEF".Contains(c)))
            {
                await ReplyAsync("Invalid seed format. Please enter a seed consisting of exactly 8 hexadecimal digits.").ConfigureAwait(false);
                return;
            }
            if (level < 1 || level > 8)
            {
                await ReplyAsync("Invalid raid level. Please enter a level between 1 and 8.").ConfigureAwait(false);
                return;
            }

            if (!Hub.Config.RotatingRaidSV.EventActive && (level == 7 || level == 8))
            {
                await ReplyAsync("Invalid Raid level. No active Events.").ConfigureAwait(false);
                return;
            }

            // Determine the CrystalType based on the given difficulty level
            var crystalType = level switch
            {
                >= 1 and <= 5 => (TeraCrystalType)0,
                6 => (TeraCrystalType)1,
                7 => (TeraCrystalType)3,
                8 => (TeraCrystalType)2,
                _ => throw new ArgumentException("Invalid difficulty level.")
            };

            // Determine the correct map
            var selectedMap = RotatingRaidBotSV.IsKitakami ? TeraRaidMapParent.Kitakami : TeraRaidMapParent.Paldea;
            var raidEmbed = RotatingRaidBotSV.RaidInfoCommand(seed, (int)crystalType, selectedMap);  // Adjusted to use the selected map
            var species = raidEmbed.Author.Value.Name.Split(" ").Last(); // Extract the species name from the embed author field
            bool isShiny = raidEmbed.Author.Value.Name.Contains("Shiny");
            // New code to extract Form
            int form = 0; // Initialize to a default value
            var statsField = raidEmbed.Fields.FirstOrDefault(x => x.Name == "**__Stats__**");
            if (statsField != null)
            {
                var formLine = statsField.Value.Split('\n').FirstOrDefault(line => line.Contains("Form:"));
                if (formLine != null)
                {
                    var formString = formLine.Split(':').Last().Trim();
                    // Extract digits from the string
                    var formDigits = Regex.Match(formString, @"\d+").Value;
                    if (int.TryParse(formDigits, out int formValue))
                    {
                        form = formValue;
                    }
                }
            }

            var description = string.Empty;
            var prevpath = "bodyparam.txt";
            var filepath = "RaidFilesSV\\bodyparam.txt";
            if (File.Exists(prevpath))
                Directory.Move(filepath, prevpath + Path.GetFileName(filepath));

            if (File.Exists(filepath))
                description = File.ReadAllText(filepath);

            var data = string.Empty;
            var prevpk = "pkparam.txt";
            var pkpath = "RaidFilesSV\\pkparam.txt";
            if (File.Exists(prevpk))
                Directory.Move(pkpath, prevpk + Path.GetFileName(pkpath));

            if (File.Exists(pkpath))
                data = File.ReadAllText(pkpath);

            RotatingRaidSettingsSV.RotatingRaidParameters newparam = new()
            {
                CrystalType = crystalType,
                Description = new[] { description },
                PartyPK = new[] { "" },
                Species = (Species)Enum.Parse(typeof(Species), species),
                SpeciesForm = form,
                Seed = seed,
                IsCoded = true,
                IsShiny = isShiny,
                AddedByRACommand = false,
                Title = $"{species}",
            };
            SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaidEmbedParameters.Add(newparam);
            await Context.Message.DeleteAsync().ConfigureAwait(false);
            var msg = $"Your new raid has been added.";
            await ReplyAsync(msg, embed: raidEmbed).ConfigureAwait(false);
        }

        [Command("ra")]
        [Summary("Adds new raid parameter next in the queue.")]
        public async Task AddNewRaidParamNext([Summary("Seed")] string seed, [Summary("Difficulty Level (1-8)")] int level)
        {
            // Check if the user already has a request
            var userId = Context.User.Id;
            if (SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaidEmbedParameters.Any(r => r.RequestedByUserID == userId))
            {
                await ReplyAsync("You already have an existing raid request in the queue.").ConfigureAwait(false);
                return;
            }
            // Validate the seed for hexadecimal format
            if (seed.Length != 8 || !seed.All(c => "0123456789abcdefABCDEF".Contains(c)))
            {
                await ReplyAsync("Invalid seed format. Please enter a seed consisting of exactly 8 hexadecimal digits.").ConfigureAwait(false);
                return;
            }
            if (level < 1 || level > 8)
            {
                await ReplyAsync("Invalid raid level. Please enter a level between 1 and 8.").ConfigureAwait(false);
                return;
            }

            if (!Hub.Config.RotatingRaidSV.EventActive && (level == 7 || level == 8))
            {
                await ReplyAsync("Invalid Raid level. No active Events.").ConfigureAwait(false);
                return;
            }

            // Determine the CrystalType based on the given difficulty level
            var crystalType = level switch
            {
                >= 1 and <= 5 => (TeraCrystalType)0,
                6 => (TeraCrystalType)1,
                7 => (TeraCrystalType)3,
                8 => (TeraCrystalType)2,
                _ => throw new ArgumentException("Invalid difficulty level.")
            };

            // Determine the correct map
            var selectedMap = RotatingRaidBotSV.IsKitakami ? TeraRaidMapParent.Kitakami : TeraRaidMapParent.Paldea;
            var raidEmbed = RotatingRaidBotSV.RaidInfoCommand(seed, (int)crystalType, selectedMap);  // Adjusted to use the selected map
            var species = raidEmbed.Author.Value.Name.Split(" ").Last(); // Extract the species name from the embed author field
            bool isShiny = raidEmbed.Author.Value.Name.Contains("Shiny");
            // New code to extract Form
            int form = 0; // Initialize to a default value
            var statsField = raidEmbed.Fields.FirstOrDefault(x => x.Name == "**__Stats__**");
            if (statsField != null)
            {
                var formLine = statsField.Value.Split('\n').FirstOrDefault(line => line.Contains("Form:"));
                if (formLine != null)
                {
                    var formString = formLine.Split(':').Last().Trim();
                    // Extract digits from the string
                    var formDigits = Regex.Match(formString, @"\d+").Value;
                    if (int.TryParse(formDigits, out int formValue))
                    {
                        form = formValue;
                    }
                }
            }

            var description = string.Empty;
            var prevpath = "bodyparam.txt";
            var filepath = "RaidFilesSV\\bodyparam.txt";
            if (File.Exists(prevpath))
                Directory.Move(filepath, prevpath + Path.GetFileName(filepath));

            if (File.Exists(filepath))
                description = File.ReadAllText(filepath);

            var data = string.Empty;
            var prevpk = "pkparam.txt";
            var pkpath = "RaidFilesSV\\pkparam.txt";
            if (File.Exists(prevpk))
                Directory.Move(pkpath, prevpk + Path.GetFileName(pkpath));

            if (File.Exists(pkpath))
                data = File.ReadAllText(pkpath);

            RotatingRaidSettingsSV.RotatingRaidParameters newparam = new()
            {
                CrystalType = crystalType,
                Description = new[] { description },
                PartyPK = new[] { "" },
                Species = (Species)Enum.Parse(typeof(Species), species),
                SpeciesForm = form,
                Seed = seed,
                IsCoded = true,
                IsShiny = isShiny,
                AddedByRACommand = true,
                RequestedByUserID = Context.User.Id,
                Title = $"{Context.User.Username}'s Requested Raid",
                User = Context.User,
            };

            // Determine the correct position to insert the new raid after the current rotation
            int insertPosition = RotatingRaidBotSV.RotationCount + 1;
            while (insertPosition < SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaidEmbedParameters.Count && SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaidEmbedParameters[insertPosition].AddedByRACommand)
            {
                insertPosition++;
            }

            SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaidEmbedParameters.Insert(insertPosition, newparam);

            await Context.Message.DeleteAsync().ConfigureAwait(false);
            var msg = $"{Context.User.Mention}, added your raid to the queue! I'll DM you when it's about to start.";
            await ReplyAsync(msg, embed: raidEmbed).ConfigureAwait(false);
        }

        [Command("rp")]
        [Summary("Adds provided showdown set Pokémon to the users Raid in Queue.")]
        public async Task AddRaidPK([Summary("Showdown Set")][Remainder] string content)
        {
            content = ReusableActions.StripCodeBlock(content);
            var set = new ShowdownSet(content);
            var template = AutoLegalityWrapper.GetTemplate(set);
            if (set.InvalidLines.Count != 0 || set.Species <= 0)
            {
                var msg = $"Unable to parse Showdown Set:\n{string.Join("\n", set.InvalidLines)}";
                await ReplyAsync(msg).ConfigureAwait(false);
                return;
            }

            try
            {
                var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
                var pkm = sav.GetLegal(template, out var result);
                var la = new LegalityAnalysis(pkm);
                var spec = GameInfo.Strings.Species[template.Species];
                pkm = EntityConverter.ConvertToType(pkm, typeof(T), out _) ?? pkm;
                if (pkm is not T pk || !la.Valid)
                {
                    var reason = result == "Timeout" ? $"That {spec} set took too long to generate." : $"I wasn't able to create a {spec} from that set.";
                    var imsg = $"Oops! {reason}";
                    if (result == "Failed")
                        imsg += $"\n{AutoLegalityWrapper.GetLegalizationHint(template, sav, pkm)}";
                    await ReplyAsync(imsg).ConfigureAwait(false);
                    return;
                }

                var userId = Context.User.Id;
                var raidParameters = SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaidEmbedParameters;
                var raidToUpdate = raidParameters.FirstOrDefault(r => r.RequestedByUserID == userId);
                string[] partyPK = content.Split('\n', StringSplitOptions.RemoveEmptyEntries); // Remove empty lines
                if (raidToUpdate != null)
                {
                    raidToUpdate.PartyPK = partyPK;
                    await Context.Message.DeleteAsync().ConfigureAwait(false);
                    var embed = RPEmbed.PokeEmbed(pkm, Context.User.Username);
                    await ReplyAsync(embed: embed).ConfigureAwait(false);
                }
                else
                {
                    var msg = "You don't have a raid in queue!";
                    await ReplyAsync(msg).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(TradeAdditionsModule<T>));
                var msg = $"Oops! An unexpected problem happened with this Showdown Set:\n```{string.Join("\n", set.GetSetLines())}```";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
        }

        [Command("rqs")]
        [Summary("Checks the number of raids before the user's request and gives an ETA.")]
        public async Task CheckQueueStatus()
        {
            var userId = Context.User.Id;

            // Assuming RotatingRaidBotSV.RotationCount holds the current position in the queue
            int currentPosition = RotatingRaidBotSV.RotationCount;

            // Find the index of the user's request in the queue
            var userRequestIndex = SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaidEmbedParameters.FindIndex(r => r.RequestedByUserID == userId);

            EmbedBuilder embed = new EmbedBuilder();

            if (userRequestIndex == -1)
            {
                embed.Title = "Queue Status";
                embed.Color = Color.Red;
                embed.Description = $"{Context.User.Mention}, you do not have a raid request in the queue.";
            }
            else
            {
                int raidsBeforeUser = userRequestIndex - currentPosition;

                if (raidsBeforeUser <= 0)
                {
                    embed.Title = "Queue Status";
                    embed.Color = Color.Green;
                    embed.Description = $"{Context.User.Mention}, your raid request is up next!";
                }
                else
                {
                    // Calculate ETA
                    int etaMinutes = raidsBeforeUser * 6;

                    embed.Title = "Queue Status";
                    embed.Color = Color.Orange;
                    embed.Description = $"{Context.User.Mention}, here's the status of your raid request:";
                    embed.AddField("Raids Before Yours", raidsBeforeUser.ToString(), true);
                    embed.AddField("Estimated Time", $"{etaMinutes} minutes", true);
                }
            }

            await Context.Message.DeleteAsync().ConfigureAwait(false);
            await ReplyAsync(embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("rqc")]
        [Summary("Removes the raid added by the user.")]
        public async Task RemoveOwnRaidParam()
        {
            var userId = Context.User.Id;
            var list = SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaidEmbedParameters;

            var raid = list.FirstOrDefault(r => r.RequestedByUserID == userId && r.AddedByRACommand);
            if (raid == null)
            {
                await ReplyAsync("You don't have a raid added.").ConfigureAwait(false);
                return;
            }

            list.Remove(raid);
            await Context.Message.DeleteAsync().ConfigureAwait(false);
            var msg = $"Cleared your Raid from the queue.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        [Command("removeRaidParams")]
        [Alias("rrp")]
        [Summary("Removes a raid parameter.")]
        [RequireSudo]
        public async Task RemoveRaidParam([Summary("Seed Index")] int index)
        {
            var list = SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaidEmbedParameters;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                list.RemoveAt(index);
                var msg = $"Raid for {raid.Title} | {raid.Seed:X8} has been removed!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Invalid raid parameter index.").ConfigureAwait(false);
        }

        [Command("toggleRaidParams")]
        [Alias("trp")]
        [Summary("Toggles raid parameter.")]
        [RequireSudo]
        public async Task ToggleRaidParam([Summary("Seed Index")] int index)
        {
            var list = SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaidEmbedParameters;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.ActiveInRotation = !raid.ActiveInRotation;
                var m = raid.ActiveInRotation ? "enabled" : "disabled";
                var msg = $"Raid for {raid.Title} | {raid.Seed:X8} has been {m}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Invalid raid parameter Index.").ConfigureAwait(false);
        }

        [Command("togglecodeRaidParams")]
        [Alias("tcrp")]
        [Summary("Toggles code raid parameter.")]
        [RequireSudo]
        public async Task ToggleCodeRaidParam([Summary("Seed Index")] int index)
        {
            var list = SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaidEmbedParameters;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.IsCoded = !raid.IsCoded;
                var m = raid.IsCoded ? "coded" : "uncoded";
                var msg = $"Raid for {raid.Title} | {raid.Seed:X8} is now {m}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Invalid raid parameter Index.").ConfigureAwait(false);
        }

        [Command("changeRaidParamTitle")]
        [Alias("crpt")]
        [Summary("Changes the title of a  raid parameter.")]
        [RequireSudo]
        public async Task ChangeRaidParamTitle([Summary("Seed Index")] int index, [Summary("Title")] string title)
        {
            var list = SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaidEmbedParameters;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.Title = title;
                var msg = $"Raid Title for {raid.Title} | {raid.Seed:X8} has been changed to: {title}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Invalid raid parameter Index.").ConfigureAwait(false);
        }

        [Command("viewraidList")]
        [Alias("vrl", "rotatinglist")]
        [Summary("Prints the raids in the current collection.")]
        public async Task GetRaidListAsync()
        {
            var list = SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaidEmbedParameters;
            int count = list.Count;
            int fields = (int)Math.Ceiling((double)count / 15);
            var embed = new EmbedBuilder
            {
                Title = "Raid List"
            };
            for (int i = 0; i < fields; i++)
            {
                int start = i * 15;
                int end = Math.Min(start + 14, count - 1);
                var fieldBuilder = new StringBuilder();
                for (int j = start; j <= end; j++)
                {
                    var raid = list[j];
                    int paramNumber = j;
                    fieldBuilder.AppendLine($"{paramNumber}.) {raid.Title} - {raid.Seed} - Status: {(raid.ActiveInRotation ? "Active" : "Inactive")}");
                }
                embed.AddField($"Raid List - Part {i + 1}", fieldBuilder.ToString(), false);
            }
            await ReplyAsync($"These are the raids currently in the list (total: {count}):", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("toggleRaidPK")]
        [Alias("trpk")]
        [Summary("Toggles raid parameter.")]
        [RequireSudo]
        public async Task ToggleRaidParamPK([Summary("Seed Index")] int index, [Summary("Showdown Set")][Remainder] string content)
        {
            var list = SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaidEmbedParameters;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.PartyPK = new[] { content };
                var m = string.Join("\n", raid.PartyPK);
                var msg = $"RaidPK for {raid.Title} | {raid.Seed:X8} has been updated to:\n{m}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Invalid raid parameter Index.").ConfigureAwait(false);
        }

        [Command("raidhelp")]
        [Alias("rh")]
        [Summary("Prints the raid help command list.")]
        public async Task GetRaidHelpListAsync()
        {
            var embed = new EmbedBuilder();
            List<string> cmds = new()
            {
                "$scl - Sets the catch limit for your raids.\n",
                "$crb - Clear all in raider ban list.\n",
                "$vrl - View all raids in the list.\n",
                "$arp - Add parameter to the collection.\nEx: [Command] [Index] [Species] [Difficulty]\n",
                "$rrp - Remove parameter from the collection.\nEx: [Command] [Index]\n",
                "$trp - Toggle the parameter as Active/Inactive in the collection.\nEx: [Command] [Index]\n",
                "$tcrp - Toggle the parameter as Coded/Uncoded in the collection.\nEx: [Command] [Index]\n",
                "$trpk - Set a PartyPK for the parameter via a showdown set.\nEx: [Command] [Index] [ShowdownSet]\n",
                "$crpt - Set the title for the parameter.\nEx: [Command] [Index]"
            };
            string msg = string.Join("", cmds.ToList());
            embed.AddField(x =>
            {
                x.Name = "Raid Help Commands";
                x.Value = msg;
                x.IsInline = false;
            });
            await ReplyAsync("Here's your raid help!", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("unbanraider")]
        [Alias("ubr")]
        [Summary("Removes the specificed NID from the banlist for Raids in SV.")]
        [RequireSudo]
        public async Task UnbanRaider([Summary("Removes the specificed NID from the banlist for Raids in SV.")] string nid)
        {
            var list = SysCord<T>.Runner.Hub.Config.RaidSV.RaiderBanList.List.ToArray();
            string msg = $"{Context.User.Mention} no user found with that NID.";
            for (int i = 0; i < list.Length; i++)
                if ($"{list[i].ID}".Equals(nid))
                {
                    msg = $"{Context.User.Mention} user {list[i].Name} - {list[i].ID} has been unbanned.";
                    SysCord<T>.Runner.Hub.Config.RaidSV.RaiderBanList.List.ToList().Remove(list[i]);
                }
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        [Command("unbanrotatingraider")]
        [Alias("ubrr")]
        [Summary("Removes the specificed NID from the banlist for Raids in SV.")]
        [RequireSudo]
        public async Task UnbanRotatingRaider([Summary("Removes the specificed NID from the banlist for Raids in SV.")] string nid)
        {
            var list = SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaiderBanList.List.ToArray();
            string msg = $"{Context.User.Mention} no user found with that NID.";
            for (int i = 0; i < list.Length; i++)
                if ($"{list[i].ID}".Equals(nid))
                {
                    msg = $"{Context.User.Mention} user {list[i].Name} - {list[i].ID} has been unbanned.";
                    SysCord<T>.Runner.Hub.Config.RotatingRaidSV.RaiderBanList.List.ToList().Remove(list[i]);
                }
            await ReplyAsync(msg).ConfigureAwait(false);
        }
    }
}