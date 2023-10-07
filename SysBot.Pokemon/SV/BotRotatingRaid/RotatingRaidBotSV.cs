using Discord;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.SV;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RaidCrawler.Core.Structures;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.Net.Http;
using System.Drawing;
using static SysBot.Base.SwitchButton;

namespace SysBot.Pokemon
{
    public class RotatingRaidBotSV : PokeRoutineExecutor9SV, ICountBot
    {
        private readonly PokeTradeHub<PK9> Hub;
        private readonly RotatingRaidSettingsSV Settings;
        public ICountSettings Counts => Settings;
        private RemoteControlAccessList RaiderBanList => Settings.RaiderBanList;

        public RotatingRaidBotSV(PokeBotState cfg, PokeTradeHub<PK9> hub) : base(cfg)
        {
            Hub = hub;
            Settings = hub.Config.RotatingRaidSV;
        }

        private int RaidCount;
        private int WinCount;
        private int LossCount;
        private int SeedIndexToReplace;
        private int StoryProgress;
        private int EventProgress;
        private int EmptyRaid = 0;
        private int LostRaid = 0;
        public static int RotationCount { get; set; }
        private ulong TodaySeed;
        private ulong OverworldOffset;
        private ulong ConnectedOffset;
        private ulong RaidBlockPointerP;
        private ulong RaidBlockPointerK;
        private readonly ulong[] TeraNIDOffsets = new ulong[3];
        private string TeraRaidCode { get; set; } = string.Empty;
        private string BaseDescription = string.Empty;
        private string[] PresetDescription = Array.Empty<string>();
        private string[] ModDescription = Array.Empty<string>();
        private readonly Dictionary<ulong, int> RaidTracker = new();
        private List<BanList> GlobalBanList = new();
        private SAV9SV HostSAV = new();
        private DateTime StartTime = DateTime.Now;
        private RaidContainer? container;

        public override async Task MainLoop(CancellationToken token)
        {
            if (Settings.GenerateParametersFromFile)
            {
                GenerateSeedsFromFile();
                Log("Done.");
            }

            if (Settings.PresetFilters.UsePresetFile)
            {
                LoadDefaultFile();
                Log("Using Preset file.");
            }

            if (Settings.ConfigureRolloverCorrection)
            {
                await RolloverCorrectionSV(token).ConfigureAwait(false);
                return;
            }

            if (Settings.RaidEmbedParameters.Count < 1)
            {
                Log("RaidEmbedParameters cannot be 0. Please setup your parameters for the raid(s) you are hosting.");
                return;
            }

            if (Settings.TimeToWait is < 0 or > 180)
            {
                Log("Time to wait must be between 0 and 180 seconds.");
                return;
            }

            if (Settings.RaidsBetweenUpdate == 0 || Settings.RaidsBetweenUpdate < -1)
            {
                Log("Raids between updating the global ban list must be greater than 0, or -1 if you want it off.");
                return;
            }

            try
            {
                Log("Identifying trainer data of the host console.");
                HostSAV = await IdentifyTrainer(token).ConfigureAwait(false);
                await InitializeHardware(Settings, token).ConfigureAwait(false);
                Log("Starting main RotatingRaidBot loop.");
                await InnerLoop(token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log(e.Message);
            }

            Log($"Ending {nameof(RotatingRaidBotSV)} loop.");
            await HardStop().ConfigureAwait(false);
        }

        private void LoadDefaultFile()
        {
            var folder = "raidfilessv";
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var prevpath = "preset.txt";
            var filepath = "raidfilessv\\preset.txt";
            if (File.Exists(prevpath))
                File.Move(prevpath, filepath);
            if (!File.Exists(filepath))
            {
                File.WriteAllText(filepath, "{shinySymbol} - {species} - {markTitle} - {genderSymbol} - {genderText}" + Environment.NewLine + "{stars} - {difficulty} - {tera}" + Environment.NewLine +
                    "{HP}/{ATK}/{DEF}/{SPA}/{SPD}/{SPE}\n{ability} | {nature}" + Environment.NewLine + "Scale: {scaleText} - {scaleNumber}" + Environment.NewLine + "{moveset}" + Environment.NewLine + "{extramoves}");
            }
            if (File.Exists(filepath))
            {
                PresetDescription = File.ReadAllLines(filepath);
                ModDescription = PresetDescription;
            }
            else
                PresetDescription = Array.Empty<string>();
        }

        private void GenerateSeedsFromFile()
        {
            var folder = "raidfilessv";
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var prevrotationpath = "raidsv.txt";
            var rotationpath = "raidfilessv\\raidsv.txt";
            if (File.Exists(prevrotationpath))
                File.Move(prevrotationpath, rotationpath);
            if (!File.Exists(rotationpath))
            {
                File.WriteAllText(rotationpath, "00000000-None-5");
                Log("Creating a default raidsv.txt file, skipping generation as file is empty.");
                return;
            }

            if (!File.Exists(rotationpath))
                Log("raidsv.txt not present, skipping parameter generation.");

            BaseDescription = string.Empty;
            var prevpath = "bodyparam.txt";
            var filepath = "raidfilessv\\bodyparam.txt";
            if (File.Exists(prevpath))
                File.Move(prevpath, filepath);
            if (File.Exists(filepath))
                BaseDescription = File.ReadAllText(filepath);

            var data = string.Empty;
            var prevpk = "pkparam.txt";
            var pkpath = "raidfilessv\\pkparam.txt";
            if (File.Exists(prevpk))
                File.Move(prevpk, pkpath);
            if (File.Exists(pkpath))
                data = File.ReadAllText(pkpath);

            DirectorySearch(rotationpath, data);
        }

        private void DirectorySearch(string sDir, string data)
        {
            Settings.RaidEmbedParameters.Clear();
            string contents = File.ReadAllText(sDir);
            string[] moninfo = contents.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < moninfo.Length; i++)
            {
                var div = moninfo[i].Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
                var monseed = div[0];
                var montitle = div[1];
                var montent = div[2];
                TeraCrystalType type = montent switch
                {
                    "6" => TeraCrystalType.Black,
                    "7" => TeraCrystalType.Might,
                    _ => TeraCrystalType.Base,
                };
                RotatingRaidSettingsSV.RotatingRaidParameters param = new()
                {
                    Seed = monseed,
                    Title = montitle,
                    Species = TradeExtensions<PK9>.EnumParse<Species>(montitle),
                    CrystalType = type,
                    PartyPK = new[] { data },
                };
                Settings.RaidEmbedParameters.Add(param);
                Log($"Parameters generated from text file for {montitle}.");
            }
        }

        private async Task InnerLoop(CancellationToken token)
        {
            bool partyReady;
            List<(ulong, TradeMyStatus)> lobbyTrainers;
            StartTime = DateTime.Now;
            var dayRoll = 0;
            RotationCount = 0;
            var raidsHosted = 0;
            while (!token.IsCancellationRequested)
            {
                // Initialize offsets at the start of the routine and cache them.
                await InitializeSessionOffsets(token).ConfigureAwait(false);
                if (RaidCount == 0)
                {
                    TodaySeed = BitConverter.ToUInt64(await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 8, token).ConfigureAwait(false), 0);
                    Log($"Today Seed: {TodaySeed:X8}");
                }

                if (!Settings.RaidEmbedParameters[RotationCount].IsSet)
                {
                    Log($"Preparing parameter for {Settings.RaidEmbedParameters[RotationCount].Species}");
                    await ReadRaids(token).ConfigureAwait(false);
                }
                else
                    Log($"Parameter for {Settings.RaidEmbedParameters[RotationCount].Species} has been set previously, skipping raid reads.");

                if (!string.IsNullOrEmpty(Settings.GlobalBanListURL))
                    await GrabGlobalBanlist(token).ConfigureAwait(false);

                var currentSeed = BitConverter.ToUInt64(await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 8, token).ConfigureAwait(false), 0);
                if (TodaySeed != currentSeed)
                {
                    var msg = $"Current Today Seed {currentSeed:X8} does not match Starting Today Seed: {TodaySeed:X8} after rolling back 1 day. ";
                    if (dayRoll != 0)
                    {
                        Log(msg + "Stopping routine for lost raid.");
                        return;
                    }
                    Log(msg);
                    await CloseGame(Hub.Config, token).ConfigureAwait(false);
                    await RolloverCorrectionSV(token).ConfigureAwait(false);
                    await StartGameRaid(Hub.Config, token).ConfigureAwait(false);

                    dayRoll++;
                    continue;
                }

