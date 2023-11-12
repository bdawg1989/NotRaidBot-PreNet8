﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Base
{
    public abstract class SwitchRoutineExecutor<T> : RoutineExecutor<T> where T : class, IConsoleBotConfig
    {
        public readonly bool UseCRLF;
        protected readonly ISwitchConnectionAsync SwitchConnection;

        protected SwitchRoutineExecutor(IConsoleBotManaged<IConsoleConnection, IConsoleConnectionAsync> cfg) : base(cfg)
        {
            UseCRLF = cfg.GetInnerConfig() is ISwitchConnectionConfig { UseCRLF: true };
            if (Connection is not ISwitchConnectionAsync connect)
                throw new System.Exception("Not a valid switch connection");
            SwitchConnection = connect;
        }

        public override async Task InitialStartup(CancellationToken token) => await EchoCommands(false, token).ConfigureAwait(false);

        public async Task Click(SwitchButton b, int delay, CancellationToken token)
        {
            await Connection.SendAsync(SwitchCommand.Click(b, UseCRLF), token).ConfigureAwait(false);
            await Task.Delay(delay, token).ConfigureAwait(false);
        }

        public async Task PressAndHold(SwitchButton b, int hold, int delay, CancellationToken token)
        {
            await Connection.SendAsync(SwitchCommand.Hold(b, UseCRLF), token).ConfigureAwait(false);
            await Task.Delay(hold, token).ConfigureAwait(false);
            await Connection.SendAsync(SwitchCommand.Release(b, UseCRLF), token).ConfigureAwait(false);
            await Task.Delay(delay, token).ConfigureAwait(false);
        }

        private SwitchButton ParseButton(string buttonString)
        {
            // Convert buttonString to SwitchButton enum
            return buttonString switch
            {
                "A" => SwitchButton.A,
                "B" => SwitchButton.B,
                "X" => SwitchButton.X,
                "Y" => SwitchButton.Y,
                "RSTICK" => SwitchButton.RSTICK,
                "LSTICK" => SwitchButton.LSTICK,
                "L" => SwitchButton.L,
                "R" => SwitchButton.R,
                "ZL" => SwitchButton.ZL,
                "ZR" => SwitchButton.ZR,
                "PLUS" => SwitchButton.PLUS,
                "MINUS" => SwitchButton.MINUS,
                "DUP" => SwitchButton.DUP,
                "DDOWN" => SwitchButton.DDOWN,
                "DLEFT" => SwitchButton.DLEFT,
                "DRIGHT" => SwitchButton.DRIGHT,
                "HOME" => SwitchButton.HOME,
                "CAPTURE" => SwitchButton.CAPTURE,
                _ => throw new ArgumentException("Invalid button string: " + buttonString),
            };
        }

        public async Task SendClickSequence(string sequence, CancellationToken token)
        {
            // Split the sequence into individual commands
            string[] commands = sequence.Split(',');

            foreach (string command in commands)
            {
                // Determine if the command is a button action or a wait time
                if (command.StartsWith("W"))
                {
                    // Extract the wait time and delay execution
                    int waitTime = int.Parse(command.Substring(1));
                    await Task.Delay(waitTime, token).ConfigureAwait(false);
                }
                else
                {
                    // Extract the button action (click, press, release)
                    SwitchButton button;
                    if (command.StartsWith("+"))
                    {
                        button = ParseButton(command.Substring(1));
                        await Connection.SendAsync(SwitchCommand.Hold(button, UseCRLF), token).ConfigureAwait(false);
                    }
                    else if (command.StartsWith("-"))
                    {
                        button = ParseButton(command.Substring(1));
                        await Connection.SendAsync(SwitchCommand.Release(button, UseCRLF), token).ConfigureAwait(false);
                    }
                    else
                    {
                        button = ParseButton(command);
                        await Connection.SendAsync(SwitchCommand.Click(button, UseCRLF), token).ConfigureAwait(false);
                    }
                }
            }
        }

        public async Task DaisyChainCommands(int delay, IEnumerable<SwitchButton> buttons, CancellationToken token)
        {
            SwitchCommand.Configure(SwitchConfigureParameter.mainLoopSleepTime, delay, UseCRLF);
            var commands = buttons.Select(z => SwitchCommand.Click(z, UseCRLF)).ToArray();
            var chain = commands.SelectMany(x => x).ToArray();
            await Connection.SendAsync(chain, token).ConfigureAwait(false);
            SwitchCommand.Configure(SwitchConfigureParameter.mainLoopSleepTime, 0, UseCRLF);
        }

        public async Task SetStick(SwitchStick stick, short x, short y, int delay, CancellationToken token)
        {
            var cmd = SwitchCommand.SetStick(stick, x, y, UseCRLF);
            await Connection.SendAsync(cmd, token).ConfigureAwait(false);
            await Task.Delay(delay, token).ConfigureAwait(false);
        }

        public async Task DetachController(CancellationToken token)
        {
            await Connection.SendAsync(SwitchCommand.DetachController(UseCRLF), token).ConfigureAwait(false);
        }

        public async Task SetScreen(ScreenState state, CancellationToken token)
        {
            await Connection.SendAsync(SwitchCommand.SetScreen(state, UseCRLF), token).ConfigureAwait(false);
        }

        public async Task EchoCommands(bool value, CancellationToken token)
        {
            var cmd = SwitchCommand.Configure(SwitchConfigureParameter.echoCommands, value ? 1 : 0, UseCRLF);
            await Connection.SendAsync(cmd, token).ConfigureAwait(false);
        }

        /// <inheritdoc cref="ReadUntilChanged(ulong,byte[],int,int,bool,bool,CancellationToken)"/>
        public async Task<bool> ReadUntilChanged(uint offset, byte[] comparison, int waitms, int waitInterval, bool match, CancellationToken token) =>
            await ReadUntilChanged(offset, comparison, waitms, waitInterval, match, false, token).ConfigureAwait(false);

        /// <summary>
        /// Reads an offset until it changes to either match or differ from the comparison value.
        /// </summary>
        /// <returns>If <see cref="match"/> is set to true, then the function returns true when the offset matches the given value.<br>Otherwise, it returns true when the offset no longer matches the given value.</br></returns>
        public async Task<bool> ReadUntilChanged(ulong offset, byte[] comparison, int waitms, int waitInterval, bool match, bool absolute, CancellationToken token)
        {
            var sw = new Stopwatch();
            sw.Start();
            do
            {
                var task = absolute
                    ? SwitchConnection.ReadBytesAbsoluteAsync(offset, comparison.Length, token)
                    : SwitchConnection.ReadBytesAsync((uint)offset, comparison.Length, token);
                var result = await task.ConfigureAwait(false);
                if (match == result.SequenceEqual(comparison))
                    return true;

                await Task.Delay(waitInterval, token).ConfigureAwait(false);
            } while (sw.ElapsedMilliseconds < waitms);
            return false;
        }

        public async Task DaySkip(CancellationToken token) => await Connection.SendAsync(SwitchCommand.DaySkip(UseCRLF), token).ConfigureAwait(false);
        public async Task ResetTime(CancellationToken token) => await Connection.SendAsync(SwitchCommand.ResetTime(UseCRLF), token).ConfigureAwait(false);
    }
}