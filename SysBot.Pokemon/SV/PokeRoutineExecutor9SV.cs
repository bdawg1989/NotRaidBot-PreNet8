using PKHeX.Core;
using SysBot.Base;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RaidCrawler.Core.Structures;
using static SysBot.Pokemon.PokeDataOffsetsSV;
using static SysBot.Base.SwitchButton;
using static System.Buffers.Binary.BinaryPrimitives;
using SysBot.Pokemon.SV.BotRaid;
using static SysBot.Pokemon.Blocks;

namespace SysBot.Pokemon
{
    public abstract class PokeRoutineExecutor9SV : PokeRoutineExecutor<PK9>
    {
        protected PokeDataOffsetsSV Offsets { get; } = new();
        public ulong returnOfs = 0;
        private ulong KeyBlockAddress = 0;
        protected PokeRoutineExecutor9SV(PokeBotState cfg) : base(cfg)
        {
        }

        public override async Task<PK9> ReadPokemon(ulong offset, CancellationToken token) => await ReadPokemon(offset, BoxFormatSlotSize, token).ConfigureAwait(false);

        public override async Task<PK9> ReadPokemon(ulong offset, int size, CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, size, token).ConfigureAwait(false);
            return new PK9(data);
        }

        public async Task<PK9> ReadPokemonMain(uint offset, int size, CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesMainAsync(offset, size, token).ConfigureAwait(false);
            var pk = new PK9(data);
            return pk;
        }

        public override async Task<PK9> ReadPokemonPointer(IEnumerable<long> jumps, int size, CancellationToken token)
        {
            var (valid, offset) = await ValidatePointerAll(jumps, token).ConfigureAwait(false);
            if (!valid)
                return new PK9();
            return await ReadPokemon(offset, token).ConfigureAwait(false);
        }

        public async Task<bool> ReadIsChanged(uint offset, byte[] original, CancellationToken token)
        {
            var result = await Connection.ReadBytesAsync(offset, original.Length, token).ConfigureAwait(false);
            return !result.SequenceEqual(original);
        }

        public override async Task<PK9> ReadBoxPokemon(int box, int slot, CancellationToken token)
        {
            // Shouldn't be reading anything but box1slot1 here. Slots are not consecutive.
            var jumps = Offsets.BoxStartPokemonPointer.ToArray();
            return await ReadPokemonPointer(jumps, BoxFormatSlotSize, token).ConfigureAwait(false);
        }

        public async Task SetBoxPokemonAbsolute(ulong offset, PK9 pkm, CancellationToken token, ITrainerInfo? sav = null)
        {
            if (sav != null)
            {
                // Update PKM to the current save's handler data
                pkm.Trade(sav);
                pkm.RefreshChecksum();
            }

            pkm.ResetPartyStats();
            await SwitchConnection.WriteBytesAbsoluteAsync(pkm.EncryptedBoxData, offset, token).ConfigureAwait(false);
        }

        public async Task SetCurrentBox(byte box, CancellationToken token)
        {
            await SwitchConnection.PointerPoke(new[] { box }, Offsets.CurrentBoxPointer, token).ConfigureAwait(false);
        }

        public async Task<byte> GetCurrentBox(CancellationToken token)
        {
            var data = await SwitchConnection.PointerPeek(1, Offsets.CurrentBoxPointer, token).ConfigureAwait(false);
            return data[0];
        }

        public async Task<SAV9SV> IdentifyTrainer(CancellationToken token)
        {
            // Check if botbase is on the correct version or later.
            await VerifyBotbaseVersion(token).ConfigureAwait(false);

            // Check title so we can warn if mode is incorrect.
            string title = await SwitchConnection.GetTitleID(token).ConfigureAwait(false);
            if (title is not (ScarletID or VioletID))
                throw new Exception($"{title} is not a valid SV title. Is your mode correct?");

            // Verify the game version.
            var game_version = await SwitchConnection.GetGameInfo("version", token).ConfigureAwait(false);
            if (!game_version.SequenceEqual(SVGameVersion))
                throw new Exception($"Game version is not supported. Expected version {SVGameVersion}, and current game version is {game_version}.");

            var sav = await GetFakeTrainerSAV(token).ConfigureAwait(false);
            InitSaveData(sav);

            if (!IsValidTrainerData())
            {
                await CheckForRAMShiftingApps(token).ConfigureAwait(false);
                throw new Exception("Refer to the SysBot.NET wiki (https://github.com/kwsch/SysBot.NET/wiki/Troubleshooting) for more information.");
            }

            if (await GetTextSpeed(token).ConfigureAwait(false) < TextSpeedOption.Fast)
                throw new Exception("Text speed should be set to FAST. Fix this for correct operation.");

            return sav;
        }

        public async Task<SAV9SV> GetFakeTrainerSAV(CancellationToken token)
        {
            var sav = new SAV9SV();
            var info = sav.MyStatus;
            var read = await SwitchConnection.PointerPeek(info.Data.Length, Offsets.MyStatusPointer, token).ConfigureAwait(false);
            read.CopyTo(info.Data, 0);
            return sav;
        }

