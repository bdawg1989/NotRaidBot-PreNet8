using PKHeX.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SysBot.Pokemon
{
    public class StopConditionSettings
    {
        private static bool HasMark(IRibbonIndex pk)
        {
            for (var mark = RibbonIndex.MarkLunchtime; mark <= RibbonIndex.MarkSlump; mark++)
            {
                if (pk.GetRibbon((int)mark))
                    return true;
            }
            return false;
        }

        public static bool HasMark(IRibbonIndex pk, out RibbonIndex result)
        {
            result = default;
            for (var mark = RibbonIndex.MarkLunchtime; mark <= RibbonIndex.MarkSlump; mark++)
            {
                if (pk.GetRibbon((int)mark))
                {
                    result = mark;
                    return true;
                }
            }
            for (var mark = RibbonIndex.MarkJumbo; mark <= RibbonIndex.MarkMini; mark++)
            {
                if (pk.GetRibbon((int)mark))
                {
                    result = mark;
                    return true;
                }
            }
            return false;
        }

        public string GetPrintName(PKM pk)
        {
            var set = ShowdownParsing.GetShowdownText(pk);
            if (pk is IRibbonIndex r)
            {
                var rstring = GetMarkName(r);
                if (!string.IsNullOrEmpty(rstring))
                    set += $"\nPokémon found to have **{GetMarkName(r)}**!";
            }
            return set;
        }

        public string GetSpecialPrintName(PKM pk)
        {
            string markEntryText = "";
            HasMark((IRibbonIndex)pk, out RibbonIndex mark);
            var index = (int)mark - (int)RibbonIndex.MarkLunchtime;
            if (index >= 0)
                markEntryText = MarkTitle[index];
            var set = $"{(pk.ShinyXor == 0 ? "■ - " : pk.ShinyXor <= 16 ? "★ - " : "")}{SpeciesName.GetSpeciesNameGeneration(pk.Species, 2, 9)}{TradeExtensions<PK9>.FormOutput(pk.Species, pk.Form, out _)}{markEntryText}\nIVs: {pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}\nNature: {(Nature)pk.Nature} | Gender: {(Gender)pk.Gender}";
            if (pk is IRibbonIndex r)
            {
                var rstring = GetMarkName(r);
                if (!string.IsNullOrEmpty(rstring))
                    set += $"\nPokémon has the **{GetMarkName(r)}**!";
            }
            if (pk is PK9 pk9)
            {
                set += $"\nScale: {PokeSizeDetailedUtil.GetSizeRating(pk9.Scale)} ({pk9.Scale})";
            }
            return set;
        }

        public readonly string[] MarkTitle =
        {
            " the Peckish"," the Sleepy"," the Dozy"," the Early Riser"," the Cloud Watcher"," the Sodden"," the Thunderstruck"," the Snow Frolicker"," the Shivering"," the Parched"," the Sandswept"," the Mist Drifter",
            " the Chosen One"," the Catch of the Day"," the Curry Connoisseur"," the Sociable"," the Recluse"," the Rowdy"," the Spacey"," the Anxious"," the Giddy"," the Radiant"," the Serene"," the Feisty"," the Daydreamer",
            " the Joyful"," the Furious"," the Beaming"," the Teary-Eyed"," the Chipper"," the Grumpy"," the Scholar"," the Rampaging"," the Opportunist"," the Stern"," the Kindhearted"," the Easily Flustered"," the Driven",
            " the Apathetic"," the Arrogant"," the Reluctant"," the Humble"," the Pompous"," the Lively"," the Worn-Out", " of the Distant Past", " the Twinkling Star", " the Paldea Champion", " the Great", " the Teeny", " the Treasure Hunter",
            " the Reliable Partner", " the Gourmet", " the One-in-a-Million", " the Former Alpha", " the Unrivaled", " the Former Titan",
        };

        public virtual bool IsUnwantedMark(string mark, IReadOnlyList<string> marklist) => marklist.Contains(mark);

        public static string GetMarkName(IRibbonIndex pk)
        {
            for (var mark = RibbonIndex.MarkLunchtime; mark <= RibbonIndex.MarkSlump; mark++)
            {
                if (pk.GetRibbon((int)mark))
                    return RibbonStrings.GetName($"Ribbon{mark}");
            }
            return "";
        }

        public string GetAlphaPrintName(PA8 pk)
        {
            string alpha = string.Empty;
            if (pk.IsAlpha) alpha = $"Alpha - ";
            var set = $"\n{alpha}{(pk.ShinyXor == 0 ? "■ - " : pk.ShinyXor <= 16 ? "★ - " : "") }{SpeciesName.GetSpeciesNameGeneration(pk.Species, 2, 8)}{TradeExtensions<PK8>.FormOutput(pk.Species, pk.Form, out _)}\nNature: {(Nature)pk.Nature} | Gender: {(Gender)pk.Gender}\nEC: {pk.EncryptionConstant:X8} | PID: {pk.PID:X8}\nIVs: {pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}";
            return set;
        }

        public string GetRaidPrintName(PKM pk)
        {
            string markEntryText = "";
            HasMark((IRibbonIndex)pk, out RibbonIndex mark);
            if (mark == RibbonIndex.MarkMightiest)
                markEntryText = " The Unrivaled";
            if (pk is PK9 pkl)
            {
                if (pkl.Scale == 0)
                    markEntryText = " The Teeny";
                if (pkl.Scale == 255)
                    markEntryText = " The Great";
            }
            return markEntryText;
        }
    }

    public enum TargetShinyType
    {
        DisableOption,  // Doesn't care
        NonShiny,       // Match nonshiny only
        AnyShiny,       // Match any shiny regardless of type
        StarOnly,       // Match star shiny only
        SquareOnly,     // Match square shiny only
    }
}
