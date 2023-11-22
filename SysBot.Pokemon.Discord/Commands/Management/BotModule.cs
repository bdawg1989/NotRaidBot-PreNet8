using Discord;
using Discord.Commands;
using PKHeX.Core;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    public class BotModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        [Command("botStatus")]
        [Summary("Gets the status of the bots.")]
        [RequireSudo]
        public async Task GetStatusAsync()
        {
            var me = SysCord<T>.Runner;
            var bots = me.Bots.Select(z => z.Bot).OfType<PokeRoutineExecutorBase>().ToArray();
            if (bots.Length == 0)
            {
                await ReplyAsync("No bots configured.").ConfigureAwait(false);
                return;
            }

            var summaries = bots.Select(GetDetailedSummary);
            var lines = string.Join(Environment.NewLine, summaries);
            await ReplyAsync(Format.Code(lines)).ConfigureAwait(false);
        }
        private string GetRunningBotIP()
        {
            var r = SysCord<T>.Runner;
            var runningBot = r.Bots.Find(x => x.IsRunning);

            // Check if a running bot is found
            if (runningBot != null)
            {
                return runningBot.Bot.Config.Connection.IP;
            }
            else
            {
                // Default IP address or logic if no running bot is found
                return "192.168.1.1";
            }
        }
        private static string GetDetailedSummary(PokeRoutineExecutorBase z)
        {
            return $"- {z.Connection.Name} | {z.Connection.Label} - {z.Config.CurrentRoutineType} ~ {z.LastTime:hh:mm:ss} | {z.LastLogged}";
        }

        [Command("botStart")]
        [Summary("Starts the currently running bot.")]
        [RequireSudo]
        public async Task StartBotAsync()
        {
            string ip = GetRunningBotIP();
            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No bot has that IP address ({ip}).").ConfigureAwait(false);
                return;
            }

            bot.Start();
        }

        [Command("botStop")]
        [Summary("Stops the currently running bot.")]
        [RequireSudo]
        public async Task StopBotAsync()
        {
            string ip = GetRunningBotIP();
            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No bot has that IP address ({ip}).").ConfigureAwait(false);
                return;
            }

            bot.Stop();
        }

        [Command("botIdle")]
        [Alias("botPause")]
        [Summary("Commands the currently running bot to Idle.")]
        [RequireSudo]
        public async Task IdleBotAsync()
        {
            string ip = GetRunningBotIP();
            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No bot has that IP address ({ip}).").ConfigureAwait(false);
                return;
            }

            bot.Pause();
        }

        [Command("botChange")]
        [Summary("Changes the routine of the currently running bot (trades).")]
        [RequireSudo]
        public async Task ChangeTaskAsync([Summary("Routine enum name")] PokeRoutineType task)
        {
            string ip = GetRunningBotIP();
            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No bot has that IP address ({ip}).").ConfigureAwait(false);
                return;
            }

            bot.Bot.Config.Initialize(task);
        }

        [Command("botRestart")]
        [Summary("Restarts the currently running bot(s).")]
        [RequireSudo]
        public async Task RestartBotAsync()
        {
            string ip = GetRunningBotIP();
            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No bot has that IP address ({ip}).").ConfigureAwait(false);
                return;
            }

            var c = bot.Bot.Connection;
            c.Reset();
            bot.Start();
        }
    }
}