        public async Task<RaidMyStatus> GetTradePartnerMyStatus(IReadOnlyList<long> pointer, CancellationToken token)
        {
            var info = new RaidMyStatus();
            var read = await SwitchConnection.PointerPeek(info.Data.Length, pointer, token).ConfigureAwait(false);
            read.CopyTo(info.Data, 0);
            return info;
        }

        public async Task InitializeHardware(IBotStateSettings settings, CancellationToken token)
        {
            Log("Detaching on startup.");
            await DetachController(token).ConfigureAwait(false);
            if (settings.ScreenOff)
            {
                Log("Turning off screen.");
                await SetScreen(ScreenState.Off, token).ConfigureAwait(false);
            }
        }

        public async Task CleanExit(CancellationToken token)
        {
            await SetScreen(ScreenState.On, token).ConfigureAwait(false);
            Log("Detaching controllers on routine exit.");
            await DetachController(token).ConfigureAwait(false);
        }

        protected virtual async Task EnterLinkCode(int code, PokeTradeHubConfig config, CancellationToken token)
        {
            // Default implementation to just press directional arrows. Can do via Hid keys, but users are slower than bots at even the default code entry.
            var keys = TradeUtil.GetPresses(code);
            foreach (var key in keys)
            {
                int delay = config.Timings.KeypressTime;
                await Click(key, delay, token).ConfigureAwait(false);
            }
            // Confirm Code outside of this method (allow synchronization)
        }

        public async Task ReOpenGame(PokeTradeHubConfig config, CancellationToken token)
        {
            await CloseGame(config, token).ConfigureAwait(false);
            await StartGame(config, token).ConfigureAwait(false);
        }

        public async Task CloseGame(PokeTradeHubConfig config, CancellationToken token)
        {
            var timing = config.Timings;
            // Close out of the game
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(HOME, 2_000 + timing.ExtraTimeReturnHome, token).ConfigureAwait(false);
            await Click(X, 1_000, token).ConfigureAwait(false);
            await Click(A, 5_000 + timing.ExtraTimeCloseGame, token).ConfigureAwait(false);
            Log("Closed out of the game!");
        }

        public async Task StartGame(PokeTradeHubConfig config, CancellationToken token)
        {
            var timing = config.Timings;
            // Open game.
            await Click(A, 1_000 + timing.ExtraTimeLoadProfile, token).ConfigureAwait(false);

            // Menus here can go in the order: Update Prompt -> Profile -> DLC check -> Unable to use DLC.
            //  The user can optionally turn on the setting if they know of a breaking system update incoming.
            if (timing.AvoidSystemUpdate)
            {
                await Click(DUP, 0_600, token).ConfigureAwait(false);
                await Click(A, 1_000 + timing.ExtraTimeLoadProfile, token).ConfigureAwait(false);
            }

            await Click(A, 1_000 + timing.ExtraTimeCheckDLC, token).ConfigureAwait(false);
            // If they have DLC on the system and can't use it, requires an UP + A to start the game.
            // Should be harmless otherwise since they'll be in loading screen.
            await Click(DUP, 0_600, token).ConfigureAwait(false);
            await Click(A, 0_600, token).ConfigureAwait(false);

            Log("Restarting the game!");

            // Switch Logo and game load screen
            await Task.Delay(12_000 + timing.ExtraTimeLoadGame, token).ConfigureAwait(false);

            for (int i = 0; i < 8; i++)
                await Click(A, 1_000, token).ConfigureAwait(false);

            var timer = 60_000;
            while (!await IsOnOverworldTitle(token).ConfigureAwait(false))
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                timer -= 1_000;
                // We haven't made it back to overworld after a minute, so press A every 6 seconds hoping to restart the game.
                // Don't risk it if hub is set to avoid updates.
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
        }

        public async Task<bool> IsConnectedOnline(ulong offset, CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
            return data[0] == 1;
        }

