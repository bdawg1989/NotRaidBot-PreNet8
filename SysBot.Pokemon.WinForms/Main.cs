﻿using PKHeX.Core;
using SysBot.Pokemon.SV.BotRaid.Helpers;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SysBot.Pokemon.WinForms
{
    public sealed partial class Main : Form
    {
        private readonly List<PokeBotState> Bots = new();
        private readonly IPokeBotRunner RunningEnvironment;
        private readonly ProgramConfig Config;
        public readonly ISwitchConnectionAsync? SwitchConnection;

        public Main()
        {
            InitializeComponent();

            if (File.Exists(Program.ConfigPath))
            {
                var lines = File.ReadAllText(Program.ConfigPath);
                Config = JsonSerializer.Deserialize(lines, ProgramConfigContext.Default.ProgramConfig) ?? new ProgramConfig();
                LogConfig.MaxArchiveFiles = Config.Hub.MaxArchiveFiles;
                LogConfig.LoggingEnabled = Config.Hub.LoggingEnabled;

                RunningEnvironment = GetRunner(Config);
                foreach (var bot in Config.Bots)
                {
                    bot.Initialize();
                    AddBot(bot);
                }
            }
            else
            {
                Config = new ProgramConfig();
                RunningEnvironment = GetRunner(Config);
                Config.Hub.Folder.CreateDefaults(Program.WorkingDirectory);
            }

            LoadControls();
            Text = $"{(string.IsNullOrEmpty(Config.Hub.BotName) ? "NOT RaidBot" : Config.Hub.BotName)} {NotRaidBot.Version} ({Config.Mode})";
            Task.Run(BotMonitor);
            InitUtil.InitializeStubs(Config.Mode);
        }

        private static IPokeBotRunner GetRunner(ProgramConfig cfg) => cfg.Mode switch
        {
            ProgramMode.SV => new PokeBotRunnerImpl<PK9>(cfg.Hub, new BotFactory9SV()),
            _ => throw new IndexOutOfRangeException("Unsupported mode."),
        };

        private async Task BotMonitor()
        {
            while (!Disposing)
            {
                try
                {
                    foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                        c.ReadState();
                }
                catch
                {
                    // Updating the collection by adding/removing bots will change the iterator
                    // Can try a for-loop or ToArray, but those still don't prevent concurrent mutations of the array.
                    // Just try, and if failed, ignore. Next loop will be fine. Locks on the collection are kinda overkill, since this task is not critical.
                }
                await Task.Delay(2_000).ConfigureAwait(false);
            }
        }

        private void LoadControls()
        {
            MinimumSize = Size;
            PG_Hub.SelectedObject = RunningEnvironment.Config;

            var routines = ((PokeRoutineType[])Enum.GetValues(typeof(PokeRoutineType))).Where(z => RunningEnvironment.SupportsRoutine(z));
            var list = routines.Select(z => new ComboItem(z.ToString(), (int)z)).ToArray();
            CB_Routine.DisplayMember = nameof(ComboItem.Text);
            CB_Routine.ValueMember = nameof(ComboItem.Value);
            CB_Routine.DataSource = list;
            CB_Routine.SelectedValue = (int)PokeRoutineType.RotatingRaidBot; // default option

            var protocols = (SwitchProtocol[])Enum.GetValues(typeof(SwitchProtocol));
            var listP = protocols.Select(z => new ComboItem(z.ToString(), (int)z)).ToArray();
            CB_Protocol.DisplayMember = nameof(ComboItem.Text);
            CB_Protocol.ValueMember = nameof(ComboItem.Value);
            CB_Protocol.DataSource = listP;
            CB_Protocol.SelectedIndex = (int)SwitchProtocol.WiFi; // default option

            comboBox1.Items.Add("Light Mode");
            comboBox1.Items.Add("Dark Mode");

            string theme = Config.Hub.ThemeOption;
            if (string.IsNullOrEmpty(theme) || !comboBox1.Items.Contains(theme))
            {
                comboBox1.SelectedIndex = 0;  // Set default selection to Light Mode if ThemeOption is empty or invalid
            }
            else
            {
                comboBox1.SelectedItem = theme;  // Set the selected item in the combo box based on ThemeOption
                if (theme == "Dark Mode")
                    ApplyDarkTheme();
                else if (theme == "Light Mode")
                    ApplyLightTheme();
            }

            LogUtil.Forwarders.Add(AppendLog);
        }

        private void AppendLog(string message, string identity)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] - {identity}: {message}{Environment.NewLine}";
            if (InvokeRequired)
                Invoke((MethodInvoker)(() => UpdateLog(line)));
            else
                UpdateLog(line);
        }

        private void UpdateLog(string line)
        {
            // ghetto truncate
            if (RTB_Logs.Lines.Length > 99_999)
                RTB_Logs.Lines = RTB_Logs.Lines.Skip(25_0000).ToArray();

            RTB_Logs.AppendText(line);
            RTB_Logs.ScrollToCaret();
        }

        private ProgramConfig GetCurrentConfiguration()
        {
            Config.Bots = Bots.ToArray();
            return Config;
        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveCurrentConfig();
            var bots = RunningEnvironment;
            if (!bots.IsRunning)
                return;

            async Task WaitUntilNotRunning()
            {
                while (bots.IsRunning)
                    await Task.Delay(10).ConfigureAwait(false);
            }

            // Try to let all bots hard-stop before ending execution of the entire program.
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            bots.StopAll();
            Task.WhenAny(WaitUntilNotRunning(), Task.Delay(5_000)).ConfigureAwait(true).GetAwaiter().GetResult();
        }

        private void SaveCurrentConfig()
        {
            var cfg = GetCurrentConfiguration();
            // The ThemeOption property is part of the Config object, so it will be saved automatically
            var lines = JsonSerializer.Serialize(cfg, ProgramConfigContext.Default.ProgramConfig);
            File.WriteAllText(Program.ConfigPath, lines);
        }


        [JsonSerializable(typeof(ProgramConfig))]
        [JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
        public sealed partial class ProgramConfigContext : JsonSerializerContext { }

        private void B_Start_Click(object sender, EventArgs e)
        {
            SaveCurrentConfig();

            LogUtil.LogInfo("Starting all bots...", "Form");
            RunningEnvironment.InitializeStart();
            SendAll(BotControlCommand.Start);
            Tab_Logs.Select();

            if (Bots.Count == 0)
                WinFormsUtil.Alert("No bots configured, but all supporting services have been started.");
        }

        private void SendAll(BotControlCommand cmd)
        {
            foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                c.SendCommand(cmd, false);

            LogUtil.LogText($"All bots have been issued a command to {cmd}.");
        }

        private void B_Stop_Click(object sender, EventArgs e)
        {
            var env = RunningEnvironment;
            if (!env.IsRunning && (ModifierKeys & Keys.Alt) == 0)
            {
                WinFormsUtil.Alert("Nothing is currently running.");
                return;
            }

            var cmd = BotControlCommand.Stop;

            if ((ModifierKeys & Keys.Control) != 0 || (ModifierKeys & Keys.Shift) != 0) // either, because remembering which can be hard
            {
                if (env.IsRunning)
                {
                    WinFormsUtil.Alert("Commanding all bots to Idle.", "Press Stop (without a modifier key) to hard-stop and unlock control, or press Stop with the modifier key again to resume.");
                    cmd = BotControlCommand.Idle;
                }
                else
                {
                    WinFormsUtil.Alert("Commanding all bots to resume their original task.", "Press Stop (without a modifier key) to hard-stop and unlock control.");
                    cmd = BotControlCommand.Resume;
                }
            }
            SendAll(cmd);
        }

        private void B_New_Click(object sender, EventArgs e)
        {
            var cfg = CreateNewBotConfig();
            if (!AddBot(cfg))
            {
                WinFormsUtil.Alert("Unable to add bot; ensure details are valid and not duplicate with an already existing bot.");
                return;
            }
            System.Media.SystemSounds.Asterisk.Play();
        }

        private bool AddBot(PokeBotState cfg)
        {
            if (!cfg.IsValid())
                return false;

            // Disallow duplicate routines.
            if (Bots.Any(z => z.Connection.Equals(cfg.Connection) && cfg.NextRoutineType == z.NextRoutineType))
                return false;

            PokeRoutineExecutorBase newBot;
            try
            {
                Console.WriteLine($"Current Mode ({Config.Mode}) does not support this type of bot ({cfg.CurrentRoutineType}).");
                newBot = RunningEnvironment.CreateBotFromConfig(cfg);
            }
            catch
            {
                return false;
            }

            try
            {
                RunningEnvironment.Add(newBot);
            }
            catch (ArgumentException ex)
            {
                WinFormsUtil.Error(ex.Message);
                return false;
            }

            AddBotControl(cfg);
            Bots.Add(cfg);
            return true;
        }

        private void AddBotControl(PokeBotState cfg)
        {
            var row = new BotController { Width = FLP_Bots.Width };
            row.Initialize(RunningEnvironment, cfg);
            FLP_Bots.Controls.Add(row);
            FLP_Bots.SetFlowBreak(row, true);
            row.Click += (s, e) =>
            {
                var details = cfg.Connection;
                TB_IP.Text = details.IP;
                NUD_Port.Value = details.Port;
                CB_Protocol.SelectedIndex = (int)details.Protocol;
                CB_Routine.SelectedValue = (int)cfg.InitialRoutine;
            };

            row.Remove += (s, e) =>
            {
                Bots.Remove(row.State);
                RunningEnvironment.Remove(row.State, !RunningEnvironment.Config.SkipConsoleBotCreation);
                FLP_Bots.Controls.Remove(row);
            };
        }

        private PokeBotState CreateNewBotConfig()
        {
            var ip = TB_IP.Text;
            var port = (int)NUD_Port.Value;
            var cfg = BotConfigUtil.GetConfig<SwitchConnectionConfig>(ip, port);
            cfg.Protocol = (SwitchProtocol)WinFormsUtil.GetIndex(CB_Protocol);

            var pk = new PokeBotState { Connection = cfg };
            var type = (PokeRoutineType)WinFormsUtil.GetIndex(CB_Routine);
            pk.Initialize(type);
            return pk;
        }

        private void FLP_Bots_Resize(object sender, EventArgs e)
        {
            foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                c.Width = FLP_Bots.Width;
        }

        private void CB_Protocol_SelectedIndexChanged(object sender, EventArgs e)
        {
            TB_IP.Visible = CB_Protocol.SelectedIndex == 0;
        }

        private void FLP_Bots_Paint(object sender, PaintEventArgs e)
        {

        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            var comboBox = sender as ComboBox;
            string selectedTheme = comboBox.SelectedItem.ToString();
            Config.Hub.ThemeOption = selectedTheme;  // Save the selected theme to the config
            SaveCurrentConfig();  // Save the config to file

            if (selectedTheme == "Light Mode")
                ApplyLightTheme();
            else if (selectedTheme == "Dark Mode")
                ApplyDarkTheme();
        }


        private void ApplyLightTheme()
        {
            // Define the color palette
            Color SoftBlue = Color.FromArgb(235, 245, 251);
            Color GentleGrey = Color.FromArgb(245, 245, 245);
            Color DarkBlue = Color.FromArgb(26, 13, 171);

            // Set the background color of the form
            this.BackColor = GentleGrey;

            // Set the foreground color of the form (text color)
            this.ForeColor = DarkBlue;

            // Set the background color of the tab control
            TC_Main.BackColor = SoftBlue;

            // Set the background color of each tab page
            foreach (TabPage page in TC_Main.TabPages)
            {
                page.BackColor = GentleGrey;
            }

            // Set the background color of the property grid
            PG_Hub.BackColor = GentleGrey;
            PG_Hub.LineColor = SoftBlue;
            PG_Hub.CategoryForeColor = DarkBlue;
            PG_Hub.CategorySplitterColor = SoftBlue;
            PG_Hub.HelpBackColor = GentleGrey;
            PG_Hub.HelpForeColor = DarkBlue;
            PG_Hub.ViewBackColor = GentleGrey;
            PG_Hub.ViewForeColor = DarkBlue;

            // Set the background color of the rich text box
            RTB_Logs.BackColor = Color.White;
            RTB_Logs.ForeColor = DarkBlue;

            // Set colors for other controls
            TB_IP.BackColor = Color.White;
            TB_IP.ForeColor = DarkBlue;

            CB_Routine.BackColor = Color.White;
            CB_Routine.ForeColor = DarkBlue;

            NUD_Port.BackColor = Color.White;
            NUD_Port.ForeColor = DarkBlue;

            B_New.BackColor = SoftBlue;
            B_New.ForeColor = DarkBlue;

            FLP_Bots.BackColor = GentleGrey;

            CB_Protocol.BackColor = Color.White;
            CB_Protocol.ForeColor = DarkBlue;

            comboBox1.BackColor = Color.White;
            comboBox1.ForeColor = DarkBlue;

            B_Stop.BackColor = SoftBlue;
            B_Stop.ForeColor = DarkBlue;

            B_Start.BackColor = SoftBlue;
            B_Start.ForeColor = DarkBlue;
        }



        private void ApplyDarkTheme()
        {
            // Define the dark theme colors
            Color DarkRed = Color.FromArgb(90, 0, 0);
            Color DarkGrey = Color.FromArgb(30, 30, 30);
            Color LightGrey = Color.FromArgb(60, 60, 60);
            Color SoftWhite = Color.FromArgb(245, 245, 245);

            // Set the background color of the form
            this.BackColor = DarkGrey;

            // Set the foreground color of the form (text color)
            this.ForeColor = SoftWhite;

            // Set the background color of the tab control
            TC_Main.BackColor = LightGrey;

            // Set the background color of each tab page
            foreach (TabPage page in TC_Main.TabPages)
            {
                page.BackColor = DarkGrey;
            }

            // Set the background color of the property grid
            PG_Hub.BackColor = DarkGrey;
            PG_Hub.LineColor = LightGrey;
            PG_Hub.CategoryForeColor = SoftWhite;
            PG_Hub.CategorySplitterColor = LightGrey;
            PG_Hub.HelpBackColor = DarkGrey;
            PG_Hub.HelpForeColor = SoftWhite;
            PG_Hub.ViewBackColor = DarkGrey;
            PG_Hub.ViewForeColor = SoftWhite;

            // Set the background color of the rich text box
            RTB_Logs.BackColor = DarkGrey;
            RTB_Logs.ForeColor = SoftWhite;

            // Set colors for other controls
            TB_IP.BackColor = LightGrey;
            TB_IP.ForeColor = SoftWhite;

            CB_Routine.BackColor = LightGrey;
            CB_Routine.ForeColor = SoftWhite;

            NUD_Port.BackColor = LightGrey;
            NUD_Port.ForeColor = SoftWhite;

            B_New.BackColor = DarkRed;
            B_New.ForeColor = SoftWhite;

            FLP_Bots.BackColor = DarkGrey;

            CB_Protocol.BackColor = LightGrey;
            CB_Protocol.ForeColor = SoftWhite;

            comboBox1.BackColor = LightGrey;
            comboBox1.ForeColor = SoftWhite;

            B_Stop.BackColor = DarkRed;
            B_Stop.ForeColor = SoftWhite;

            B_Start.BackColor = DarkRed;
            B_Start.ForeColor = SoftWhite;
        }
    }
}
