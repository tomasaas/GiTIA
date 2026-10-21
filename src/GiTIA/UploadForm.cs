using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GiTIA.Core;
using Microsoft.VisualBasic;

namespace GiTIA
{
    /// <summary>Asks what is being uploaded: which project, what changed, who, and where the archive comes from.</summary>
    class UploadForm : Form
    {
        // Filled in when the user presses «Last opp». ZapFile is null when the archive should come from TIA.
        public string Project, Description, Uploader, ZapFile;

        readonly ComboBox project = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
        readonly TextBox description = new TextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Size = new Size(480, 100) };
        readonly TextBox tested = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Size = new Size(480, 60) };
        readonly TextBox uploader = new TextBox { Width = 340 };
        readonly RadioButton fromTia = new RadioButton { Text = "Hent fra TIA Portal som er åpen (kompilerer, lagrer og arkiverer)", AutoSize = true, Checked = true };
        readonly RadioButton fromFile = new RadioButton { Text = "Jeg har arkivert selv – bruk denne .zap-filen:", AutoSize = true };
        readonly TextBox file = new TextBox { Width = 380 };
        readonly bool askTestLevel;

        public UploadForm(IEnumerable<string> projects, string selected, string userName, bool askTestLevel)
        {
            this.askTestLevel = tested.Visible = askTestLevel;
            Text = "Last opp ny versjon";
            Font = new Font("Segoe UI", 9.75f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            project.Items.AddRange(projects.ToArray<object>());
            project.SelectedItem = selected;
            uploader.Text = userName;
            file.Enter += (s, e) => fromFile.Checked = true;

            var ok = new Button { Text = "Last opp", AutoSize = true };
            var cancel = new Button { Text = "Avbryt", AutoSize = true, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => Accept();
            CancelButton = cancel;
            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(12) };
            grid.Controls.AddRange(new Control[]
            {
                Caption("Prosjekt:"), Row(project, NewButton("Nytt prosjekt…", NewProject)),
                Caption("Hva er endret:"), description,
                new Label { Text = "Testet på anlegget:", AutoSize = true, Margin = new Padding(3, 8, 12, 3), Visible = askTestLevel }, tested,
                Caption("Lastet opp av:"), uploader,
                Caption("Prosjektfil:"), Column(fromTia, fromFile, Row(file, NewButton("Bla gjennom…", Browse))),
                new Label(), Row(ok, cancel),
            });
            Controls.Add(grid);
        }

        public static string PickZap(IWin32Window owner, string folder)
        {
            using (var dialog = new OpenFileDialog { Title = "Velg TIA-arkivet (.zapNN)", Filter = "TIA-arkiv (*.zap*)|*.zap*", InitialDirectory = folder })
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
        }

        void NewProject()
        {
            var name = Interaction.InputBox("Navn på det nye prosjektet, slik det skal hete for alle:", "Nytt prosjekt").Trim();
            if (name == "") return;
            try { Store.CheckName(name); }
            catch (ArgumentException e)
            {
                MessageBox.Show(this, e.Message, "Nytt prosjekt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var existing = project.Items.Cast<string>().FirstOrDefault(p => p.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) MessageBox.Show(this, $"«{existing}» finnes allerede, så det er valgt.", "Nytt prosjekt");
            else project.Items.Add(name);
            project.SelectedItem = existing ?? name;
        }

        void Browse()
        {
            var picked = PickZap(this, null);
            if (picked == null) return;
            file.Text = picked;
            fromFile.Checked = true;
        }

        void Accept()
        {
            var problem =
                project.SelectedItem == null ? "Velg prosjekt (eller trykk «Nytt prosjekt…»)." :
                description.Text.Trim() == "" ? "Skriv kort hva som er endret." :
                askTestLevel && tested.Text.Trim() == "" ? "Skriv hvor godt endringen er testet på anlegget (eller at den ikke er testet)." :
                uploader.Text.Trim() == "" ? "Skriv hvem som laster opp." :
                fromFile.Checked && !File.Exists(file.Text) ? "Velg arkivfilen (.zapNN) du vil laste opp." :
                fromFile.Checked && !Path.GetExtension(file.Text).StartsWith(".zap", StringComparison.OrdinalIgnoreCase) ? "Filen må være et TIA-arkiv (.zapNN)." :
                null;
            if (problem != null)
            {
                MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Project = (string)project.SelectedItem;
            // On one line and in front, so it stays in the commit subject (the history) even if the description has blank lines.
            Description = (askTestLevel ? $"[Testet: {string.Join(" ", tested.Text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))}] " : "") + description.Text.Trim();
            Uploader = uploader.Text.Trim();
            ZapFile = fromFile.Checked ? file.Text : null;
            DialogResult = DialogResult.OK;
        }

        static Label Caption(string text) => new Label { Text = text, AutoSize = true, Margin = new Padding(3, 8, 12, 3) };

        static Button NewButton(string text, Action click)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (s, e) => click();
            return button;
        }

        static FlowLayoutPanel Row(params Control[] controls) => Flow(FlowDirection.LeftToRight, controls);
        static FlowLayoutPanel Column(params Control[] controls) => Flow(FlowDirection.TopDown, controls);

        static FlowLayoutPanel Flow(FlowDirection direction, Control[] controls)
        {
            var panel = new FlowLayoutPanel { FlowDirection = direction, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            panel.Controls.AddRange(controls);
            return panel;
        }
    }
}
