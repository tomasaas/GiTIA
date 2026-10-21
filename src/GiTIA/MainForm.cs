using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GiTIA.Core;

namespace GiTIA
{
    /// <summary>Projects on the left; status, actions and version history of the selected one on the right.</summary>
    class MainForm : Form
    {
        static readonly Color TakenRow = Color.MistyRose, MineRow = Color.FromArgb(222, 235, 255);

        Settings settings = Settings.Load();
        Store store;
        List<Project> projects = new List<Project>();
        bool busy; // a user action is running
        readonly SemaphoreSlim gate = new SemaphoreSlim(1); // one git/TIA job at a time

        readonly ListView projectList = List(("Prosjekt", 240), ("Status", 300), ("Sist lastet opp", 230));
        readonly ListView historyList = List(("Dato", 140), ("Lastet opp av", 160), ("Hva er endret", 520));
        readonly Label banner = new Label { Dock = DockStyle.Top, Height = 64, Padding = new Padding(12, 0, 12, 0), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White };
        readonly Button checkOutButton = new Button { Text = "Sjekk ut (lås og last ned nyeste)", AutoSize = true };
        readonly Button downloadButton = new Button { Text = "Last ned valgt versjon", AutoSize = true };
        readonly Button unlockButton = new Button { Text = "Lås opp", AutoSize = true };
        readonly ToolStrip toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 4, 6, 4) };
        readonly ToolStripStatusLabel status = new ToolStripStatusLabel();
        readonly SplitContainer split = new SplitContainer { Dock = DockStyle.Fill };

        public MainForm()
        {
            Text = "GiTIA – TIA-prosjekter";
            Font = new Font("Segoe UI", 9.75f);
            Size = new Size(1400, 800);
            StartPosition = FormStartPosition.CenterScreen;
            banner.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);

            toolbar.Items.Add("Oppdater", null, async (s, e) => await RefreshProjects());
            toolbar.Items.Add(new ToolStripButton("Last opp ny versjon…", null, (s, e) => UploadClick()) { Font = new Font(Font, FontStyle.Bold) });
            toolbar.Items.Add("Innstillinger", null, async (s, e) => { if (EditSettings()) await RefreshProjects(); });

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            buttons.Controls.AddRange(new Control[] { checkOutButton, downloadButton, unlockButton });
            split.Panel1.Controls.Add(projectList);
            split.Panel2.Controls.AddRange(new Control[] { historyList, buttons, banner }); // docked last to first: banner on top
            Controls.AddRange(new Control[] { split, toolbar, new StatusStrip { Items = { status } } });

            projectList.SelectedIndexChanged += (s, e) => ShowSelected();
            checkOutButton.Click += (s, e) => CheckOutClick();
            downloadButton.Click += (s, e) => DownloadClick();
            unlockButton.Click += (s, e) => UnlockClick();
            var timer = new System.Windows.Forms.Timer { Interval = 60_000, Enabled = true };
            timer.Tick += async (s, e) => { if (gate.CurrentCount > 0) await RefreshProjects(quiet: true); };
            Connect();
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            split.SplitterDistance = 640;
            if (store == null) EditSettings();
            await RefreshProjects();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy && MessageBox.Show(this, "Appen er ikke ferdig (f.eks. med en opplasting). Lukker du nå, blir jobben ikke fullført.\n\nLukke likevel?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                e.Cancel = true;
            base.OnFormClosing(e);
        }

        Project Selected => projectList.SelectedItems.Count > 0 ? (Project)projectList.SelectedItems[0].Tag : null;

        void Connect() => store = settings.RepoUrl == "" ? null : new Store(settings.RepoUrl, settings.RepoFolder, Store.CurrentAccount);

        async Task RefreshProjects(bool quiet = false)
        {
            if (store == null) projects = new List<Project>();
            else if (!await Run("Henter status…", () => projects = store.Refresh(), quiet)) return;
            ShowProjects();
        }

        async void CheckOutClick()
        {
            var name = Selected.Name;
            string file = null;
            var ok = await Run("Sjekker ut og laster ned nyeste versjon…", () => file = Download(store.CheckOut(name, settings.UserName)));
            await RefreshProjects();
            if (ok) Reveal(file);
        }

        async void DownloadClick()
        {
            var name = Selected.Name;
            var commit = historyList.SelectedItems.Count > 0 ? ((Revision)historyList.SelectedItems[0].Tag).Commit : null;
            await RefreshProjects(); // the lock warning below must be up to date
            var p = projects.FirstOrDefault(x => x.Name == name);
            var revision = p?.Revisions.FirstOrDefault(r => r.Commit == commit) ?? p?.Latest;
            if (revision == null) return;
            if (p.Lock != null && !p.Lock.Mine && MessageBox.Show(this,
                    $"{p.Lock.Name.ToUpper()} HAR SJEKKET UT DETTE PROSJEKTET\n({When(p.Lock.Since)}, på PC {p.Lock.Machine})\n\n" +
                    $"{p.Lock.Name} jobber på det nå. Endringer du gjør i filen du laster ned, kan ikke slås sammen med det {p.Lock.Name} gjør, " +
                    $"og blir overskrevet når {p.Lock.Name} laster opp sin versjon.\n\nVil du laste ned likevel, f.eks. bare for å se på det?",
                    "Prosjektet er i bruk!", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            string file = null;
            if (await Run("Laster ned…", () => file = Download(revision))) Reveal(file);
        }

        async void UnlockClick()
        {
            var p = Selected;
            var question = p.Lock.Mine
                ? $"Angre utsjekken av {p.Name}?\n\nProsjektet blir ledig for andre. Endringer du ikke har lastet opp, blir ikke delt."
                : $"{p.Lock.Name} sjekket ut {p.Name} {When(p.Lock.Since)}.\n\nBryt låsen bare hvis du vet at {p.Lock.Name} ikke jobber på det " +
                  $"(f.eks. glemte å laste opp før hjemreise). Laster {p.Lock.Name} opp senere, kan det overskrive det du gjør.\n\nBryte låsen?";
            if (MessageBox.Show(this, question, "Lås opp", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            await Run("Låser opp…", () => store.Unlock(p.Name, settings.UserName));
            await RefreshProjects();
        }

        async void UploadClick()
        {
            if (store == null)
            {
                MessageBox.Show(this, "Fyll inn git-repoet under Innstillinger først.", Text);
                return;
            }
            await RefreshProjects(); // the warnings below must be up to date
            var preselect = Selected?.Lock?.Mine == true ? Selected : projects.FirstOrDefault(p => p.Lock?.Mine == true) ?? Selected;
            string name, description, uploader, zap;
            using (var form = new UploadForm(projects.Select(p => p.Name), preselect?.Name, settings.UserName, settings.AskTestLevel))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                (name, description, uploader, zap) = (form.Project, form.Description, form.Uploader, form.ZapFile);
            }
            var project = projects.FirstOrDefault(p => p.Name == name);
            var warning = UploadWarning(project);
            if (warning != null && MessageBox.Show(this, warning + "\n\nLast opp likevel?", "Sjekk før opplasting",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            var archivedByApp = zap == null;
            if (archivedByApp)
            {
                Exception error = null;
                var archiveName = Store.ArchiveName(DateTime.Now, uploader, name);
                await Run("Kobler til TIA Portal…", () =>
                {
                    try { zap = Tia.CompileAndArchive(settings.ArchiveFolder, archiveName, name, Ask, SetStatus); }
                    catch (Exception e) { error = e; }
                });
                if (error != null)
                {
                    MessageBox.Show(this, Tia.Explain(error) + "\n\n" + Tia.ManualSteps, "Fikk ikke hentet prosjektet fra TIA automatisk",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    zap = UploadForm.PickZap(this, settings.ArchiveFolder);
                    archivedByApp = false;
                }
                if (zap == null) return;
            }

            if (!await Run("Laster opp… (store prosjekter kan ta noen minutter)", () => store.Upload(name, zap, description, uploader))) return;
            if (archivedByApp && settings.DeleteArchiveAfterUpload)
                try { File.Delete(zap); } catch (Exception) { } // best effort: the upload itself went fine
            await RefreshProjects();
            MessageBox.Show(this, $"Ny versjon av {name} er lastet opp." +
                                  (project?.Lock?.Mine == true ? "\n\nProsjektet er sjekket inn og er ledig for andre." : ""),
                "Ferdig", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static string UploadWarning(Project p)
        {
            if (p?.Latest == null) return null; // new project
            if (p.Lock == null)
                return $"Du har ikke sjekket ut {p.Name}. Nyeste versjon er fra {When(p.Latest.Date)} av {p.Latest.Author}: «{p.Latest.Message}».\n\n" +
                       "Er det den versjonen du har jobbet videre på?";
            if (!p.Lock.Mine)
                return $"{p.Lock.Name} har sjekket ut {p.Name} ({When(p.Lock.Since)}) og jobber trolig på det nå. " +
                       $"Laster du opp, kan {p.Lock.Name} sin opplasting senere overskrive din, eller omvendt.";
            if (p.Lock.Base != p.Latest.Commit)
                return $"{p.Latest.Author} lastet opp en ny versjon etter at du sjekket ut ({When(p.Latest.Date)}: «{p.Latest.Message}»). " +
                       "Din opplasting blir den nyeste, og den versjonen finnes da bare i historikken.";
            return null;
        }

        // Runs slow git/TIA work off the UI thread, one job at a time. User actions lock the window; errors are shown, not thrown.
        async Task<bool> Run(string text, Action work, bool quiet = false)
        {
            if (!quiet)
            {
                busy = UseWaitCursor = true;
                split.Enabled = toolbar.Enabled = false;
            }
            await gate.WaitAsync();
            try
            {
                if (!quiet) status.Text = text;
                await Task.Run(work);
                status.Text = "Oppdatert " + DateTime.Now.ToString("HH:mm");
                return true;
            }
            catch (Exception e)
            {
                status.Text = "Feil: " + e.Message.Split('\n')[0];
                if (!quiet) MessageBox.Show(this, e.Message, "Noe gikk galt", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                gate.Release();
                if (!quiet)
                {
                    busy = UseWaitCursor = false;
                    split.Enabled = toolbar.Enabled = true;
                }
            }
        }

        // Called from the TIA worker thread: asks on the UI thread and waits for the answer.
        bool Ask(string question) => (bool)Invoke(new Func<bool>(() => MessageBox.Show(this, question, Text,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes));

        void SetStatus(string text) => BeginInvoke(new Action(() => status.Text = text));

        // Saves a version under the team's naming convention in the download folder.
        string Download(Revision revision)
        {
            var file = Path.Combine(settings.DownloadFolder, revision.FileName);
            store.Download(revision, file);
            return file;
        }

        static void Reveal(string file) => Process.Start("explorer.exe", $"/select,\"{file}\"");

        void ShowProjects()
        {
            var selected = Selected?.Name;
            projectList.BeginUpdate();
            projectList.Items.Clear();
            foreach (var p in projects)
            {
                var item = projectList.Items.Add(new ListViewItem(new[]
                {
                    p.Name,
                    p.Lock == null ? "Ledig" : p.Lock.Mine ? $"Sjekket ut av deg ({When(p.Lock.Since)})" : $"SJEKKET UT AV {p.Lock.Name} ({When(p.Lock.Since)})",
                    p.Latest == null ? "" : $"{When(p.Latest.Date)} – {p.Latest.Author}",
                }) { Tag = p });
                if (p.Lock != null) item.BackColor = p.Lock.Mine ? MineRow : TakenRow;
                item.Selected = p.Name == selected;
            }
            if (projectList.SelectedItems.Count == 0 && projectList.Items.Count > 0) projectList.Items[0].Selected = true;
            projectList.EndUpdate();
            ShowSelected();
        }

        void ShowSelected()
        {
            var p = Selected;
            historyList.BeginUpdate();
            historyList.Items.Clear();
            foreach (var r in p?.Revisions ?? new List<Revision>())
                historyList.Items.Add(new ListViewItem(new[] { r.Date.ToString("dd.MM.yyyy HH:mm"), r.Author, r.Message }) { Tag = r });
            if (historyList.Items.Count > 0) historyList.Items[0].Selected = true;
            historyList.EndUpdate();

            checkOutButton.Enabled = p?.Lock == null && p?.Latest != null;
            downloadButton.Enabled = p?.Latest != null;
            unlockButton.Enabled = p?.Lock != null;
            unlockButton.Text = p?.Lock?.Mine == true ? "Angre utsjekk" : "Bryt låsen";
            if (store == null) Banner("Start med å fylle inn git-repoet under Innstillinger.", Color.DimGray);
            else if (p == null) Banner(projects.Count == 0 ? "Ingen prosjekter ennå. Trykk «Last opp ny versjon» for å legge inn det første." : "Velg et prosjekt.", Color.DimGray);
            else if (p.Lock == null) Banner($"{p.Name} er ledig. Sjekk det ut før du begynner å jobbe på det.", Color.SeaGreen);
            else if (p.Lock.Mine) Banner($"Du har sjekket ut {p.Name} ({When(p.Lock.Since)}). Trykk «Last opp ny versjon» når du er ferdig for dagen.", Color.RoyalBlue);
            else Banner($"SJEKKET UT AV {p.Lock.Name.ToUpper()} ({When(p.Lock.Since)}) – {p.Lock.Name} jobber på dette prosjektet nå. Ikke jobb på det samtidig.", Color.Firebrick);
        }

        void Banner(string text, Color color)
        {
            banner.Text = text;
            banner.BackColor = color;
        }

        bool EditSettings()
        {
            var edited = Settings.Load();
            var save = new Button { Text = "Lagre", Dock = DockStyle.Bottom, Height = 36, DialogResult = DialogResult.OK };
            using (var form = new Form { Text = "Innstillinger", Size = new Size(680, 480), StartPosition = FormStartPosition.CenterParent, Font = Font, MinimizeBox = false, MaximizeBox = false })
            {
                form.Controls.AddRange(new Control[] { new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = edited, ToolbarVisible = false }, save });
                if (form.ShowDialog(this) != DialogResult.OK) return false;
            }
            edited.RepoUrl = edited.RepoUrl.Trim();
            edited.Save();
            settings = edited;
            Connect();
            return true;
        }

        static string When(DateTime time) => time.Date == DateTime.Today ? "i dag " + time.ToString("HH:mm") : time.ToString("dd.MM.yyyy HH:mm");

        static ListView List(params (string Text, int Width)[] columns)
        {
            var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
            foreach (var column in columns) list.Columns.Add(column.Text, column.Width);
            return list;
        }
    }
}
