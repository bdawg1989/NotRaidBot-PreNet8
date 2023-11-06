using Discord;
using Newtonsoft.Json;
using PKHeX.Core;
using RaidCrawler.Core.Structures;
using SysBot.Base;
using SysBot.Pokemon.SV.BotRaid.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchButton;
using static SysBot.Pokemon.RotatingRaidSettingsSV;
using static SysBot.Pokemon.SV.BotRaid.Blocks;

namespace SysBot.Pokemon.SV.BotRaid
{
    public class RotatingRaidBotSV : PokeRoutineExecutor9SV
    {
        private readonly PokeRaidHub<PK9> Hub;
        private readonly RotatingRaidSettingsSV Settings;
        private RemoteControlAccessList RaiderBanList => Settings.RaiderBanList;

        public RotatingRaidBotSV(PokeBotState cfg, PokeRaidHub<PK9> hub) : base(cfg)
        {
            Hub = hub;
            Settings = hub.Config.RotatingRaidSV;
        }

        private int LobbyError;
        private int RaidCount;
        private int WinCount;
        private int LossCount;
        private int SeedIndexToReplace = -1;
        public static GameProgress GameProgress;
        public int StoryProgress;
        private int EventProgress;
        private int EmptyRaid = 0;
        private int LostRaid = 0;
        private bool firstRun = true;
        bool timedOut = false;
        public static int RotationCount { get; set; }
        private ulong TodaySeed;
        private ulong OverworldOffset;
        private ulong ConnectedOffset;
        private ulong RaidBlockPointerP;
        private ulong RaidBlockPointerK;
        private readonly ulong[] TeraNIDOffsets = new ulong[3];
        private string TeraRaidCode { get; set; } = string.Empty;
        private string BaseDescription = string.Empty;
        private readonly Dictionary<ulong, int> RaidTracker = new();
        private SAV9SV HostSAV = new();
        private DateTime StartTime = DateTime.Now;
        public static RaidContainer? container;
        private static int attemptCount = 0;
        public static bool IsKitakami = false;
        public class PlayerInfo
        {
            public string OT { get; set; }
            public int RaidCount { get; set; }
        }


        public override async Task MainLoop(CancellationToken token)
        {

            if (Settings.GenerateRaidsFromFile)
            {
                GenerateSeedsFromFile();
                Log("Done.");
                Settings.GenerateRaidsFromFile = false;
            }

            if (Settings.ConfigureRolloverCorrection)
            {
                await RolloverCorrectionSV(token).ConfigureAwait(false);
                return;
            }

            if (Settings.ActiveRaids.Count < 1)
            {
                Log("ActiveRaids cannot be 0. Please setup your parameters for the raid(s) you are hosting.");
                return;
            }

            if (Settings.TimeToWait is < 0 or > 180)
            {
                Log("Time to wait must be between 0 and 180 seconds.");
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
            finally
            {
                SaveSeeds();
            }
            Log($"Ending {nameof(RotatingRaidBotSV)} loop.");
            await HardStop().ConfigureAwait(false);
        }
        public class PlayerDataStorage
        {
            private readonly string filePath;

            public PlayerDataStorage(string baseDirectory)
            {
                var directoryPath = Path.Combine(baseDirectory, "raidfilessv");
                Directory.CreateDirectory(directoryPath);
                filePath = Path.Combine(directoryPath, "player_data.json");

                if (!File.Exists(filePath))
                    File.WriteAllText(filePath, "{}"); // Create a new JSON file if it does not exist.
            }

            public Dictionary<ulong, PlayerInfo> LoadPlayerData()
            {
                string json = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<Dictionary<ulong, PlayerInfo>>(json) ?? new Dictionary<ulong, PlayerInfo>();
            }

            public void SavePlayerData(Dictionary<ulong, PlayerInfo> data)
            {
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
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
                File.WriteAllText(rotationpath, "000091EC-Kricketune-3-6,0000717F-Seviper-3-6");
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

        private void SaveSeeds()
        {
            // Exit the function if saving seeds to file is not enabled
            if (!Settings.SaveSeedsToFile)
                return;

            // Filter out raids that don't need to be saved
            var raidsToSave = Settings.ActiveRaids.Where(raid => !raid.AddedByRACommand).ToList();

            // Exit the function if there are no raids to save
            if (!raidsToSave.Any())
                return;

            // Define directory and file paths
            var directoryPath = "raidfilessv";
            var fileName = "savedSeeds.txt";
            var savePath = Path.Combine(directoryPath, fileName);

            // Create directory if it doesn't exist
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            // Initialize StringBuilder to build the save string
            StringBuilder sb = new StringBuilder();

            // Loop through each raid to be saved
            foreach (var raid in raidsToSave)
            {
                // Increment the StoryProgressLevel by 1 before saving
                int incrementedStoryProgressLevel = raid.StoryProgressLevel + 1;

                // Build the string to save, including the incremented StoryProgressLevel
                sb.Append($"{raid.Seed}-{raid.Species}-{raid.DifficultyLevel}-{incrementedStoryProgressLevel},");
            }

            // Remove the trailing comma at the end
            if (sb.Length > 0)
                sb.Length--;

            // Write the built string to the file
            File.WriteAllText(savePath, sb.ToString());
        }

        private void DirectorySearch(string sDir, string data)
        {
            // Clear the active raids before populating it
            Settings.ActiveRaids.Clear();

            // Read the entire content from the file into a string
            string contents = File.ReadAllText(sDir);

            // Split the string based on commas to get each raid entry
            string[] moninfo = contents.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            // Iterate over each raid entry
            for (int i = 0; i < moninfo.Length; i++)
            {
                // Split the entry based on dashes to get individual pieces of information
                var div = moninfo[i].Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);

                // Check if the split result has exactly 4 parts
                if (div.Length != 4)
                {
                    Log($"Error processing entry: {moninfo[i]}. Expected 4 parts but found {div.Length}. Skipping this entry.");
                    continue; // Skip processing this entry and move to the next one
                }

                // Extracting seed, title, and difficulty level
                var monseed = div[0];
                var montitle = div[1];

                if (!int.TryParse(div[2], out int difficultyLevel))
                {
                    Log($"Unable to parse difficulty level for entry: {moninfo[i]}");
                    continue;
                }

                // Extract and convert the StoryProgressLevel
                if (!int.TryParse(div[3], out int storyProgressLevelFromSeed))
                {
                    Log($"Unable to parse StoryProgressLevel for entry: {moninfo[i]}");
                    continue;
                }

                int convertedStoryProgressLevel = storyProgressLevelFromSeed - 1; // Converting based on given conditions

                // Determine the TeraCrystalType based on the difficulty level
                TeraCrystalType type = difficultyLevel switch
                {
                    6 => TeraCrystalType.Black,
                    7 => TeraCrystalType.Might,
                    _ => TeraCrystalType.Base,
                };

                // Create a new RotatingRaidParameters object and populate its properties
                RotatingRaidParameters param = new()
                {
                    Seed = monseed,
                    Title = montitle,
                    Species = RaidExtensions<PK9>.EnumParse<Species>(montitle),
                    CrystalType = type,
                    PartyPK = new[] { data },
                    DifficultyLevel = difficultyLevel,  // Set the DifficultyLevel
                    StoryProgressLevel = convertedStoryProgressLevel  // Set the converted StoryProgressLevel
                };

                // Add the RotatingRaidParameters object to the ActiveRaids list
                Settings.ActiveRaids.Add(param);

                // Log the raid parameter generation
                Log($"Parameters generated from text file for {montitle}.");
            }
        }

        private async Task InnerLoop(CancellationToken token)
        {
            bool partyReady;
            List<(ulong, RaidMyStatus)> lobbyTrainers;
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
                    Log($"Preparing to store index for {Settings.ActiveRaids[RotationCount].Species}");
                    await ReadRaids(true, token).ConfigureAwait(false);
                }

                if (!Settings.ActiveRaids[RotationCount].IsSet)
                {
                    Log($"Preparing parameter for {Settings.ActiveRaids[RotationCount].Species}");
                    await ReadRaids(false, token).ConfigureAwait(false);
                }
                else
                    Log($"Parameter for {Settings.ActiveRaids[RotationCount].Species} has been set previously, skipping raid reads.");

                var currentSeed = BitConverter.ToUInt64(await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 8, token).ConfigureAwait(false), 0);
                if (TodaySeed != currentSeed || LobbyError >= 3)
                {
                    var msg = "";
                    if (TodaySeed != currentSeed)
                        msg = $"Current Today Seed {currentSeed:X8} does not match Starting Today Seed: {TodaySeed:X8}.\n ";

                    if (LobbyError >= 3)
                    {
                        msg = $"Failed to create a lobby {LobbyError} times.\n ";
                        dayRoll++;
                    }

                    if (dayRoll != 0 && SeedIndexToReplace != -1 && RaidCount != 0)
                    {
                        Log(msg + "Raid Lost initiating recovery sequence.");
                        bool denFound = false;
                        while (!denFound)
                        {
                            await Click(B, 0_500, token).ConfigureAwait(false);
                            await Click(HOME, 3_500, token).ConfigureAwait(false);
                            Log("Closed out of the game!");

                            await RolloverCorrectionSV(token).ConfigureAwait(false);
                            await Click(A, 1_500, token).ConfigureAwait(false);
                            Log("Back in the game!");

                            while (!await IsConnectedOnline(ConnectedOffset, token).ConfigureAwait(false))
                            {
                                Log("Connecting...");
                                if (!await ConnectToOnline(Hub.Config, token).ConfigureAwait(false))
                                    continue;

                                await RecoverToOverworld(token).ConfigureAwait(false);
                            }

                            await RecoverToOverworld(token).ConfigureAwait(false);

                            // Check if there's a lobby.
                            if (!await GetLobbyReady(true, token).ConfigureAwait(false))
                            {
                                continue;
                            }
                            else
                            {
                                Log("Den Found, continuing routine!");
                                TodaySeed = BitConverter.ToUInt64(await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 8, token).ConfigureAwait(false), 0);
                                LobbyError = 0;
                                denFound = true;
                                await Click(B, 1_000, token).ConfigureAwait(false);
                                await Task.Delay(2_000, token).ConfigureAwait(false);
                                await Click(A, 1_000, token).ConfigureAwait(false);
                                await Task.Delay(5_000, token).ConfigureAwait(false);
                                await Click(B, 1_000, token).ConfigureAwait(false);
                                await Click(B, 1_000, token).ConfigureAwait(false);
                                await Task.Delay(1_000, token).ConfigureAwait(false);

                            }
                        };
                        await Task.Delay(0_050, token).ConfigureAwait(false);
                        if (denFound)
                        {
                            await SVSaveGameOverworld(token).ConfigureAwait(false);
                            await Task.Delay(0_500, token).ConfigureAwait(false);
                            await Click(B, 1_000, token).ConfigureAwait(false);
                            continue;
                        }
                    }
                    Log(msg);
                    await CloseGame(Hub.Config, token).ConfigureAwait(false);
                    await RolloverCorrectionSV(token).ConfigureAwait(false);
                    await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
                    dayRoll++;
                    continue;
                }

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
                if (!await GetLobbyReady(false, token).ConfigureAwait(false))
                    continue;

