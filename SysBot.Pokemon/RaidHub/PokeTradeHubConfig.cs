using System.ComponentModel;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace SysBot.Pokemon
{
    public sealed class PokeTradeHubConfig : BaseConfig
    {
        private const string BotTrade = nameof(BotTrade);
        private const string BotEncounter = nameof(BotEncounter);
        private const string Integration = nameof(Integration);

        [Category(Operation), Description("Add extra time for slower Switches.")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public TimingSettings Timings { get; set; } = new();

        [Category(BotEncounter), Description("Name of the Discord Bot the Program is Running. This will Title the window for easier recognition. Requires program restart.")]
        public string BotName { get; set; } = string.Empty;

        [Browsable(false)]
        [Category(BotEncounter), Description("Users Theme Option Choice.")]
        public string ThemeOption { get; set; } = string.Empty;

        [Category(BotEncounter)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public RotatingRaidSettingsSV RotatingRaidSV { get; set; } = new();

        // Integration

        [Category(Integration)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public DiscordSettings Discord { get; set; } = new();

    }
}