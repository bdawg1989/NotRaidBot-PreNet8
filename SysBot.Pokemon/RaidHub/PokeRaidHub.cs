using PKHeX.Core;
using SysBot.Base;
using System.Collections.Concurrent;

namespace SysBot.Pokemon
{
    /// <summary>
    /// Centralizes logic for trade bot coordination.
    /// </summary>
    /// <typeparam name="T">Type of <see cref="PKM"/> to distribute.</typeparam>
    public class PokeRaidHub<T> where T : PKM, new()
    {
        public PokeRaidHub(PokeTradeHubConfig config)
        {
            Config = config;
        }

        public readonly PokeTradeHubConfig Config;

        /// <summary> Trade Bots only, used to delegate multi-player tasks </summary>
        public readonly ConcurrentPool<PokeRoutineExecutorBase> Bots = new();
        public bool RaidBotsReady => !Bots.All(z => z.Config.CurrentRoutineType == PokeRoutineType.Idle);
    }
}