                if (Hub.Config.Stream.CreateAssets)
                    await GetRaidSprite(token).ConfigureAwait(false);

                // Clear NIDs.
                await SwitchConnection.WriteBytesAbsoluteAsync(new byte[32], TeraNIDOffsets[0], token).ConfigureAwait(false);

                // Connect online and enter den.
                if (!await PrepareForRaid(token).ConfigureAwait(false))
                {
                    Log("Failed to prepare the raid, rebooting the game.");
                    await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                    continue;
                }

                // Wait until we're in lobby.
                if (!await GetLobbyReady(token).ConfigureAwait(false))
                    continue;

                if (Settings.RaidEmbedParameters[RotationCount].AddedByRACommand)
                {
                    var user = Settings.RaidEmbedParameters[RotationCount].User;
                    var code = await GetRaidCode(token).ConfigureAwait(false);
                    if (user != null)
                        await user.SendMessageAsync($"Your Raid Code is **{code}**").ConfigureAwait(false);
                }

                // Read trainers until someone joins.
                (partyReady, lobbyTrainers) = await ReadTrainers(token).ConfigureAwait(false);
                if (!partyReady)
                {
                    if (LostRaid >= Settings.LobbyOptions.SkipRaidLimit && Settings.LobbyOptions.LobbyMethodOptions == LobbyMethodOptions.SkipRaid)
                    {
                        await SkipRaidOnLosses(token).ConfigureAwait(false);
                        continue;
                    }

                    // Should add overworld recovery with a game restart fallback.
                    await RegroupFromBannedUser(token).ConfigureAwait(false);

                    if (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                    {
                        Log("Something went wrong, attempting to recover.");
                        await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                        continue;
                    }

                    // Clear trainer OTs.
                    Log("Clearing stored OTs");
                    for (int i = 0; i < 3; i++)
                    {
                        List<long> ptr = new(Offsets.Trader2MyStatusPointer);
                        ptr[2] += i * 0x30;
                        await SwitchConnection.PointerPoke(new byte[16], ptr, token).ConfigureAwait(false);
                    }
                    continue;
                }
                await CompleteRaid(lobbyTrainers, token).ConfigureAwait(false);
                raidsHosted++;
                if (raidsHosted == Settings.TotalRaidsToHost && Settings.TotalRaidsToHost > 0)
                    break;
            }
            if (Settings.TotalRaidsToHost > 0 && raidsHosted != 0)
                Log("Total raids to host has been met.");
        }

        public override async Task HardStop()
        {
            await CleanExit(CancellationToken.None).ConfigureAwait(false);
        }

        private async Task GrabGlobalBanlist(CancellationToken token)
        {
            using var httpClient = new HttpClient();
            var url = Settings.GlobalBanListURL;
            var data = await httpClient.GetStringAsync(url, token).ConfigureAwait(false);
            GlobalBanList = JsonConvert.DeserializeObject<List<BanList>>(data)!;
            if (GlobalBanList.Count is not 0)
                Log($"There are {GlobalBanList.Count} entries on the global ban list.");
            else
                Log("Failed to fetch the global ban list. Ensure you have the correct URL.");
        }

        private async Task LocateSeedIndex(CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 2304, token).ConfigureAwait(false);
            for (int i = 0; i < 69; i++)
            {
                var seed = BitConverter.ToUInt32(data.Slice(0x20 + (i * 0x20), 4));
                if (seed == 0)
                {
                    SeedIndexToReplace = i;
                    Log($"Index located at {i}");
                    return;
                }
            }

