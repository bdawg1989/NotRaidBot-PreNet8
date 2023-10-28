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
}