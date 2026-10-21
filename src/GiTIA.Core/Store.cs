using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GiTIA.Core
{
    public class Lock
    {
        public string Name, Account, Machine, Base;
        public DateTime Since;
        public bool Mine;
    }

    public class Revision
    {
        public string Project, Commit, Author, Message, RepoPath;
        public DateTime Date;
        public string FileName => Store.ArchiveName(Date, Author, Project) + Path.GetExtension(RepoPath);
    }

    public class Project
    {
        public string Name;
        public Lock Lock;
        public List<Revision> Revisions = new List<Revision>(); // newest first
        public Revision Latest => Revisions.FirstOrDefault();
    }

    /// <summary>
    /// The shared store: a git repository with one folder per TIA project. A folder holds the newest
    /// archive (&lt;project&gt;.zapNN, kept in Git LFS) and, while someone has the project checked out,
    /// lock.txt saying who. Every change starts from the remote's latest state and is pushed at once,
    /// so a rejected push just means someone else got there first: start over from their state.
    /// The local clone belongs to the app; anything in it that is not pushed gets thrown away.
    /// </summary>
    public class Store
    {
        const string LockFile = "lock.txt";
        const string LfsRule = "*.zap* filter=lfs diff=lfs merge=lfs -text";
        readonly string url, dir, account;
        string branch;
        bool hasCommits;

        public Store(string url, string dir, string account)
        {
            this.url = url;
            this.dir = dir;
            this.account = account;
        }

        public static string CurrentAccount => Environment.UserDomainName + "\\" + Environment.UserName;

        /// <summary>The team's file naming convention: date_who_project.</summary>
        public static string ArchiveName(DateTime date, string who, string project) =>
            string.Join("-", $"{date:yyyy-MM-dd}_{who}_{project}".Split(Path.GetInvalidFileNameChars()));

        public static void CheckName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.StartsWith(".") || name.EndsWith(".")
                || name.IndexOfAny(Path.GetInvalidFileNameChars().Concat(new[] { '[', ']' }).ToArray()) >= 0)
                throw new ArgumentException($"«{name}» kan ikke brukes som prosjektnavn. Unngå \\ / : * ? \" < > | [ ] og punktum først eller sist.");
        }

        public List<Project> Refresh()
        {
            Sync();
            var projects = Directory.GetDirectories(dir).Select(Path.GetFileName).Where(n => !n.StartsWith("."))
                .Select(n => new Project { Name = n, Lock = ReadLock(n) })
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var revision in History())
                projects.FirstOrDefault(p => p.Name == revision.Project)?.Revisions.Add(revision);
            return projects;
        }

        /// <summary>Locks the project for you and returns its newest version: the one to download and work on.</summary>
        public Revision CheckOut(string project, string name)
        {
            Revision latest = null;
            Publish($"{project}: sjekket ut av {name}", name, () =>
            {
                if (!Directory.Exists(Path.Combine(dir, project))) throw new InvalidOperationException($"Prosjektet {project} finnes ikke.");
                var current = ReadLock(project);
                if (current != null && !current.Mine)
                    throw new InvalidOperationException($"{project} er allerede sjekket ut av {current.Name} (siden {current.Since:dd.MM HH:mm}).");
                latest = History().FirstOrDefault(r => r.Project == project);
                if (current != null) return;
                File.WriteAllLines(Path.Combine(dir, project, LockFile), new[]
                {
                    "navn=" + name,
                    "konto=" + account,
                    "maskin=" + Environment.MachineName,
                    "tid=" + DateTime.Now.ToString("o"),
                    "base=" + latest?.Commit,
                });
                Run("add", "--", project + "/" + LockFile);
            });
            return latest;
        }

        public void Unlock(string project, string name) =>
            Publish($"{project}: låst opp av {name}", name, () => Run("rm", "-q", "--ignore-unmatch", "--", project + "/" + LockFile));

        /// <summary>Makes <paramref name="zapFile"/> the newest version of the project, and checks it in if you had it checked out.</summary>
        public void Upload(string project, string zapFile, string description, string uploader)
        {
            CheckName(project);
            var extension = Path.GetExtension(zapFile).ToLowerInvariant();
            if (!extension.StartsWith(".zap")) throw new ArgumentException("Velg et TIA-arkiv (en .zapNN-fil).");
            Publish($"{project}: {description}", uploader, () =>
            {
                var attributes = Path.Combine(dir, ".gitattributes");
                if (!File.Exists(attributes) || !File.ReadAllText(attributes).Contains(LfsRule))
                {
                    File.AppendAllText(attributes, "\n" + LfsRule + "\n");
                    Run("add", ".gitattributes");
                }
                // Reuse an existing folder even if the name was typed with different case.
                var folder = Directory.GetDirectories(dir).Select(Path.GetFileName)
                    .FirstOrDefault(n => n.Equals(project, StringComparison.OrdinalIgnoreCase)) ?? project;
                Directory.CreateDirectory(Path.Combine(dir, folder));
                Run("rm", "-q", "--ignore-unmatch", "--", folder + "/*.zap*");
                File.Copy(zapFile, Path.Combine(dir, folder, folder + extension));
                Run("add", "--", folder + "/" + folder + extension);
                if (ReadLock(folder)?.Mine == true) Run("rm", "-q", "--", folder + "/" + LockFile);
            });
            try { Run("lfs", "prune"); } catch (GitException) { } // frees local copies of old, already pushed archives
        }

        /// <summary>Downloads one version's archive to <paramref name="file"/>. Only that file is fetched from LFS.</summary>
        public void Download(Revision revision, string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var part = file + ".part";
            try
            {
                using (var output = File.Create(part))
                    Git.Run(dir, new[] { "cat-file", "--filters", revision.Commit + ":" + revision.RepoPath }, output: output,
                        env: new Dictionary<string, string> { ["GIT_LFS_SKIP_SMUDGE"] = "0" });
                File.Delete(file);
                File.Move(part, file);
            }
            finally { File.Delete(part); }
        }

        void Sync()
        {
            if (branch == null) // first sync this run
            {
                if (!Directory.Exists(Path.Combine(dir, ".git")))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dir));
                    Git.Run(Path.GetDirectoryName(dir), "clone", "-q", url, dir);
                }
                Run("lfs", "install", "--local");
                branch = DefaultBranch();
            }
            Run("fetch", "-q", "--prune", "origin");
            hasCommits = Run("branch", "-r", "--list", "origin/" + branch).Trim() != "";
            if (hasCommits) Run("checkout", "-q", "-f", "-B", branch, "origin/" + branch);
            else
            {
                // Empty remote: start from nothing (dropping any commit whose push failed); the first push creates the branch.
                Run("symbolic-ref", "HEAD", "refs/heads/" + branch);
                Run("update-ref", "-d", "HEAD");
                Run("read-tree", "--empty");
            }
            Run("clean", "-q", "-f", "-d");
        }

        // The remote's default branch, so everyone pushes to the same one whatever their own git defaults to.
        string DefaultBranch()
        {
            const string prefix = "ref: refs/heads/";
            var head = Run("ls-remote", "--symref", "origin", "HEAD").Split('\n').FirstOrDefault(l => l.StartsWith(prefix));
            return head?.Substring(prefix.Length).Split('\t')[0] ?? "main";
        }

        void Publish(string message, string author, Action change)
        {
            for (var attempt = 1; ; attempt++)
            {
                Sync();
                change();
                if (Run("diff", "--cached", "--name-only").Trim() == "") return;
                Git.Run(dir, new[] { "commit", "-q", "-F", "-" }, input: message, env: new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_NAME"] = author,
                    ["GIT_COMMITTER_NAME"] = author,
                    ["EMAIL"] = Environment.UserName + "@" + Environment.MachineName, // only used when git has no user.email
                });
                try
                {
                    Run("push", "origin", "HEAD:refs/heads/" + branch);
                    return;
                }
                catch (GitException e) when (attempt < 3 && e.Message.Contains("[rejected]")) { } // someone pushed first
            }
        }

        IEnumerable<Revision> History()
        {
            if (!hasCommits) yield break;
            var log = Run("log", "--format=%x1e%H%x1f%an%x1f%aI%x1f%s", "--name-only", "--diff-filter=AMR", "--", ":(glob)*/*.zap*");
            foreach (var entry in log.Split('\x1e').Skip(1))
            {
                var lines = entry.Split('\n');
                var fields = lines[0].Split('\x1f');
                foreach (var path in lines.Skip(1).Where(l => l != ""))
                {
                    var project = path.Split('/')[0];
                    var message = fields[3].StartsWith(project + ": ") ? fields[3].Substring(project.Length + 2) : fields[3];
                    yield return new Revision
                    {
                        Project = project,
                        Commit = fields[0],
                        Author = fields[1],
                        Date = DateTime.Parse(fields[2], CultureInfo.InvariantCulture),
                        Message = message,
                        RepoPath = path,
                    };
                }
            }
        }

        Lock ReadLock(string project)
        {
            var file = Path.Combine(dir, project, LockFile);
            if (!File.Exists(file)) return null;
            var values = File.ReadAllLines(file).Select(l => l.Split(new[] { '=' }, 2)).Where(p => p.Length == 2)
                .GroupBy(p => p[0].Trim()).ToDictionary(g => g.Key, g => g.First()[1].Trim());
            string Get(string key) => values.TryGetValue(key, out var value) ? value : "";
            DateTime.TryParse(Get("tid"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var since);
            return new Lock
            {
                Name = Get("navn"),
                Account = Get("konto"),
                Machine = Get("maskin"),
                Since = since,
                Base = Get("base"),
                Mine = Get("konto").Equals(account, StringComparison.OrdinalIgnoreCase),
            };
        }

        string Run(params string[] args) => Git.Run(dir, args);
    }
}