            data = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerK + 0x10, 0xC80, token).ConfigureAwait(false);
            for (int i = 69; i < 95; i++)
            {
                var seed = BitConverter.ToUInt32(data.Slice((i - 69) * 0x20, 4));
                if (seed == 0)
                {
                    SeedIndexToReplace = i;
                    Log($"Index located at {i}");
                    return;
                }
            }
            Log($"Index not located.");
        }
        private async Task CompleteRaid(List<(ulong, TradeMyStatus)> trainers, CancellationToken token)
        {
            bool ready = false;
            List<(ulong, TradeMyStatus)> lobbyTrainersFinal = new();
            if (await IsConnectedToLobby(token).ConfigureAwait(false))
            {
                int b = 0;
                Log("Preparing for battle!");
                while (!await IsInRaid(token).ConfigureAwait(false))
                    await Click(A, 1_000, token).ConfigureAwait(false);

                if (await IsInRaid(token).ConfigureAwait(false))
                {
                    // Clear NIDs to refresh player check.
                    await SwitchConnection.WriteBytesAbsoluteAsync(new byte[32], TeraNIDOffsets[0], token).ConfigureAwait(false);
                    await Task.Delay(5_000, token).ConfigureAwait(false);

                    // Loop through trainers again in case someone disconnected.
                    for (int i = 0; i < 3; i++)
                    {
                        var player = i + 2;
                        var nidOfs = TeraNIDOffsets[i];
                        var data = await SwitchConnection.ReadBytesAbsoluteAsync(nidOfs, 8, token).ConfigureAwait(false);
                        var nid = BitConverter.ToUInt64(data, 0);

                        if (nid == 0)
                            continue;

                        List<long> ptr = new(Offsets.Trader2MyStatusPointer);
                        ptr[2] += i * 0x30;
                        var trainer = await GetTradePartnerMyStatus(ptr, token).ConfigureAwait(false);

                        if (string.IsNullOrWhiteSpace(trainer.OT) || HostSAV.OT == trainer.OT)
                            continue;

                        lobbyTrainersFinal.Add((nid, trainer));
                        var tr = trainers.FirstOrDefault(x => x.Item2.OT == trainer.OT);
                        if (tr != default)
                            Log($"Player {i + 2} matches lobby check for {trainer.OT}.");
                        else Log($"New Player {i + 2}: {trainer.OT} | TID: {trainer.DisplayTID} | NID: {nid}.");
                    }
                    var nidDupe = lobbyTrainersFinal.Select(x => x.Item1).ToList();
                    var dupe = lobbyTrainersFinal.Count > 1 && nidDupe.Distinct().Count() == 1;
                    if (dupe)
                    {
                        // We read bad data, reset game to end early and recover.
                        var msg = "Oops! Something went wrong, resetting to recover.";
                        await EnqueueEmbed(null, msg, false, false, false, false, token).ConfigureAwait(false);
                        await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                        return;
                    }

                    var names = lobbyTrainersFinal.Select(x => x.Item2.OT).ToList();
                    bool hatTrick = lobbyTrainersFinal.Count == 3 && names.Distinct().Count() == 1;

                    await Task.Delay(15_000, token).ConfigureAwait(false);
                    await EnqueueEmbed(names, "", hatTrick, false, false, true, token).ConfigureAwait(false);
                }

                while (await IsConnectedToLobby(token).ConfigureAwait(false))
                {
                    b++;
                    switch (Settings.LobbyOptions.Action)
                    {
                        case RaidAction.AFK: await Task.Delay(3_000, token).ConfigureAwait(false); break;
                        case RaidAction.MashA: await Click(A, 3_500, token).ConfigureAwait(false); break;
                        case RaidAction.TurboA: await Click(A, 1_500, token).ConfigureAwait(false); break;
                    }
                    if (b % 10 == 0)
                        Log("Still in battle...");
                }

                Log("Raid lobby disbanded!");
                await Click(B, 0_500, token).ConfigureAwait(false);
                await Click(B, 0_500, token).ConfigureAwait(false);
                await Click(DDOWN, 0_500, token).ConfigureAwait(false);

                if (!await IsConnectedToLobby(token).ConfigureAwait(false) && await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                {
                    Log("We lost the raid...");
                    LossCount++;
                    LostRaid++;
                }

                if (!await IsConnectedToLobby(token).ConfigureAwait(false) && !await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                {
                    Settings.AddCompletedRaids();
                    Log($"We defeated {Settings.RaidEmbedParameters[RotationCount].Species}!");
                    WinCount++;
                    if (trainers.Count > 0 && Settings.CatchLimit != 0)
                        ApplyPenalty(trainers);

                    if (Settings.RaidEmbedParameters.Count > 1)
                        await SanitizeRotationCount(token).ConfigureAwait(false);

                    await EnqueueEmbed(null, "", false, false, true, false, token).ConfigureAwait(false);
                    ready = true;
                }

                if (Settings.LobbyOptions.LobbyMethodOptions == LobbyMethodOptions.SkipRaid)
                {
                    Log($"Lost/Empty Lobbies: {LostRaid}/{Settings.LobbyOptions.SkipRaidLimit}");

                    if (LostRaid >= Settings.LobbyOptions.SkipRaidLimit)
                    {
                        Log($"We had {Settings.LobbyOptions.SkipRaidLimit} lost/empty raids.. Moving on!");
                        await SanitizeRotationCount(token).ConfigureAwait(false);
                        await EnqueueEmbed(null, "", false, false, true, false, token).ConfigureAwait(false);
                        ready = true;
                    }
                }
            }

            Log("Returning to overworld...");
            while (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                await Click(A, 1_000, token).ConfigureAwait(false);

            await LocateSeedIndex(token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await CloseGame(Hub.Config, token).ConfigureAwait(false);
            if (ready)
                await StartGameRaid(Hub.Config, token).ConfigureAwait(false);

            else if (!ready)
            {
                if (Settings.RaidEmbedParameters.Count > 1)
                {
                    if (RotationCount < Settings.RaidEmbedParameters.Count && Settings.RaidEmbedParameters.Count > 1)
                        RotationCount++;
                    if (RotationCount >= Settings.RaidEmbedParameters.Count && Settings.RaidEmbedParameters.Count > 1)
                    {
                        RotationCount = 0;
                        Log($"Resetting Rotation Count to {RotationCount}");
                    }
                    Log($"Moving on to next rotation for {Settings.RaidEmbedParameters[RotationCount].Species}.");
                    await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
                }
                else
                    await StartGame(Hub.Config, token).ConfigureAwait(false);
            }

            if (Settings.KeepDaySeed)
                await OverrideTodaySeed(token).ConfigureAwait(false);
        }

        private void ApplyPenalty(List<(ulong, TradeMyStatus)> trainers)
        {
            for (int i = 0; i < trainers.Count; i++)
            {
                var nid = trainers[i].Item1;
                var name = trainers[i].Item2.OT;
                if (RaidTracker.ContainsKey(nid) && nid != 0)
                {
                    var entry = RaidTracker[nid];
                    var Count = entry + 1;
                    RaidTracker[nid] = Count;
                    Log($"Player: {name} completed the raid with catch count: {Count}.");

                    if (Settings.CatchLimit != 0 && Count == Settings.CatchLimit)
                        Log($"Player: {name} has met the catch limit {Count}/{Settings.CatchLimit}, adding to the block list for this session for {Settings.RaidEmbedParameters[RotationCount].Species}.");
                }
            }
        }

        private async Task OverrideTodaySeed(CancellationToken token)
        {
            var todayoverride = BitConverter.GetBytes(TodaySeed);
            List<long> ptr = new(Offsets.RaidBlockPointerP);
            ptr[2] += 0x8;
            await SwitchConnection.PointerPoke(todayoverride, ptr, token).ConfigureAwait(false);
        }

        private async Task OverrideSeedIndex(int index, CancellationToken token)
        {
            List<long> ptr;
            if (index < 69)
            {
                ptr = new(Offsets.RaidBlockPointerP)
                {
                    [3] = 0x40 + (index + 1) * 0x20
                };
            }
            else
            {
                ptr = new(Offsets.RaidBlockPointerK)
                {
                    [3] = 0xCE8 + (index - 69) * 0x20
                };
            }

            var seed = uint.Parse(Settings.RaidEmbedParameters[RotationCount].Seed, NumberStyles.AllowHexSpecifier);
            byte[] inj = BitConverter.GetBytes(seed);
            var currseed = await SwitchConnection.PointerPeek(4, ptr, token).ConfigureAwait(false);
            Log($"Replacing {BitConverter.ToString(currseed)} with {BitConverter.ToString(inj)}.");
            await SwitchConnection.PointerPoke(inj, ptr, token).ConfigureAwait(false);

            var ptr2 = ptr;
            ptr2[3] += 0x08;
            var crystal = BitConverter.GetBytes((int)Settings.RaidEmbedParameters[RotationCount].CrystalType);
            var currcrystal = await SwitchConnection.PointerPeek(1, ptr2, token).ConfigureAwait(false);
            if (currcrystal != crystal)
                await SwitchConnection.PointerPoke(crystal, ptr2, token).ConfigureAwait(false);

        }

        private async Task SanitizeRotationCount(CancellationToken token)
        {
            await Task.Delay(0_050, token).ConfigureAwait(false);

            // Check if the current raid was added by the RA command, and remove it.
            if (Settings.RaidEmbedParameters[RotationCount].AddedByRACommand)
            {
                Log($"Raid for {Settings.RaidEmbedParameters[RotationCount].Species} was added via RA command and will be removed from the rotation list.");
                Settings.RaidEmbedParameters.RemoveAt(RotationCount);
                // Do not increment the RotationCount here, since the next raid has now taken the index of the removed raid.
            }
            else
            {
                // If the current raid wasn't added by the RA command, move to the next raid.
                RotationCount++;
            }

            if (RotationCount >= Settings.RaidEmbedParameters.Count)
            {
                RotationCount = 0;
                Log($"Resetting Rotation Count to {RotationCount}");
            }
            else
            {
                Log($"Next raid in the list: {Settings.RaidEmbedParameters[RotationCount].Species}.");
                while (RotationCount < Settings.RaidEmbedParameters.Count && !Settings.RaidEmbedParameters[RotationCount].ActiveInRotation)
                {
                    Log($"{Settings.RaidEmbedParameters[RotationCount].Species} is disabled. Moving to next active raid in rotation.");
                    RotationCount++;
                }
                if (RotationCount >= Settings.RaidEmbedParameters.Count)
                {
                    RotationCount = 0;
                    Log($"Resetting Rotation Count to {RotationCount}");
                }
            }
        }

        private async Task InjectPartyPk(string battlepk, CancellationToken token)
        {
            var set = new ShowdownSet(battlepk);
            var template = AutoLegalityWrapper.GetTemplate(set);
            PK9 pk = (PK9)HostSAV.GetLegal(template, out _);
            pk.ResetPartyStats();
            var offset = await SwitchConnection.PointerAll(Offsets.BoxStartPokemonPointer, token).ConfigureAwait(false);
            await SwitchConnection.WriteBytesAbsoluteAsync(pk.EncryptedBoxData, offset, token).ConfigureAwait(false);
        }

        private async Task<bool> PrepareForRaid(CancellationToken token)
        {
            var len = string.Empty;
            foreach (var l in Settings.RaidEmbedParameters[RotationCount].PartyPK)
                len += l;
            if (len.Length > 1 && EmptyRaid == 0)
            {
                Log("Preparing PartyPK to inject..");
                await SetCurrentBox(0, token).ConfigureAwait(false);
                var res = string.Join("\n", Settings.RaidEmbedParameters[RotationCount].PartyPK);
                if (res.Length > 4096)
                    res = res[..4096];
                await InjectPartyPk(res, token).ConfigureAwait(false);

                await Click(X, 2_000, token).ConfigureAwait(false);
                await Click(DRIGHT, 0_500, token).ConfigureAwait(false);
                Log("Scrolling through menus...");
                await SetStick(SwitchStick.LEFT, 0, -32000, 1_000, token).ConfigureAwait(false);
                await SetStick(SwitchStick.LEFT, 0, 0, 0, token).ConfigureAwait(false);
                Log("Tap tap...");
                for (int i = 0; i < 2; i++)
                    await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                await Click(A, 3_500, token).ConfigureAwait(false);
                await Click(Y, 0_500, token).ConfigureAwait(false);
                await Click(DLEFT, 0_800, token).ConfigureAwait(false);
                await Click(Y, 0_500, token).ConfigureAwait(false);
                for (int i = 0; i < 2; i++)
                    await Click(B, 1_500, token).ConfigureAwait(false);
                Log("Battle PK is ready!");
            }

            Log("Preparing lobby...");
            // Make sure we're connected.
            while (!await IsConnectedOnline(ConnectedOffset, token).ConfigureAwait(false))
            {
                Log("Connecting...");
                await RecoverToOverworld(token).ConfigureAwait(false);
                if (!await ConnectToOnline(Hub.Config, token).ConfigureAwait(false))
                    return false;
            }

            for (int i = 0; i < 6; i++)
                await Click(B, 0_500, token).ConfigureAwait(false);

            await Task.Delay(1_500, token).ConfigureAwait(false);

            // If not in the overworld, we've been attacked so quit earlier.
            if (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                return false;

            await Click(A, 3_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false);
            if (!Settings.RaidEmbedParameters[RotationCount].IsCoded || Settings.RaidEmbedParameters[RotationCount].IsCoded && EmptyRaid == Settings.LobbyOptions.EmptyRaidLimit && Settings.LobbyOptions.LobbyMethodOptions == LobbyMethodOptions.OpenLobby)
            {
                if (Settings.RaidEmbedParameters[RotationCount].IsCoded && EmptyRaid == Settings.LobbyOptions.EmptyRaidLimit && Settings.LobbyOptions.LobbyMethodOptions == LobbyMethodOptions.OpenLobby)
                    Log($"We had {Settings.LobbyOptions.EmptyRaidLimit} empty raids.. Opening this raid to all!");
                await Click(DDOWN, 1_000, token).ConfigureAwait(false);
            }
            await Click(A, 8_000, token).ConfigureAwait(false);
            return true;
        }
        private async Task<bool> GetLobbyReady(CancellationToken token)
        {
            if (Settings.RaidEmbedParameters[RotationCount].AddedByRACommand)
            {
                var user = Settings.RaidEmbedParameters[RotationCount].User;
                if (user != null)
                    await user.SendMessageAsync("Your raid is about to start!").ConfigureAwait(false);
            }
            var x = 0;
            Log("Connecting to lobby...");
            while (!await IsConnectedToLobby(token).ConfigureAwait(false))
            {
                await Click(A, 1_000, token).ConfigureAwait(false);
                x++;
                if (x == 45)
                {
                    Log("Failed to connect to lobby, restarting game incase we were in battle/bad connection.");
                    await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                    Log("Attempting to restart routine!");
                    return false;
                }
            }
            return true;
        }

        private async Task<string> GetRaidCode(CancellationToken token)
        {
            var data = await SwitchConnection.PointerPeek(6, Offsets.TeraRaidCodePointer, token).ConfigureAwait(false);
            TeraRaidCode = Encoding.ASCII.GetString(data);
            Log($"Raid Code: {TeraRaidCode}");
            return $"\n{TeraRaidCode}\n";
        }

        private async Task<bool> CheckIfTrainerBanned(TradeMyStatus trainer, ulong nid, int player, bool updateBanList, CancellationToken token)
        {
            Log($"Player {player}: {trainer.OT} | TID: {trainer.DisplayTID} | NID: {nid}");
            if (!RaidTracker.ContainsKey(nid))
                RaidTracker.Add(nid, 0);

            int val = 0;
            var msg = string.Empty;
            var banResultCC = Settings.RaidsBetweenUpdate == -1 ? (false, "") : await BanService.IsRaiderBanned(trainer.OT, Settings.BanListURL, Connection.Label, updateBanList).ConfigureAwait(false);
            var banResultCFW = RaiderBanList.List.FirstOrDefault(x => x.ID == nid);
            var banGlobalCFW = false;
            BanList user = new();
            for (int i = 0; i < GlobalBanList.Count; i++)
            {
                var gNID = GlobalBanList[i].NIDs;
                for (int g = 0; g < gNID.Length; g++)
                {
                    if (gNID[g] == nid)
                    {
                        Log($"NID: {nid} found on GlobalBanList.");
                        if (GlobalBanList[i].enabled)
                            banGlobalCFW = true;
                        user = GlobalBanList[i];
                        break;
                    }
                }
                if (banGlobalCFW is true)
                    break;
            }
            bool isBanned = banResultCFW != default || banGlobalCFW || banResultCC.Item1;

            bool blockResult = false;
            var blockCheck = RaidTracker.ContainsKey(nid);
            if (blockCheck)
            {
                RaidTracker.TryGetValue(nid, out val);
                if (val >= Settings.CatchLimit && Settings.CatchLimit != 0) // Soft pity - block user
                {
                    blockResult = true;
                    RaidTracker[nid] = val + 1;
                    Log($"Player: {trainer.OT} current penalty count: {val}.");
                }
                if (val == Settings.CatchLimit + 2 && Settings.CatchLimit != 0) // Hard pity - ban user
                {
                    msg = $"{trainer.OT} is now banned for repeatedly attempting to go beyond the catch limit for {Settings.RaidEmbedParameters[RotationCount].Species} on {DateTime.Now}.";
                    Log(msg);
                    RaiderBanList.List.Add(new() { ID = nid, Name = trainer.OT, Comment = msg });
                    blockResult = false;
                    await EnqueueEmbed(null, $"Penalty #{val}\n" + msg, false, true, false, false, token).ConfigureAwait(false);
                    return true;
                }
                if (blockResult && !isBanned)
                {
                    msg = $"Penalty #{val}\n{trainer.OT} has already reached the catch limit.\nPlease do not join again.\nRepeated attempts to join like this will result in a ban from future raids.";
                    Log(msg);
                    await EnqueueEmbed(null, msg, false, true, false, false, token).ConfigureAwait(false);
                    return true;
                }
            }

            if (isBanned)
            {
                msg = banResultCC.Item1 ? banResultCC.Item2 : banGlobalCFW ? $"{trainer.OT} was found in the global ban list.\nReason: {user.Comment}" : $"Penalty #{val}\n{banResultCFW!.Name} was found in the host's ban list.\n{banResultCFW.Comment}";
                Log(msg);
                await EnqueueEmbed(null, msg, false, true, false, false, token).ConfigureAwait(false);
                return true;
            }
            return false;
        }

        private async Task<(bool, List<(ulong, TradeMyStatus)>)> ReadTrainers(CancellationToken token)
        {
            await EnqueueEmbed(null, "", false, false, false, false, token).ConfigureAwait(false);

            List<(ulong, TradeMyStatus)> lobbyTrainers = new();
            var wait = TimeSpan.FromSeconds(Settings.TimeToWait);
            var endTime = DateTime.Now + wait;
            bool full = false;
            bool updateBanList = Settings.RaidsBetweenUpdate != -1 && (RaidCount == 0 || RaidCount % Settings.RaidsBetweenUpdate == 0);

            while (!full && (DateTime.Now < endTime))
            {
                for (int i = 0; i < 3; i++)
                {
                    var player = i + 2;
                    Log($"Waiting for Player {player} to load...");

                    var nidOfs = TeraNIDOffsets[i];
                    var data = await SwitchConnection.ReadBytesAbsoluteAsync(nidOfs, 8, token).ConfigureAwait(false);
                    var nid = BitConverter.ToUInt64(data, 0);
                    while (nid == 0 && (DateTime.Now < endTime))
                    {
                        await Task.Delay(0_500, token).ConfigureAwait(false);
                        data = await SwitchConnection.ReadBytesAbsoluteAsync(nidOfs, 8, token).ConfigureAwait(false);
                        nid = BitConverter.ToUInt64(data, 0);
                    }

                    List<long> ptr = new(Offsets.Trader2MyStatusPointer);
                    ptr[2] += i * 0x30;
                    var trainer = await GetTradePartnerMyStatus(ptr, token).ConfigureAwait(false);

                    while (trainer.OT.Length == 0 && (DateTime.Now < endTime))
                    {
                        await Task.Delay(0_500, token).ConfigureAwait(false);
                        trainer = await GetTradePartnerMyStatus(ptr, token).ConfigureAwait(false);
                    }

                    if (nid != 0 && !string.IsNullOrWhiteSpace(trainer.OT))
                    {
                        if (await CheckIfTrainerBanned(trainer, nid, player, updateBanList, token).ConfigureAwait(false))
                            return (false, lobbyTrainers);

                        updateBanList = false;
                    }

                    if (lobbyTrainers.FirstOrDefault(x => x.Item1 == nid) != default && trainer.OT.Length > 0)
                        lobbyTrainers[i] = (nid, trainer);
                    else if (nid > 0 && trainer.OT.Length > 0)
                        lobbyTrainers.Add((nid, trainer));

                    full = lobbyTrainers.Count == 3;
                    if (full || (DateTime.Now >= endTime))
                        break;
                }
            }

            await Task.Delay(5_000, token).ConfigureAwait(false);

            RaidCount++;
            if (lobbyTrainers.Count == 0)
            {
                EmptyRaid++;
                LostRaid++;
                Log($"Nobody joined the raid, recovering...");
                if (Settings.LobbyOptions.LobbyMethodOptions == LobbyMethodOptions.OpenLobby)
                    Log($"Empty Raid Count #{EmptyRaid}");
                if (Settings.LobbyOptions.LobbyMethodOptions == LobbyMethodOptions.SkipRaid)
                    Log($"Lost/Empty Lobbies: {LostRaid}/{Settings.LobbyOptions.SkipRaidLimit}");

                return (false, lobbyTrainers);
            }
            Log($"Raid #{RaidCount} is starting!");
            if (EmptyRaid != 0)
                EmptyRaid = 0;
            return (true, lobbyTrainers);
        }

        private async Task<bool> IsConnectedToLobby(CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesMainAsync(Offsets.TeraLobbyIsConnected, 1, token).ConfigureAwait(false);
            return data[0] != 0x00; // 0 when in lobby but not connected
        }

        private async Task<bool> IsInRaid(CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesMainAsync(Offsets.LoadedIntoDesiredState, 1, token).ConfigureAwait(false);
            return data[0] == 0x02; // 2 when in raid, 1 when not
        }

        private async Task RolloverCorrectionSV(CancellationToken token)
        {
            var scrollroll = Settings.DateTimeFormat switch
            {
                DTFormat.DDMMYY => 0,
                DTFormat.YYMMDD => 2,
                _ => 1,
            };

            for (int i = 0; i < 2; i++)
                await Click(B, 0_150, token).ConfigureAwait(false);

            for (int i = 0; i < 2; i++)
                await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(A, 1_250, token).ConfigureAwait(false); // Enter settings

            await PressAndHold(DDOWN, 2_000, 0_250, token).ConfigureAwait(false); // Scroll to system settings
            await Click(A, 1_250, token).ConfigureAwait(false);

            if (Settings.UseOvershoot)
            {
                await PressAndHold(DDOWN, Settings.HoldTimeForRollover, 1_000, token).ConfigureAwait(false);
                await Click(DUP, 0_500, token).ConfigureAwait(false);
            }
            else if (!Settings.UseOvershoot)
            {
                for (int i = 0; i < 39; i++)
                    await Click(DDOWN, 0_100, token).ConfigureAwait(false);
            }

            await Click(A, 1_250, token).ConfigureAwait(false);
            for (int i = 0; i < 2; i++)
                await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(A, 0_500, token).ConfigureAwait(false);
            for (int i = 0; i < scrollroll; i++) // 0 to roll day for DDMMYY, 1 to roll day for MMDDYY, 3 to roll hour
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(DDOWN, 0_200, token).ConfigureAwait(false);

            for (int i = 0; i < 8; i++) // Mash DRIGHT to confirm
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(A, 0_200, token).ConfigureAwait(false); // Confirm date/time change
            await Click(HOME, 1_000, token).ConfigureAwait(false); // Back to title screen
        }

        private async Task RegroupFromBannedUser(CancellationToken token)
        {
            Log("Attempting to remake lobby..");
            await Click(B, 2_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false);
            await Click(B, 1_000, token).ConfigureAwait(false);
        }

        private async Task InitializeSessionOffsets(CancellationToken token)
        {
            Log("Caching session offsets...");
            OverworldOffset = await SwitchConnection.PointerAll(Offsets.OverworldPointer, token).ConfigureAwait(false);
            ConnectedOffset = await SwitchConnection.PointerAll(Offsets.IsConnectedPointer, token).ConfigureAwait(false);
            RaidBlockPointerP = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerP, token).ConfigureAwait(false);
            RaidBlockPointerK = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerK, token).ConfigureAwait(false);

            var nidPointer = new long[] { Offsets.LinkTradePartnerNIDPointer[0], Offsets.LinkTradePartnerNIDPointer[1], Offsets.LinkTradePartnerNIDPointer[2] };
            for (int p = 0; p < TeraNIDOffsets.Length; p++)
            {
                nidPointer[2] = Offsets.LinkTradePartnerNIDPointer[2] + (p * 0x8);
                TeraNIDOffsets[p] = await SwitchConnection.PointerAll(nidPointer, token).ConfigureAwait(false);
            }
            Log("Caching offsets complete!");
        }
        private async Task EnqueueEmbed(List<string>? names, string message, bool hatTrick, bool disband, bool upnext, bool raidstart, CancellationToken token)
        {
            // Title can only be up to 256 characters.
            var title = hatTrick && names is not null ? $"**🪄🎩✨ {names[0]} with the Hat Trick! ✨🎩🪄**" : Settings.RaidEmbedParameters[RotationCount].Title.Length > 0 ? Settings.RaidEmbedParameters[RotationCount].Title : "Tera Raid Notification";
            if (title.Length > 256)
                title = title[..256];

            // Description can only be up to 4096 characters.
            var description = Settings.RaidEmbedParameters[RotationCount].Description.Length > 0 ? string.Join("\n", Settings.RaidEmbedParameters[RotationCount].Description) : "";
            if (description.Length > 4096)
                description = description[..4096];

            string code = string.Empty;
            if (names is null && !upnext)
            {
                if (Settings.LobbyOptions.LobbyMethodOptions == LobbyMethodOptions.OpenLobby)
                {
                    code = $"**{(Settings.RaidEmbedParameters[RotationCount].IsCoded && EmptyRaid < Settings.LobbyOptions.EmptyRaidLimit ? await GetRaidCode(token).ConfigureAwait(false) : "Free For All")}**";
                }
                else
                {
                    code = $"**{(Settings.RaidEmbedParameters[RotationCount].IsCoded && !Settings.HideRaidCode ? await GetRaidCode(token).ConfigureAwait(false) : Settings.RaidEmbedParameters[RotationCount].IsCoded && Settings.HideRaidCode ? "||Is Hidden!||" : "Free For All")}**";
                }
            }

            if (EmptyRaid == Settings.LobbyOptions.EmptyRaidLimit && Settings.LobbyOptions.LobbyMethodOptions == LobbyMethodOptions.OpenLobby)
                EmptyRaid = 0;

            if (disband) // Wait for trainer to load before disband
                await Task.Delay(5_000, token).ConfigureAwait(false);

            byte[]? bytes = Array.Empty<byte>();
            if (Settings.TakeScreenshot && !upnext)
                bytes = await SwitchConnection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();

            string disclaimer = Settings.RaidEmbedParameters.Count > 1 ? "NotRaidBot v1.8.6 by Gengar\n" : "";

            var userIdWhoRequested = Settings.RaidEmbedParameters[RotationCount].RequestedByUserID;
            var userMention = string.Empty;
            if (upnext && Settings.RaidEmbedParameters[RotationCount].AddedByRACommand)
            {
                userMention = $"<@{userIdWhoRequested}>, your requested raid for {Settings.RaidEmbedParameters[RotationCount].Species} is about to begin!";
            }

            var turl = string.Empty;
            var form = string.Empty;

            Log($"Rotation Count: {RotationCount} | Species is {Settings.RaidEmbedParameters[RotationCount].Species}");
            PK9 pk = new()
            {
                Species = (ushort)Settings.RaidEmbedParameters[RotationCount].Species,
                Form = (byte)Settings.RaidEmbedParameters[RotationCount].SpeciesForm
            };
            if (pk.Form != 0)
                form = $"-{pk.Form}";
            if (Settings.RaidEmbedParameters[RotationCount].IsShiny == true)
                CommonEdits.SetIsShiny(pk, true);
            else
                CommonEdits.SetIsShiny(pk, false);

            if (Settings.RaidEmbedParameters[RotationCount].SpriteAlternateArt && Settings.RaidEmbedParameters[RotationCount].IsShiny)
                turl = AltPokeImg(pk);
            else
                turl = TradeExtensions<PK9>.PokeImg(pk, false, false);

            if (Settings.RaidEmbedParameters[RotationCount].Species is 0)
                turl = "https://i.imgur.com/uHSaGGJ.png";

            // Fetch the dominant color from the image only AFTER turl is assigned
            (int R, int G, int B) dominantColor = GetDominantColor(turl);

            // Use the dominant color, unless it's a disband or hatTrick situation
            var embedColor = disband ? Discord.Color.Red : hatTrick ? Discord.Color.Purple : new Discord.Color(dominantColor.R, dominantColor.G, dominantColor.B);
            var embed = new EmbedBuilder()
            {
                Title = disband ? $"**Raid canceled: [{TeraRaidCode}]**" : upnext && Settings.TotalRaidsToHost != 0 ? $"Preparing Raid {RaidCount}/{Settings.TotalRaidsToHost}" : upnext && Settings.TotalRaidsToHost == 0 ? $"Preparing Raid" : title,
                Color = embedColor,
                Description = disband ? message : upnext ? Settings.RaidEmbedParameters[RotationCount].Title : raidstart ? "" : description,
                ImageUrl = bytes.Length > 0 ? "attachment://zap.jpg" : default,
            }.WithFooter(new EmbedFooterBuilder()
            {
                Text = $"Host: {HostSAV.OT} | Uptime: {StartTime - DateTime.Now:d\\.hh\\:mm\\:ss}\n" +
                       $"Raids: {RaidCount} | Wins: {WinCount} | Losses: {LossCount}\n" + disclaimer
            });

            if (!disband && names is null && !upnext)
            {
                embed.AddField(Settings.IncludeCountdown ? $"**Raid Starting: <t:{DateTimeOffset.Now.ToUnixTimeSeconds() + Settings.TimeToWait}:R>**" : $"**Waiting in lobby!**", $"Raid Code: {code}");
            }

            if (!disband && names is not null && !upnext)
            {
                var players = string.Empty;
                if (names.Count == 0)
                    players = "Though our party did not make it :(";
                else
                {
                    int i = 2;
                    names.ForEach(x =>
                    {
                        players += $"Player {i} - **{x}**\n";
                        i++;
                    });
                }

                embed.AddField($"**Raid #{RaidCount} is starting!**", players);
            }
            var fileName = $"raidecho{RotationCount}.jpg";
            embed.ThumbnailUrl = turl;
            embed.WithImageUrl($"attachment://{fileName}");
            EchoUtil.RaidEmbed(bytes, fileName, embed);
        }
        public (int R, int G, int B) GetDominantColor(string imageUrl)
        {
            using var httpClient = new HttpClient();
            using var response = httpClient.GetAsync(imageUrl).Result;
            using var stream = response.Content.ReadAsStreamAsync().Result;
            using var image = new Bitmap(stream);

            var colorCount = new Dictionary<System.Drawing.Color, int>();

            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var pixelColor = image.GetPixel(x, y);

                    // If the pixel is mostly transparent or very light, skip it
                    if (pixelColor.A < 128 || pixelColor.GetBrightness() > 0.9) continue;

                    var brightnessFactor = (int)(pixelColor.GetBrightness() * 100);
                    var saturationFactor = (int)(pixelColor.GetSaturation() * 100);
                    var combinedFactor = brightnessFactor + saturationFactor; // Combine both brightness and saturation for weight

                    var quantizedColor = System.Drawing.Color.FromArgb(
                        pixelColor.R / 10 * 10,
                        pixelColor.G / 10 * 10,
                        pixelColor.B / 10 * 10
                    );

                    if (colorCount.ContainsKey(quantizedColor))
                    {
                        colorCount[quantizedColor] += combinedFactor;
                    }
                    else
                    {
                        colorCount[quantizedColor] = combinedFactor;
                    }
                }
            }

            if (colorCount.Count == 0)
                return (255, 255, 255);  // Return white if no suitable pixels found

            var dominantColor = colorCount.Aggregate((a, b) => a.Value > b.Value ? a : b).Key;
            return (dominantColor.R, dominantColor.G, dominantColor.B);
        }

        // From PokeTradeBotSV, modified.
        private async Task<bool> ConnectToOnline(PokeTradeHubConfig config, CancellationToken token)
        {
            if (await IsConnectedOnline(ConnectedOffset, token).ConfigureAwait(false))
                return true;

            await Click(X, 3_000, token).ConfigureAwait(false);
            await Click(L, 5_000 + config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);

            // Try one more time.
            if (!await IsConnectedOnline(ConnectedOffset, token).ConfigureAwait(false))
            {
                Log("Failed to connect the first time, trying again...");
                await RecoverToOverworld(token).ConfigureAwait(false);
                await Click(X, 3_000, token).ConfigureAwait(false);
                await Click(L, 5_000 + config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);
            }

            var wait = 0;
            while (!await IsConnectedOnline(ConnectedOffset, token).ConfigureAwait(false))
            {
                await Task.Delay(0_500, token).ConfigureAwait(false);
                if (++wait > 30) // More than 15 seconds without a connection.
                    return false;
            }

            // There are several seconds after connection is established before we can dismiss the menu.
            await Task.Delay(3_000 + config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);
            await Click(A, 1_000, token).ConfigureAwait(false);
            return true;
        }

        // From PokeTradeBotSV.
        private async Task<bool> RecoverToOverworld(CancellationToken token)
        {
            if (await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                return true;

            Log("Attempting to recover to overworld.");
            var attempts = 0;
            while (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
            {
                attempts++;
                if (attempts >= 30)
                    break;

                await Click(B, 1_300, token).ConfigureAwait(false);
                if (await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                    break;

                await Click(B, 2_000, token).ConfigureAwait(false);
                if (await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                    break;

                await Click(A, 1_300, token).ConfigureAwait(false);
                if (await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                    break;
            }

            // We didn't make it for some reason.
            if (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
            {
                Log("Failed to recover to overworld, rebooting the game.");
                await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
            }
            await Task.Delay(1_000, token).ConfigureAwait(false);
            return true;
        }

        public async Task StartGameRaid(PokeTradeHubConfig config, CancellationToken token)
        {
            var timing = config.Timings;
            await Click(A, 1_000 + timing.ExtraTimeLoadProfile, token).ConfigureAwait(false);
            if (timing.AvoidSystemUpdate)
            {
                await Click(DUP, 0_600, token).ConfigureAwait(false);
                await Click(A, 1_000 + timing.ExtraTimeLoadProfile, token).ConfigureAwait(false);
            }

            await Click(A, 1_000 + timing.ExtraTimeCheckDLC, token).ConfigureAwait(false);
            await Click(DUP, 0_600, token).ConfigureAwait(false);
            await Click(A, 0_600, token).ConfigureAwait(false);

            Log("Restarting the game!");

            await Task.Delay(19_000 + timing.ExtraTimeLoadGame, token).ConfigureAwait(false);

            if (Settings.RaidEmbedParameters.Count > 1)
            {
                Log($"Rotation for {Settings.RaidEmbedParameters[RotationCount].Species} has been found.\nAttempting to override seed.");
                await OverrideSeedIndex(SeedIndexToReplace, token).ConfigureAwait(false);
                Log("Seed override completed.");
            }

            await Task.Delay(1_000, token).ConfigureAwait(false);

            for (int i = 0; i < 8; i++)
                await Click(A, 1_000, token).ConfigureAwait(false);

            var timer = 60_000;
            while (!await IsOnOverworldTitle(token).ConfigureAwait(false))
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                timer -= 1_000;
                if (timer <= 0 && !timing.AvoidSystemUpdate)
                {
                    Log("Still not in the game, initiating rescue protocol!");
                    while (!await IsOnOverworldTitle(token).ConfigureAwait(false))
                        await Click(A, 6_000, token).ConfigureAwait(false);
                    break;
                }
            }

            await Task.Delay(5_000 + timing.ExtraTimeLoadOverworld, token).ConfigureAwait(false);
            Log("Back in the overworld!");
            LostRaid = 0;
        }

        private async Task SkipRaidOnLosses(CancellationToken token)
        {
            Log($"We had {Settings.LobbyOptions.SkipRaidLimit} lost/empty raids.. Moving on!");
            await SanitizeRotationCount(token).ConfigureAwait(false);
            await CloseGame(Hub.Config, token).ConfigureAwait(false);
            await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
        }

        private static string AltPokeImg(PKM pkm)
        {
            string pkmform = string.Empty;
            if (pkm.Form != 0)
                pkmform = $"-{pkm.Form}";

            return _ = $"https://raw.githubusercontent.com/zyro670/PokeTextures/main/Placeholder_Sprites/scaled_up_sprites/Shiny/AlternateArt/" + $"{pkm.Species}{pkmform}" + ".png";
        }

        private async Task GetRaidSprite(CancellationToken token)
        {
            PK9 pk = new()
            {
                Species = (ushort)Settings.RaidEmbedParameters[RotationCount].Species
            };
            if (Settings.RaidEmbedParameters[RotationCount].IsShiny)
                CommonEdits.SetIsShiny(pk, true);
            else
                CommonEdits.SetIsShiny(pk, false);
            PK9 pknext = new()
            {
                Species = Settings.RaidEmbedParameters.Count > 1 && RotationCount + 1 < Settings.RaidEmbedParameters.Count ? (ushort)Settings.RaidEmbedParameters[RotationCount + 1].Species : (ushort)Settings.RaidEmbedParameters[0].Species,
            };
            if (Settings.RaidEmbedParameters.Count > 1 && RotationCount + 1 < Settings.RaidEmbedParameters.Count ? Settings.RaidEmbedParameters[RotationCount + 1].IsShiny : Settings.RaidEmbedParameters[0].IsShiny)
                CommonEdits.SetIsShiny(pknext, true);
            else
                CommonEdits.SetIsShiny(pknext, false);

            await Hub.Config.Stream.StartRaid(this, pk, pknext, RotationCount, Hub, 1, token).ConfigureAwait(false);
        }

        #region RaidCrawler
        // via RaidCrawler modified for this proj
        private async Task ReadRaids(CancellationToken token)
        {
            Log("Starting raid reads..");
            if (RaidBlockPointerP == 0)
                RaidBlockPointerP = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerP, token).ConfigureAwait(false);

            if (RaidBlockPointerK == 0)
                RaidBlockPointerK = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerK, token).ConfigureAwait(false);

            string id = await SwitchConnection.GetTitleID(token).ConfigureAwait(false);
            var game = id switch
            {
                RaidCrawler.Core.Structures.Offsets.ScarletID => "Scarlet",
                RaidCrawler.Core.Structures.Offsets.VioletID => "Violet",
                _ => "",
            };
            container = new(game);
            container.SetGame(game);

            var BaseBlockKeyPointer = await SwitchConnection.PointerAll(Offsets.BlockKeyPointer, token).ConfigureAwait(false);

            StoryProgress = await GetStoryProgress(BaseBlockKeyPointer, token).ConfigureAwait(false);
            EventProgress = Math.Min(StoryProgress, 3);

            await ReadEventRaids(BaseBlockKeyPointer, container, token).ConfigureAwait(false);

            var data = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP + RaidBlock.HEADER_SIZE, (int)RaidBlock.SIZE_BASE, token).ConfigureAwait(false);

            (int delivery, int enc) = container.ReadAllRaids(data, StoryProgress, EventProgress, 0, TeraRaidMapParent.Paldea);
            if (enc > 0)
                Log($"Failed to find encounters for {enc} raid(s).");

            if (delivery > 0)
                Log($"Invalid delivery group ID for {delivery} raid(s). Try deleting the \"cache\" folder.");

            var raids = container.Raids;
            var encounters = container.Encounters;
            var rewards = container.Rewards;
            container.ClearRaids();
            container.ClearEncounters();
            container.ClearRewards();

            data = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerK, (int)RaidBlock.SIZE_KITAKAMI, token).ConfigureAwait(false);

            (delivery, enc) = container.ReadAllRaids(data, StoryProgress, EventProgress, 0, TeraRaidMapParent.Kitakami);

            if (enc > 0)
                Log($"Failed to find encounters for {enc} raid(s).");

            if (delivery > 0)
                Log($"Invalid delivery group ID for {delivery} raid(s). Try deleting the \"cache\" folder.");

            var allRaids = raids.Concat(container.Raids).ToList().AsReadOnly();
            var allEncounters = encounters.Concat(container.Encounters).ToList().AsReadOnly();
            var allRewards = rewards.Concat(container.Rewards).ToList().AsReadOnly();

            container.SetRaids(allRaids);
            container.SetEncounters(allEncounters);
            container.SetRewards(allRewards);

            bool done = false;
            for (int i = 0; i < container.Raids.Count; i++)
            {
                if (done is true)
                    break;

                var (pk, seed) = IsSeedReturned(container.Encounters[i], container.Raids[i]);
                for (int a = 0; a < Settings.RaidEmbedParameters.Count; a++)
                {
                    if (done is true)
                        break;

                    var set = uint.Parse(Settings.RaidEmbedParameters[a].Seed, NumberStyles.AllowHexSpecifier);
                    if (seed == set)
                    {
                        var res = GetSpecialRewards(container.Rewards[i]);
                        if (string.IsNullOrEmpty(res))
                            res = string.Empty;
                        else
                            res = "**Special Rewards:**\n" + res;
                        Log($"Seed {seed:X8} found for {(Species)container.Encounters[i].Species}");
                        Settings.RaidEmbedParameters[a].Seed = $"{seed:X8}";
                        var stars = container.Raids[i].IsEvent ? container.Encounters[i].Stars : RaidExtensions.GetStarCount(container.Raids[i], container.Raids[i].Difficulty, StoryProgress, container.Raids[i].IsBlack);
                        string starcount = string.Empty;
                        switch (stars)
                        {
                            case 1: starcount = "1 ☆"; break;
                            case 2: starcount = "2 ☆"; break;
                            case 3: starcount = "3 ☆"; break;
                            case 4: starcount = "4 ☆"; break;
                            case 5: starcount = "5 ☆"; break;
                            case 6: starcount = "6 ☆"; break;
                            case 7: starcount = "7 ☆"; break;
                        }
                        Settings.RaidEmbedParameters[a].IsShiny = container.Raids[i].IsShiny;
                        Settings.RaidEmbedParameters[a].CrystalType = container.Raids[i].IsBlack ? TeraCrystalType.Black : container.Raids[i].IsEvent && stars == 7 ? TeraCrystalType.Might : container.Raids[i].IsEvent ? TeraCrystalType.Distribution : TeraCrystalType.Base;
                        Settings.RaidEmbedParameters[a].Species = (Species)container.Encounters[i].Species;
                        Settings.RaidEmbedParameters[a].SpeciesForm = container.Encounters[i].Form;
                        var pkinfo = Hub.Config.StopConditions.GetRaidPrintName(pk);
                        pkinfo += $"\nTera Type: {(MoveType)container.Raids[i].TeraType}";
                        var strings = GameInfo.GetStrings(1);
                        var moves = new ushort[4] { container.Encounters[i].Move1, container.Encounters[i].Move2, container.Encounters[i].Move3, container.Encounters[i].Move4 };
                        var movestr = string.Concat(moves.Where(z => z != 0).Select(z => $"{strings.Move[z]}ㅤ{Environment.NewLine}")).TrimEnd(Environment.NewLine.ToCharArray());
                        var extramoves = string.Empty;
                        if (container.Encounters[i].ExtraMoves.Length != 0)
                        {
                            var extraMovesList = container.Encounters[i].ExtraMoves.Where(z => z != 0).Select(z => $"{strings.Move[z]}ㅤ{Environment.NewLine}");
                            extramoves = string.Concat(extraMovesList.Take(extraMovesList.Count() - 1)).TrimEnd(Environment.NewLine.ToCharArray());
                            extramoves += extraMovesList.LastOrDefault()?.TrimEnd(Environment.NewLine.ToCharArray());
                        }

                        if (Settings.PresetFilters.UsePresetFile)
                        {
                            string tera = $"{(MoveType)container.Raids[i].TeraType}";
                            if (!string.IsNullOrEmpty(Settings.RaidEmbedParameters[a].Title) && !Settings.PresetFilters.ForceTitle)
                                ModDescription[0] = Settings.RaidEmbedParameters[a].Title;

                            if (Settings.RaidEmbedParameters[a].Description.Length > 0 && !Settings.PresetFilters.ForceDescription)
                            {
                                string[] presetOverwrite = new string[Settings.RaidEmbedParameters[a].Description.Length + 1];
                                presetOverwrite[0] = ModDescription[0];
                                for (int l = 0; l < Settings.RaidEmbedParameters[a].Description.Length; l++)
                                    presetOverwrite[l + 1] = Settings.RaidEmbedParameters[a].Description[l];

                                ModDescription = presetOverwrite;
                            }

                            var raidDescription = ProcessRaidPlaceholders(ModDescription, pk);

                            for (int j = 0; j < raidDescription.Length; j++)
                            {
                                raidDescription[j] = raidDescription[j]
                                .Replace("{tera}", tera)
                                .Replace("{difficulty}", $"{stars}")
                                .Replace("{stars}", starcount)
                                .Trim();
                                raidDescription[j] = Regex.Replace(raidDescription[j], @"\s+", " ");
                            }

                            if (Settings.PresetFilters.IncludeMoves)
                                raidDescription = raidDescription.Concat(new string[] { Environment.NewLine, movestr, extramoves }).ToArray();

                            if (Settings.PresetFilters.IncludeRewards)
                                raidDescription = raidDescription.Concat(new string[] { res.Replace("\n", Environment.NewLine) }).ToArray();

                            if (Settings.PresetFilters.TitleFromPreset)
                            {
                                if (string.IsNullOrEmpty(Settings.RaidEmbedParameters[a].Title) || Settings.PresetFilters.ForceTitle)
                                    Settings.RaidEmbedParameters[a].Title = raidDescription[0];

                                if (Settings.RaidEmbedParameters[a].Description == null || Settings.RaidEmbedParameters[a].Description.Length == 0 || Settings.RaidEmbedParameters[a].Description.All(string.IsNullOrEmpty) || Settings.PresetFilters.ForceDescription)
                                    Settings.RaidEmbedParameters[a].Description = raidDescription.Skip(1).ToArray();
                            }
                            else if (!Settings.PresetFilters.TitleFromPreset)
                            {
                                if (Settings.RaidEmbedParameters[a].Description == null || Settings.RaidEmbedParameters[a].Description.Length == 0 || Settings.RaidEmbedParameters[a].Description.All(string.IsNullOrEmpty) || Settings.PresetFilters.ForceDescription)
                                    Settings.RaidEmbedParameters[a].Description = raidDescription.ToArray();
                            }
                        }

                        else if (!Settings.PresetFilters.UsePresetFile)
                        {
                            Settings.RaidEmbedParameters[a].Description = new[] { "\n**Raid Info:**", pkinfo, "\n**Moveset:**", movestr, extramoves, BaseDescription, res };
                            Settings.RaidEmbedParameters[a].Title = $"{(Species)container.Encounters[i].Species} {starcount} - {(MoveType)container.Raids[i].TeraType}";
                        }

                        Settings.RaidEmbedParameters[a].IsSet = true;
                        if (RaidCount == 0)
                        {
                            RotatingRaidSettingsSV.RotatingRaidParameters param = new();
                            param = Settings.RaidEmbedParameters[a];
                            foreach (var p in Settings.RaidEmbedParameters.ToList())
                            {
                                if (p.Seed == param.Seed)
                                    break;
                                RotationCount++;

                                if (RotationCount >= Settings.RaidEmbedParameters.Count)
                                {
                                    RotationCount = 0;
                                }
                            }
                        }
                        SeedIndexToReplace = i;
                        done = true;
                    }
                }
            }
        }
        #endregion
    }
}