                if (Settings.ActiveRaids[RotationCount].AddedByRACommand)
                {
                    var user = Settings.ActiveRaids[RotationCount].User;
                    var code = await GetRaidCode(token).ConfigureAwait(false);
                    if (user != null)
                    {
                        try
                        {
                            await user.SendMessageAsync($"Your Raid Code is **{code}**").ConfigureAwait(false);
                        }
                        catch (Discord.Net.HttpException ex)
                        {
                            // Handle exception (e.g., log the error or send a message to a logging channel)
                            Log($"Failed to send DM to {user.Username}. They might have DMs turned off. Exception: {ex.Message}");
                        }
                    }
                }


                // Read trainers until someone joins.
                (partyReady, lobbyTrainers) = await ReadTrainers(token).ConfigureAwait(false);
                if (!partyReady)
                {
                    if (LostRaid >= Settings.LobbyOptions.SkipRaidLimit && Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.SkipRaid)
                    {
                        await SkipRaidOnLosses(token).ConfigureAwait(false);
                        EmptyRaid = 0;
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
                await CompleteRaid(token).ConfigureAwait(false);
                raidsHosted++;
                if (raidsHosted == Settings.TotalRaidsToHost && Settings.TotalRaidsToHost > 0)
                    break;
            }
            if (Settings.TotalRaidsToHost > 0 && raidsHosted != 0)
                Log("Total raids to host has been met.");
        }

        public override async Task HardStop()
        {
            try
            {
                Directory.Delete("cache", true);
            }
            catch (Exception)
            {
                //dgaf about cache not existing
            }

            // Remove all entries in ActiveRaids where AddedByRACommand is true
            Settings.ActiveRaids.RemoveAll(p => p.AddedByRACommand);

            await CleanExit(CancellationToken.None).ConfigureAwait(false);
        }

