using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;

namespace GiTIA
{
    /// <summary>TIA Portal V21 Openness: compile, save and archive the project open in a running TIA Portal.</summary>
    static class Tia
    {
        public const string ManualSteps =
            "Gjør det manuelt i TIA Portal i stedet:\n" +
            "1. Høyreklikk PLS-en (og HMI-en) → Compile → Hardware and software (only changes).\n" +
            "2. Lagre prosjektet.\n" +
            "3. Project → Archive…, kryss av for komprimering (gir en .zapNN-fil).\n" +
            "4. Trykk OK her og velg arkivfilen du lagde.";

        // Siemens wants the Openness DLLs loaded from the TIA install, not copied next to the app.
        public static void RegisterAssemblyResolver()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
            {
                var name = new AssemblyName(e.Name).Name;
                if (!name.StartsWith("Siemens.Engineering")) return null;
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Siemens\Automation\Openness\21.0\PublicAPI\21.0.0.0\net48"))
                {
                    var baseDll = key?.GetValue("Siemens.Engineering.Base") as string;
                    var file = baseDll == null ? null : Path.Combine(Path.GetDirectoryName(baseDll), name + ".dll");
                    return File.Exists(file) ? Assembly.LoadFrom(file) : null;
                }
            };
        }

        /// <summary>What to tell the user about a failure. Must not touch Siemens types: the DLLs may be what is missing.</summary>
        public static string Explain(Exception e)
        {
            if ((e is FileNotFoundException || e is FileLoadException) && e.Message.Contains("Siemens.Engineering"))
                return "Fant ikke TIA Portal V21 med Openness på denne PC-en.";
            if (e.GetType().Name == "EngineeringSecurityException")
                return "TIA Portal nektet tilgang. Windows-brukeren din må være med i gruppen «Siemens TIA Openness» " +
                       "(logg av og på etter at IT har lagt deg til), og du må svare «Yes» når TIA spør om tilgang.";
            return e.Message;
        }

        /// <summary>
        /// Compiles every device, saves, and archives the open project to folder\name.zapNN.
        /// Returns the archive path, or null if the user would rather fix something first.
        /// </summary>
        /// <param name="ask">Asks the user a yes/no question; yes means carry on.</param>
        public static string CompileAndArchive(string folder, string name, string repoProject, Func<string, bool> ask, Action<string> status)
        {
            status("Kobler til TIA Portal… (svar «Yes» hvis TIA spør om tilgang)");
            var processes = TiaPortal.GetProcesses();
            if (processes.Count == 0) throw new InvalidOperationException("TIA Portal V21 kjører ikke. Start TIA og åpne prosjektet.");
            var open = processes.Where(p => p.ProjectPath != null).ToList();
            if (open.Count == 0) throw new InvalidOperationException("TIA Portal kjører, men har ikke noe prosjekt åpent.");
            if (open.Count > 1)
                throw new InvalidOperationException($"Flere TIA Portal-vinduer har åpne prosjekter ({string.Join(", ", open.Select(p => p.ProjectPath.Name))}). " +
                                                    "Lukk dem du ikke skal laste opp, og prøv igjen.");

            using (var portal = open[0].Attach())
            {
                var project = portal.Projects.First();
                if (project.Name.IndexOf(repoProject, StringComparison.OrdinalIgnoreCase) < 0
                    && repoProject.IndexOf(project.Name, StringComparison.OrdinalIgnoreCase) < 0
                    && !ask($"TIA har prosjektet «{project.Name}» åpent, men du laster opp til «{repoProject}».\n\nEr det riktig prosjekt?"))
                    return null;

                var errors = new List<string>();
                foreach (var device in Devices(project.Devices, project.DeviceGroups))
                {
                    var compiler = device.GetService<ICompilable>();
                    if (compiler == null) continue;
                    status($"Kompilerer {device.Name}…");
                    var result = compiler.Compile();
                    if (result.ErrorCount == 0) continue;
                    errors.Add($"{device.Name}: {result.ErrorCount} feil");
                    errors.AddRange(Errors(result.Messages).Select(error => "    " + error));
                }
                if (errors.Count > 0 && !ask("Kompileringen feilet:\n\n" + string.Join("\n", errors.Take(15)) + (errors.Count > 15 ? "\n…" : "") +
                                             "\n\nVil du laste opp likevel?\n\nNei = avbryt, så du kan rette feilene i TIA først."))
                    return null;

                status("Lagrer prosjektet…");
                project.Save();
                var file = name + project.Path.Extension.Replace(".ap", ".zap"); // .ap21 -> .zap21
                Directory.CreateDirectory(folder);
                File.Delete(Path.Combine(folder, file)); // left over from an earlier try today
                status("Arkiverer…");
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        project.Archive(new DirectoryInfo(folder), file, ProjectArchivationMode.Compressed);
                        return Path.Combine(folder, file);
                    }
                    catch (Exception) when (attempt == 1) { Thread.Sleep(5000); } // right after Save, TIA can still be busy writing
                }
            }
        }

        static IEnumerable<Device> Devices(DeviceComposition devices, DeviceUserGroupComposition groups) =>
            devices.Concat(groups.SelectMany(g => Devices(g.Devices, g.Groups)));

        // The real errors sit deep in the tree; the levels above them have no text.
        static IEnumerable<string> Errors(CompilerResultMessageComposition messages)
        {
            foreach (var message in messages)
            {
                if (message.State == CompilerResultState.Error && !string.IsNullOrWhiteSpace(message.Description))
                    yield return string.IsNullOrEmpty(message.Path) ? message.Description : $"{message.Path}: {message.Description}";
                foreach (var inner in Errors(message.Messages)) yield return inner;
            }
        }
    }
}
