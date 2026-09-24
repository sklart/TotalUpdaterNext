using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TotalUpdater.Next.Catalog;
using TotalUpdater.Next.Core;
using TotalUpdater.Next.Core.Installation;
using TotalUpdater.Next.Core.Versions;
using TotalUpdater.Next.Infrastructure.Installation;
using TotalUpdater.Next.Sources;
using TotalUpdater.Next.TotalCommander;

namespace TotalUpdater.Next.Tests
{
    internal static class NewPluginInstallationTests
    {
        private sealed class VersionProbe : IVersionProbe
        { public LocalVersion Probe(string filePath) { return FileVersionProbe.Create("2.0", VersionSource.FileVersion, VersionConfidence.Exact); } }
        private sealed class CatalogProvider : IUpdateSourceProvider
        {
            public string Name { get { return "catalog-test"; } }
            public bool CanHandle(CatalogSource source) { return true; }
            public System.Threading.Tasks.Task<SourceQueryResult> QueryAsync(CatalogSource source, System.Threading.CancellationToken cancellationToken)
            {
                return System.Threading.Tasks.Task.FromResult(new SourceQueryResult { Status = SourceQueryStatus.Success,
                    Release = new RemoteRelease { Version = VersionValue.Parse("2.0"), VersionText = "2.0",
                        Packages = new List<RemotePackage> { new RemotePackage { Architecture = RemotePackageArchitecture.Combined,
                            Url = new Uri("https://example.test/sample.zip") } } } });
            }
        }
        private sealed class Fixture : IDisposable
        {
            public string Root, Tc, Ini, BackupRoot;
            public PackageInspection Package;
            public PluginCatalogEntry Entry;
            public CatalogInstallCandidate Candidate;
            public TotalCommanderConfiguration Configuration;
            public PluginType Type;
            public NewPluginInstallPlan Plan;
            public Fixture(PluginType type, bool x64 = false, string iniText = null, bool x64Only = false, bool unicodeOnly = false)
            {
                Type = type;
                Root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tu-new-tests-" + Guid.NewGuid().ToString("N"));
                Tc = Path.Combine(Root, "TotalCmd"); Directory.CreateDirectory(Tc);
                File.WriteAllBytes(Path.Combine(Tc, "TOTALCMD.EXE"), new byte[] { 1 });
                BackupRoot = Path.Combine(Root, "backups");
                Ini = Path.Combine(Tc, "wincmd.ini");
                File.WriteAllText(Ini, iniText ?? "[Configuration]\r\n; untouched\r\n", Encoding.UTF8);
                var ext = type == PluginType.Wcx ? ".wcx" : type == PluginType.Wfx ? ".wfx" : type == PluginType.Wlx ? ".wlx" : ".wdx";
                var zip = Path.Combine(Root, "sample.zip");
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    Write(archive, "pluginst.inf", "[plugininstall]\n" + "description=Sample plugin\n" + "type=" + type.ToString().ToLowerInvariant() + "\nfile=sample" + (unicodeOnly ? ".u" + ext.Substring(1) : ext + (x64Only ? "64" : "")) + "\nversion=2.0\ndefaultdir=sample\n" + (type == PluginType.Wcx ? "defaultextension=7z,zip\n" : ""));
                    if (!x64Only && !unicodeOnly) Write(archive, "sample" + ext, "new");
                    if (unicodeOnly) Write(archive, "sample.u" + ext.Substring(1), "new unicode");
                    if (x64 || x64Only) Write(archive, "sample" + ext + "64", "new64");
                }
                Package = new PackageInspector().Inspect(zip);
                Entry = new PluginCatalogEntry { Id = "sample-" + type.ToString().ToLowerInvariant(), Name = "Sample", Type = type.ToString(), Aliases = new List<string> { "sample" + ext } };
                if (type == PluginType.Wcx) { var binary = Package.Files.Single(x => x.RelativePath.Equals("sample" + ext, StringComparison.OrdinalIgnoreCase)); Entry.IdentityEvidenceName = "VerifiedPackage"; Entry.WcxRegistration = new WcxRegistrationEvidence { Extensions = new List<string> { "7z", "zip" }, PackerCaps = 735, PackageSha256 = Package.PackageSha256, BinarySha256 = binary.Sha256, Architecture = "x86", VerifiedUtc = DateTime.UtcNow, Source = "test" }; }
                Candidate = new CatalogInstallCandidate { Entry = Entry, Version = VersionValue.Parse("2.0"), DownloadUrl = new Uri("https://example.test/sample.zip") };
                Reload();
            }
            public void Reload()
            {
                Configuration = new TotalCommanderConfiguration { IniPath = Ini, InstallDirectory = Tc,
                    Document = new IniDocumentReader().Read(Ini) };
            }
            public NewPluginInstallPlan Build()
            {
                Reload(); Plan = new NewPluginInstallPlanBuilder().Build(Entry, Candidate, Package, Configuration, new InstalledPlugin[0], BackupRoot);
                return Plan;
            }
            public InstalledPlugin Rediscover()
            {
                var section = Type == PluginType.Wcx ? "PackerPlugins" : Type == PluginType.Wfx ? "FileSystemPlugins" : Type == PluginType.Wlx ? "ListerPlugins" : "ContentPlugins";
                var document = new IniDocumentReader().Read(Ini);
                var entry = document.GetSection(section)?.Entries.FirstOrDefault(x => x.Key != "RedirectSection");
                var primary = Plan.PrimaryPath;
                var binaries = Plan.RequiredBinaryPaths.Select(path => new PluginBinary { Path = path, Exists = File.Exists(path),
                    Architecture = path.EndsWith("64", StringComparison.OrdinalIgnoreCase) ? PluginArchitecture.X64 : PluginArchitecture.X86,
                    LocalVersion = FileVersionProbe.Create("2.0", VersionSource.FileVersion, VersionConfidence.Exact) }).ToList();
                return entry == null || (Type == PluginType.Wcx ? !entry.Value.EndsWith("," + primary, StringComparison.OrdinalIgnoreCase) : !String.Equals(entry.Value, primary, StringComparison.OrdinalIgnoreCase)) ? null :
                    new InstalledPlugin { Identity = new PluginIdentity { Id = Entry.Id }, Type = Type, PrimaryPath = primary,
                        FileExists = File.Exists(primary), Binaries = binaries,
                        LocalVersion = FileVersionProbe.Create("2.0", VersionSource.FileVersion, VersionConfidence.Exact) };
            }
            public void Dispose()
            {
                if (Package != null && Directory.Exists(Package.StagingDirectory)) Directory.Delete(Package.StagingDirectory, true);
                if (Root != null && Root.StartsWith(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory), StringComparison.OrdinalIgnoreCase) && Directory.Exists(Root)) Directory.Delete(Root, true);
            }
        }
        private static void Write(ZipArchive zip, string name, string value)
        {
            using (var output = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8)) output.Write(value);
        }
        private static bool Fails(Action action) { try { action(); return false; } catch { return true; } }
        private static bool ArchitectureFails(Action action) { try { action(); return false; } catch (ArchitectureMismatchException) { return true; } }
        private static bool EncodingFails(Action action) { try { action(); return false; } catch (EncodingConflictException) { return true; } }
        public static void Run(Action<bool, string> check)
        {
            IList<string> extensions;
            check(WcxRegistration.TryNormalizeExtensions(" .7z, zip ,7Z ", out extensions) && extensions.Count == 2 && extensions[0] == "7z" && extensions[1] == "zip", "WCX extension normalization and duplicate removal");
            check(!WcxRegistration.TryNormalizeExtensions("7z,bad=value", out extensions) && !WcxRegistration.TryNormalizeExtensions("", out extensions), "WCX invalid or empty extensions blocked");
            using (var f = new Fixture(PluginType.Wlx))
            {
                check(NewPluginArchitectureValidator.DetectTotalCommander(f.Tc) == PluginArchitecture.X86 &&
                    NewPluginArchitectureValidator.DetectPackage(f.Package, f.Type) == PluginArchitecture.X86 && f.Build() != null,
                    "x86 TC + x86 plugin accepted from inspected binaries");
            }
            using (var f = new Fixture(PluginType.Wlx, x64Only: true))
                check(ArchitectureFails(() => f.Build()), "x86 TC + x64-only plugin blocked by ArchitectureMismatch");
            using (var f = new Fixture(PluginType.Wlx, unicodeOnly: true))
                check(NewPluginArchitectureValidator.DetectPackage(f.Package, f.Type) == PluginArchitecture.X86 && f.Build() != null,
                    "x86 TC + Unicode-x86 plugin accepted");
            using (var f = new Fixture(PluginType.Wlx))
            {
                File.Delete(Path.Combine(f.Tc, "TOTALCMD.EXE"));
                check(ArchitectureFails(() => f.Build()), "unknown TC architecture blocks new installation");
            }
            using (var f = new Fixture(PluginType.Wlx, x64Only: true))
            {
                File.Delete(Path.Combine(f.Tc, "TOTALCMD.EXE")); File.WriteAllBytes(Path.Combine(f.Tc, "TOTALCMD64.EXE"), new byte[] { 1 });
                check(NewPluginArchitectureValidator.DetectTotalCommander(f.Tc) == PluginArchitecture.X64 &&
                    NewPluginArchitectureValidator.DetectPackage(f.Package, f.Type) == PluginArchitecture.X64 && f.Build() != null,
                    "x64 TC + x64 plugin accepted from inspected binaries");
            }
            using (var f = new Fixture(PluginType.Wlx))
            {
                File.Delete(Path.Combine(f.Tc, "TOTALCMD.EXE")); File.WriteAllBytes(Path.Combine(f.Tc, "TOTALCMD64.EXE"), new byte[] { 1 });
                check(ArchitectureFails(() => f.Build()), "x64 TC + x86-only plugin blocked by ArchitectureMismatch");
            }
            using (var f = new Fixture(PluginType.Wlx, x64: true))
            {
                File.WriteAllBytes(Path.Combine(f.Tc, "TOTALCMD64.EXE"), new byte[] { 1 });
                check(NewPluginArchitectureValidator.DetectTotalCommander(f.Tc) == (PluginArchitecture.X86 | PluginArchitecture.X64) &&
                    f.Build() != null, "dual TC + dual plugin accepted");
            }
            using (var f = new Fixture(PluginType.Wlx))
            {
                File.WriteAllBytes(Path.Combine(f.Tc, "TOTALCMD64.EXE"), new byte[] { 1 });
                check(ArchitectureFails(() => f.Build()), "dual TC + x86-only plugin blocked by ArchitectureMismatch");
            }
            using (var f = new Fixture(PluginType.Wlx, x64Only: true))
            {
                File.WriteAllBytes(Path.Combine(f.Tc, "TOTALCMD64.EXE"), new byte[] { 1 });
                check(ArchitectureFails(() => f.Build()), "dual TC + x64-only plugin blocked by ArchitectureMismatch");
            }
            using (var f = new Fixture(PluginType.Wlx))
            {
                var plan = f.Build(); File.WriteAllBytes(Path.Combine(f.Tc, "TOTALCMD64.EXE"), new byte[] { 1 });
                check(ArchitectureFails(() => NewPluginTransactionalInstaller.Preflight(plan, () => false)), "preflight detects TC architecture changed after plan");
            }
            var catalogEntry = new PluginCatalogEntry { Id = "sample-wlx", Name = "Sample", Type = "Wlx", Aliases = new List<string> { "sample.wlx" },
                Sources = new List<CatalogSource> { new CatalogSource { Provider = "totalcmd.net", Id = "sample-wlx", Priority = 10 } } };
            var catalogCandidate = new CatalogInstallService(new[] { new CatalogProvider() }).CheckAsync(catalogEntry, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            check(catalogCandidate.Version.CompareTo(VersionValue.Parse("2.0")) == VersionComparison.Equal && catalogCandidate.DownloadUrl != null &&
                catalogCandidate.SourceName == "catalog-test", "catalog install candidate resolves without fake InstalledPlugin");
            foreach (var type in new[] { PluginType.Wfx, PluginType.Wlx, PluginType.Wdx })
            using (var f = new Fixture(type))
            {
                var plan = f.Build();
                check(plan.PluginType == type && plan.ConfigurationFiles.Count == 1 && plan.Files.Count == 1, "new " + type + " plan");
                check(plan.TargetDirectory == Path.Combine(f.Tc, "plugins", type.ToString().ToLowerInvariant(), "sample"), "fallback plugins/type/defaultdir " + type);
                var change = plan.ConfigurationFiles[0].Changes.Single();
                check(change.Section == (type == PluginType.Wfx ? "FileSystemPlugins" : type == PluginType.Wlx ? "ListerPlugins" : "ContentPlugins") &&
                    change.Key == (type == PluginType.Wfx ? "sample" : "0") && change.Value == plan.PrimaryPath, "registration key/path " + type);
                var original = File.ReadAllBytes(f.Ini);
                var manifest = new NewPluginTransactionalInstaller(isTotalCommanderRunning: () => false).Install(plan, f.Rediscover);
                check(manifest.ManifestVersion == 4 && manifest.State == InstallStateMachine.Completed && File.Exists(plan.PrimaryPath), "new " + type + " transactional install");
                check(manifest.ConfigurationFiles.Count == 1 && File.Exists(manifest.ConfigurationFiles[0].BackupPath) &&
                    original.SequenceEqual(File.ReadAllBytes(manifest.ConfigurationFiles[0].BackupPath)), "new " + type + " INI backup");
                new NewPluginRollbackService().Rollback(manifest, f.BackupRoot);
                check(!File.Exists(plan.PrimaryPath) && original.SequenceEqual(File.ReadAllBytes(f.Ini)) && manifest.State == InstallStateMachine.RolledBack,
                    "new " + type + " rollback files and INI");
            }
            using (var f = new Fixture(PluginType.Wlx, true))
            {
                var plan = f.Build();
                check(plan.RequiredBinaryPaths.Count == 2 && plan.ConfigurationFiles[0].Changes.Any(x => x.Section == "ListerPlugins64" && x.Key == "0" && x.Value == "1"), "x64 marker same key");
            }
            foreach (var type in new[] { PluginType.Wfx, PluginType.Wdx })
            using (var f = new Fixture(type, true))
            {
                var plan = f.Build();
                check(plan.ConfigurationFiles[0].Changes.Any(x => x.Section == (type == PluginType.Wfx ? "FileSystemPlugins64" : "ContentPlugins64") &&
                    x.Key == (type == PluginType.Wfx ? "sample" : "0") && x.Value == "1"), "x64 marker " + type);
            }
            using (var f = new Fixture(PluginType.Wlx))
            {
                f.Package.DefaultDir = "..\\escape";
                check(Fails(() => f.Build()), "new plugin rejects unsafe defaultdir");
            }
            using (var f = new Fixture(PluginType.Wcx))
            {
                var plan = f.Build(); var changes = plan.ConfigurationFiles.Single().Changes;
                check(changes.Count == 2 && changes.All(x => x.Section == "PackerPlugins" && x.Value == "735," + plan.PrimaryPath) && changes.Select(x => x.Key).OrderBy(x => x).SequenceEqual(new[] { "7z", "zip" }), "WCX multi-extension registration uses verified caps");
                var original = File.ReadAllBytes(f.Ini); var manifest = new NewPluginTransactionalInstaller(isTotalCommanderRunning: () => false).Install(plan, f.Rediscover);
                check(manifest.State == InstallStateMachine.Completed && new IniDocumentReader().Read(f.Ini).GetSection("PackerPlugins").GetValue("7z") == "735," + plan.PrimaryPath, "WCX transactional install writes PackerPlugins");
                new NewPluginRollbackService().Rollback(manifest, f.BackupRoot); check(original.SequenceEqual(File.ReadAllBytes(f.Ini)) && !File.Exists(plan.PrimaryPath), "WCX rollback restores registrations and files");
            }
            using (var f = new Fixture(PluginType.Wcx))
            {
                f.Entry.WcxRegistration.PackerCaps = 0; check(Fails(() => f.Build()), "WCX missing packerCaps blocked");
                f.Entry.WcxRegistration.PackerCaps = 735; f.Entry.WcxRegistration.BinarySha256 = new string('0', 64); check(Fails(() => f.Build()), "WCX stale hash-bound evidence blocked");
            }
            using (var f = new Fixture(PluginType.Wcx, false, "[PackerPlugins]\n7z=123,other.wcx\n"))
                check(Fails(() => f.Build()), "WCX occupied extension conflict blocked");
            using (var f = new Fixture(PluginType.Wlx))
            {
                f.Entry.Aliases.Clear(); f.Entry.Aliases.Add("other.wlx");
                check(Fails(() => f.Build()), "ZIP binary must match catalog identity alias");
            }
            using (var f = new Fixture(PluginType.Wlx))
            {
                var userCatalog = Path.Combine(f.Root, "user-catalog.json");
                File.WriteAllText(userCatalog, "[{\"id\":\"sample-wlx\",\"name\":\"Sample\",\"type\":\"Wlx\",\"aliases\":[\"sample.wlx\"],\"sources\":[{\"provider\":\"totalcmd.net\",\"id\":\"sample-wlx\",\"priority\":10}]}]");
                var catalog = new CatalogService(userCatalog);
                var resolver = new TotalCommanderConfigurationResolver();
                var discovery = new PluginDiscoveryService(resolver, new LocalVersionResolver(null, new IVersionProbe[] { new VersionProbe() }), catalog);
                var plan = f.Build();
                var installed = new NewPluginTransactionalInstaller(isTotalCommanderRunning: () => false).Install(plan, () =>
                    discovery.Discover(new TotalCommanderConfiguration { IniPath = f.Ini, InstallDirectory = f.Tc, Document = new IniDocumentReader().Read(f.Ini) })
                        .FirstOrDefault(x => x.Identity.Id == f.Entry.Id));
                check(installed.State == InstallStateMachine.Completed && discovery.Discover(new TotalCommanderConfiguration { IniPath = f.Ini,
                    InstallDirectory = f.Tc, Document = new IniDocumentReader().Read(f.Ini) }).Any(x => x.Identity.Id == f.Entry.Id),
                    "real PluginDiscovery verifies newly registered plugin");
            }
            using (var f = new Fixture(PluginType.Wdx, false, "[Configuration]\nPluginBaseDir=custom\n"))
                check(f.Build().TargetDirectory == Path.Combine(f.Tc, "custom", "wdx", "sample"), "PluginBaseDir respected");
            using (var f = new Fixture(PluginType.Wdx))
            {
                File.WriteAllBytes(f.Ini, new UTF8Encoding(false).GetBytes("[Configuration]\n; русский комментарий\nPluginBaseDir=плагины\n"));
                var plan = f.Build();
                check(plan.TargetDirectory == Path.Combine(f.Tc, "плагины", "wdx", "sample"), "UTF-8 no-BOM PluginBaseDir decoded consistently");
                var manifest = new NewPluginTransactionalInstaller(isTotalCommanderRunning: () => false).Install(plan, f.Rediscover);
                check(manifest.State == InstallStateMachine.Completed, "UTF-8 no-BOM INI end-to-end install");
            }
            using (var f = new Fixture(PluginType.Wlx, false, "[ListerPlugins]\r\n0=old.wlx\r\n2=other.wlx\r\n"))
                check(f.Build().ConfigurationFiles.Single().Changes.Single().Key == "1", "numeric key fills first hole");
            using (var f = new Fixture(PluginType.Wlx, true, "[ListerPlugins64]\r\n0=1\r\n"))
                check(f.Build().ConfigurationFiles.Single().Changes.All(x => x.Key == "1"), "numeric key avoids occupied x64 marker");
            using (var f = new Fixture(PluginType.Wfx, false, "[FileSystemPlugins]\r\nsample=old.wfx\r\n"))
                check(Fails(() => f.Build()), "WFX key collision blocked");
            using (var f = new Fixture(PluginType.Wlx, false, "[ListerPlugins]\r\nRedirectSection=plugins.ini\r\n"))
            {
                var redirected = Path.Combine(f.Tc, "plugins.ini"); File.WriteAllText(redirected, "[ListerPlugins]\r\n; keep\r\n");
                var originalMain = File.ReadAllBytes(f.Ini);
                var plan = f.Build();
                check(plan.ConfigurationFiles.Single().Path == redirected && originalMain.SequenceEqual(File.ReadAllBytes(f.Ini)), "redirected section targets real INI");
                new NewPluginTransactionalInstaller(isTotalCommanderRunning: () => false).Install(plan, () =>
                    new InstalledPlugin { Identity = new PluginIdentity { Id = f.Entry.Id }, Type = f.Type, FileExists = true,
                        PrimaryPath = plan.PrimaryPath, Binaries = new List<PluginBinary> { new PluginBinary { Path = plan.PrimaryPath, Exists = true } },
                        LocalVersion = FileVersionProbe.Create("2.0", VersionSource.FileVersion, VersionConfidence.Exact) });
                check(originalMain.SequenceEqual(File.ReadAllBytes(f.Ini)) && new IniDocumentReader().Read(redirected).GetSection("ListerPlugins").GetValue("0") == plan.PrimaryPath,
                    "redirected INI committed without main INI rewrite");
            }
            using (var f = new Fixture(PluginType.Wlx, false, "[ListerPlugins]\nRedirectSection=plugins.ini\n"))
            {
                File.WriteAllText(Path.Combine(f.Tc, "plugins.ini"), "[ListerPlugins]\nRedirectSection=wincmd.ini\n");
                check(Fails(() => f.Build()), "redirect loop blocked");
            }
            using (var f = new Fixture(PluginType.Wdx, false, "[Configuration]\nAlternateUserIni=alt.ini\n[ContentPlugins]\nRedirectSection=1\n"))
            {
                var alternate = Path.Combine(f.Tc, "alt.ini"); File.WriteAllText(alternate, "[ContentPlugins]\n");
                f.Reload(); f.Configuration.AlternateUserIni = alternate;
                var plan = new NewPluginInstallPlanBuilder().Build(f.Entry, f.Candidate, f.Package, f.Configuration, new InstalledPlugin[0], f.BackupRoot);
                check(plan.ConfigurationFiles.Single().Path == alternate, "RedirectSection=1 targets AlternateUserIni");
            }
            using (var f = new Fixture(PluginType.Wlx, false, "[ListerPlugins]\nRedirectSection=missing.ini\n"))
                check(Fails(() => f.Build()), "missing redirected INI blocked");
            using (var f = new Fixture(PluginType.Wlx))
            {
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("; comment\n[Configuration]\n x =  y  \n[ListerPlugins]\n; preserve\n")).ToArray();
                File.WriteAllBytes(f.Ini, bytes);
                var patch = new IniPatchEngine().Begin(f.Ini); new IniPatchEngine().Add(patch, "ListerPlugins", "0", "plugin.wlx");
                check(Encoding.UTF8.GetString(patch.NewBytes).Contains("; preserve\n0=plugin.wlx\n") &&
                    patch.NewBytes.Take(3).SequenceEqual(Encoding.UTF8.GetPreamble()), "INI BOM/LF/comments/whitespace retained");
                var cp1251 = Encoding.GetEncoding(1251); var legacy = cp1251.GetBytes("; комментарий\r\n[ListerPlugins]\r\n  x = untouched  \r\n");
                File.WriteAllBytes(f.Ini, legacy); var cp1251Engine = new IniPatchEngine(cp1251); patch = cp1251Engine.Begin(f.Ini);
                cp1251Engine.Add(patch, "ListerPlugins", "0", "плагин.wlx");
                check(cp1251.GetString(patch.NewBytes).Contains("; комментарий\r\n[ListerPlugins]\r\n  x = untouched  \r\n0=плагин.wlx\r\n") &&
                    patch.NewBytes.Take(legacy.Length).SequenceEqual(legacy), "CP1251 Cyrillic insertion and original bytes/CRLF retained");
                var beforeConflict = patch.NewBytes;
                check(EncodingFails(() => cp1251Engine.Add(patch, "ListerPlugins", "1", "emoji-😀.wlx")) &&
                    Object.ReferenceEquals(beforeConflict, patch.NewBytes), "CP1251 unrepresentable character blocked without lossy question mark");
                var western = new IniPatchEngine(Encoding.GetEncoding(1252));
                File.WriteAllBytes(f.Ini, Encoding.ASCII.GetBytes("[ListerPlugins]\r\n; keep\r\n"));
                var westernPatch = western.Begin(f.Ini); var westernOriginal = westernPatch.NewBytes;
                check(EncodingFails(() => western.Add(westernPatch, "ListerPlugins", "0", "плагин.wlx")) &&
                    Object.ReferenceEquals(westernOriginal, westernPatch.NewBytes) &&
                    File.ReadAllBytes(f.Ini).SequenceEqual(westernOriginal), "western ANSI + Cyrillic blocked before INI change");
                var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("[ListerPlugins]\r\n; keep\r\n")).ToArray();
                File.WriteAllBytes(f.Ini, utf16); patch = new IniPatchEngine().Begin(f.Ini); new IniPatchEngine().Add(patch, "ListerPlugins", "0", "плагин.wlx");
                check(patch.NewBytes.Take(utf16.Length).SequenceEqual(utf16) && Encoding.Unicode.GetString(patch.NewBytes, 2, patch.NewBytes.Length - 2).Contains("0=плагин.wlx\r\n"),
                    "UTF-16 BOM and original bytes retained");
                var utf8NoBom = new UTF8Encoding(false).GetBytes("[ListerPlugins]\n; русский текст\n");
                File.WriteAllBytes(f.Ini, utf8NoBom); patch = new IniPatchEngine().Begin(f.Ini); new IniPatchEngine().Add(patch, "ListerPlugins", "0", "плагин.wlx");
                check(patch.NewBytes.Take(utf8NoBom.Length).SequenceEqual(utf8NoBom) &&
                    new UTF8Encoding(false, true).GetString(patch.NewBytes).Contains("0=плагин.wlx\n"), "UTF-8 without BOM retained");
            }
            using (var f = new Fixture(PluginType.Wdx))
            {
                var plan = f.Build(); File.AppendAllText(f.Ini, "; concurrent\r\n");
                check(Fails(() => NewPluginTransactionalInstaller.Preflight(plan, () => false)) && !File.Exists(plan.PrimaryPath), "concurrent INI edit rejected before install");
            }
            using (var f = new Fixture(PluginType.Wdx))
            {
                var plan = f.Build(); File.SetAttributes(f.Ini, FileAttributes.ReadOnly);
                try { check(Fails(() => NewPluginTransactionalInstaller.Preflight(plan, () => false)), "read-only INI blocks installation without elevation"); }
                finally { File.SetAttributes(f.Ini, FileAttributes.Normal); }
            }
            using (var f = new Fixture(PluginType.Wlx))
            {
                var plan = f.Build(); var original = File.ReadAllBytes(f.Ini);
                var failed = Fails(() => new NewPluginTransactionalInstaller(checkpoint: point => { if (point == "AfterFiles") File.AppendAllText(f.Ini, "; foreign\r\n"); },
                    isTotalCommanderRunning: () => false).Install(plan, f.Rediscover));
                check(failed && !File.Exists(plan.PrimaryPath) && File.ReadAllText(f.Ini).Contains("foreign"), "ConfigConflict keeps foreign edit and rolls back files");
            }
            using (var f = new Fixture(PluginType.Wlx))
            {
                var plan = f.Build(); var before = File.ReadAllBytes(f.Ini);
                check(Fails(() => new NewPluginTransactionalInstaller(checkpoint: point => { if (point == "AfterIni") throw new IOException("post-install failure"); },
                    isTotalCommanderRunning: () => false).Install(plan, f.Rediscover)) &&
                    before.SequenceEqual(File.ReadAllBytes(f.Ini)) && !File.Exists(plan.PrimaryPath), "post-install failure full rollback");
            }
            using (var f = new Fixture(PluginType.Wlx))
            {
                var plan = f.Build(); var manifest = new NewPluginTransactionalInstaller(isTotalCommanderRunning: () => false).Install(plan, f.Rediscover);
                File.AppendAllText(f.Ini, "; user edit\r\n");
                string message;
                try { new NewPluginRollbackService().Rollback(manifest, f.BackupRoot); message = ""; }
                catch (IOException ex) { message = ex.Message; }
                check(manifest.State == InstallStateMachine.ConfigRecoveryConflict && File.Exists(plan.PrimaryPath) && File.ReadAllText(f.Ini).Contains("user edit") &&
                    message.Contains("INI изменён после установки") && !message.Contains("Файл плагина изменён"),
                    "user-edited INI blocks rollback without clobber");
                check(BackupService.FindIncomplete(f.BackupRoot).Any(x => x.TransactionId == manifest.TransactionId), "ConfigRecoveryConflict remains visible to startup recovery");
                File.WriteAllBytes(f.Ini, plan.ConfigurationFiles.Single().InstalledBytes);
                new RecoveryService(f.BackupRoot).Recover(manifest, null);
                check(manifest.State == InstallStateMachine.RolledBack && !File.Exists(plan.PrimaryPath), "ConfigRecoveryConflict retry after restoring INI hash");
            }
            using (var f = new Fixture(PluginType.Wlx))
            {
                var plan = f.Build(); var manifest = new NewPluginTransactionalInstaller(isTotalCommanderRunning: () => false).Install(plan, f.Rediscover);
                File.WriteAllText(plan.PrimaryPath, "user-edited DLL");
                string message;
                try { new NewPluginRollbackService().Rollback(manifest, f.BackupRoot); message = ""; }
                catch (IOException ex) { message = ex.Message; }
                check(manifest.State == InstallStateMachine.RecoveryConflict && File.Exists(plan.PrimaryPath) &&
                    message.Contains("Файл плагина изменён после установки") && !message.Contains("INI изменён"),
                    "modified plugin DLL is RecoveryConflict, not ConfigRecoveryConflict");
                check(BackupService.FindIncomplete(f.BackupRoot).Any(x => x.TransactionId == manifest.TransactionId), "RecoveryConflict remains visible to startup recovery");
                File.Copy(plan.Files.Single().Source.StagedPath, plan.PrimaryPath, true);
                new RecoveryService(f.BackupRoot).Recover(manifest, null);
                check(manifest.State == InstallStateMachine.RolledBack && !File.Exists(plan.PrimaryPath), "RecoveryConflict retry after restoring plugin hash");
            }
            using (var f = new Fixture(PluginType.Wlx))
            {
                var plan = f.Build(); var manifest = new NewPluginTransactionalInstaller(isTotalCommanderRunning: () => false).Install(plan, f.Rediscover);
                File.AppendAllText(f.Ini, "; user edit\r\n"); File.WriteAllText(plan.PrimaryPath, "user-edited DLL");
                check(Fails(() => new NewPluginRollbackService().Rollback(manifest, f.BackupRoot)) &&
                    manifest.State == InstallStateMachine.ConfigRecoveryConflict, "config conflict classified first when both modified");
                File.WriteAllBytes(f.Ini, plan.ConfigurationFiles.Single().InstalledBytes);
                check(Fails(() => new RecoveryService(f.BackupRoot).Recover(manifest, null)) &&
                    manifest.State == InstallStateMachine.RecoveryConflict, "retry reclassifies remaining plugin-file conflict");
                File.Copy(plan.Files.Single().Source.StagedPath, plan.PrimaryPath, true);
                new RecoveryService(f.BackupRoot).Recover(manifest, null);
                check(manifest.State == InstallStateMachine.RolledBack, "mixed conflicts can be retried to completion");
            }
            using (var f = new Fixture(PluginType.Wlx))
            {
                var plan = f.Build(); var manifest = new NewPluginTransactionalInstaller(isTotalCommanderRunning: () => false).Install(plan, f.Rediscover);
                File.WriteAllText(plan.PrimaryPath, "user-edited DLL");
                check(Fails(() => new NewPluginRollbackService().Rollback(manifest, f.BackupRoot)) &&
                    manifest.State == InstallStateMachine.RecoveryConflict, "plugin conflict classified first");
                File.AppendAllText(f.Ini, "; user edit\r\n");
                check(Fails(() => new RecoveryService(f.BackupRoot).Recover(manifest, null)) &&
                    manifest.State == InstallStateMachine.ConfigRecoveryConflict, "retry reclassifies new INI conflict");
            }
            using (var f = new Fixture(PluginType.Wlx))
            {
                var plan = f.Build(); var manifest = new NewPluginTransactionalInstaller(isTotalCommanderRunning: () => false).Install(plan, f.Rediscover);
                var foreign = Path.Combine(plan.TargetDirectory, "foreign.txt"); File.WriteAllText(foreign, "keep");
                new NewPluginRollbackService().Rollback(manifest, f.BackupRoot);
                check(File.Exists(foreign) && !File.Exists(plan.PrimaryPath), "foreign file survives new-install rollback");
            }
            using (var f = new Fixture(PluginType.Wlx))
            {
                var plan = f.Build(); var manifest = new BackupService().CreateNew(plan);
                InstallStateMachine.Set(manifest, InstallStateMachine.Installing); BackupService.Save(manifest);
                Directory.CreateDirectory(plan.TargetDirectory); File.Copy(plan.Files[0].Source.StagedPath, plan.PrimaryPath);
                new NewPluginRollbackService().Rollback(BackupService.Load(Path.Combine(plan.BackupDirectory, "manifest.json"), f.BackupRoot), f.BackupRoot);
                check(!File.Exists(plan.PrimaryPath) && manifest.ConfigurationFiles.Single().OriginalSha256 == PackageInspector.Hash(f.Ini), "crash after files before INI recovery");
            }
            using (var f = new Fixture(PluginType.Wlx))
            {
                var plan = f.Build(); var original = File.ReadAllBytes(f.Ini); var manifest = new BackupService().CreateNew(plan);
                InstallStateMachine.Set(manifest, InstallStateMachine.Installing); BackupService.Save(manifest);
                Directory.CreateDirectory(plan.TargetDirectory); File.Copy(plan.Files[0].Source.StagedPath, plan.PrimaryPath);
                File.WriteAllBytes(f.Ini, plan.ConfigurationFiles.Single().InstalledBytes);
                new NewPluginRollbackService().Rollback(BackupService.Load(Path.Combine(plan.BackupDirectory, "manifest.json"), f.BackupRoot), f.BackupRoot);
                check(!File.Exists(plan.PrimaryPath) && original.SequenceEqual(File.ReadAllBytes(f.Ini)), "crash after INI before verification recovery");
            }
        }
    }
}
