﻿using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;


namespace SysBot.Pokemon
{
    public class RotatingRaidSettingsSV : IBotStateSettings
    {
        private const string Hosting = nameof(Hosting);
        private const string Counts = nameof(Counts);
        private const string FeatureToggle = nameof(FeatureToggle);
        public enum TeraIconType
        {
            Icon1, // Use special set
            Icon2 // Use boring set
        }
        // Action1 dropdown enum
        public enum Action1Type
        {
            GoAllOut,
            HangTough,
            HealUp,
            Move1,
            Move2,
            Move3,
            Move4
        }
        public enum ScreenshotTimingOptions
        {
            [Description("1500 milliseconds")]
            _1500 = 1500, // Team SS
            [Description("22000 milliseconds")]
            _22000 = 22000 // Everything SS
        }

        public override string ToString() => "RotatingRaidSV Settings";

        [Category(Hosting), Description("Your Active Raid List lives here.")]
        public List<RotatingRaidParameters> ActiveRaids { get; set; } = new();

        public RotatingRaidSettingsCategory RaidSettings { get; set; } = new RotatingRaidSettingsCategory();

        public RotatingRaidPresetFiltersCategory EmbedToggles { get; set; } = new RotatingRaidPresetFiltersCategory();

        [Category(Hosting), Description("Settings related to Events."), Browsable(true)]
        public EventSettingsCategory EventSettings { get; set; } = new();

        [Category(Hosting), Description("Lobby Options"), DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public LobbyFiltersCategory LobbyOptions { get; set; } = new();

        [Category(Hosting), Description("Users NIDs here are banned raiders.")]
        public RemoteControlAccessList RaiderBanList { get; set; } = new() { AllowIfEmpty = false };

        public MiscSettingsCategory MiscSettings { get; set; } = new MiscSettingsCategory();
        [Browsable(false)]
        public bool ScreenOff
        {
            get => MiscSettings.ScreenOff;
            set => MiscSettings.ScreenOff = value;
        }

        public class RotatingRaidParameters
        {
            public override string ToString() => $"{Title}";
            public bool ActiveInRotation { get; set; } = true;
            public int DifficultyLevel { get; set; } = 0;
            public int StoryProgressLevel { get; set; } = 5;
            public TeraCrystalType CrystalType { get; set; } = TeraCrystalType.Base;
            [Browsable(false)]
            public string[] Description { get; set; } = Array.Empty<string>();
            public bool IsCoded { get; set; } = true;
            [Browsable(false)]
            public bool IsSet { get; set; } = false;
            public bool IsShiny { get; set; } = true;
            public Species Species { get; set; } = Species.None;
            public int SpeciesForm { get; set; } = 0;
            public string[] PartyPK { get; set; } = Array.Empty<string>();
            public bool SpriteAlternateArt { get; set; } = false;
            public string Seed { get; set; } = "0";
            public Action1Type Action1 { get; set; } = Action1Type.GoAllOut;
            public int Action1Delay { get; set; } = 5; // Default delay of 5 seconds
            public string Title { get; set; } = string.Empty;
            [Browsable(false)]
            public bool AddedByRACommand { get; set; } = false;
            [Browsable(false)]
            public ulong RequestedByUserID { get; set; } // Add this line for User ID
            [Browsable(false)]
            [System.Text.Json.Serialization.JsonIgnore]
            public SocketUser? User { get; set; }
        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<EventSettingsCategory>))]
        public class EventSettingsCategory
        {
            public override string ToString() => "Event Settings";

            [Category(Hosting), Description("Set to \"true\" when events are active to properly process level 7 (event) and level 5 (distribution) raids.")]
            public bool EventActive { get; set; } = false;

