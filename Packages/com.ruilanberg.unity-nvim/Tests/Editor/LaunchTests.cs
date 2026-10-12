using System.Collections.Generic;
using NUnit.Framework;
using UnityNvim.Editor.Launch;

namespace UnityNvim.Editor.Tests
{
    public class LaunchTests
    {
        // Same vectors as tests/project_spec.lua: the Lua plugin must compute the same address.
        [TestCase("c:/users/usuario/documents/projects/dinx/front-child", "7da3be4f")]
        [TestCase("/Users/me/Game Project", "7f0f5ac1")]
        [TestCase("c:/projetos/ação é", "a3ba0682")]
        [TestCase("a", "e40c292c")]
        public void Hash_MatchesLuaVectors(string path, string expected) =>
            Assert.AreEqual(expected, ServerAddress.Hash(path));

        [Test]
        public void Normalize_FollowsSharedRules()
        {
            Assert.AreEqual("c:/users/me/proj", ServerAddress.Normalize(@"C:\Users\Me\Proj\", true));
            Assert.AreEqual("c:/", ServerAddress.Normalize("C:/", true));
            Assert.AreEqual("/Users/Me/Proj", ServerAddress.Normalize("/Users/Me/Proj/", false));
        }

        [Test]
        public void Address_PerPlatform()
        {
            Assert.AreEqual(@"\\.\pipe\unity-nvim-7da3be4f",
                ServerAddress.For(@"C:\Users\Usuario\Documents\Projects\Dinx\front-child", true, null));
            Assert.AreEqual("/var/folders/x/T/unity-nvim-7f0f5ac1.sock", ServerAddress.For("/Users/me/Game Project", false, "/var/folders/x/T/"));
            Assert.AreEqual("/tmp/unity-nvim-7f0f5ac1.sock", ServerAddress.For("/Users/me/Game Project", false, ""));
        }

        [TestCase("plain", "plain")]
        [TestCase("with space", "\"with space\"")]
        [TestCase("", "\"\"")]
        [TestCase(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
        [TestCase("say \"hi\"", "\"say \\\"hi\\\"\"")]
        [TestCase(@"a\\b", @"a\\b")]
        public void QuoteWindows_FollowsCommandLineToArgvW(string arg, string expected) =>
            Assert.AreEqual(expected, ProcessUtil.QuoteWindows(arg));

        [TestCase("plain/path-1.cs", "plain/path-1.cs")]
        [TestCase("it's here", "'it'\\''s here'")]
        [TestCase("$HOME", "'$HOME'")]
        public void QuotePosix(string arg, string expected) =>
            Assert.AreEqual(expected, ProcessUtil.QuotePosix(arg));

        [Test]
        public void SplitCommandLine_GroupsQuotes()
        {
            CollectionAssert.AreEqual(new[] { "wezterm", "start", "--cwd", "{root}", "--", "{nvim_argv}" },
                ProcessUtil.SplitCommandLine("wezterm start --cwd {root} -- {nvim_argv}"));
            CollectionAssert.AreEqual(new[] { "C:\\Program Files\\x.exe", "-t", "a b" },
                ProcessUtil.SplitCommandLine("\"C:\\Program Files\\x.exe\" -t 'a b'"));
        }

        static LaunchContext Context(string file = "/p/Assets/A.cs", string shell = null) => new LaunchContext
        {
            Nvim = "/usr/bin/nvim",
            Address = "/tmp/unity-nvim-1.sock",
            File = file,
            Line = 12,
            Column = 3,
            Root = "/p",
            Title = "unity-nvim: P",
            LoginShell = shell,
        };

        [Test]
        public void Expand_NvimArgvIsSpliced()
        {
            var args = TemplateExpander.Expand(new[] { "--directory", "{root}", "--title", "{title}", "{nvim_argv}" }, Context());
            CollectionAssert.AreEqual(new List<string>
            {
                "--directory", "/p", "--title", "unity-nvim: P",
                "/usr/bin/nvim", "--listen", "/tmp/unity-nvim-1.sock",
                "-c", "let &titlestring = 'unity-nvim: P' | set title",
                "+call cursor(12,3)", "/p/Assets/A.cs",
            }, args);
        }

        [Test]
        public void Expand_NoFileOmitsCursorAndFile()
        {
            var args = TemplateExpander.Expand(new[] { "{nvim_argv}" }, Context(file: ""));
            CollectionAssert.DoesNotContain(args, "+call cursor(12,3)");
            Assert.AreEqual("set title", args[args.Count - 1].Substring(args[args.Count - 1].Length - 9));
        }

        [Test]
        public void Expand_LoginShellWrapsNvim()
        {
            var args = TemplateExpander.Expand(new[] { "{nvim_argv}" }, Context(shell: "/bin/zsh"));
            Assert.AreEqual("/bin/zsh", args[0]);
            Assert.AreEqual("-lc", args[1]);
            StringAssert.StartsWith("exec /usr/bin/nvim --listen /tmp/unity-nvim-1.sock", args[2]);
        }

        [Test]
        public void Expand_AppleScriptCommandIsEscaped()
        {
            var ctx = Context(file: "/p/Assets/Say \"hi\".cs");
            var args = TemplateExpander.Expand(new[] { "tell app \"Terminal\" to do script \"{nvim_cmd_as}\"" }, ctx);
            StringAssert.StartsWith("tell app \"Terminal\" to do script \"cd /p && exec /usr/bin/nvim", args[0]);
            StringAssert.Contains("'/p/Assets/Say \\\"hi\\\".cs'", args[0]);
        }

        [Test]
        public void OpenExpression_DoublesSingleQuotes()
        {
            var expr = NvimRemote.OpenExpression("C:/it's/A.cs", 4, 2);
            StringAssert.StartsWith("luaeval(\"", expr);
            StringAssert.EndsWith("['C:/it''s/A.cs', 4, 2])", expr);
            StringAssert.DoesNotContain("\\", expr);
        }
    }
}
