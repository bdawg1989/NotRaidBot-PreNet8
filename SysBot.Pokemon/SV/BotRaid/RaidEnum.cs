using PKHeX.Core;
using System.Collections.Generic;

namespace SysBot.Pokemon
{
    public enum DTFormat
    {
        MMDDYY,
        DDMMYY,
        YYMMDD,
    }
    public enum TeraCrystalType : int
    {
        Base = 0,
        Black = 1,
        Distribution = 2,
        Might = 3,
    }
    public enum LobbyMethodOptions
    {
        SkipRaid,
        OpenLobby,
        ContinueRaid
    }

    public enum RaidAction
    {
        AFK,
        MashA
    }

    public enum GameProgress : byte
    {
        Beginning = 0,
        UnlockedTeraRaids = 1,
        Unlocked3Stars = 2,
        Unlocked4Stars = 3,
        Unlocked5Stars = 4,
        Unlocked6Stars = 5,
        None = 6,
    }

    public class DataBlock
    {
        public string? Name { get; set; }
        public uint Key { get; set; }
        public SCTypeCode Type { get; set; }
        public SCTypeCode SubType { get; set; }
        public IReadOnlyList<long>? Pointer { get; set; }
        public bool IsEncrypted { get; set; }
        public int Size { get; set; }
    }

    public static class Blocks
    {
        public static readonly long[] SaveBlockKeyPointer = { 0x4617648, 0xD8, 0x0, 0x0, 0x30, 0x08 };

        public static class RaidDataBlocks
        {
            public static readonly DataBlock KUnlockedTeraRaidBattles = new()
            {
                Name = "KUnlockedTeraRaidBattles",
                Key = 0x27025EBF,
                Type = SCTypeCode.Bool1,
                Pointer = SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 1,
            };

            public static readonly DataBlock KUnlockedRaidDifficulty3 = new()
            {
                Name = "KUnlockedRaidDifficulty3",
                Key = 0xEC95D8EF,
                Type = SCTypeCode.Bool1,
                Pointer = SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 1,
            };

            public static readonly DataBlock KUnlockedRaidDifficulty4 = new()
            {
                Name = "KUnlockedRaidDifficulty4",
                Key = 0xA9428DFE,
                Type = SCTypeCode.Bool1,
                Pointer = SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 1,
            };

            public static readonly DataBlock KUnlockedRaidDifficulty5 = new()
            {
                Name = "KUnlockedRaidDifficulty5",
                Key = 0x9535F471,
                Type = SCTypeCode.Bool1,
                Pointer = SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 1,
            };

            public static readonly DataBlock KUnlockedRaidDifficulty6 = new()
            {
                Name = "KUnlockedRaidDifficulty6",
                Key = 0x6E7F8220,
                Type = SCTypeCode.Bool1,
                Pointer = SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 1,
            };
        }
    }
}