using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Smite2Patcher
{
    internal sealed class MainForm : Form
    {
        private static readonly Color Background = Color.FromArgb(18, 20, 25);
        private static readonly Color PanelColor = Color.FromArgb(29, 32, 40);
        private static readonly Color FgColor = Color.FromArgb(232, 234, 240);
        private static readonly Color Muted = Color.FromArgb(150, 156, 170);
        private static readonly Color Accent = Color.FromArgb(226, 176, 74);
        private static readonly Color Good = Color.FromArgb(120, 200, 140);
        private static readonly Color Bad = Color.FromArgb(235, 120, 110);

        private readonly Label gpuValue = ValueLabel();
        private readonly Label driverValue = ValueLabel();
        private readonly Label displayValue = ValueLabel();
        private readonly Label gameValue = ValueLabel();
        private readonly Label stateValue = ValueLabel();
        private readonly Label lastGameValue = ValueLabel();
        private readonly Label planText = new Label();
        private readonly TextBox output = new TextBox();
        private readonly Button applyButton = new Button();
        private readonly Button restoreButton = new Button();

        private SystemSnapshot pc = new SystemSnapshot();
        private PatchPlan plan = new PatchPlan();

        private readonly SystemSnapshot? fixedSnapshot;
        private readonly List<(Control Control, string Ukrainian, string English)> texts = new List<(Control, string, string)>();
        private readonly LinkLabel languageLink = new LinkLabel();

        public MainForm() : this(null)
        {
        }

        /// <summary>A fixed snapshot renders how another PC would see the window (diagnostics only).</summary>
        public MainForm(SystemSnapshot? snapshot)
        {
            fixedSnapshot = snapshot;
            Font = new Font("Segoe UI", 9.75f);
            BackColor = Background;
            ForeColor = FgColor;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // Pixel sizes below are for 100% Windows scaling; fonts scale on their own.
            AutoScaleMode = AutoScaleMode.None;
            float scale;
            using (var graphics = CreateGraphics())
            {
                scale = graphics.DpiX / 96f;
            }
            int S(int pixels) => (int)Math.Round(pixels * scale);
            var contentWidth = S(552);
            ClientSize = new Size(S(600), S(740));
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (ArgumentException)
            {
            }

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(S(24), S(16), S(24), S(20)),
                BackColor = Background,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var title = Localized(new Label
            {
                Font = new Font("Segoe UI Semibold", 17f),
                ForeColor = FgColor,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, S(4)),
            }, "SMITE 2 — оптимізація", "SMITE 2 — optimizer");

            languageLink.AutoSize = true;
            languageLink.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            languageLink.Margin = new Padding(0, S(8), 0, 0);
            languageLink.LinkColor = Accent;
            languageLink.ActiveLinkColor = Accent;
            languageLink.LinkBehavior = LinkBehavior.HoverUnderline;
            languageLink.LinkClicked += (sender, args) =>
            {
                Lang.Choose(!Lang.Ukrainian);
                ApplyTexts();
                Reload(initial: true);
            };
            var header = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(title, 0, 0);
            header.Controls.Add(languageLink, 1, 0);

            var subtitle = new Label
            {
                ForeColor = Muted,
                AutoSize = true,
                MaximumSize = new Size(contentWidth, 0),
                Margin = new Padding(0, 0, 0, S(14)),
            };
            Localized(subtitle,
                "Натисни «Оптимізувати» один раз. Далі запускай гру як завжди — патчер більше не потрібен.",
                "Click \"Optimize\" once. After that, launch the game as usual — you will not need the patcher again.");

            var info = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                BackColor = PanelColor,
                ColumnCount = 2,
                Padding = new Padding(S(14), S(10), S(14), S(10)),
                Margin = new Padding(0, 0, 0, S(16)),
            };
            info.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var value in new[] { gpuValue, driverValue, displayValue, gameValue, stateValue, lastGameValue })
            {
                value.MaximumSize = new Size(contentWidth - S(150), 0);
            }
            AddRow(info, "Відеокарта", "Graphics card", gpuValue, S(18));
            AddRow(info, "Драйвер", "Driver", driverValue, S(18));
            AddRow(info, "Монітор", "Monitor", displayValue, S(18));
            AddRow(info, "Гра", "Game", gameValue, S(18));
            AddRow(info, "Стан", "Status", stateValue, S(18));
            AddRow(info, "Остання гра", "Last game", lastGameValue, S(18));

            var planTitle = new Label
            {
                Font = new Font("Segoe UI Semibold", 10.5f),
                ForeColor = FgColor,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, S(6)),
            };
            Localized(planTitle, "Що патчер змінить на цьому ПК", "What the patcher will change on this PC");
            planText.AutoSize = true;
            planText.MaximumSize = new Size(contentWidth, 0);
            planText.ForeColor = FgColor;
            planText.Margin = new Padding(0, 0, 0, S(16));

            var buttons = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 0, S(14)),
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

            applyButton.Font = new Font("Segoe UI Semibold", 12f);
            applyButton.Height = S(50);
            applyButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            applyButton.Margin = new Padding(0, 0, S(10), 0);
            StyleButton(applyButton, Accent, Color.FromArgb(24, 20, 10));
            applyButton.Click += async (sender, args) => await RunAsync(restore: false);

            Localized(restoreButton, "Відкотити все", "Restore everything");
            restoreButton.Height = S(50);
            restoreButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            restoreButton.Margin = new Padding(0);
            StyleButton(restoreButton, PanelColor, FgColor);
            restoreButton.Click += async (sender, args) => await RunAsync(restore: true);
            buttons.Controls.Add(applyButton, 0, 0);
            buttons.Controls.Add(restoreButton, 1, 0);

            output.Dock = DockStyle.Fill;
            output.Multiline = true;
            output.ReadOnly = true;
            output.ScrollBars = ScrollBars.Vertical;
            output.BorderStyle = BorderStyle.None;
            output.BackColor = PanelColor;
            output.ForeColor = FgColor;
            output.Font = new Font("Segoe UI", 9.5f);
            output.Margin = new Padding(0);

            var rows = new Control[] { header, subtitle, info, planTitle, planText, buttons, output };
            for (var row = 0; row < rows.Length; row++)
            {
                root.RowStyles.Add(row == rows.Length - 1 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
                root.Controls.Add(rows[row], 0, row);
            }
            Controls.Add(root);
            ApplyTexts();
            Shown += (sender, args) => Reload(initial: true);
        }

        private Control Localized(Control control, string ukrainian, string english)
        {
            texts.Add((control, ukrainian, english));
            return control;
        }

        private void ApplyTexts()
        {
            Text = Lang.T("SMITE 2 · Оптимізація", "SMITE 2 · Optimizer");
            foreach (var (control, ukrainian, english) in texts)
            {
                control.Text = Lang.T(ukrainian, english);
            }
            languageLink.Text = Lang.Ukrainian ? "English" : "Українська";
        }

        private void Reload(bool initial)
        {
            UseWaitCursor = true;
            try
            {
                pc = fixedSnapshot ?? SystemProbe.Capture();
                plan = PatchPlan.Build(pc);
            }
            finally
            {
                UseWaitCursor = false;
            }

            gpuValue.Text = pc.Gpu.Name.Length > 0 ? pc.Gpu.Name : Lang.T("не знайдено", "not found");
            gpuValue.ForeColor = pc.Gpu.IsNvidia ? FgColor : Bad;
            driverValue.Text = pc.Gpu.DriverVersion > 0
                ? NvDrs.FormatDriver(pc.Gpu.DriverVersion) + (pc.Gpu.DriverVersion >= Nv.MinOverrideDriver ? "" : Lang.T("  — застарілий, онови через NVIDIA App", "  — outdated, update it in the NVIDIA App"))
                : Lang.T("не прочитано", "unknown");
            driverValue.ForeColor = pc.Gpu.DriverVersion >= Nv.MinOverrideDriver ? FgColor : Bad;
            displayValue.Text = pc.RefreshHz > 0 ? pc.Width + "×" + pc.Height + ", " + pc.RefreshHz + Lang.T(" Гц", " Hz") : Lang.T("не прочитано", "unknown");
            gameValue.Text = pc.GameInstalled ? Lang.T("SMITE 2 у Steam знайдено", "SMITE 2 found in Steam") : Lang.T("SMITE 2 у Steam не знайдено", "SMITE 2 not found in Steam");
            gameValue.ForeColor = pc.GameInstalled ? FgColor : Muted;
            DescribeLastGame(pc.LastGame);

            if (!plan.Supported)
            {
                stateValue.Text = Lang.T("не підтримується", "not supported");
                stateValue.ForeColor = Bad;
                planText.Text = plan.BlockReason ?? "";
                applyButton.Enabled = false;
                applyButton.BackColor = PanelColor;
                restoreButton.Enabled = false;
                return;
            }

            var state = PatchEngine.ReadState(pc, plan);
            stateValue.Text = state.FullyApplied ? Lang.T("оптимізовано ✓", "optimized ✓") : state.BackupExists ? Lang.T("частково оптимізовано", "partly optimized") : Lang.T("ще не оптимізовано", "not optimized yet");
            stateValue.ForeColor = state.FullyApplied ? Good : Accent;
            planText.Text = string.Join(Environment.NewLine, plan.Summary.Select(line => "•  " + line)) +
                            (plan.Warnings.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, plan.Warnings.Select(line => "⚠  " + line)) : "");
            applyButton.Enabled = true;
            applyButton.BackColor = Accent;
            applyButton.Text = state.FullyApplied ? Lang.T("Застосувати ще раз", "Apply again") : Lang.T("Оптимізувати", "Optimize");
            restoreButton.Enabled = state.BackupExists;

            if (initial)
            {
                output.Text = state.FullyApplied ? InGameText() : Lang.T("Перед стартом закрий SMITE 2. Steam патчер закриє й відкриє сам, якщо треба.", "Close SMITE 2 before you start. If needed, the patcher closes and reopens Steam by itself.");
            }
        }

        private async Task RunAsync(bool restore)
        {
            if (SystemProbe.IsRunning("Hemingway-Win64-Shipping") || SystemProbe.IsRunning("Hemingway"))
            {
                MessageBox.Show(this, Lang.T("Спершу закрий SMITE 2.", "Close SMITE 2 first."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var steamNeeded = restore ? PatchEngine.RestoreNeedsSteamClosed() : plan.SteamFlag && PatchEngine.SteamEdits(pc).Count > 0;
            var mayCloseSteam = true;
            if (steamNeeded && SteamClient.IsRunning())
            {
                var answer = MessageBox.Show(this,
                    Lang.T("Щоб змінити параметри запуску SMITE 2, Steam треба на кілька секунд закрити. Патчер закриє його і відкриє знову.\n\nЗакрити Steam зараз?", "To change SMITE 2's launch options, Steam has to close for a few seconds. The patcher will close it and open it again.\n\nClose Steam now?"),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                mayCloseSteam = answer == DialogResult.Yes;
            }

            applyButton.Enabled = false;
            restoreButton.Enabled = false;
            output.Text = "";
            var snapshot = pc;
            var currentPlan = plan;
            try
            {
                await Task.Run(() =>
                {
                    if (restore)
                    {
                        PatchEngine.Restore(snapshot, mayCloseSteam, Log);
                    }
                    else
                    {
                        PatchEngine.Apply(snapshot, currentPlan, mayCloseSteam, Log);
                    }
                });
                Log("");
                Log(restore ? Lang.T("Готово. Усе повернуто, як було.", "Done. Everything is back to how it was.") : Lang.T("Готово ✓", "Done ✓") + Environment.NewLine + Environment.NewLine + InGameText());
            }
            catch (Exception error)
            {
                Log("✗ " + error.Message);
            }
            finally
            {
                Reload(initial: false);
            }
        }

        /// <summary>Proof from the game's own log that DLSS ran on the NVIDIA GPU.</summary>
        private void DescribeLastGame(LastGame? game)
        {
            if (game == null)
            {
                lastGameValue.Text = Lang.T("ще не запускалась", "not launched yet");
                lastGameValue.ForeColor = Muted;
                return;
            }

            var parts = new List<string> { game.When.ToString("dd.MM HH:mm") };
            var problem = false;
            if (game.Adapter.Length > 0)
            {
                parts.Add(game.AdapterIsNvidia ? "NVIDIA ✓" : Lang.T("✗ гра йшла на ", "✗ the game ran on ") + game.Adapter);
                problem |= !game.AdapterIsNvidia;
            }
            var source = game.DlssVersion.Length > 0 || game.DlssFromNvidia
                ? ", " + (game.DlssVersion.Length > 0 ? game.DlssVersion + " " : "") +
                  (game.DlssFromNvidia ? Lang.T("від NVIDIA", "via NVIDIA") : Lang.T("з файлів гри", "from game files"))
                : "";
            parts.Add(game.DlssActive
                ? "DLSS ✓ " + game.DlssInput + " → " + game.DlssOutput +
                  (game.SrPresetApplied > 0 ? ", Preset " + LastGame.PresetLetter(game.SrPresetApplied) : "") + source
                : Lang.T("DLSS не вмикався — вибери DLSS у графіці гри", "DLSS did not start — pick DLSS in the game's graphics settings"));
            if (game.XeFgOwnsFrames)
            {
                parts.Add(Lang.T("✗ кадри перехоплює Intel XeFG", "✗ Intel XeFG is taking over the frames"));
                problem = true;
            }
            else if (game.XeFgFlagObeyed)
            {
                parts.Add(Lang.T("Intel XeFG вимкнено ✓", "Intel XeFG disabled ✓"));
            }
            if (game.FrameGenerationUsed)
            {
                parts.Add("FG ✓");
            }

            var created = DateTime.MinValue;
            try
            {
                var backup = BackupData.Load();
                if (backup != null)
                {
                    DateTime.TryParse(backup.Created, out created);
                }
            }
            catch (Exception error) when (error is System.IO.IOException || error is System.Runtime.Serialization.SerializationException)
            {
            }
            if (created > DateTime.MinValue && game.When < created)
            {
                parts.Add(Lang.T("(це запуск ще до патча)", "(this launch was before the patch)"));
            }

            lastGameValue.Text = string.Join(" · ", parts);
            lastGameValue.ForeColor = problem ? Bad : game.DlssActive ? Good : Accent;
        }

        private string InGameText()
        {
            var lines = plan.InGame.Select((line, index) => (index + 1) + ". " + line);
            return Lang.T("У самій грі:", "In the game itself:") + Environment.NewLine +
                   string.Join(Environment.NewLine, lines) + Environment.NewLine + Environment.NewLine +
                   Lang.T("Порада: у матчі не тисни Alt-Tab — генерація кадрів вмикається заново, і гра на мить фризить.",
                          "Tip: do not Alt-Tab during a match — frame generation restarts and the game freezes for a moment.") + Environment.NewLine +
                   Lang.T("Після оновлення гри чи драйвера зайди спершу на тренування — перші запуски богів і скінів компілюють шейдери.",
                          "After a game or driver update, start with a practice match — the first time gods and skins load, shaders get compiled.");
        }

        private void Log(string line)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(Log), line);
                return;
            }
            output.AppendText((output.TextLength > 0 ? Environment.NewLine : "") + line);
        }

        private void AddRow(TableLayoutPanel table, string ukrainian, string english, Label value, int gap)
        {
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(Localized(new Label { ForeColor = Muted, AutoSize = true, Margin = new Padding(0, 3, gap, 3) }, ukrainian, english), 0, row);
            value.Margin = new Padding(0, 3, 0, 3);
            table.Controls.Add(value, 1, row);
        }

        private static Label ValueLabel() => new Label { AutoSize = true, ForeColor = FgColor };

        private static void StyleButton(Button button, Color background, Color foreground)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = background;
            button.ForeColor = foreground;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }
    }
}
