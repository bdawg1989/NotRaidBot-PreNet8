﻿using System.ComponentModel;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace SysBot.Pokemon
{
    public sealed class PokeTradeHubConfig : BaseConfig
    {
        private const string BotTrade = nameof(BotTrade);
        private const string BotEncounter = nameof(BotEncounter);
        private const string Integration = nameof(Integration);

        [Browsable(false)]
        public override bool Shuffled => Distribution.Shuffled;
        [Browsable(false)]
        [Category(Operation)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public QueueSettings Queues { get; set; } = new();

        [Category(Operation), Description("Add extra time for slower Switches.")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public TimingSettings Timings { get; set; } = new();

        [Category(BotEncounter), Description("Name of the Discord Bot the Program is Running. This will Title the window for easier recognition. Requires program restart.")]
        public string BotName { get; set; } = string.Empty;
        [Browsable(false)]
        [Category(BotEncounter), Description("Users Theme Option Choice.")]
        public string ThemeOption { get; set; } = string.Empty;
        // Trade Bots
        [Browsable(false)]
        [Category(BotTrade)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public TradeSettings Trade { get; set; } = new();
        [Browsable(false)]
        [Category(BotTrade), Description("Settings for idle distribution trades.")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public DistributionSettings Distribution { get; set; } = new();
        [Browsable(false)]
        [Category(BotTrade)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public TradeCordSettings TradeCord { get; set; } = new();
        [Browsable(false)]
        [Category(BotTrade)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public SeedCheckSettings SeedCheckSWSH { get; set; } = new();
        [Browsable(false)]
        [Category(BotTrade)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public TradeAbuseSettings TradeAbuse { get; set; } = new();

        // Encounter Bots - For finding or hosting Pokémon in-game.
        [Browsable(false)]
        [Category(BotEncounter), Description("Stop conditions for EggBot, FossilBot, and EncounterBot.")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public StopConditionSettings StopConditions { get; set; } = new();

        [Browsable(false)]
        [Category(BotEncounter)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public RaidSettingsSV RaidSV { get; set; } = new();

        [Category(BotEncounter)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public RotatingRaidSettingsSV RotatingRaidSV { get; set; } = new();

        [Browsable(false)]
        [Category(BotTrade)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public EtumrepDumpSettings EtumrepDump { get; set; } = new();

        // Integration

        [Category(Integration)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public DiscordSettings Discord { get; set; } = new();
        [Browsable(false)]
        [Category(Integration)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public TwitchSettings Twitch { get; set; } = new();
        [Browsable(false)]
        [Category(Integration)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public YouTubeSettings YouTube { get; set; } = new();
        [Browsable(false)]
        [Category(Integration), Description("Configure generation of assets for streaming.")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public StreamSettings Stream { get; set; } = new();
        [Browsable(false)]
        [Category(Integration), Description("Allows favored users to join the queue with a more favorable position than unfavored users.")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public FavoredPrioritySettings Favoritism { get; set; } = new();
    }
}