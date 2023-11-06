﻿using PKHeX.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace SysBot.Pokemon.SV.BotRaid
{
    public static class BlockUtil
    {
        public static SCBlock CreateObjectBlock(uint key, ReadOnlySpan<byte> data)
        {
            var block = (SCBlock)FormatterServices.GetUninitializedObject(typeof(SCBlock));
            var keyInfo = typeof(SCBlock).GetField("Key", BindingFlags.Instance | BindingFlags.Public)!;
            keyInfo.SetValue(block, key);
            var typeInfo = typeof(SCBlock).GetProperty("Type")!;
            typeInfo.SetValue(block, SCTypeCode.Object);
            var dataInfo = typeof(SCBlock).GetField("Data", BindingFlags.Instance | BindingFlags.Public)!;
            dataInfo.SetValue(block, data.ToArray());
            return block;
        }

        public static SCBlock CreateDummyBlock(uint key, SCTypeCode dummy)
        {
            var block = (SCBlock)FormatterServices.GetUninitializedObject(typeof(SCBlock));
            var keyInfo = typeof(SCBlock).GetField("Key", BindingFlags.Instance | BindingFlags.Public)!;
            keyInfo.SetValue(block, key);
            var typeInfo = typeof(SCBlock).GetProperty("Type")!;
            typeInfo.SetValue(block, dummy);
            var dataInfo = typeof(SCBlock).GetField("Data", BindingFlags.Instance | BindingFlags.Public)!;
            dataInfo.SetValue(block, Array.Empty<byte>());
            return block;
        }

        public static void AddBlockToFakeSAV(SAV9SV sav, SCBlock block)
        {
            var list = new List<SCBlock>();
            foreach (var b in sav.Accessor.BlockInfo) list.Add(b);
            list.Add(block);
            var typeInfo = typeof(SAV9SV).GetField("<AllBlocks>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
            typeInfo.SetValue(sav, list);
            typeInfo = typeof(SaveBlockAccessor9SV).GetField("<BlockInfo>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
            typeInfo.SetValue(sav.Blocks, list);
        }

        public static void EditBlock(SCBlock block, SCTypeCode type, ReadOnlySpan<byte> data)
        {
            EditBlockType(block, type);
            var dataInfo = typeof(SCBlock).GetField("Data", BindingFlags.Instance | BindingFlags.Public)!;
            dataInfo.SetValue(block, data.ToArray());
        }

        public static void EditBlock(SCBlock block, SCTypeCode type, uint value)
        {
            EditBlockType(block, type);
            var dataInfo = typeof(SCBlock).GetField("Data", BindingFlags.Instance | BindingFlags.Public)!;
            dataInfo.SetValue(block, BitConverter.GetBytes(value));
        }

        public static void EditBlock(SCBlock block, SCTypeCode type, int value)
        {
            EditBlockType(block, type);
            var dataInfo = typeof(SCBlock).GetField("Data", BindingFlags.Instance | BindingFlags.Public)!;
            dataInfo.SetValue(block, BitConverter.GetBytes(value));
        }

        public static void EditBlock(SCBlock block, SCTypeCode type, byte value)
        {
            EditBlockType(block, type);
            var dataInfo = typeof(SCBlock).GetField("Data", BindingFlags.Instance | BindingFlags.Public)!;
            dataInfo.SetValue(block, new byte[] { value });
        }

        public static void EditBlockType(SCBlock block, SCTypeCode type)
        {
            var typeInfo = typeof(SCBlock).GetProperty("Type")!;
            typeInfo.SetValue(block, type);
        }

        public static SCBlock FindOrDefault(this SCBlockAccessor Accessor, uint Key) => Accessor.BlockInfo.FindOrDefault(Key);

        public static SCBlock FindOrDefault(this IReadOnlyList<SCBlock> blocks, uint key)
        {
            var res = blocks.Where(block => block.Key == key).FirstOrDefault();
            return res is not null ? res : CreateDummyBlock(key, SCTypeCode.None);
        }

        public static byte[] EncryptBlock(uint key, byte[] block) => DecryptBlock(key, block);

        public static byte[] DecryptBlock(uint key, byte[] block)
        {
            var rng = new SCXorShift32(key);
            for (var i = 0; i < block.Length; i++)
                block[i] = (byte)(block[i] ^ rng.Next());
            return block;
        }
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

            public static readonly DataBlock KWildSpawnsEnabled = new()
            {
                Name = "KUnlockedRaidDifficulty6",
                Key = 0xC812EDC7,
                Type = SCTypeCode.Bool1,
                Pointer = SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 1,
            };
        }
    }
}
