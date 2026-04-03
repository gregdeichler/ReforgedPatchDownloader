using ReforgedPatchDownloaderApp;

var failures = new List<string>();

Run("Parser keeps URL lines from becoming fake patch headings", TestUrlLinesDoNotBecomeHeadings, failures);
Run("Parser reads stable release and patch options", TestBasicCatalogParsing, failures);
Run("Parser accepts emoji-prefixed patch headings from the live site", TestEmojiHeadingParsing, failures);
Run("App version comparison handles semantic versions", TestVersionComparison, failures);

if (failures.Count > 0)
{
    Console.Error.WriteLine("Tests failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine("- " + failure);
    }

    Environment.Exit(1);
}

Console.WriteLine("All tests passed.");

static void Run(string name, Action test, ICollection<string> failures)
{
    try
    {
        test();
        Console.WriteLine("[PASS] " + name);
    }
    catch (Exception ex)
    {
        failures.Add(name + ": " + ex.Message);
    }
}

static void TestUrlLinesDoNotBecomeHeadings()
{
    const string homeHtml = """
<section>
  <p>Current Stable v5.3.4</p>
  <p>Status: Live</p>
  <p>Updated: 2026-03-31</p>
  <p>Updated Modules: A, D, V</p>
</section>
""";

    const string downloadsHtml = """
<h2>Core Modules</h2>
<h3>PATCH-A - Creatures</h3>
<p>Improves creature visuals.</p>
<p>Download <a href="https://cdn.example.com/patch-A.mpq">[link]</a> v5.3.4</p>
<h3>PATCH-U - UI</h3>
<p>Standard</p>
<p>Download <a href="https://cdn.example.com/patch-U-standard.mpq">[link]</a> v5.3.4</p>
<p>Compatible Version</p>
<p>Download <a href="https://cdn.example.com/patch-U-compatible.mpq">[link]</a> v5.3.4</p>
""";

    var catalog = PatchCatalogParser.ParseCatalog(homeHtml, downloadsHtml);
    Assert(catalog.Patches.Count == 3, "Expected 3 real patch options, found " + catalog.Patches.Count + ".");
}

static void TestBasicCatalogParsing()
{
    const string homeHtml = """
<section>
  <p>Current Stable</p>
  <p>v5.3.4</p>
  <p>Status: Live</p>
  <p>Updated: 2026-03-31</p>
  <p>Updated Modules: A, D, V</p>
</section>
""";

    const string downloadsHtml = """
<h2>Optional Enhancements</h2>
<h3>PATCH-V — Spell Visual Effects</h3>
<p>Beautiful spell refinements.</p>
<p>Download <a href="https://cdn.example.com/patch-V.mpq">[link]</a> v5.3.4</p>
""";

    var catalog = PatchCatalogParser.ParseCatalog(homeHtml, downloadsHtml);
    var patch = catalog.Patches.Single();

    Assert(catalog.Release.StableVersion == "v5.3.4", "Stable version was not parsed.");
    Assert(catalog.Release.ReleaseDate == "2026-03-31", "Release date was not parsed.");
    Assert(patch.Name == "PATCH-V", "Patch name was not parsed.");
    Assert(patch.Category == "Optional", "Category should normalize to Optional.");
    Assert(patch.Title.Contains("Spell Visual Effects", StringComparison.Ordinal), "Patch title was not parsed correctly.");
}

static void TestEmojiHeadingParsing()
{
    const string homeHtml = """
<section>
  <p>Current Stable v5.3.4</p>
  <p>Status: Live</p>
  <p>Updated: 2026-03-31</p>
</section>
""";

    const string downloadsHtml = """
<h2>Core Modules</h2>
<h3>## ⚔️ PATCH-G — Gear & Weapons</h3>
<p>Core</p>
<p>Gear and weapon visuals used by multiple enhancements.</p>
<p>Download <a href="https://cdn.example.com/patch-G.mpq">Download</a> Updated • v5.0.1</p>
""";

    var catalog = PatchCatalogParser.ParseCatalog(homeHtml, downloadsHtml);
    var patch = catalog.Patches.Single();

    Assert(patch.Name == "PATCH-G", "Emoji-prefixed heading did not parse patch name.");
    Assert(patch.Title.Contains("Gear & Weapons", StringComparison.Ordinal), "Emoji-prefixed heading did not parse title.");
}

static void TestVersionComparison()
{
    Assert(AppUpdateService.IsNewerVersion("2.3.0", "2.3.1"), "Expected 2.3.1 to be newer than 2.3.0.");
    Assert(!AppUpdateService.IsNewerVersion("2.3.1", "2.3.1"), "Equal versions should not report as newer.");
    Assert(!AppUpdateService.IsNewerVersion("2.3.2", "2.3.1"), "Older versions should not report as newer.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
