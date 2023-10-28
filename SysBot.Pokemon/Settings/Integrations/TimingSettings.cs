using System.ComponentModel;

namespace SysBot.Pokemon
{
    public class TimingSettings
    {
        private const string OpenGame = nameof(OpenGame);
        private const string CloseGame = nameof(CloseGame);
        private const string Raid = nameof(Raid);
        private const string Misc = nameof(Misc);
        public override string ToString() => "Extra Time Settings";

        // Opening the game.
        [Category(OpenGame), Description("Extra time in milliseconds to wait for profiles to load when starting the game.")]
        public int ExtraTimeLoadProfile { get; set; }

        [Category(OpenGame), Description("Extra time in milliseconds to wait to check if DLC is usable.")]
        public int ExtraTimeCheckDLC { get; set; }

        [Category(OpenGame), Description("Extra time in milliseconds to wait before clicking A in title screen.")]
        public int ExtraTimeLoadGame { get; set; } = 5000;

        [Category(OpenGame), Description("[BDSP] Extra time in milliseconds to wait for the overworld to load after the title screen.")]
        public int ExtraTimeLoadOverworld { get; set; } = 3000;

        // Closing the game.
        [Category(CloseGame), Description("Extra time in milliseconds to wait after pressing HOME to minimize the game.")]
        public int ExtraTimeReturnHome { get; set; }

        [Category(CloseGame), Description("Extra time in milliseconds to wait after clicking to close the game.")]
        public int ExtraTimeCloseGame { get; set; }

        // Miscellaneous settings.
        [Category(Misc), Description("[SWSH/SV] Extra time in milliseconds to wait after clicking + to connect to Y-Comm (SWSH) or L to connect online (SV).")]
        public int ExtraTimeConnectOnline { get; set; }

        [Category(Misc), Description("Number of times to attempt reconnecting to a socket connection after a connection is lost. Set this to -1 to try indefinitely.")]
        public int ReconnectAttempts { get; set; } = 30;

        [Category(Misc), Description("Extra time in milliseconds to wait between attempts to reconnect. Base time is 30 seconds.")]
        public int ExtraReconnectDelay { get; set; }

        [Category(Misc), Description("[SV] Extra time in milliseconds to wait for the Poké Portal to load.")]
        public int ExtraTimeLoadPortal { get; set; } = 1000;

        [Category(Misc), Description("Time to wait after each keypress when navigating Switch menus or entering Link Code.")]
        public int KeypressTime { get; set; } = 200;

        [Category(Misc), Description("Enable this to decline incoming system updates.")]
        public bool AvoidSystemUpdate { get; set; }
    }
}
