using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace GiTIA.Core
{
    public class GitException : System.Exception
    {
        public GitException(string message) : base(message) { }
    }

    /// <summary>Runs the git command line (Git for Windows, which includes Git LFS).</summary>
    public static class Git
    {
        /// <summary>Runs git in <paramref name="dir"/> and returns stdout. Throws GitException if git fails.</summary>
        /// <param name="input">Text sent to stdin.</param>
        /// <param name="output">When given, stdout is copied here as raw bytes instead of being returned.</param>
        /// <param name="env">Extra environment variables; these win over the defaults below.</param>
        public static string Run(string dir, string[] args, string input = null, Stream output = null, IDictionary<string, string> env = null)
        {
            var psi = new ProcessStartInfo("git", string.Join(" ", new[] { "-c", "core.quotepath=false" }.Concat(args).Select(Quote)))
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // Nobody sees a console prompt; Git Credential Manager opens its own login window instead.
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            // Archives are big: only fetch one from LFS when that version is actually downloaded.
            psi.EnvironmentVariables["GIT_LFS_SKIP_SMUDGE"] = "1";
            foreach (var pair in env ?? new Dictionary<string, string>()) psi.EnvironmentVariables[pair.Key] = pair.Value;

            Process process;
            try { process = Process.Start(psi); }
            catch (Win32Exception) { throw new GitException("Fant ikke git. Installer Git for Windows (https://git-scm.com) og start appen på nytt."); }
            using (process)
            {
                var error = process.StandardError.ReadToEndAsync();
                if (input != null)
                {
                    var bytes = new UTF8Encoding(false).GetBytes(input);
                    process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                }
                process.StandardInput.Close();
                var text = "";
                if (output != null) process.StandardOutput.BaseStream.CopyTo(output);
                else text = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new GitException($"git {args[0]} feilet:\n{(error.Result + text).Trim()}");
                return text;
            }
        }

        public static string Run(string dir, params string[] args) => Run(dir, args, null);

        // Windows command-line quoting (what CommandLineToArgvW expects); .NET Framework has no ArgumentList.
        static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            for (var i = 0; i < arg.Length; i++)
            {
                var backslashes = 0;
                while (i < arg.Length && arg[i] == '\\') { backslashes++; i++; }
                if (i == arg.Length) { sb.Append('\\', backslashes * 2); break; }
                if (arg[i] == '"') sb.Append('\\', backslashes * 2 + 1).Append('"');
                else sb.Append('\\', backslashes).Append(arg[i]);
            }
            return sb.Append('"').ToString();
        }
    }
}