        public async Task<ulong> GetTradePartnerNID(ulong offset, CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 8, token).ConfigureAwait(false);
            return BitConverter.ToUInt64(data, 0);
        }

        public async Task ClearTradePartnerNID(ulong offset, CancellationToken token)
        {
            var data = new byte[8];
            await SwitchConnection.WriteBytesAbsoluteAsync(data, offset, token).ConfigureAwait(false);
        }

        public async Task<bool> IsOnOverworld(ulong offset, CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
            return data[0] == 0x11;
        }

        // Only used to check if we made it off the title screen; the pointer isn't viable until a few seconds after clicking A.
        public async Task<bool> IsOnOverworldTitle(CancellationToken token)
        {
            var (valid, offset) = await ValidatePointerAll(Offsets.OverworldPointer, token).ConfigureAwait(false);
            if (!valid)
                return false;
            return await IsOnOverworld(offset, token).ConfigureAwait(false);
        }

        // 0x10 if fully loaded into Poké Portal.
        public async Task<bool> IsInPokePortal(ulong offset, CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
            return data[0] == 0x10;
        }

        // 0x14 in a box and during trades, trade evolutions, and move learning.
        public async Task<bool> IsInBox(ulong offset, CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
            return data[0] == 0x14;
        }

        public async Task<TextSpeedOption> GetTextSpeed(CancellationToken token)
        {
            var data = await SwitchConnection.PointerPeek(1, Offsets.ConfigPointer, token).ConfigureAwait(false);
            return (TextSpeedOption)(data[0] & 3);
        }


        // Zyro additions
        public async Task SetBoxPokemonEgg(PK9 pkm, ulong ofs, CancellationToken token)
        {
            pkm.ResetPartyStats();
            await SwitchConnection.WriteBytesAbsoluteAsync(pkm.EncryptedPartyData, ofs, token).ConfigureAwait(false);
        }

        public async Task SVSaveGameOverworld(CancellationToken token)
        {
            Log("Saving the game...");
            await Click(X, 2_000, token).ConfigureAwait(false);
            await Click(R, 1_800, token).ConfigureAwait(false);
            await Click(A, 1_000, token).ConfigureAwait(false);
            await Task.Delay(6_000, token).ConfigureAwait(false);
            await Click(B, 1_500, token).ConfigureAwait(false);
        }

        // Save Block Additions from TeraFinder/RaidCrawler/sv-livemap

        public async Task<object?> ReadBlock(DataBlock block, CancellationToken token)
        {
            if (block.IsEncrypted)
                return await ReadEncryptedBlock(block, token).ConfigureAwait(false);
            else
                return await ReadDecryptedBlock(block, token).ConfigureAwait(false);
        }
        public async Task<bool> WriteBlock(object data, DataBlock block, CancellationToken token, object? toExpect = default)
        {
            if (block.IsEncrypted)
                return await WriteEncryptedBlockSafe(block, toExpect, data, token).ConfigureAwait(false);
            else
                return await WriteDecryptedBlock((byte[])data!, block, token).ConfigureAwait(false);
        }
        private async Task<bool> WriteEncryptedBlockSafe(DataBlock block, object? toExpect, object toWrite, CancellationToken token)
        {
            if (toExpect == default || toWrite == default)
                return false;

            return block.Type switch
            {
                SCTypeCode.Object => await WriteEncryptedBlockObject(block, (byte[])toExpect, (byte[])toWrite, token),
                SCTypeCode.Array => await WriteEncryptedBlockArray(block, (byte[])toExpect, (byte[])toWrite, token).ConfigureAwait(false),
                SCTypeCode.Bool1 or SCTypeCode.Bool2 or SCTypeCode.Bool3 => await WriteEncryptedBlockBool(block, (bool)toExpect, (bool)toWrite, token).ConfigureAwait(false),
                SCTypeCode.Byte or SCTypeCode.SByte => await WriteEncryptedBlockByte(block, (byte)toExpect, (byte)toWrite, token).ConfigureAwait(false),
                SCTypeCode.UInt32 => await WriteEncryptedBlockUint(block, (uint)toExpect, (uint)toWrite, token).ConfigureAwait(false),
                SCTypeCode.Int32 => await WriteEncryptedBlockInt32(block, (int)toExpect, (int)toWrite, token).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Block {block.Name} (Type {block.Type}) is currently not supported.")
            };
        }
        private async Task<bool> WriteEncryptedBlockInt32(DataBlock block, int valueToExpect, int valueToInject, CancellationToken token)
        {
            if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
                throw new InvalidOperationException("No remote connection");

            //Always read and decrypt first to validate address and data
            ulong address;
            try { address = await GetBlockAddress(block, token).ConfigureAwait(false); }
            catch (Exception) { return false; }
            //If we get there without exceptions, the block address is valid
            var header = await SwitchConnection.ReadBytesAbsoluteAsync(address, 5, token).ConfigureAwait(false);
            header = BlockUtil.DecryptBlock(block.Key, header);
            //Validate ram data
            var ram = ReadInt32LittleEndian(header.AsSpan()[1..]);
            if (ram != valueToExpect) return false;
            //If we get there then both block address and block data are valid, we can safely inject
            WriteInt32LittleEndian(header.AsSpan()[1..], valueToInject);
            header = BlockUtil.EncryptBlock(block.Key, header);
            await SwitchConnection.WriteBytesAbsoluteAsync(header, address, token).ConfigureAwait(false);
            return true;
        }
        private async Task<byte[]> ReadDecryptedBlock(DataBlock block, CancellationToken token)
        {
            if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
                throw new InvalidOperationException("No remote connection");

            var data = await SwitchConnection.PointerPeek(block.Size, block.Pointer!, token).ConfigureAwait(false);
            return data;
        }

        private async Task<ulong> GetBlockAddress(DataBlock block, CancellationToken token, bool prepareAddress = true)
        {
            KeyBlockAddress = 0;

            if (KeyBlockAddress == 0)
                KeyBlockAddress = await SwitchConnection.PointerAll(block.Pointer!, token).ConfigureAwait(false);

            var keyblock = await SwitchConnection.ReadBytesAbsoluteAsync(KeyBlockAddress, 16, token).ConfigureAwait(false);
            var start = BitConverter.ToUInt64(keyblock.AsSpan()[..8]);
            var end = BitConverter.ToUInt64(keyblock.AsSpan()[8..]);
            var ct = (ulong)48;

            while (start < end)
            {
                var block_ct = (end - start) / ct;
                var mid = start + (block_ct >> 1) * ct;

                var data = await SwitchConnection.ReadBytesAbsoluteAsync(mid, 4, token).ConfigureAwait(false);
                var found = BitConverter.ToUInt32(data);
                if (found == block.Key)
                {
                    if (prepareAddress)
                        mid = await PrepareAddress(mid, token).ConfigureAwait(false);
                    return mid;
                }

                if (found >= block.Key)
                    end = mid;
                else start = mid + ct;
            }
            throw new ArgumentOutOfRangeException(nameof(block));
        }

        private async Task<ulong> PrepareAddress(ulong address, CancellationToken token) =>
            BitConverter.ToUInt64(await SwitchConnection.ReadBytesAbsoluteAsync(address + 8, 8, token).ConfigureAwait(false));

        private async Task<bool> WriteEncryptedBlockUint(DataBlock block, uint valueToExpect, uint valueToInject, CancellationToken token)
        {
            if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
                throw new InvalidOperationException("No remote connection");

            //Always read and decrypt first to validate address and data
            ulong address;
            try { address = await GetBlockAddress(block, token).ConfigureAwait(false); }
            catch (Exception) { return false; }
            //If we get there without exceptions, the block address is valid
            var header = await SwitchConnection.ReadBytesAbsoluteAsync(address, 5, token).ConfigureAwait(false);
            header = BlockUtil.DecryptBlock(block.Key, header);
            //Validate ram data
            var ram = ReadUInt32LittleEndian(header.AsSpan()[1..]);
            if (ram != valueToExpect) return false;
            //If we get there then both block address and block data are valid, we can safely inject
            WriteUInt32LittleEndian(header.AsSpan()[1..], valueToInject);
            header = BlockUtil.EncryptBlock(block.Key, header);
            await SwitchConnection.WriteBytesAbsoluteAsync(header, address, token).ConfigureAwait(false);
            return true;
        }
        private async Task<byte[]> ReadEncryptedBlockHeader(DataBlock block, CancellationToken token)
        {
            if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
                throw new InvalidOperationException("No remote connection");

            var address = await GetBlockAddress(block, token).ConfigureAwait(false);
            var header = await SwitchConnection.ReadBytesAbsoluteAsync(address, 5, token).ConfigureAwait(false);
            header = BlockUtil.DecryptBlock(block.Key, header);
            return header;
        }
        private async Task<int> ReadEncryptedBlockInt32(DataBlock block, CancellationToken token)
        {
            var header = await ReadEncryptedBlockHeader(block, token).ConfigureAwait(false);
            return ReadInt32LittleEndian(header.AsSpan()[1..]);
        }

        private async Task<bool> WriteEncryptedBlockByte(DataBlock block, byte valueToExpect, byte valueToInject, CancellationToken token)
        {
            if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
                throw new InvalidOperationException("No remote connection");

            //Always read and decrypt first to validate address and data
            ulong address;
            try { address = await GetBlockAddress(block, token).ConfigureAwait(false); }
            catch (Exception) { return false; }
            //If we get there without exceptions, the block address is valid
            var header = await SwitchConnection.ReadBytesAbsoluteAsync(address, 5, token).ConfigureAwait(false);
            header = BlockUtil.DecryptBlock(block.Key, header);
            //Validate ram data
            var ram = header[1];
            if (ram != valueToExpect) return false;
            //If we get there then both block address and block data are valid, we can safely inject
            header[1] = valueToInject;
            header = BlockUtil.EncryptBlock(block.Key, header);
            await SwitchConnection.WriteBytesAbsoluteAsync(header, address, token).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> WriteDecryptedBlock(byte[] data, DataBlock block, CancellationToken token)
        {
            if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
                throw new InvalidOperationException("No remote connection");

            var pointer = await SwitchConnection.PointerAll(block.Pointer!, token).ConfigureAwait(false);
            await SwitchConnection.WriteBytesAbsoluteAsync(data, pointer, token).ConfigureAwait(false);

            return true;
        }
        private async Task<bool> WriteEncryptedBlockBool(DataBlock block, bool valueToExpect, bool valueToInject, CancellationToken token)
        {
            if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
                throw new InvalidOperationException("No remote connection");

            //Always read and decrypt first to validate address and data
            ulong address;
            try { address = await GetBlockAddress(block, token).ConfigureAwait(false); }
            catch (Exception) { return false; }
            //If we get there without exceptions, the block address is valid
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(address, block.Size, token).ConfigureAwait(false);
            data = BlockUtil.DecryptBlock(block.Key, data);
            //Validate ram data
            var ram = data[0] == 2;
            if (ram != valueToExpect) return false;
            //If we get there then both block address and block data are valid, we can safely inject
            data[0] = valueToInject ? (byte)2 : (byte)1;
            data = BlockUtil.EncryptBlock(block.Key, data);
            await SwitchConnection.WriteBytesAbsoluteAsync(data, address, token).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> WriteEncryptedBlockArray(DataBlock block, byte[] arrayToExpect, byte[] arrayToInject, CancellationToken token)
        {
            if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
                throw new InvalidOperationException("No remote connection");

            //Always read and decrypt first to validate address and data
            ulong address;
            try { address = await GetBlockAddress(block, token).ConfigureAwait(false); }
            catch (Exception) { return false; }
            //If we get there without exceptions, the block address is valid
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(address, 6 + block.Size, token).ConfigureAwait(false);
            data = BlockUtil.DecryptBlock(block.Key, data);
            //Validate ram data
            var ram = data[6..];
            if (!ram.SequenceEqual(arrayToExpect)) return false;
            //If we get there then both block address and block data are valid, we can safely inject
            Array.ConstrainedCopy(arrayToInject, 0, data, 6, block.Size);
            data = BlockUtil.EncryptBlock(block.Key, data);
            await SwitchConnection.WriteBytesAbsoluteAsync(data, address, token).ConfigureAwait(false);
            return true;
        }

        public async Task<bool> WriteEncryptedBlockObject(DataBlock block, byte[] valueToExpect, byte[] valueToInject, CancellationToken token)
        {
            if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
                throw new InvalidOperationException("No remote connection");

            //Always read and decrypt first to validate address and data
            ulong address;
            try { address = await GetBlockAddress(block, token).ConfigureAwait(false); }
            catch (Exception) { return false; }
            //If we get there without exceptions, the block address is valid
            var header = await SwitchConnection.ReadBytesAbsoluteAsync(address, 5, token).ConfigureAwait(false);
            header = BlockUtil.DecryptBlock(block.Key, header);
            var size = ReadUInt32LittleEndian(header.AsSpan()[1..]);
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(address, 5 + (int)size, token);
            var ram = BlockUtil.DecryptBlock(block.Key, data)[5..];
            if (!ram.SequenceEqual(valueToExpect)) { return false; }
            //If we get there then both block address and block data are valid, we can safely inject
            Array.ConstrainedCopy(valueToInject.ToArray(), 0, data, 5, block.Size);
            data = BlockUtil.EncryptBlock(block.Key, data);
            await SwitchConnection.WriteBytesAbsoluteAsync(data, address, token).ConfigureAwait(false);
            return true;
        }

        private async Task<object?> ReadEncryptedBlock(DataBlock block, CancellationToken token)
        {
            return block.Type switch
            {
                SCTypeCode.Object => await ReadEncryptedBlockObject(block, token).ConfigureAwait(false),
                SCTypeCode.Array => await ReadEncryptedBlockArray(block, token).ConfigureAwait(false),
                SCTypeCode.Bool1 or SCTypeCode.Bool2 or SCTypeCode.Bool3 => await ReadEncryptedBlockBool(block, token).ConfigureAwait(false),
                SCTypeCode.Byte or SCTypeCode.SByte => await ReadEncryptedBlockByte(block, token).ConfigureAwait(false),
                SCTypeCode.UInt32 => await ReadEncryptedBlockUint(block, token).ConfigureAwait(false),
                SCTypeCode.Int32 => await ReadEncryptedBlockInt32(block, token).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Block {block.Name} (Type {block.Type}) is currently not supported.")
            };
        }
        private async Task<byte[]?> ReadEncryptedBlockArray(DataBlock block, CancellationToken token)
        {
            if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
                throw new InvalidOperationException("No remote connection");

            var address = await GetBlockAddress(block, token).ConfigureAwait(false);
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(address, 6 + block.Size, token).ConfigureAwait(false);
            data = BlockUtil.DecryptBlock(block.Key, data);
            return data[6..];
        }
        public async Task<bool> ReadEncryptedBlockBool(DataBlock block, CancellationToken token)
        {
            if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
                throw new InvalidOperationException("No remote connection");
            var address = await GetBlockAddress(block, token).ConfigureAwait(false);
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(address, block.Size, token).ConfigureAwait(false);
            var res = BlockUtil.DecryptBlock(block.Key, data);
            return res[0] == 2;
        }
        private async Task<byte> ReadEncryptedBlockByte(DataBlock block, CancellationToken token)
        {
            var header = await ReadEncryptedBlockHeader(block, token).ConfigureAwait(false);
            return header[1];
        }
        private async Task<uint> ReadEncryptedBlockUint(DataBlock block, CancellationToken token)
        {
            var header = await ReadEncryptedBlockHeader(block, token).ConfigureAwait(false);
            return ReadUInt32LittleEndian(header.AsSpan()[1..]);
        }
        private async Task<byte[]?> ReadEncryptedBlockObject(DataBlock block, CancellationToken token)
        {
            if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
                throw new InvalidOperationException("No remote connection");

            var address = await GetBlockAddress(block, token).ConfigureAwait(false);
            var header = await SwitchConnection.ReadBytesAbsoluteAsync(address, 5, token).ConfigureAwait(false);
            header = BlockUtil.DecryptBlock(block.Key, header);
            var size = ReadUInt32LittleEndian(header.AsSpan()[1..]);
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(address, 5 + (int)size, token);
            var res = BlockUtil.DecryptBlock(block.Key, data)[5..];
            return res;
        }
        public async Task<ulong> SearchSaveKey(ulong baseBlock, uint key, CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(baseBlock + 8, 16, token).ConfigureAwait(false);
            var start = BitConverter.ToUInt64(data.AsSpan()[..8]);
            var end = BitConverter.ToUInt64(data.AsSpan()[8..]);

            while (start < end)
            {
                var block_ct = (end - start) / 48;
                var mid = start + (block_ct >> 1) * 48;

                data = await SwitchConnection.ReadBytesAbsoluteAsync(mid, 4, token).ConfigureAwait(false);
                var found = BitConverter.ToUInt32(data);
                if (found == key)
                    return mid;

                if (found >= key)
                    end = mid;
                else start = mid + 48;
            }
            return start;
        }

        private static byte[] DecryptBlock(uint key, byte[] block)
        {
            var rng = new SCXorShift32(key);
            for (int i = 0; i < block.Length; i++)
                block[i] = (byte)(block[i] ^ rng.Next());
            return block;
        }

    public async Task<ulong> SearchSaveKeyRaid(ulong BaseBlockKeyPointer, uint key, CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(BaseBlockKeyPointer + 8, 16, token).ConfigureAwait(false);
            var start = BitConverter.ToUInt64(data.AsSpan()[..8]);
            var end = BitConverter.ToUInt64(data.AsSpan()[8..]);

            while (start < end)
            {
                var block_ct = (end - start) / 48;
                var mid = start + (block_ct >> 1) * 48;

                data = await SwitchConnection.ReadBytesAbsoluteAsync(mid, 4, token).ConfigureAwait(false);
                var found = BitConverter.ToUInt32(data);
                if (found == key)
                    return mid;

                if (found >= key)
                    end = mid;
                else start = mid + 48;
            }
            return start;
        }

        public async Task<byte[]> ReadSaveBlockRaid(ulong BaseBlockKeyPointer, uint key, int size, CancellationToken token)
        {
            var block_ofs = await SearchSaveKeyRaid(BaseBlockKeyPointer, key, token).ConfigureAwait(false);
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(block_ofs + 8, 0x8, token).ConfigureAwait(false);
            block_ofs = BitConverter.ToUInt64(data, 0);

            var block = await SwitchConnection.ReadBytesAbsoluteAsync(block_ofs, size, token).ConfigureAwait(false);
            return DecryptBlock(key, block);
        }

        public async Task<byte[]> ReadSaveBlockObject(ulong BaseBlockKeyPointer, uint key, CancellationToken token)
        {
            var header_ofs = await SearchSaveKeyRaid(BaseBlockKeyPointer, key, token).ConfigureAwait(false);
            var data = await SwitchConnection.ReadBytesAbsoluteAsync(header_ofs + 8, 8, token).ConfigureAwait(false);
            header_ofs = BitConverter.ToUInt64(data);

            var header = await SwitchConnection.ReadBytesAbsoluteAsync(header_ofs, 5, token).ConfigureAwait(false);
            header = DecryptBlock(key, header);

            var size = BitConverter.ToUInt32(header.AsSpan()[1..]);
            var obj = await SwitchConnection.ReadBytesAbsoluteAsync(header_ofs, (int)size + 5, token).ConfigureAwait(false);
            return DecryptBlock(key, obj)[5..];
        }

        public async Task<byte[]> ReadBlockDefault(ulong BaseBlockKeyPointer, uint key, string? cache, bool force, CancellationToken token)
        {
            var folder = Path.Combine(Directory.GetCurrentDirectory(), "cache");
            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, cache ?? "");
            if (force is false && cache is not null && File.Exists(path))
                return File.ReadAllBytes(path);

            var bin = await ReadSaveBlockObject(BaseBlockKeyPointer, key, token).ConfigureAwait(false);
            File.WriteAllBytes(path, bin);
            return bin;
        }

        private readonly IReadOnlyList<uint> DifficultyFlags = new List<uint>() { 0xEC95D8EF, 0xA9428DFE, 0x9535F471, 0x6E7F8220 };
        public async Task<int> GetStoryProgress(ulong BaseBlockKeyPointer, CancellationToken token)
        {
            for (int i = DifficultyFlags.Count - 1; i >= 0; i--)
            {
                // See https://github.com/Lincoln-LM/sv-live-map/pull/43
                var block = await ReadSaveBlockRaid(BaseBlockKeyPointer, DifficultyFlags[i], 1, token).ConfigureAwait(false);
                if (block[0] == 2)
                    return i + 1;
            }
            return 0;
        }
        public async Task<GameProgress> ReadGameProgress(CancellationToken token)
        {

            var Unlocked6Stars = await ReadEncryptedBlockBool(RaidDataBlocks.KUnlockedRaidDifficulty6, token).ConfigureAwait(false);
            if (Unlocked6Stars)
                return GameProgress.Unlocked6Stars;

            var Unlocked5Stars = await ReadEncryptedBlockBool(RaidDataBlocks.KUnlockedRaidDifficulty5, token).ConfigureAwait(false);

            if (Unlocked5Stars)
                return GameProgress.Unlocked5Stars;

            var Unlocked4Stars = await ReadEncryptedBlockBool(RaidDataBlocks.KUnlockedRaidDifficulty4, token).ConfigureAwait(false);

            if (Unlocked4Stars)
                return GameProgress.Unlocked4Stars;

            var Unlocked3Stars = await ReadEncryptedBlockBool(RaidDataBlocks.KUnlockedRaidDifficulty3, token).ConfigureAwait(false);

            if (Unlocked3Stars)
                return GameProgress.Unlocked3Stars;

            return GameProgress.UnlockedTeraRaids;
        }


        public async Task ReadEventRaids(ulong BaseBlockKeyPointer, RaidContainer container, CancellationToken token, bool force = false)
        {
            var prio_file = Path.Combine(Directory.GetCurrentDirectory(), "cache", "raid_priority_array");
            if (!force && File.Exists(prio_file))
            {
                (_, var version) = FlatbufferDumper.DumpDeliveryPriorities(File.ReadAllBytes(prio_file));
                var blk = await ReadBlockDefault(BaseBlockKeyPointer, RaidCrawler.Core.Structures.Offsets.BCATRaidPriorityLocation, "raid_priority_array.tmp", true, token).ConfigureAwait(false);
                (_, var v2) = FlatbufferDumper.DumpDeliveryPriorities(blk);
                if (version != v2)
                    force = true;

                var tmp_file = Path.Combine(Directory.GetCurrentDirectory(), "cache", "raid_priority_array.tmp");
                if (File.Exists(tmp_file))
                    File.Delete(tmp_file);

                if (v2 == 0) // raid reset
                    return;
            }

            var delivery_raid_prio = await ReadBlockDefault(BaseBlockKeyPointer, RaidCrawler.Core.Structures.Offsets.BCATRaidPriorityLocation, "raid_priority_array", force, token).ConfigureAwait(false);
            (var group_id, var priority) = FlatbufferDumper.DumpDeliveryPriorities(delivery_raid_prio);
            if (priority == 0)
                return;

            var delivery_raid_fbs = await ReadBlockDefault(BaseBlockKeyPointer, RaidCrawler.Core.Structures.Offsets.BCATRaidBinaryLocation, "raid_enemy_array", force, token).ConfigureAwait(false);
            var delivery_fixed_rewards = await ReadBlockDefault(BaseBlockKeyPointer, RaidCrawler.Core.Structures.Offsets.BCATRaidFixedRewardLocation, "fixed_reward_item_array", force, token).ConfigureAwait(false);
            var delivery_lottery_rewards = await ReadBlockDefault(BaseBlockKeyPointer, RaidCrawler.Core.Structures.Offsets.BCATRaidLotteryRewardLocation, "lottery_reward_item_array", force, token).ConfigureAwait(false);

            container.DistTeraRaids = TeraDistribution.GetAllEncounters(delivery_raid_fbs);
            container.MightTeraRaids = TeraMight.GetAllEncounters(delivery_raid_fbs);
            container.DeliveryRaidPriority = group_id;
            container.DeliveryRaidFixedRewards = FlatbufferDumper.DumpFixedRewards(delivery_fixed_rewards);
            container.DeliveryRaidLotteryRewards = FlatbufferDumper.DumpLotteryRewards(delivery_lottery_rewards);
        }

        public static (PK9, uint) IsSeedReturned(ITeraRaid enc, Raid raid)
        {
            var param = enc.GetParam();
            var blank = new PK9
            {
                Species = enc.Species,
                Form = enc.Form
            };
            Encounter9RNG.GenerateData(blank, param, EncounterCriteria.Unrestricted, raid.Seed);

            return (blank, raid.Seed);
        }

        public static string GetSpecialRewards(IReadOnlyList<(int, int, int)> rewards)
        {
            string s = string.Empty;
            int abilitycapsule = 0;
            int bottlecap = 0;
            int abilitypatch = 0;
            int sweetherba = 0;
            int saltyherba = 0;
            int sourherba = 0;
            int bitterherba = 0;
            int spicyherba = 0;
            int xl = 0;
            int l = 0;
            int rare = 0;

            for (int i = 0; i < rewards.Count; i++)
            {
                switch (rewards[i].Item1)
                {
                    case 0050: rare++; break;
                    case 0645: abilitycapsule++; break;
                    case 0795: bottlecap++; break;
                    case 1127: l++; break;
                    case 1128: xl++; break;
                    case 1606: abilitypatch++; break;
                    case 1904: sweetherba++; break;
                    case 1905: saltyherba++; break;
                    case 1906: sourherba++; break;
                    case 1907: bitterherba++; break;
                    case 1908: spicyherba++; break;
                }
            }

            s += (rare > 0) ? $"Rare Candy x{rare}\n" : "";
            s += (l > 0) ? $"Exp. Candy L x{l}\n" : "";
            s += (xl > 0) ? $"Exp. Candy XL x{xl}\n" : "";
            s += (abilitycapsule > 0) ? $"Ability Capsule x{abilitycapsule}\n" : "";
            s += (bottlecap > 0) ? $"Bottle Cap x{bottlecap}\n" : "";
            s += (abilitypatch > 0) ? $"Ability Patch x{abilitypatch}\n" : "";
            s += (sweetherba > 0) ? $"Sweet Herba Mystica x{sweetherba}\n" : "";
            s += (saltyherba > 0) ? $"Salty Herba  Mystica x{saltyherba}\n" : "";
            s += (sourherba > 0) ? $"Sour Herba  Mystica x{sourherba}\n" : "";
            s += (bitterherba > 0) ? $"Bitter Herba  Mystica x{bitterherba}\n" : "";
            s += (spicyherba > 0) ? $"Spicy Herba  Mystica x{spicyherba}\n" : "";

            return s;
        }

        public static string[] ProcessRaidPlaceholders(string[] description, PKM pk)
        {
            string[] raidDescription = Array.Empty<string>();

            if (description.Length > 0)
                raidDescription = description.ToArray();

            string markEntryText = "";
            string markTitle = "";
            string scaleText = "";
            string scaleNumber = "";
            string shinySymbol = pk.ShinyXor == 0 ? "■" : pk.ShinyXor <= 16 ? "★" : "";
            string shinySymbolText = pk.ShinyXor == 0 ? "Square Shiny" : pk.ShinyXor <= 16 ? "Star Shiny" : "";
            string shiny = pk.ShinyXor <= 16 ? "Shiny" : "";
            string species = SpeciesName.GetSpeciesNameGeneration(pk.Species, 2, 9);
            string IVList = $"{pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}";
            string MaxIV = "";
            string HP = pk.IV_HP.ToString();
            string ATK = pk.IV_ATK.ToString();
            string DEF = pk.IV_DEF.ToString();
            string SPA = pk.IV_SPA.ToString();
            string SPD = pk.IV_SPD.ToString();
            string SPE = pk.IV_SPE.ToString();
            string nature = $"{(Nature)pk.Nature}";
            string genderSymbol = pk.Gender == 0 ? "♂" : pk.Gender == 1 ? "♀" : "⚥";
            string genderText = $"{(Gender)pk.Gender}";
            string ability = $"{GameInfo.GetStrings(1).Ability[pk.Ability]}";

            if (pk.IV_HP == 31 && pk.IV_ATK == 31 && pk.IV_DEF == 31 && pk.IV_SPA == 31 && pk.IV_SPD == 31 && pk.IV_SPE == 31)
                MaxIV = "6IV";

            RaidExtensions<PK9>.HasMark((IRibbonIndex)pk, out RibbonIndex mark);
            if (mark == RibbonIndex.MarkMightiest)
                markEntryText = "the Unrivaled";
            if (pk is PK9 pkl)
            {
                scaleText = $"{PokeSizeDetailedUtil.GetSizeRating(pkl.Scale)}";
                scaleNumber = pkl.Scale.ToString();
                if (pkl.Scale == 0)
                {
                    markEntryText = "The Teeny";
                    markTitle = "Teeny";
                }
                if (pkl.Scale == 255)
                {
                    markEntryText = "The Great";
                    markTitle = "Jumbo";
                }
            }

            for (int i = 0; i < raidDescription.Length; i++)
                raidDescription[i] = raidDescription[i].Replace("{markEntryText}", markEntryText)
                        .Replace("{markTitle}", markTitle).Replace("{scaleText}", scaleText).Replace("{scaleNumber}", scaleNumber).Replace("{shinySymbol}", shinySymbol).Replace("{shinySymbolText}", shinySymbolText)
                        .Replace("{shinyText}", shiny).Replace("{species}", species).Replace("{IVList}", IVList).Replace("{MaxIV}", MaxIV).Replace("{HP}", HP).Replace("{ATK}", ATK).Replace("{DEF}", DEF).Replace("{SPA}", SPA)
                        .Replace("{SPD}", SPD).Replace("{SPE}", SPE).Replace("{nature}", nature).Replace("{ability}", ability).Replace("{genderSymbol}", genderSymbol).Replace("{genderText}", genderText);

            return raidDescription;
        }
    }
}