        private async Task LocateSeedIndex(CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 2304, token).ConfigureAwait(false);
            for (int i = 0; i < 69; i++)
            {
                var seed = BitConverter.ToUInt32(data.Slice(0x20 + i * 0x20, 4));
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
                    IsKitakami = true;
                    return;
                }
            }
            Log($"Index not located.");
        }

        private async Task CompleteRaid(CancellationToken token)
        {
            var trainers = new List<(ulong, RaidMyStatus)>();

            // Ensure connection to lobby and log status
            if (!await CheckIfConnectedToLobbyAndLog(token))
                return;

            // Ensure in raid
            if (!await EnsureInRaid(token))
                return;

            // Update final list of lobby trainers
            var lobbyTrainersFinal = new List<(ulong, RaidMyStatus)>();
            if (!await UpdateLobbyTrainersFinal(lobbyTrainersFinal, trainers, token))
                return;

            // Handle duplicates and embeds
            if (!await HandleDuplicatesAndEmbeds(lobbyTrainersFinal, token))
                return;

            // Process battle actions
            if (!await ProcessBattleActions(token))
                return;

            // Handle end of raid actions
            bool ready = await HandleEndOfRaidActions(token);

            // Finalize raid completion
            await FinalizeRaidCompletion(trainers, ready, token);
        }



        private async Task<bool> CheckIfConnectedToLobbyAndLog(CancellationToken token)
        {
            if (await IsConnectedToLobby(token).ConfigureAwait(false))
            {
                Log("Preparing for battle!");
                return true;
            }
            return false;
        }
        private async Task<bool> EnsureInRaid(CancellationToken linkedToken)
        {
            while (!await IsInRaid(linkedToken).ConfigureAwait(false))
            {
                if (linkedToken.IsCancellationRequested)
                {
                    Log("Oops! Something went wrong, resetting to recover.");
                    await ReOpenGame(Hub.Config, linkedToken).ConfigureAwait(false);
                    return false;
                }
                await Click(A, 1_000, linkedToken).ConfigureAwait(false);
            }
            return true;
        }
        public async Task<bool> UpdateLobbyTrainersFinal(List<(ulong, RaidMyStatus)> lobbyTrainersFinal, List<(ulong, RaidMyStatus)> trainers, CancellationToken token)
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var storage = new PlayerDataStorage(baseDirectory);
            var playerData = storage.LoadPlayerData();

            // Clear NIDs to refresh player check.
            await SwitchConnection.WriteBytesAbsoluteAsync(new byte[32], TeraNIDOffsets[0], token).ConfigureAwait(false);
            await Task.Delay(5_000, token).ConfigureAwait(false);

            // Loop through trainers again in case someone disconnected.
            for (int i = 0; i < 3; i++)
            {
                try
                {
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

                    if (!playerData.TryGetValue(nid, out var info))
                    {
                        // New player
                        playerData[nid] = new PlayerInfo { OT = trainer.OT, RaidCount = 1 };
                        Log($"New Player: {trainer.OT} | TID: {trainer.DisplayTID} | NID: {nid}.");
                    }
                    else
                    {
                        // Returning player
                        info.RaidCount++;
                        playerData[nid] = info; // Update the info back to the dictionary.
                        Log($"Returning Player: {trainer.OT} | TID: {trainer.DisplayTID} | NID: {nid} | Raids: {info.RaidCount}");
                    }
                }
                catch (IndexOutOfRangeException ex)
                {
                    Log($"Index out of range exception caught: {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    Log($"An unknown error occurred: {ex.Message}");
                    return false;
                }
            }

            // Save player data after processing all players.
            storage.SavePlayerData(playerData);
            return true;
        }
        private async Task<bool> HandleDuplicatesAndEmbeds(List<(ulong, RaidMyStatus)> lobbyTrainersFinal, CancellationToken token)
        {
            var nidDupe = lobbyTrainersFinal.Select(x => x.Item1).ToList();
            var dupe = lobbyTrainersFinal.Count > 1 && nidDupe.Distinct().Count() == 1;
            if (dupe)
            {
                // We read bad data, reset game to end early and recover.
                var msg = "Oops! Something went wrong, resetting to recover.";
                bool success = false;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        await Task.Delay(20_000, token).ConfigureAwait(false);
                        await EnqueueEmbed(null, "", false, false, false, false, token).ConfigureAwait(false);
                        success = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log($"Attempt {attempt} failed with error: {ex.Message}");
                        if (attempt == 3)
                        {
                            Log("All attempts failed. Continuing without sending embed.");
                        }
                    }
                }

                if (!success)
                {
                    await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                    return false;
                }
            }

            var names = lobbyTrainersFinal.Select(x => x.Item2.OT).ToList();
            bool hatTrick = lobbyTrainersFinal.Count == 3 && names.Distinct().Count() == 1;

            bool embedSuccess = false;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await EnqueueEmbed(names, "", hatTrick, false, false, true, token).ConfigureAwait(false);
                    embedSuccess = true;
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Attempt {attempt} failed with error: {ex.Message}");
                    if (attempt == 3)
                    {
                        Log("All attempts failed. Continuing without sending embed.");
                    }
                }
            }

            return embedSuccess;
        }
        private async Task<bool> ProcessBattleActions(CancellationToken token)
        {
            int nextUpdateMinute = 2;
            DateTime battleStartTime = DateTime.Now;
            bool hasPerformedAction1 = false;
            bool timedOut = false;

            while (await IsConnectedToLobby(token).ConfigureAwait(false))
            {
                TimeSpan timeInBattle = DateTime.Now - battleStartTime;

                // Check for battle timeout
                if (timeInBattle.TotalMinutes >= 10)
                {
                    Log("Battle timed out after 10 minutes. Even Netflix asked if I was still watching...");
                    timedOut = true;
                    break;
                }

                // Handle the first action with a delay
                if (!hasPerformedAction1)
                {
                    int action1DelayInSeconds = Settings.ActiveRaids[RotationCount].Action1Delay;
                    var action1Name = Settings.ActiveRaids[RotationCount].Action1;
                    int action1DelayInMilliseconds = action1DelayInSeconds * 1000;
                    Log($"Waiting {action1DelayInSeconds} seconds. No rush, we're chilling.");
                    await Task.Delay(action1DelayInMilliseconds, token).ConfigureAwait(false);
                    await MyActionMethod(token).ConfigureAwait(false);
                    Log($"{action1Name} done. Wasn't that fun?");
                    hasPerformedAction1 = true;
                }
                else
                {
                    // Execute raid actions based on configuration
                    switch (Settings.LobbyOptions.Action)
                    {
                        case RaidAction.AFK:
                            await Task.Delay(3_000, token).ConfigureAwait(false);
                            break;

                        case RaidAction.MashA:
                            if (await IsConnectedToLobby(token).ConfigureAwait(false))
                            {
                                int mashADelayInMilliseconds = (int)(Settings.LobbyOptions.MashADelay * 1000);
                                await Click(A, mashADelayInMilliseconds, token).ConfigureAwait(false);
                            }
                            break;
                    }
                }

                // Periodic battle status log at 2-minute intervals
                if (timeInBattle.TotalMinutes >= nextUpdateMinute)
                {
                    Log($"{nextUpdateMinute} minutes have passed. We are still in battle...");
                    nextUpdateMinute += 2; // Update the time for the next status update.
                }

                // Make sure to wait some time before the next iteration to prevent a tight loop
                await Task.Delay(1000, token); // Wait for a second before checking again
            }

            return !timedOut;
        }

        private async Task<bool> HandleEndOfRaidActions(CancellationToken token)
        {
            LobbyFiltersCategory settings = new LobbyFiltersCategory(); 

            Log("Raid lobby disbanded!");
            await Task.Delay(1_500 + settings.ExtraTimeLobbyDisband, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(DDOWN, 0_500, token).ConfigureAwait(false);
            await Click(A, 0_500, token).ConfigureAwait(false);

            if (Settings.ActiveRaids.Count > 1)
            {
                await SanitizeRotationCount(token).ConfigureAwait(false);
            }

            await EnqueueEmbed(null, "", false, false, true, false, token).ConfigureAwait(false);
            bool ready = true;

            if (Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.SkipRaid)
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

            return ready;
        }
        private async Task FinalizeRaidCompletion(List<(ulong, RaidMyStatus)> trainers, bool ready, CancellationToken token)
        {
            Log("Returning to overworld...");

            while (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                await Click(A, 1_000, token).ConfigureAwait(false);

            await CountRaids(trainers, token).ConfigureAwait(false);
            await LocateSeedIndex(token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await CloseGame(Hub.Config, token).ConfigureAwait(false);

            if (ready)
                await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
            else
            {
                if (Settings.ActiveRaids.Count > 1)
                {
                    if (RotationCount < Settings.ActiveRaids.Count && Settings.ActiveRaids.Count > 1)
                        RotationCount++;
                    if (RotationCount >= Settings.ActiveRaids.Count && Settings.ActiveRaids.Count > 1)
                    {
                        RotationCount = 0;
                        Log($"Resetting Rotation Count to {RotationCount}");
                    }
                    Log($"Moving on to next rotation for {Settings.ActiveRaids[RotationCount].Species}.");
                    await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
                }
                else
                    await StartGame(Hub.Config, token).ConfigureAwait(false);
            }

            if (Settings.KeepDaySeed)
                await OverrideTodaySeed(token).ConfigureAwait(false);
        }

        public async Task MyActionMethod(CancellationToken token)
        {
            // Let's rock 'n roll with these moves!
            switch (Settings.ActiveRaids[RotationCount].Action1)
            {
                case Action1Type.GoAllOut:
                    await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                case Action1Type.HangTough:
                case Action1Type.HealUp:
                    await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    int ddownTimes = Settings.ActiveRaids[RotationCount].Action1 == Action1Type.HangTough ? 1 : 2;
                    for (int i = 0; i < ddownTimes; i++)
                    {
                        await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                case Action1Type.Move1:
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                case Action1Type.Move2:
                case Action1Type.Move3:
                case Action1Type.Move4:
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    int moveDdownTimes = Settings.ActiveRaids[RotationCount].Action1 == Action1Type.Move2 ? 1 : Settings.ActiveRaids[RotationCount].Action1 == Action1Type.Move3 ? 2 : 3;
                    for (int i = 0; i < moveDdownTimes; i++)
                    {
                        await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                default:
                    Console.WriteLine("Unknown action, what's the move?");
                    throw new InvalidOperationException("Unknown action type!");
            }

            // Check for Action2, if defined.
            // TODO: Implement Action2 logic here.
        }

        private async Task CountRaids(List<(ulong, RaidMyStatus)>? trainers, CancellationToken token)
        {
            int countP = 0;
            int countK = 0;

            // Read data from RaidBlockPointerP
            var dataP = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 2304, token).ConfigureAwait(false);
            for (int i = 0; i < 69; i++)
            {
                var seed = BitConverter.ToUInt32(dataP.Slice(0 + i * 32, 4));
                if (seed != 0)
                    countP++;
            }

            // Read data from RaidBlockPointerK for the remaining raids
            var dataK = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerK, 26 * 32, token).ConfigureAwait(false);
            for (int i = 0; i < 26; i++)
            {
                var seed = BitConverter.ToUInt32(dataK.Slice(0 + i * 32, 4));
                if (seed != 0)
                    countK++;
            }

            if (trainers is not null)
            {
                Log("Back in the overworld, checking if we won or lost.");

                if (countP <= 68 && countK == 26 || countP == 69 && countK <= 25)
                {
                    Log("Yay!  We defeated the raid!");
                    WinCount++;
                }
                else
                {
                    Log("Dang, we lost the raid.");
                    LossCount++;
                }
            }
        }

        private async Task OverrideTodaySeed(CancellationToken token)
        {
            Log("Attempting to override Today Seed...");

            var todayoverride = BitConverter.GetBytes(TodaySeed);
            List<long> ptr = new(Offsets.RaidBlockPointerP);
            ptr[3] += 0x8;
            await SwitchConnection.PointerPoke(todayoverride, ptr, token).ConfigureAwait(false);

            Log("Today Seed override complete.");
        }

        private async Task OverrideSeedIndex(int index, CancellationToken token)
        {
            if (index == -1)
                return;
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

            var seed = uint.Parse(Settings.ActiveRaids[RotationCount].Seed, NumberStyles.AllowHexSpecifier);
            byte[] inj = BitConverter.GetBytes(seed);
            var currseed = await SwitchConnection.PointerPeek(4, ptr, token).ConfigureAwait(false);
            Log($"Replacing {BitConverter.ToString(currseed)} with {BitConverter.ToString(inj)}.");
            await SwitchConnection.PointerPoke(inj, ptr, token).ConfigureAwait(false);

            var ptr2 = ptr;
            ptr2[3] += 0x08;
            var crystal = BitConverter.GetBytes((int)Settings.ActiveRaids[RotationCount].CrystalType);
            var currcrystal = await SwitchConnection.PointerPeek(1, ptr2, token).ConfigureAwait(false);
            if (currcrystal != crystal)
                await SwitchConnection.PointerPoke(crystal, ptr2, token).ConfigureAwait(false);
        }

        private async Task<bool> DenStatus(int index, CancellationToken token)
        {
            if (index == -1)
                return false;
            List<long> ptr;
            if (index < 69)
            {
                ptr = new(Offsets.RaidBlockPointerP)
                {
                    [3] = 0x40 + (index + 1) * 0x20 - 0x10
                };
            }
            else
            {
                ptr = new(Offsets.RaidBlockPointerK)
                {
                    [3] = 0xCE8 + (index - 69) * 0x20 - 0x10
                };
            }
            var data = await SwitchConnection.PointerPeek(2, ptr, token).ConfigureAwait(false);
            var status = BitConverter.ToUInt16(data);
            var msg = status == 1 ? "active" : "inactive"; // 1 = Den Found
            Log($"Den is {msg}.");
            return status == 1;
        }

        private async Task SanitizeRotationCount(CancellationToken token)
        {
            await Task.Delay(0_050, token).ConfigureAwait(false);

            // First, check if ActiveRaids is empty. If it is, no further processing is needed.
            if (Settings.ActiveRaids.Count == 0)
            {
                Log("ActiveRaids is empty. Exiting SanitizeRotationCount.");
                return;
            }

            // Validate that the RotationCount is within the range of ActiveRaids.
            if (RotationCount >= Settings.ActiveRaids.Count)
            {
                RotationCount = 0;
                Log($"Resetting Rotation Count to {RotationCount}");
            }

            // Check if the current raid was added by the RA command, and remove it.
            if (Settings.ActiveRaids[RotationCount].AddedByRACommand)
            {
                Log($"Raid for {Settings.ActiveRaids[RotationCount].Species} was added via RA command and will be removed from the rotation list.");
                Settings.ActiveRaids.RemoveAt(RotationCount);
                // Do not increment the RotationCount here, since the next raid has now taken the index of the removed raid.

                // Re-check RotationCount after removal to avoid index out-of-range errors
                if (RotationCount >= Settings.ActiveRaids.Count)
                {
                    RotationCount = 0;
                    Log($"Resetting Rotation Count to {RotationCount}");
                }
            }
            else
            {
                // If the current raid wasn't added by the RA command, move to the next raid.
                RotationCount++;
            }

            if (firstRun)
            {
                RotationCount = 0; // Start back at 0 on first run.
                Log($"Resetting Rotation Count to {RotationCount}");
                firstRun = false;
            }

            if (Settings.RandomRotation)
            {
                ProcessRandomRotation();
                return;  // Exit early after processing random rotation
            }

            // Check RotationCount again after possibly incrementing it
            if (RotationCount >= Settings.ActiveRaids.Count)
            {
                RotationCount = 0;
                Log($"Resetting Rotation Count to {RotationCount}");
            }
            else
            {
                Log($"Next raid in the list: {Settings.ActiveRaids[RotationCount].Species}.");
                while (RotationCount < Settings.ActiveRaids.Count && !Settings.ActiveRaids[RotationCount].ActiveInRotation)
                {
                    Log($"{Settings.ActiveRaids[RotationCount].Species} is disabled. Moving to next active raid in rotation.");
                    RotationCount++;
                    if (RotationCount >= Settings.ActiveRaids.Count)
                    {
                        RotationCount = 0;
                        Log($"Resetting Rotation Count to {RotationCount}");
                    }
                }
            }
        }

        private void ProcessRandomRotation()
        {
            // Check the remaining raids for any added by the RA command
            for (var i = RotationCount; i < Settings.ActiveRaids.Count; i++)
            {
                if (Settings.ActiveRaids[i].AddedByRACommand)
                {
                    RotationCount = i;
                    Log($"Setting Rotation Count to {RotationCount}");
                    return;  // Exit method as a raid added by RA command was found
                }
            }

            // If no raid added by RA command was found, select a random raid
            var random = new Random();
            RotationCount = random.Next(Settings.ActiveRaids.Count);
            Log($"Setting Rotation Count to {RotationCount}");
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
            Log("Preparing lobby...");
            LobbyFiltersCategory settings = new LobbyFiltersCategory();
            int attempts = 0;  // Counter to track the number of connection attempts.
            int maxAttempts = 5;  // Maximum allowed connection attempts before considering it a softban.

            while (true)  // Outer loop to repeat the entire process after waiting for 31 minutes
            {
                // Initial check for online connection.
                bool isConnected = await IsConnectedOnline(ConnectedOffset, token).ConfigureAwait(false);
                Log($"Initial online status check returned: {isConnected}");

                attempts = 0;  // Reset the attempts counter for a fresh start.

                while (!isConnected && attempts < maxAttempts)
                {
                    attempts++;
                    Log($"Connecting... (Attempt {attempts} of {maxAttempts})");

                    await RecoverToOverworld(token).ConfigureAwait(false);
                    if (!await ConnectToOnline(Hub.Config, token).ConfigureAwait(false))
                        return false;

                    await Task.Delay(5000, token).ConfigureAwait(false);  // Wait 5 seconds to check again.
                    await Click(B, 0_500, token).ConfigureAwait(false);
                    await Click(B, 0_500, token).ConfigureAwait(false);
                    await Click(B, 0_500, token).ConfigureAwait(false);
                    await Click(B, 0_500, token).ConfigureAwait(false);

                    // Check again after attempting to connect.
                    isConnected = await IsConnectedOnline(ConnectedOffset, token).ConfigureAwait(false);
                    Log($"Online status check after connection attempt returned: {isConnected}");
                }

                // Send an embed message and press 'B' button after the 5th attempt.
                if (attempts == maxAttempts)
                {
                    await Click(B, 0_500, token).ConfigureAwait(false);
                    await Click(B, 0_500, token).ConfigureAwait(false);

                    // Create and send an embed notifying of technical difficulties.
                    EmbedBuilder embed = new EmbedBuilder();
                    embed.Title = "Experiencing Technical Difficulties";
                    embed.Description = "The bot is experiencing issues connecting online. Please stand by as we try to resolve the issue.";
                    embed.Color = Color.Red;
                    embed.ThumbnailUrl = "https://genpkm.com/images/x.png";  // Setting the thumbnail URL directly
                    EchoUtil.RaidEmbed(null, "", embed);
                }


                // If we've reached the maximum number of attempts, assume a softban scenario.
                if (attempts >= maxAttempts)
                {
                    Log("Reached maximum connection attempts. Assuming softban. Waiting for 31 minutes...");
                    await Task.Delay(TimeSpan.FromMinutes(31), token).ConfigureAwait(false);  // Using TimeSpan for clarity

                    // Assuming ReOpenGame is an async method you have defined somewhere
                    await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                    Log("Game reopening process has been initiated.");
                }

                // If we've successfully connected, break out of the outer loop.
                if (isConnected)
                {
                    break;
                }
            }

            await Task.Delay(2_500, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);

            // Inject PartyPK after we save the game.
            var len = string.Empty;
            foreach (var l in Settings.ActiveRaids[RotationCount].PartyPK)
                len += l;
            if (len.Length > 1 && EmptyRaid == 0)
            {
                Log("Preparing PartyPK. Sit tight.");
                await Task.Delay(2_500 + settings.ExtraTimeLobbyDisband, token).ConfigureAwait(false);
                await SetCurrentBox(0, token).ConfigureAwait(false);
                var res = string.Join("\n", Settings.ActiveRaids[RotationCount].PartyPK);
                if (res.Length > 4096)
                    res = res[..4096];
                await InjectPartyPk(res, token).ConfigureAwait(false);

                await Click(X, 2_000, token).ConfigureAwait(false);
                await Click(DRIGHT, 0_500, token).ConfigureAwait(false);
                await SetStick(SwitchStick.LEFT, 0, -32000, 1_000, token).ConfigureAwait(false);
                await SetStick(SwitchStick.LEFT, 0, 0, 0, token).ConfigureAwait(false);
                for (int i = 0; i < 2; i++)
                    await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                await Click(A, 3_500, token).ConfigureAwait(false);
                await Click(Y, 0_500, token).ConfigureAwait(false);
                await Click(DLEFT, 0_800, token).ConfigureAwait(false);
                await Click(Y, 0_500, token).ConfigureAwait(false);
                for (int i = 0; i < 2; i++)
                    await Click(B, 1_500, token).ConfigureAwait(false);
                Log("PartyPK switch successful.");
            }

            for (int i = 0; i < 4; i++)
                await Click(B, 1_000, token).ConfigureAwait(false);

            await Task.Delay(1_500, token).ConfigureAwait(false);

            // If not in the overworld, we've been attacked so quit earlier.
            if (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                return false;

            await Click(A, 3_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false);


            if (firstRun) // If it's the first run
            {
                Log("First Run detected. Opening Lobby up to all to start raid rotation.");
                await Click(DDOWN, 1_000, token).ConfigureAwait(false);
            }
            else if (!Settings.ActiveRaids[RotationCount].IsCoded || Settings.ActiveRaids[RotationCount].IsCoded && EmptyRaid == Settings.LobbyOptions.EmptyRaidLimit && Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby)
            {
                // If not the first run, then apply the Settings logic
                if (Settings.ActiveRaids[RotationCount].IsCoded && EmptyRaid == Settings.LobbyOptions.EmptyRaidLimit && Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby)
                    Log($"We had {Settings.LobbyOptions.EmptyRaidLimit} empty raids.. Opening this raid to all!");
                await Click(DDOWN, 1_000, token).ConfigureAwait(false);
            }
            else
            {
                await Click(A, 3_000, token).ConfigureAwait(false);
            }

            await Click(A, 8_000, token).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> GetLobbyReady(bool recovery, CancellationToken token)
        {
            if (Settings.ActiveRaids[RotationCount].AddedByRACommand)
            {
                var user = Settings.ActiveRaids[RotationCount].User;
                if (user != null)
                {
                    try
                    {
                        await user.SendMessageAsync("Your raid is about to start!").ConfigureAwait(false);
                    }
                    catch (Discord.Net.HttpException ex)
                    {
                        // Handle exception (e.g., log the error or send a message to a logging channel)
                        Log($"Failed to send DM to {user.Username}. They might have DMs turned off. Exception: {ex.Message}");
                    }
                }
            }

            var x = 0;
            Log("Connecting to lobby...");
            while (!await IsConnectedToLobby(token).ConfigureAwait(false))
            {
                await Click(A, 1_000, token).ConfigureAwait(false);
                x++;
                if (x == 15 && recovery)
                {
                    Log("No den here! Rolling again.");
                    return false;
                }
                if (x == 45)
                {
                    Log("Failed to connect to lobby, restarting game incase we were in battle/bad connection.");
                    LobbyError++;
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

        private async Task<bool> CheckIfTrainerBanned(RaidMyStatus trainer, ulong nid, int player, CancellationToken token)
        {
            if (!RaidTracker.ContainsKey(nid))
                RaidTracker.Add(nid, 0);

            var msg = string.Empty;
            var banResultCFW = RaiderBanList.List.FirstOrDefault(x => x.ID == nid);
            bool isBanned = banResultCFW != default;

            if (isBanned)
            {
                msg = $"{banResultCFW!.Name} was found in the host's ban list.\n{banResultCFW.Comment}";
                Log(msg);
                await EnqueueEmbed(null, msg, false, true, false, false, token).ConfigureAwait(false);
                return true;
            }
            return false;
        }

        private async Task<(bool, List<(ulong, RaidMyStatus)>)> ReadTrainers(CancellationToken token)
        {
            await EnqueueEmbed(null, "", false, false, false, false, token).ConfigureAwait(false);

            List<(ulong, RaidMyStatus)> lobbyTrainers = new();
            var wait = TimeSpan.FromSeconds(Settings.TimeToWait);
            var endTime = DateTime.Now + wait;
            bool full = false;

            while (!full && DateTime.Now < endTime)
            {
                for (int i = 0; i < 3; i++)
                {
                    var player = i + 2;
                    Log($"Waiting for Player {player} to load...");

                    var nidOfs = TeraNIDOffsets[i];
                    var data = await SwitchConnection.ReadBytesAbsoluteAsync(nidOfs, 8, token).ConfigureAwait(false);
                    var nid = BitConverter.ToUInt64(data, 0);
                    while (nid == 0 && DateTime.Now < endTime)
                    {
                        await Task.Delay(0_500, token).ConfigureAwait(false);
                        data = await SwitchConnection.ReadBytesAbsoluteAsync(nidOfs, 8, token).ConfigureAwait(false);
                        nid = BitConverter.ToUInt64(data, 0);
                    }

                    List<long> ptr = new(Offsets.Trader2MyStatusPointer);
                    ptr[2] += i * 0x30;
                    var trainer = await GetTradePartnerMyStatus(ptr, token).ConfigureAwait(false);

                    while (trainer.OT.Length == 0 && DateTime.Now < endTime)
                    {
                        await Task.Delay(0_500, token).ConfigureAwait(false);
                        trainer = await GetTradePartnerMyStatus(ptr, token).ConfigureAwait(false);
                    }

                    if (nid != 0 && !string.IsNullOrWhiteSpace(trainer.OT))
                    {
                        if (await CheckIfTrainerBanned(trainer, nid, player, token).ConfigureAwait(false))
                            return (false, lobbyTrainers);
                    }

                    // Check if the NID is already in the list to prevent duplicates
                    if (lobbyTrainers.Any(x => x.Item1 == nid))
                    {
                        Log($"Duplicate NID detected: {nid}. Skipping...");
                        continue; // Skip adding this NID if it's a duplicate
                    }

                    // If NID is not a duplicate and has a valid trainer OT, add to the list
                    if (nid > 0 && trainer.OT.Length > 0)
                        lobbyTrainers.Add((nid, trainer));

                    full = lobbyTrainers.Count == 3;
                    if (full || DateTime.Now >= endTime)
                        break;
                }
            }

            await Task.Delay(5_000, token).ConfigureAwait(false);

            if (lobbyTrainers.Count == 0)
            {
                EmptyRaid++;
                LostRaid++;
                Log($"Nobody joined the raid, recovering...");
                if (Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby)
                    Log($"Empty Raid Count #{EmptyRaid}");
                if (Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.SkipRaid)
                    Log($"Lost/Empty Lobbies: {LostRaid}/{Settings.LobbyOptions.SkipRaidLimit}");

                return (false, lobbyTrainers);
            }

            RaidCount++; // Increment RaidCount only when a raid is actually starting.
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

        private async Task AdvanceDaySV(CancellationToken token)
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

            await Click(DUP, 0_200, token).ConfigureAwait(false); // Advance a day

            for (int i = 0; i < 8; i++) // Mash DRIGHT to confirm
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(A, 0_200, token).ConfigureAwait(false); // Confirm date/time change
            await Click(HOME, 1_000, token).ConfigureAwait(false); // Back to title screen

            await Click(A, 0_200, token).ConfigureAwait(false); // Back in Game
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
            if (firstRun)
            {
                GameProgress = await ReadGameProgress(token).ConfigureAwait(false);
                Log($"Current Game Progress identified as {GameProgress}.");
            }

            var nidPointer = new long[] { Offsets.LinkTradePartnerNIDPointer[0], Offsets.LinkTradePartnerNIDPointer[1], Offsets.LinkTradePartnerNIDPointer[2] };
            for (int p = 0; p < TeraNIDOffsets.Length; p++)
            {
                nidPointer[2] = Offsets.LinkTradePartnerNIDPointer[2] + p * 0x8;
                TeraNIDOffsets[p] = await SwitchConnection.PointerAll(nidPointer, token).ConfigureAwait(false);
            }
            Log("Caching offsets complete!");
        }

        private static async Task<bool> IsValidImageUrlAsync(string url)
        {
            using (var httpClient = new HttpClient())
            {
                try
                {
                    var response = await httpClient.GetAsync(url);
                    return response.IsSuccessStatusCode;
                }
                catch (HttpRequestException ex) when (ex.InnerException is WebException webEx && webEx.Status == WebExceptionStatus.TrustFailure)
                {

                }
                catch (Exception ex)
                {

                }
                return false;
            }
        }

        Dictionary<string, string> TypeAdvantages = new Dictionary<string, string>()
{
    { "normal", "Fighting" },
    { "fire", "Water, Ground, Rock" },
    { "water", "Electric, Grass" },
    { "grass", "Flying, Poison, Bug, Fire, Ice" },
    { "electric", "Ground" },
    { "ice", "Fighting, Rock, Steel, Fire" },
    { "fighting", "Flying, Psychic, Fairy" },
    { "poison", "Ground, Psychic" },
    { "ground", "Water, Ice, Grass" },
    { "flying", "Rock, Electric, Ice" },
    { "psychic", "Bug, Ghost, Dark" },
    { "bug", "Flying, Rock, Fire" },
    { "rock", "Fighting, Ground, Steel, Water, Grass" },
    { "ghost", "Ghost, Dark" },
    { "dragon", "Ice, Dragon, Fairy" },
    { "dark", "Fighting, Bug, Fairy" },
    { "steel", "Fighting, Ground, Fire" },
    { "fairy", "Poison, Steel" }
};

        private string GetTypeAdvantage(string teraType)
        {
            // Check if the type exists in the dictionary and return the corresponding advantage
            if (TypeAdvantages.TryGetValue(teraType.ToLower(), out string advantage))
            {
                return advantage;
            }
            return "Unknown Type";  // Return "Unknown Type" if the type doesn't exist in our dictionary
        }

        private async Task EnqueueEmbed(List<string>? names, string message, bool hatTrick, bool disband, bool upnext, bool raidstart, CancellationToken token)
        {
            if (firstRun)
            {
                // First Run detected. Not sending the embed to start raid rotation.
                return;
            }
            if (Settings.ActiveRaids[RotationCount].AddedByRACommand)
            {
                await Task.Delay(Settings.RequestEmbedTime * 1000).ConfigureAwait(false);  // Delay for RequestEmbedTime seconds
            }

            // Description can only be up to 4096 characters.
            //var description = Settings.ActiveRaids[RotationCount].Description.Length > 0 ? string.Join("\n", Settings.ActiveRaids[RotationCount].Description) : "";
            var description = Settings.EmbedToggles.RaidEmbedDescription.Length > 0 ? string.Join("\n", Settings.EmbedToggles.RaidEmbedDescription) : "";
            if (description.Length > 4096) description = description[..4096];

            string code = string.Empty;
            if (names is null && !upnext)
            {
                if (Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby)
                {
                    code = $"**{(Settings.ActiveRaids[RotationCount].IsCoded && EmptyRaid < Settings.LobbyOptions.EmptyRaidLimit ? await GetRaidCode(token).ConfigureAwait(false) : "Free For All")}**";
                }
                else
                {
                    code = $"**{(Settings.ActiveRaids[RotationCount].IsCoded && !Settings.HideRaidCode ? await GetRaidCode(token).ConfigureAwait(false) : Settings.ActiveRaids[RotationCount].IsCoded && Settings.HideRaidCode ? "||Is Hidden!||" : "Free For All")}**";
                }
            }

            if (EmptyRaid == Settings.LobbyOptions.EmptyRaidLimit && Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby)
                EmptyRaid = 0;

            if (disband) // Wait for trainer to load before disband
                await Task.Delay(5_000, token).ConfigureAwait(false);

            byte[]? bytes = Array.Empty<byte>();
            if (Settings.TakeScreenshot && !upnext)
                try
                {
                    // Assuming this is another place where a network call is made
                    bytes = await SwitchConnection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
                }
                catch (Exception ex)
                {
                    Log($"Error while fetching pixels: {ex.Message}");
                }

            string disclaimer = Settings.ActiveRaids.Count > 1
                                ? $"NRB {NotRaidBot.Version} - notpaldea.net"
                                : "";

            var turl = string.Empty;
            var form = string.Empty;

            Log($"Rotation Count: {RotationCount} | Species is {Settings.ActiveRaids[RotationCount].Species}");
            PK9 pk = new()
            {
                Species = (ushort)Settings.ActiveRaids[RotationCount].Species,
                Form = (byte)Settings.ActiveRaids[RotationCount].SpeciesForm
            };
            if (pk.Form != 0)
                form = $"-{pk.Form}";
            if (Settings.ActiveRaids[RotationCount].IsShiny == true)
                pk.SetIsShiny(true);
            else
                pk.SetIsShiny(false);

            if (Settings.ActiveRaids[RotationCount].SpriteAlternateArt && Settings.ActiveRaids[RotationCount].IsShiny)
            {
                var altUrl = AltPokeImg(pk);

                try
                {
                    // Check if AltPokeImg URL is valid
                    if (await IsValidImageUrlAsync(altUrl))
                    {
                        turl = altUrl;
                    }
                    else
                    {
                        Settings.ActiveRaids[RotationCount].SpriteAlternateArt = false;  // Set SpriteAlternateArt to false if no img found
                        turl = RaidExtensions<PK9>.PokeImg(pk, false, false);
                        Log($"AltPokeImg URL was not valid. Setting SpriteAlternateArt to false.");
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception and use the default sprite
                    Log($"Error while validating alternate image URL: {ex.Message}");
                    Settings.ActiveRaids[RotationCount].SpriteAlternateArt = false;  // Set SpriteAlternateArt to false due to error
                    turl = RaidExtensions<PK9>.PokeImg(pk, false, false);
                }
            }
            else
            {
                turl = RaidExtensions<PK9>.PokeImg(pk, false, false);
            }

            if (Settings.ActiveRaids[RotationCount].Species is 0)
                turl = "https://genpkm.com/images/combat.png";

            // Fetch the dominant color from the image only AFTER turl is assigned
            (int R, int G, int B) dominantColor = RaidExtensions<PK9>.GetDominantColor(turl);

            // Use the dominant color, unless it's a disband or hatTrick situation
            var embedColor = disband ? Color.Red : hatTrick ? Color.Purple : new Color(dominantColor.R, dominantColor.G, dominantColor.B);

            TimeSpan duration = new TimeSpan(0, 2, 31);

            // Calculate the future time by adding the duration to the current time
            DateTimeOffset futureTime = DateTimeOffset.Now.Add(duration);

            // Convert the future time to Unix timestamp
            long futureUnixTime = futureTime.ToUnixTimeSeconds();

            // Create the future time message using Discord's timestamp formatting
            string futureTimeMessage = $"**Raid Posting: <t:{futureUnixTime}:R>**";

            // Initialize the EmbedBuilder object
            var embed = new EmbedBuilder()
            {
                Title = disband ? $"**Raid canceled: [{TeraRaidCode}]**" : upnext && Settings.TotalRaidsToHost != 0 ? $"Raid Ended - Preparing Next Raid!" : upnext && Settings.TotalRaidsToHost == 0 ? $"Raid Ended - Preparing Next Raid!" : "",
                Color = embedColor,
                Description = disband ? message : upnext ? Settings.TotalRaidsToHost == 0 ? $"# {Settings.ActiveRaids[RotationCount].Title}\n\n{futureTimeMessage}" : $"# {Settings.ActiveRaids[RotationCount].Title}\n\n{futureTimeMessage}" : raidstart ? "" : description,
                ImageUrl = bytes.Length > 0 ? "attachment://zap.jpg" : default,
            };

            // Only include footer if not posting 'upnext' embed with the 'Preparing Raid' title
            if (!(upnext && Settings.TotalRaidsToHost == 0))
            {
                string programIconUrl = $"https://genpkm.com/images/icon4.png";
                int raidsInRotationCount = Hub.Config.RotatingRaidSV.ActiveRaids.Count(r => !r.AddedByRACommand);
                // Calculate uptime
                TimeSpan uptime = DateTime.Now - StartTime;

                // Check for singular or plural days/hours
                string dayLabel = uptime.Days == 1 ? "day" : "days";
                string hourLabel = uptime.Hours == 1 ? "hour" : "hours";
                string minuteLabel = uptime.Minutes == 1 ? "minute" : "minutes";

                // Format the uptime string, omitting the part if the value is 0
                string uptimeFormatted = "";
                if (uptime.Days > 0)
                {
                    uptimeFormatted += $"{uptime.Days} {dayLabel} ";
                }
                if (uptime.Hours > 0 || uptime.Days > 0) // Show hours if there are any hours, or if there are days even if hours are 0
                {
                    uptimeFormatted += $"{uptime.Hours} {hourLabel} ";
                }
                if (uptime.Minutes > 0 || uptime.Hours > 0 || uptime.Days > 0) // Show minutes if there are any minutes, or if there are hours/days even if minutes are 0
                {
                    uptimeFormatted += $"{uptime.Minutes} {minuteLabel}";
                }

                // Trim any excess whitespace from the string
                uptimeFormatted = uptimeFormatted.Trim();
                embed.WithFooter(new EmbedFooterBuilder()
                {
                    Text = $"Completed Raids: {RaidCount} (W: {WinCount} | L: {LossCount})\nActiveRaids: {raidsInRotationCount} | Uptime: {uptimeFormatted}\n" + disclaimer,
                    IconUrl = programIconUrl
                });
            }

            // Prepare the tera icon URL
            string teraType = RaidEmbedInfo.RaidSpeciesTeraType.ToLower();
            string folderName = Settings.SelectedTeraIconType == TeraIconType.Icon1 ? "icon1" : "icon2"; // Add more conditions for more icon types
            string teraIconUrl = $"https://genpkm.com/images/teraicons/{folderName}/{teraType}.png";

            // Only include author (header) if not posting 'upnext' embed with the 'Preparing Raid' title
            if (!(upnext && Settings.TotalRaidsToHost == 0))
            {
                // Set the author (header) of the embed with the tera icon
                embed.WithAuthor(new EmbedAuthorBuilder()
                {
                    Name = RaidEmbedInfo.RaidEmbedTitle,
                    IconUrl = teraIconUrl
                });
            }
            if (!disband && !upnext && !raidstart)
            {
                StringBuilder statsField = new StringBuilder();
                statsField.AppendLine($"**Gender**: {RaidEmbedInfo.RaidSpeciesGender}");
                statsField.AppendLine($"**Nature**: {RaidEmbedInfo.RaidSpeciesNature}");
                statsField.AppendLine($"**Ability**: {RaidEmbedInfo.RaidSpeciesAbility}");
                statsField.AppendLine($"**IVs**: {RaidEmbedInfo.RaidSpeciesIVs}");
                statsField.AppendLine($"**Scale**: {RaidEmbedInfo.ScaleText}({RaidEmbedInfo.ScaleNumber})");

                if (Settings.IncludeSeed)
                {
                    statsField.AppendLine($"**Seed**: `{Settings.ActiveRaids[RotationCount].Seed}`");
                }

                embed.AddField("**__Stats__**", statsField.ToString(), true);
                embed.AddField("\u200b", "\u200b", true);
            }

            if (!disband && !upnext && !raidstart && Settings.EmbedToggles.IncludeMoves)
            {
                embed.AddField("**__Moves__**", string.IsNullOrEmpty($"{RaidEmbedInfo.ExtraMoves}") ? string.IsNullOrEmpty($"{RaidEmbedInfo.Moves}") ? "No Moves To Display" : $"{RaidEmbedInfo.Moves}" : $"{RaidEmbedInfo.Moves}\n**Extra Moves:**\n{RaidEmbedInfo.ExtraMoves}", true);
                RaidEmbedInfo.ExtraMoves = string.Empty;
            }

            if (!disband && !upnext && !raidstart && !Settings.EmbedToggles.IncludeMoves)
            {
                embed.AddField(" **__Special Rewards__**", string.IsNullOrEmpty($"{RaidEmbedInfo.SpecialRewards}") ? "No Rewards To Display" : $"{RaidEmbedInfo.SpecialRewards}", true);

            }

            if (!disband && names is null && !upnext)
            {
                embed.AddField(Settings.IncludeCountdown ? $"**__Raid Starting__**:\n**<t:{DateTimeOffset.Now.ToUnixTimeSeconds() + Settings.TimeToWait}:R>**" : $"**Waiting in lobby!**", $"Raid Code: {code}", true);
                embed.AddField("\u200b", "\u200b", true);
            }

            if (!disband && !upnext && !raidstart && Settings.EmbedToggles.IncludeMoves)
            {
                embed.AddField(" **__Special Rewards__**", string.IsNullOrEmpty($"{RaidEmbedInfo.SpecialRewards}") ? "No Rewards To Display" : $"{RaidEmbedInfo.SpecialRewards}", true);
            }
            // Fetch the type advantage using the static RaidSpeciesTeraType from RaidEmbedInfo
            string typeAdvantage = GetTypeAdvantage(RaidEmbedInfo.RaidSpeciesTeraType);

            // Only include the Type Advantage if not posting 'upnext' embed with the 'Preparing Raid' title and if the raid isn't starting or disbanding
            if (!disband && !upnext && !raidstart && Settings.EmbedToggles.IncludeTypeAdvantage)
            {
                embed.AddField(" **__Type Advantage__**", typeAdvantage, true);
            }
            if (!disband && names is not null && !upnext)
            {
                var players = string.Empty;
                if (names.Count == 0)
                    players = "Our party dipped on us :/";
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

        private async Task<bool> ConnectToOnline(PokeRaidHubConfig config, CancellationToken token)
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

        public async Task StartGameRaid(PokeRaidHubConfig config, CancellationToken token)
        {
            var timing = config.Timings;
            await Click(A, 1_000 + timing.ExtraTimeLoadProfile, token).ConfigureAwait(false);

            if (timing.CheckGameDelay)
            {
                await Task.Delay(3_000, token).ConfigureAwait(false);
            }

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

            if (Settings.ActiveRaids.Count > 1)
            {
                Log($"Rotation for {Settings.ActiveRaids[RotationCount].Species} has been found.");
                Log($"Checking Current Game Progress Level.");

                var desiredProgress = (GameProgress)Settings.ActiveRaids[RotationCount].StoryProgressLevel;
                if (GameProgress != desiredProgress)
                {
                    Log($"Updating game progress level to: {desiredProgress}");
                    await WriteProgressLive(desiredProgress).ConfigureAwait(false);
                    GameProgress = desiredProgress;
                    Log($"Done.");
                }
                else
                {
                    Log($"Game progress level is already {GameProgress}. No update needed.");
                }

                if (Settings.DisableOverworldSpawns)
                {
                    Log("Checking current state of Overworld Spawns before attempting to disable.");
                    var currentSpawnsEnabled = (bool?)await ReadBlock(RaidDataBlocks.KWildSpawnsEnabled, CancellationToken.None);
                    if (currentSpawnsEnabled.HasValue)
                    {
                        Log($"Current Overworld Spawns state: {currentSpawnsEnabled.Value}");

                        // If the spawns are already disabled, no need to write `false` again
                        if (currentSpawnsEnabled.Value)
                        {
                            Log("Overworld Spawns are enabled, attempting to disable.");
                            var toexpect = currentSpawnsEnabled;
                            await WriteBlock(false, RaidDataBlocks.KWildSpawnsEnabled, CancellationToken.None, toexpect);
                        }
                        else
                        {
                            Log("Overworld Spawns are already disabled, no action taken.");
                        }
                    }
                    else
                    {
                        Log("Could not read the current state of Overworld Spawns. Skipping the disable action.");
                    }
                }

                Log($"Attempting to override seed for {Settings.ActiveRaids[RotationCount].Species}.");
                await OverrideSeedIndex(SeedIndexToReplace, token).ConfigureAwait(false);
                Log("Seed override completed.");                
            }

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

        private async Task WriteProgressLive(GameProgress progress)
        {
            if (Connection is null)
                return;

            if (progress >= GameProgress.Unlocked3Stars)
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty3, CancellationToken.None);
                await WriteBlock(true, RaidDataBlocks.KUnlockedRaidDifficulty3, CancellationToken.None, toexpect);
            }
            else
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty3, CancellationToken.None);
                await WriteBlock(false, RaidDataBlocks.KUnlockedRaidDifficulty3, CancellationToken.None, toexpect);
            }

            if (progress >= GameProgress.Unlocked4Stars)
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty4, CancellationToken.None);
                await WriteBlock(true, RaidDataBlocks.KUnlockedRaidDifficulty4, CancellationToken.None, toexpect);
            }
            else
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty4, CancellationToken.None);
                await WriteBlock(false, RaidDataBlocks.KUnlockedRaidDifficulty4, CancellationToken.None, toexpect);
            }

            if (progress >= GameProgress.Unlocked5Stars)
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty5, CancellationToken.None);
                await WriteBlock(true, RaidDataBlocks.KUnlockedRaidDifficulty5, CancellationToken.None, toexpect);
            }
            else
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty5, CancellationToken.None);
                await WriteBlock(false, RaidDataBlocks.KUnlockedRaidDifficulty5, CancellationToken.None, toexpect);
            }

            if (progress >= GameProgress.Unlocked6Stars)
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty6, CancellationToken.None);
                await WriteBlock(true, RaidDataBlocks.KUnlockedRaidDifficulty6, CancellationToken.None, toexpect);
            }
            else
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty6, CancellationToken.None);
                await WriteBlock(false, RaidDataBlocks.KUnlockedRaidDifficulty6, CancellationToken.None, toexpect);
            }
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

        #region RaidCrawler
        // via RaidCrawler modified for this proj
        private async Task ReadRaids(bool init, CancellationToken token)
        {

            Log("Starting raid reads..");
            if (init)
            {
                if (RaidBlockPointerP == 0)
                    RaidBlockPointerP = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerP, token).ConfigureAwait(false);

                if (RaidBlockPointerK == 0)
                    RaidBlockPointerK = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerK, token).ConfigureAwait(false);
            }
            else
            {
                if (SeedIndexToReplace >= 0 && SeedIndexToReplace <= 69)
                {
                    if (RaidBlockPointerP == 0)
                        RaidBlockPointerP = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerP, token).ConfigureAwait(false);
                }
                else
                {
                    if (RaidBlockPointerK == 0)
                        RaidBlockPointerK = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerK, token).ConfigureAwait(false);
                }
            }

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

            var dataP = Array.Empty<byte>();
            var dataK = Array.Empty<byte>();
            int delivery;
            int enc;

            if (init || SeedIndexToReplace >= 0 && SeedIndexToReplace <= 69)
            {
                dataP = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP + RaidBlock.HEADER_SIZE, (int)RaidBlock.SIZE_BASE, token).ConfigureAwait(false);
            }
            if (init || SeedIndexToReplace >= 70)
            {
                dataK = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerK, (int)RaidBlock.SIZE_KITAKAMI, token).ConfigureAwait(false);
            }

            if (init || SeedIndexToReplace >= 0 && SeedIndexToReplace <= 69)
            {
                (delivery, enc) = container.ReadAllRaids(dataP, StoryProgress, EventProgress, 0, TeraRaidMapParent.Paldea);

                if (enc > 0)
                {
                    Log($"Failed to find encounters for {enc} raid(s).  Stop the bot, delete any Event raids in ActiveRaids, day roll to refresh map.");
                    await GoHome(Hub.Config, token).ConfigureAwait(false);
                    await AdvanceDaySV(token).ConfigureAwait(false);
                    await Task.Delay(5_000, token).ConfigureAwait(false);
                    await SaveGame(Hub.Config, token).ConfigureAwait(false);
                    await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                }


                if (delivery > 0)
                    Log($"Invalid delivery group ID for {delivery} raid(s). Try deleting the \"cache\" folder.");

                // Check the raids to see if any are event raids for Paldea
                foreach (var raid in container.Raids)
                {
                    if (raid.IsEvent)
                    {
                        Settings.EventSettings.EventActive = true;
                        break; // Exit loop if an event raid is found
                    }
                }
            }

            var raids = container.Raids;
            var encounters = container.Encounters;
            var rewards = container.Rewards;
            container.ClearRaids();
            container.ClearEncounters();
            container.ClearRewards();

            if (init || SeedIndexToReplace >= 70 && SeedIndexToReplace <= 94)
            {
                (delivery, enc) = container.ReadAllRaids(dataK, StoryProgress, EventProgress, 0, TeraRaidMapParent.Kitakami);

                if (enc > 0)
                    Log($"Failed to find encounters for {enc} raid(s).");

                if (delivery > 0)
                    Log($"Invalid delivery group ID for {delivery} raid(s). Try deleting the \"cache\" folder.");

                // Check the raids to see if any are event raids for Kitakami
                foreach (var raid in container.Raids)
                {
                    if (raid.IsEvent)
                    {
                        Settings.EventSettings.EventActive = true;
                        break; // Exit loop if an event raid is found
                    }
                }
            }

            var allRaids = raids.Concat(container.Raids).ToList().AsReadOnly();
            var allEncounters = encounters.Concat(container.Encounters).ToList().AsReadOnly();
            var allRewards = rewards.Concat(container.Rewards).ToList().AsReadOnly();

            container.SetRaids(allRaids);
            container.SetEncounters(allEncounters);
            container.SetRewards(allRewards);
            if (init)
            {
                for (int rc = 0; rc < Settings.ActiveRaids.Count; rc++)
                {
                    uint targetSeed = uint.Parse(Settings.ActiveRaids[rc].Seed, NumberStyles.AllowHexSpecifier);

                    for (int i = 0; i < container.Raids.Count; i++)
                    {
                        if (container.Raids[i].Seed == targetSeed)
                        {
                            SeedIndexToReplace = i;
                            RotationCount = rc;
                            Log($"Den ID: {SeedIndexToReplace} stored.");
                            Log($"Rotation Count set to {RotationCount}");
                            return;
                        }
                    }
                }
            }
            bool done = false;
            for (int i = 0; i < container.Raids.Count; i++)
            {
                if (done is true)
                    break;

                var (pk, seed) = IsSeedReturned(container.Encounters[i], container.Raids[i]);
                for (int a = 0; a < Settings.ActiveRaids.Count; a++)
                {
                    if (done is true)
                        break;

                    uint set;
                    try
                    {
                        set = uint.Parse(Settings.ActiveRaids[a].Seed, NumberStyles.AllowHexSpecifier);
                    }
                    catch (FormatException)
                    {
                        Log($"Invalid seed format detected. Removing {Settings.ActiveRaids[a].Seed} from list.");
                        Settings.ActiveRaids.RemoveAt(a);
                        a--;  // Decrement the index so that it does not skip the next element.
                        continue;  // Skip to the next iteration.
                    }

                    if (seed == set)
                    {
                        var res = GetSpecialRewards(container.Rewards[i]);
                        RaidEmbedInfo.SpecialRewards = res;
                        if (string.IsNullOrEmpty(res))
                            res = string.Empty;
                        else
                            res = "**Special Rewards:**\n" + res;
                        Log($"Seed {seed:X8} found for {(Species)container.Encounters[i].Species}");
                        Settings.ActiveRaids[a].Seed = $"{seed:X8}";
                        var stars = container.Raids[i].IsEvent ? container.Encounters[i].Stars : container.Raids[i].GetStarCount(container.Raids[i].Difficulty, StoryProgress, container.Raids[i].IsBlack);
                        string starcount = string.Empty;
                        switch (stars)
                        {
                            case 1: starcount = "1 ★"; break;
                            case 2: starcount = "2 ★"; break;
                            case 3: starcount = "3 ★"; break;
                            case 4: starcount = "4 ★"; break;
                            case 5: starcount = "5 ★"; break;
                            case 6: starcount = "6 ★"; break;
                            case 7: starcount = "7 ★"; break;
                        }
                        Settings.ActiveRaids[a].IsShiny = container.Raids[i].IsShiny;
                        Settings.ActiveRaids[a].CrystalType = container.Raids[i].IsBlack ? TeraCrystalType.Black : container.Raids[i].IsEvent && stars == 7 ? TeraCrystalType.Might : container.Raids[i].IsEvent ? TeraCrystalType.Distribution : TeraCrystalType.Base;
                        Settings.ActiveRaids[a].Species = (Species)container.Encounters[i].Species;
                        Settings.ActiveRaids[a].SpeciesForm = container.Encounters[i].Form;
                        var pkinfo = RaidExtensions<PK9>.GetRaidPrintName(pk);
                        var strings = GameInfo.GetStrings(1);
                        var moves = new ushort[4] { container.Encounters[i].Move1, container.Encounters[i].Move2, container.Encounters[i].Move3, container.Encounters[i].Move4 };
                        var movestr = string.Concat(moves.Where(z => z != 0).Select(z => $"{strings.Move[z]}ㅤ{Environment.NewLine}")).TrimEnd(Environment.NewLine.ToCharArray());
                        var extramoves = string.Empty;
                        if (container.Encounters[i].ExtraMoves.Length != 0)
                        {
                            var extraMovesList = container.Encounters[i].ExtraMoves.Where(z => z != 0).Select(z => $"{strings.Move[z]}\n");
                            extramoves = string.Concat(extraMovesList.Take(extraMovesList.Count()));
                            RaidEmbedInfo.ExtraMoves = extramoves;
                        }
                        var titlePrefix = container.Raids[i].IsShiny ? "Shiny" : "";
                        RaidEmbedInfo.RaidSpecies = (Species)container.Encounters[i].Species;
                        RaidEmbedInfo.RaidEmbedTitle = $"{starcount} {titlePrefix} {(Species)container.Encounters[i].Species}{pkinfo}";
                        RaidEmbedInfo.RaidSpeciesGender = $"{(pk.Gender == 0 ? "Male" : pk.Gender == 1 ? "Female" : "")}";
                        RaidEmbedInfo.RaidSpeciesNature = GameInfo.Strings.Natures[pk.Nature];
                        RaidEmbedInfo.RaidSpeciesAbility = $"{(Ability)pk.Ability}";
                        RaidEmbedInfo.RaidSpeciesIVs = $"{pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}";
                        RaidEmbedInfo.RaidSpeciesTeraType = $"{(MoveType)container.Raids[i].TeraType}";
                        RaidEmbedInfo.Moves = string.Concat(moves.Where(z => z != 0).Select(z => $"{strings.Move[z]}\n")).TrimEnd(Environment.NewLine.ToCharArray());
                        RaidEmbedInfo.ScaleText = $"{PokeSizeDetailedUtil.GetSizeRating(pk.Scale)}";
                        RaidEmbedInfo.ScaleNumber = pk.Scale;
                        /*if (Settings.PresetFilters.UsePresetFile)
                        {
                            string tera = $"{(MoveType)container.Raids[i].TeraType}";
                            if (!string.IsNullOrEmpty(Settings.ActiveRaids[a].Title) && !Settings.PresetFilters.ForceTitle)
                                ModDescription[0] = Settings.ActiveRaids[a].Title;

                            if (Settings.ActiveRaids[a].Description.Length > 0 && !Settings.PresetFilters.ForceDescription)
                            {
                                string[] presetOverwrite = new string[Settings.ActiveRaids[a].Description.Length + 1];
                                presetOverwrite[0] = ModDescription[0];
                                for (int l = 0; l < Settings.ActiveRaids[a].Description.Length; l++)
                                    presetOverwrite[l + 1] = Settings.ActiveRaids[a].Description[l];

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
                                if (string.IsNullOrEmpty(Settings.ActiveRaids[a].Title) || Settings.PresetFilters.ForceTitle)
                                    Settings.ActiveRaids[a].Title = raidDescription[0];

                                if (Settings.ActiveRaids[a].Description == null || Settings.ActiveRaids[a].Description.Length == 0 || Settings.ActiveRaids[a].Description.All(string.IsNullOrEmpty) || Settings.PresetFilters.ForceDescription)
                                    Settings.ActiveRaids[a].Description = raidDescription.Skip(1).ToArray();
                            }
                            else if (!Settings.PresetFilters.TitleFromPreset)
                            {
                                if (Settings.ActiveRaids[a].Description == null || Settings.ActiveRaids[a].Description.Length == 0 || Settings.ActiveRaids[a].Description.All(string.IsNullOrEmpty) || Settings.PresetFilters.ForceDescription)
                                    Settings.ActiveRaids[a].Description = raidDescription.ToArray();
                            }
                        }

                        else if (!Settings.PresetFilters.UsePresetFile)
                        {
                            Settings.ActiveRaids[a].Description = new[] { "\n**Raid Info:**", pkinfo, "\n**Moveset:**", movestr, extramoves, BaseDescription, res };
                            Settings.ActiveRaids[a].Title = $"{(Species)container.Encounters[i].Species} {starcount} - {(MoveType)container.Raids[i].TeraType}";
                        } */

                        Settings.ActiveRaids[a].IsSet = false; // we don't use zyro's preset.txt file, ew.
                        done = true;
                    }
                }
            }
        }
        #endregion

        public static (PK9, Embed) RaidInfoCommand(string seedValue, int contentType, TeraRaidMapParent map, int storyProgressLevel, int raidDeliveryGroupID, bool isEvent = false)
        {
            byte[] enabled = StringToByteArray("00000001");
            byte[] area = StringToByteArray("00000001");
            byte[] displaytype = StringToByteArray("00000001");
            byte[] spawnpoint = StringToByteArray("00000001");
            byte[] thisseed = StringToByteArray(seedValue);
            byte[] unused = StringToByteArray("00000000");
            byte[] content = StringToByteArray($"0000000{contentType}"); // change this to 1 for 6-Star, 2 for 1-6 Star Events, 3 for Mighty 7-Star Raids
            byte[] leaguepoints = StringToByteArray("00000000");
            byte[] raidbyte = enabled.Concat(area).ToArray().Concat(displaytype).ToArray().Concat(spawnpoint).ToArray().Concat(thisseed).ToArray().Concat(unused).ToArray().Concat(content).ToArray().Concat(leaguepoints).ToArray();

            storyProgressLevel = storyProgressLevel switch
            {
                3 => 1,
                4 => 2,
                5 => 3,
                6 => 4,
                0 => 0,
                _ => 4 // default 6Unlocked
            };

            var raid = new Raid(raidbyte, map); // map is -> TeraRaidMapParent.Paldea or .Kitakami
            var progress = storyProgressLevel;
            var raid_delivery_group_id = raidDeliveryGroupID;
            var encounter = raid.GetTeraEncounter(container, progress, raid_delivery_group_id);
            var reward = encounter.GetRewards(container, raid, 0);
            var stars = raid.IsEvent ? encounter.Stars : raid.GetStarCount(raid.Difficulty, storyProgressLevel, raid.IsBlack);
            var teraType = raid.GetTeraType(encounter);
            var form = encounter.Form;

            var param = encounter.GetParam();
            var pk = new PK9
            {
                Species = encounter.Species,
                Form = encounter.Form,
                Move1 = encounter.Move1,
                Move2 = encounter.Move2,
                Move3 = encounter.Move3,
                Move4 = encounter.Move4,
            };
            if (raid.IsShiny) pk.SetIsShiny(true);
            Encounter9RNG.GenerateData(pk, param, EncounterCriteria.Unrestricted, raid.Seed);
            var strings = GameInfo.GetStrings(1);
            var movesList = "";
            for (int i = 0; i < pk.Moves.Length; i++)
            {
                if (pk.Moves[i] != 0)
                {
                    movesList += $"\\- {strings.Move[pk.Moves[i]]}\n";
                }
            }
            var extraMoves = "";
            for (int i = 0; i < encounter.ExtraMoves.Length; i++)
            {
                if (encounter.ExtraMoves[i] != 0)
                {
                    extraMoves += $"\\- {strings.Move[encounter.ExtraMoves[i]]}\n";
                }
            }
            if (!string.IsNullOrEmpty(extraMoves)) movesList += $"**Extra Moves:**\n{extraMoves}";
            var specialRewards = GetSpecialRewards(reward);
            var teraTypeLower = strings.Types[teraType].ToLower();
            var teraIconUrl = $"https://genpkm.com/images/teraicons/icon1/{teraTypeLower}.png";
            var disclaimer = $"NotRaidBot {NotRaidBot.Version} by Gengar & Kai\nhttps://notpaldea.net";
            var titlePrefix = raid.IsShiny ? "Shiny " : "";
            var formName = ShowdownParsing.GetStringFromForm(pk.Form, strings, pk.Species, pk.Context);
            var authorName = $"{stars} ★ {titlePrefix}{(Species)encounter.Species}{(pk.Form != 0 ? $"-{formName}" : "")}{(isEvent ? " (Event Raid)" : "")}";

            (int R, int G, int B) = RaidExtensions<PK9>.GetDominantColor(RaidExtensions<PK9>.PokeImg(pk, false, false));
            var embedColor = new Color(R, G, B);

            var embed = new EmbedBuilder
            {
                Color = embedColor,
                ThumbnailUrl = RaidExtensions<PK9>.PokeImg(pk, false, false),
            };
            embed.AddField(x =>
            {
                x.Name = "**__Stats__**";
                x.Value = $"{Format.Bold($"TeraType:")} {strings.Types[teraType]} \n" +
                          $"{Format.Bold($"Ability:")} {strings.Ability[pk.Ability]}\n" +
                          $"{Format.Bold("Nature:")} {(Nature)pk.Nature}\n" +
                          $"{Format.Bold("IVs:")} {pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}\n" +
                          $"{Format.Bold($"Scale:")} {PokeSizeDetailedUtil.GetSizeRating(pk.Scale)}";
                x.IsInline = true;
            });

            embed.AddField("**__Moves__**", movesList, true);
            embed.AddField("**__Special Rewards__**", string.IsNullOrEmpty(specialRewards) ? "No Rewards To Display" : specialRewards, true);

            var programIconUrl = "https://genpkm.com/images/icon4.png";
            embed.WithFooter(new EmbedFooterBuilder()
            {
                Text = $"" + disclaimer,
                IconUrl = programIconUrl
            });

            embed.WithAuthor(auth =>
            {
                auth.Name = authorName;
                auth.IconUrl = teraIconUrl;
            });

            return (pk, embed.Build());
        }

        public static byte[] StringToByteArray(string hex)
        {
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            Array.Reverse(bytes);
            return bytes;
        }

        private async Task<bool> PrepareForDayroll(CancellationToken token)
        {
            // Make sure we're connected.
            while (!await IsConnectedOnline(ConnectedOffset, token).ConfigureAwait(false))
            {
                Log("Connecting...");
                await RecoverToOverworld(token).ConfigureAwait(false);
                if (!await ConnectToOnline(Hub.Config, token).ConfigureAwait(false))
                    return false;
            }
            return true;
        }

        public async Task<bool> CheckForLobby(CancellationToken token)
        {
            var x = 0;
            Log("Connecting to lobby...");
            while (!await IsConnectedToLobby(token).ConfigureAwait(false))
            {
                await Click(A, 1_000, token).ConfigureAwait(false);
                x++;
                if (x == 15)
                {
                    Log("No den here! Rolling again.");
                    return false;
                }
            }
            return true;
        }

        private async Task<bool> SaveGame(PokeRaidHubConfig config, CancellationToken token)
        {

            await Click(X, 3_000, token).ConfigureAwait(false);
            await Click(R, 3_000 + config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false);
            await Click(A, 1_000, token).ConfigureAwait(false);
            await Click(B, 1_000, token).ConfigureAwait(false);
            return true;
        }

        public class RaidEmbedInfo
        {
            public static string RaidEmbedTitle = string.Empty;
            public static Species RaidSpecies = Species.None;
            public static string RaidSpeciesGender = string.Empty;
            public static string RaidSpeciesIVs = string.Empty;
            public static string RaidSpeciesAbility = string.Empty;
            public static string RaidSpeciesNature = string.Empty;
            public static string RaidSpeciesTeraType = string.Empty;
            public static string Moves = string.Empty;
            public static string ExtraMoves = string.Empty;
            public static string ScaleText = string.Empty;
            public static string SpecialRewards = string.Empty;
            public static int ScaleNumber;
        }
    }
}