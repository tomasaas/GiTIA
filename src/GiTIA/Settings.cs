using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.IO;
using System.Linq;
using System.Windows.Forms.Design;
using System.Xml.Serialization;

namespace GiTIA
{
    /// <summary>Per-user settings, edited in a PropertyGrid and stored in %APPDATA%\GiTIA\settings.xml.</summary>
    public class Settings
    {
        static readonly string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GiTIA", "settings.xml");
        static readonly string Documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        [Category("1. Deling"), DisplayName("Git-repo (URL)")]
        [Description("Det felles repoet på Bitbucket eller GitHub, f.eks. https://bitbucket.org/firma/tia-prosjekter.git. Bruk et eget repo bare til dette, og samme adresse hos alle.")]
        public string RepoUrl { get; set; } = "";

        [Category("2. Deg"), DisplayName("Navnet ditt")]
        [Description("Vises for de andre når du har sjekket ut et prosjekt, og foreslås som «Lastet opp av».")]
        public string UserName { get; set; } = Environment.UserName;

        [Category("3. Filer"), DisplayName("Arkivmappe")]
        [Description("Her lagres .zap-arkivet appen lager fra TIA før det lastes opp.")]
        [Editor(typeof(FolderNameEditor), typeof(UITypeEditor))]
        public string ArchiveFolder { get; set; } = Path.Combine(Documents, "TIA-arkiv");

        [Category("3. Filer"), DisplayName("Slett arkivfilen etter opplasting")]
        [Description("True: arkivet appen lagde fra TIA slettes når det er lastet opp (det ligger jo i git). Filer du velger selv, slettes aldri.")]
        public bool DeleteArchiveAfterUpload { get; set; }

        [Category("3. Filer"), DisplayName("Nedlastingsmappe")]
        [Description("Her havner prosjekter du sjekker ut eller laster ned.")]
        [Editor(typeof(FolderNameEditor), typeof(UITypeEditor))]
        public string DownloadFolder { get; set; } = Path.Combine(Documents, "TIA-nedlastinger");

        // The app's own clone of the repo; another URL gets its own clone.
        [Browsable(false)]
        public string RepoFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GiTIA",
            string.Concat(RepoUrl.Select(c => char.IsLetterOrDigit(c) ? c : '_')));

        public static Settings Load()
        {
            try
            {
                using (var stream = File.OpenRead(SettingsFile))
                    return (Settings)new XmlSerializer(typeof(Settings)).Deserialize(stream);
            }
            catch (Exception e) when (e is IOException || e is InvalidOperationException) { return new Settings(); } // first run, or unreadable
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
            using (var stream = File.Create(SettingsFile))
                new XmlSerializer(typeof(Settings)).Serialize(stream, this);
        }
    }
}