            [Category(Hosting), Description("Set this value to the value of the event den location (Event Index) from Tera Finder.  Make sure you are at this location if you plan to host event raids.  -1 means No Event.")]
            public int RaidDeliveryGroupID { get; set; } = -1;
        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<RotatingRaidSettingsCategory>))]
        public class RotatingRaidSettingsCategory
        {
            public override string ToString() => "Raid Settings";

            [Category(Hosting), Description("When enabled, the bot will attempt to auto-generate your raids from the \"raidsv.txt\" file on botstart.")]
            public bool GenerateRaidsFromFile { get; set; } = true;

            [Category(Hosting), Description("When enabled, the bot will save your current ActiveRaids list to the \"savedSeeds.txt\" file on bot stop.")]
            public bool SaveSeedsToFile { get; set; } = true;

            [Category(Hosting), Description("Enter the total number of raids to host before the bot automatically stops. Default is 0 to ignore this setting.")]
            public int TotalRaidsToHost { get; set; } = 0;

            [Category(Hosting), Description("When enabled, the bot will randomly pick a Raid to run, while keeping requests prioritized.")]
            public bool RandomRotation { get; set; } = false;

            [Category(Hosting), Description("When true, no one will be able to request raids.")]
            public bool DisableRequests { get; set; } = false;

            [Category(FeatureToggle), Description("Prevent attacks.  When true, Overworld Spawns (Pokémon) are disabled on the next seed injection.  When false, Overworld Spawns (Pokémon) are enabled on the next seed injection.")]
            public bool DisableOverworldSpawns { get; set; } = true;

            [Category(Hosting), Description("Minimum amount of seconds to wait before starting a raid.")]
            public int TimeToWait { get; set; } = 90;

            [Category(Hosting), Description("When enabled, the bot will inject the current day seed to tomorrow's day seed.")]
            public bool KeepDaySeed { get; set; } = true;

            [Category(FeatureToggle), Description("When enabled, the bot will roll back the time by 5 hours to keep your day from changing.  Be sure that when you start the bot the Switch Time is past 12:01am and before 7:00pm.")]
            public bool EnableTimeRollBack { get; set; } = true;
        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<RotatingRaidPresetFiltersCategory>))]
        public class RotatingRaidPresetFiltersCategory
        {
            public override string ToString() => "Embed Toggles";

            [Category(Hosting), Description("Raid embed description.")]
            public string[] RaidEmbedDescription { get; set; } = Array.Empty<string>();

            [Category(FeatureToggle), Description("Choose the TeraType Icon set to use in the author area of the embed.  Icon1 are custom, Icon2 is not.")]
            public TeraIconType SelectedTeraIconType { get; set; } = TeraIconType.Icon1;

            [Category(Hosting), Description("If true, the bot will show Moves on embeds.")]
            public bool IncludeMoves { get; set; } = true;

            [Category(Hosting), Description("When true, the embed will display current seed.")]
            public bool IncludeSeed { get; set; } = true;

            [Category(FeatureToggle), Description("When enabled, the embed will countdown the amount of seconds in \"TimeToWait\" until starting the raid.")]
            public bool IncludeCountdown { get; set; } = true;

            [Category(Hosting), Description("If true, the bot will show Type Advantages on embeds.")]
            public bool IncludeTypeAdvantage { get; set; } = true;

            [Category(Hosting), Description("If true, the bot will show Special Rewards on embeds.")]
            public bool IncludeRewards { get; set; } = true;

            [Category(Hosting), Description("Select which rewards to display in the embed.")]
            public List<string> RewardsToShow { get; set; } = new List<string>
{
    "Rare Candy",
    "Ability Capsule",
    "Bottle Cap",
    "Ability Patch",
    "Exp. Candy L",
    "Exp. Candy XL",
    "Sweet Herba Mystica",
    "Salty Herba Mystica",
    "Sour Herba Mystica",
    "Bitter Herba Mystica",
    "Spicy Herba Mystica",
    "Pokeball",
    "Shards"
};

            [Category(Hosting), Description("Amount of time (in seconds) to post a requested raid embed.")]
            public int RequestEmbedTime { get; set; } = 30;

            [Category(FeatureToggle), Description("When enabled, the bot will attempt take screenshots for the Raid Embeds. If you experience crashes often about \"Size/Parameter\" try setting this to false.")]
            public bool TakeScreenshot { get; set; } = true;

            [Category(Hosting), Description("Delay in milliseconds for capturing a screenshot once in the raid.\n300 Captures Raid Mon.\n1500 Captures Players Only.\n22000 Captures players and Raid Mon.")]
            public ScreenshotTimingOptions ScreenshotTiming { get; set; } = ScreenshotTimingOptions._1500; // default to 1500 ms

            [Category(FeatureToggle), Description("When enabled, the bot will hide the raid code from the Discord embed.")]
            public bool HideRaidCode { get; set; } = false;

        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<LobbyFiltersCategory>))]
        public class LobbyFiltersCategory
        {
            public override string ToString() => "Lobby Filters";

            [Category(Hosting), Description("OpenLobby - Opens the Lobby after x Empty Lobbies\nSkipRaid - Moves on after x losses/empty Lobbies\nContinue - Continues hosting the raid")]
            public LobbyMethodOptions LobbyMethod { get; set; } = LobbyMethodOptions.SkipRaid; // Changed the property name here

            [Category(Hosting), Description("Empty raid limit per parameter before the bot hosts an uncoded raid. Default is 3 raids.")]
            public int EmptyRaidLimit { get; set; } = 3;

            [Category(Hosting), Description("Empty/Lost raid limit per parameter before the bot moves on to the next one. Default is 3 raids.")]
            public int SkipRaidLimit { get; set; } = 3;

            [Category(FeatureToggle), Description("Set the action you would want your bot to perform. 'AFK' will make the bot idle, while 'MashA' presses A every 2.5s")]
            public RaidAction Action { get; set; } = RaidAction.MashA;

            [Category(FeatureToggle), Description("Delay for the 'MashA' action in seconds.  [3.5 is default]")]
            public double MashADelay { get; set; } = 3.5;  // Default value set to 3.5 seconds

            [Category(FeatureToggle), Description("Extra time in milliseconds to wait after Lobby Disbands in Raid before deciding to not capture the raidmon.")]
            public int ExtraTimeLobbyDisband { get; set; } = 0;

            [Category(FeatureToggle), Description("Extra time in milliseconds to wait before changing partypk.")]
            public int ExtraTimePartyPK { get; set; } = 0;
        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<MiscSettingsCategory>))]
        public class MiscSettingsCategory
        {
            public override string ToString() => "Misc. Settings";

            [Category(FeatureToggle), Description("Set your Switch Date/Time format in the Date/Time settings. The day will automatically rollback by 1 if the Date changes.")]
            public DTFormat DateTimeFormat { get; set; } = DTFormat.MMDDYY;

            [Category(Hosting), Description("When enabled, the bot will use the overshoot method to apply rollover correction, otherwise will use DDOWN clicks.")]
            public bool UseOvershoot { get; set; } = false;

            [Category(Hosting), Description("Amount of times to hit DDOWN for accessing date/time settings during rollover correction. [Default: 39 Clicks]")]
            public int DDOWNClicks { get; set; } = 39;

            [Category(Hosting), Description("Time to scroll down duration in milliseconds for accessing date/time settings during rollover correction. You want to have it overshoot the Date/Time setting by 1, as it will click DUP after scrolling down. [Default: 930ms]")]
            public int HoldTimeForRollover { get; set; } = 900;

            [Category(Hosting), Description("When enabled, start the bot when you are on the HOME screen with the game closed. The bot will only run the rollover routine so you can try to configure accurate timing.")]
            public bool ConfigureRolloverCorrection { get; set; } = false;

            [Category(FeatureToggle), Description("When enabled, the screen will be turned off during normal bot loop operation to save power.")]
            public bool ScreenOff { get; set; }

            private int _completedRaids;

            [Category(Counts), Description("Raids Started")]
            public int CompletedRaids
            {
                get => _completedRaids;
                set => _completedRaids = value;
            }

            [Category(Counts), Description("When enabled, the counts will be emitted when a status check is requested.")]
            public bool EmitCountsOnStatusCheck { get; set; }

            public int AddCompletedRaids() => Interlocked.Increment(ref _completedRaids);

            public IEnumerable<string> GetNonZeroCounts()
            {
                if (!EmitCountsOnStatusCheck)
                    yield break;
                if (CompletedRaids != 0)
                    yield return $"Started Raids: {CompletedRaids}";
            }

        }

            public class CategoryConverter<T> : TypeConverter
        {
            public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

            public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) => TypeDescriptor.GetProperties(typeof(T));

            public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType != typeof(string) && base.CanConvertTo(context, destinationType);
        }
    }
}