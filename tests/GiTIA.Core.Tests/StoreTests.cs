using System;
using System.IO;
using System.Linq;
using GiTIA.Core;
using NUnit.Framework;

namespace GiTIA.Core.Tests
{
    // Runs real git against a local bare repository standing in for Bitbucket/GitHub.
    public class StoreTests
    {
        string root;

        [SetUp]
        public void SetUp()
        {
            // Space and æøå on purpose: paths and names like these are normal at work.
            root = Path.Combine(Path.GetTempPath(), "GiTIA test æø " + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(root);
            Git.Run(root, "init", "-q", "--bare", "remote.git");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }

        [Test]
        public void TwoUsersShareVersionsAndLocks()
        {
            var url = "file:///" + Path.Combine(root, "remote.git").Replace('\\', '/');
            var ola = new Store(url, Path.Combine(root, "ola", "repo"), @"FIRMA\ola");
            var kaare = new Store(url, Path.Combine(root, "kåre", "repo"), @"FIRMA\kare");
            Assert.That(ola.Refresh(), Is.Empty);

            var v1 = Zap("v1.zap21", 1);
            ola.Upload("Pumpestasjon Ø", v1, "Første versjon", "Ola Nordmann");

            var project = kaare.Refresh().Single();
            Assert.That(project.Name, Is.EqualTo("Pumpestasjon Ø"));
            Assert.That(project.Lock, Is.Null);
            Assert.That(project.Latest.Author, Is.EqualTo("Ola Nordmann"));
            Assert.That(project.Latest.Message, Is.EqualTo("Første versjon"));
            Assert.That(project.Latest.FileName, Does.EndWith("_Ola Nordmann_Pumpestasjon Ø.zap21"));

            Assert.That(kaare.CheckOut(project.Name, "Kåre").Commit, Is.EqualTo(project.Latest.Commit));
            var seenByOla = ola.Refresh().Single().Lock;
            Assert.That(seenByOla.Name, Is.EqualTo("Kåre"));
            Assert.That(seenByOla.Mine, Is.False);
            Assert.That(seenByOla.Base, Is.EqualTo(project.Latest.Commit));
            Assert.That(kaare.Refresh().Single().Lock.Mine, Is.True);
            Assert.Throws<InvalidOperationException>(() => ola.CheckOut(project.Name, "Ola Nordmann"));

            var v2 = Zap("v2.zap21", 2);
            kaare.Upload(project.Name, v2, "La til \"PID\"-regulator\n\nog fikset alarmer", "Kåre");
            project = ola.Refresh().Single();
            Assert.That(project.Lock, Is.Null, "uploading checks the project back in");
            Assert.That(project.Revisions.Select(r => r.Author + ": " + r.Message),
                Is.EqualTo(new[] { "Kåre: La til \"PID\"-regulator", "Ola Nordmann: Første versjon" }));

            var file = Path.Combine(root, "nedlastet", "arkiv.zap21");
            ola.Download(project.Revisions[1], file);
            Assert.That(File.ReadAllBytes(file), Is.EqualTo(File.ReadAllBytes(v1)));
            ola.Download(project.Latest, file);
            Assert.That(File.ReadAllBytes(file), Is.EqualTo(File.ReadAllBytes(v2)));

            // The archive itself lives in Git LFS; git only holds a pointer.
            Assert.That(Git.Run(Path.Combine(root, "remote.git"), "show", "main:" + project.Latest.RepoPath),
                Does.StartWith("version https://git-lfs"));

            // Breaking someone else's forgotten lock.
            kaare.CheckOut(project.Name, "Kåre");
            ola.Unlock(project.Name, "Ola Nordmann");
            Assert.That(kaare.Refresh().Single().Lock, Is.Null);
        }

        [TestCase("Pumpestasjon Ø")]
        [TestCase("8MW Fjernvarme (Kvitebjørn)")]
        public void AcceptsNormalProjectNames(string name) => Assert.DoesNotThrow(() => Store.CheckName(name));

        [TestCase("")]
        [TestCase(" Foran")]
        [TestCase("a/b")]
        [TestCase(".git")]
        [TestCase("Linje[2]")]
        public void RejectsBadProjectNames(string name) => Assert.Throws<ArgumentException>(() => Store.CheckName(name));

        string Zap(string name, int seed)
        {
            var bytes = new byte[200_000];
            new Random(seed).NextBytes(bytes);
            var file = Path.Combine(root, name);
            File.WriteAllBytes(file, bytes);
            return file;
        }
    }
}
